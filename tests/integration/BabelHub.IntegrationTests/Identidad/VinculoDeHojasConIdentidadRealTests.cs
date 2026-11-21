using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Roles = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// T62 de <c>identidad-seguridad</c> (AC-IDE-70 a AC-IDE-72) con <b>Identidad, Reclamaciones y Auditoría reales</b> sobre PostgreSQL en el host compuesto: el cambio de correo de un cliente vincula las hojas del correo anterior
/// y escribe la auditoría general dentro de la transacción de Identidad; con esa auditoría caída el cambio no se aplica (el código sigue vigente y el reintento funciona); el reemplazo de una cuenta sin verificar (IDE-84) desvincula
/// sus hojas; y el correo anterior no queda en ningún registro. La auditoría caída se simula con un decorador del <see cref="IAuditoria"/> real que falla solo para la vinculación.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class VinculoDeHojasConIdentidadRealTests(PostgresDelEcommerceCompletoFixture bd)
{
    private const string Frase = "una frase larga y segura para el cliente";

    private static readonly DateTimeOffset Inicio = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private sealed class RelojMovil(DateTimeOffset inicio) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class CapturadorDeCorreo : IEnviadorDeCorreo
    {
        public List<CorreoDeCodigo> Codigos { get; } = [];

        public Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeEnvio.Enviado);

        public Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default)
        {
            lock (Codigos)
            {
                Codigos.Add(correo);
            }

            return Task.FromResult(ResultadoDeEnvio.Enviado);
        }
    }

    private sealed class InterruptorDeAuditoria
    {
        public volatile bool Caida;
    }

    /// <summary>El <see cref="IAuditoria"/> real con una falla controlada: solo la vinculación de hojas lanza <see cref="AuditoriaNoDisponibleException"/>, como lo haría una auditoría que no puede escribir.</summary>
    private sealed class AuditoriaInterrumpible(IAuditoria real, InterruptorDeAuditoria interruptor) : IAuditoria
    {
        public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default) =>
            interruptor.Caida && solicitud.Accion.Equals(AccionesAuditables.HojaDeReclamacionVinculada)
                ? throw new AuditoriaNoDisponibleException()
                : real.Registrar(solicitud, cancelacion);
    }

    private static ValorSecreto S(string valor) => new(valor);

    private async Task<Guid> SembrarCliente(AnfitrionRealDelEcommerce anfitrion, string correo, bool verificado)
    {
        var id = Guid.NewGuid();
        var resumen = await anfitrion.Como(ActorActual.DelSistema(), async sp =>
        {
            var (contrasena, _) = Contrasena.Crear(Frase, _ => false);
            return await sp.GetRequiredService<IHasherDeContrasenas>().Resumir(contrasena!);
        });
        await bd.Ejecutar(
            $"""
            INSERT INTO identidad.usuarios (id, nombre, correo, correo_verificado, rol, activo, hash_contrasena, creada_en)
              VALUES ('{id}', 'Cliente Original', '{correo}', {(verificado ? "true" : "false")}, 'cliente', true, '{resumen}', DATE '2026-10-01');
            """);
        return id;
    }

    private static Task<Resultado<ConstanciaDeHoja>> RegistrarHoja(AnfitrionRealDelEcommerce anfitrion, ActorActual actor, string correoDeContacto, string marca) =>
        anfitrion.Como(actor, sp => sp.GetRequiredService<IReclamaciones>().Registrar(
            new RegistrarHojaEntrada("Ana Pérez Quispe", "12345678", "Av. Los Olivos 123, Ayacucho", correoDeContacto, "Libro", 49.90m, "reclamo", $"Llegó dañado ({marca}).", null, $"clave-del-formulario-{marca}", null)));

    private async Task<AnfitrionRealDelEcommerce> Anfitrion(RelojMovil reloj, CapturadorDeCorreo correo, InterruptorDeAuditoria interruptor)
    {
        await bd.Ejecutar(
            """
            INSERT INTO identidad.versiones_de_textos (id, terminos, privacidad, reembolso, vigente_desde) VALUES (1, 't-vinculo', 'p-vinculo', 'r-vinculo', DATE '2026-11-01')
              ON CONFLICT (id) DO UPDATE SET terminos = EXCLUDED.terminos, privacidad = EXCLUDED.privacidad, reembolso = EXCLUDED.reembolso, vigente_desde = EXCLUDED.vigente_desde;
            """);
        return bd.Anfitrion(s =>
        {
            s.AddSingleton<IReloj>(reloj);
            s.AddSingleton<IEnviadorDeCorreo>(correo);
            // El Inventario no está en este host: sus pendientes de la cuenta (reservas) se quitan del compuesto; los de los demás módulos reales siguen contando.
            foreach (var d in s.Where(d => d.ServiceType == typeof(IPendientesDeCuenta) && d.ImplementationType == typeof(Ecommerce.Infrastructure.Identidad.InventarioPendientesDeCuenta)).ToList())
            {
                s.Remove(d);
            }

            var original = s.Last(d => d.ServiceType == typeof(IAuditoria));
            s.Remove(original);
            s.AddScoped<IAuditoria>(sp => new AuditoriaInterrumpible((IAuditoria)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!), interruptor));
        });
    }

    private async Task<(Guid Id, ValorSecreto Marca)> ClienteConElIngresoCompleto(AnfitrionRealDelEcommerce anfitrion, RelojMovil reloj, string correo)
    {
        var id = await SembrarCliente(anfitrion, correo, verificado: true);
        var actor = ActorActual.DeUsuario(id, Roles.Cliente, null);
        var marca = (await anfitrion.Como(actor, sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().RegistrarElIngresoCompleto(id, null, "PE"))).Valor.MarcaNueva!.Revelar();
        reloj.Ahora = Inicio.AddHours(25);
        return (id, S(marca));
    }

    private Task<long> HojasVinculadas(string marca) => bd.Consultar<long>($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marca})%' AND cuenta_id IS NOT NULL");

    [Fact]
    [Trait("Criterio", "AC-IDE-71")]
    [Trait("Criterio", "AC-IDE-70")]
    public async Task Al_cambiar_el_correo_la_hoja_del_correo_anterior_queda_vinculada_con_su_auditoria_y_conserva_sus_datos_sin_dejar_el_correo_anterior_en_ningun_registro()
    {
        var reloj = new RelojMovil(Inicio);
        var correo = new CapturadorDeCorreo();
        await using var anfitrion = await Anfitrion(reloj, correo, new InterruptorDeAuditoria());
        var anterior = $"c{Guid.NewGuid():N}@example.test";
        var nuevo = $"nuevo-c{Guid.NewGuid():N}@example.test";
        var (id, marca) = await ClienteConElIngresoCompleto(anfitrion, reloj, anterior);
        var actor = ActorActual.DeUsuario(id, Roles.Cliente, null);
        var marcaDeLaHoja = Guid.NewGuid().ToString("N")[..12];
        var registro = await RegistrarHoja(anfitrion, ActorActual.Anonimo(), anterior, marcaDeLaHoja);
        Assert.True(registro.EsExito, registro.Error?.Clave);
        var numero = registro.Valor.Numero;
        Assert.Equal(0L, await HojasVinculadas(marcaDeLaHoja));

        var pedido = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().Solicitar(id, nuevo, S(Frase), marca, "PE"));
        Assert.True(pedido.EsExito, pedido.Error?.Clave);
        var codigo = Assert.Single(correo.Codigos, c => c.Destinatario == nuevo).Codigo.Revelar();
        var verificado = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().VerificarElCorreoNuevo(id, S(codigo)));

        Assert.True(verificado.EsExito, verificado.Error?.Clave);
        Assert.Equal(nuevo, await bd.Consultar<string>($"SELECT correo FROM identidad.usuarios WHERE id = '{id}'"));
        Assert.Equal(id, await bd.Consultar<Guid>($"SELECT cuenta_id FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marcaDeLaHoja})%'"));

        // La auditoría general: un solo registro de la vinculación con el cliente como objeto, el disparador y el conteo, sin el correo ni el número de la hoja.
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'HojaDeReclamacionVinculada' AND objeto_tipo = 'usuario' AND objeto_id = '{id:D}'"));
        Assert.Equal("cambio-de-correo", await bd.Consultar<string>($"SELECT detalle->>'disparador' FROM auditoria.registros WHERE accion = 'HojaDeReclamacionVinculada' AND objeto_id = '{id:D}'"));
        Assert.Equal("1", await bd.Consultar<string>($"SELECT detalle->>'conteo' FROM auditoria.registros WHERE accion = 'HojaDeReclamacionVinculada' AND objeto_id = '{id:D}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE detalle::text LIKE '%{numero}%'"));

        // IDE-70: la hoja conserva el contacto con que se emitió (el correo anterior, que es su dato) y el correo anterior no aparece en la auditoría, en los eventos de seguridad ni en las alertas.
        Assert.Equal(anterior, await bd.Consultar<string>($"SELECT correo FROM reclamaciones.hojas_reclamacion WHERE numero = '{numero}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE detalle::text ILIKE '%{anterior}%' OR objeto_id ILIKE '%{anterior}%' OR motivo ILIKE '%{anterior}%'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE detalle::text ILIKE '%{anterior}%' OR objeto_id ILIKE '%{anterior}%' OR motivo ILIKE '%{anterior}%'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.alertas a WHERE a::text ILIKE '%{anterior}%'"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-72")]
    public async Task Con_la_auditoria_de_la_vinculacion_caida_el_cambio_de_correo_no_se_aplica_deja_el_codigo_vigente_y_el_reintento_funciona()
    {
        var reloj = new RelojMovil(Inicio);
        var correo = new CapturadorDeCorreo();
        var interruptor = new InterruptorDeAuditoria();
        await using var anfitrion = await Anfitrion(reloj, correo, interruptor);
        var anterior = $"c{Guid.NewGuid():N}@example.test";
        var nuevo = $"nuevo-c{Guid.NewGuid():N}@example.test";
        var (id, marca) = await ClienteConElIngresoCompleto(anfitrion, reloj, anterior);
        var actor = ActorActual.DeUsuario(id, Roles.Cliente, null);
        var marcaDeLaHoja = Guid.NewGuid().ToString("N")[..12];
        Assert.True((await RegistrarHoja(anfitrion, ActorActual.Anonimo(), anterior, marcaDeLaHoja)).EsExito);
        var pedido = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().Solicitar(id, nuevo, S(Frase), marca, "PE"));
        Assert.True(pedido.EsExito, pedido.Error?.Clave);
        var codigo = Assert.Single(correo.Codigos, c => c.Destinatario == nuevo).Codigo.Revelar();

        interruptor.Caida = true;
        var rechazado = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().VerificarElCorreoNuevo(id, S(codigo)));

        Assert.False(rechazado.EsExito);
        Assert.Equal(CategoriaDeError.NoDisponible, rechazado.Error!.Categoria);
        Assert.Equal(anterior, await bd.Consultar<string>($"SELECT correo FROM identidad.usuarios WHERE id = '{id}'"));
        Assert.Equal(0L, await HojasVinculadas(marcaDeLaHoja));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'HojaDeReclamacionVinculada' AND objeto_id = '{id:D}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE accion = 'CorreoCambiado' AND objeto_id = '{id:D}'"));

        // Con la auditoría de vuelta, el mismo código (aún vigente) completa el cambio y la hoja queda vinculada.
        interruptor.Caida = false;
        var reintento = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().VerificarElCorreoNuevo(id, S(codigo)));
        Assert.True(reintento.EsExito, reintento.Error?.Clave);
        Assert.Equal(nuevo, await bd.Consultar<string>($"SELECT correo FROM identidad.usuarios WHERE id = '{id}'"));
        Assert.Equal(1L, await HojasVinculadas(marcaDeLaHoja));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-71")]
    public async Task Al_reemplazar_una_cuenta_sin_verificar_sus_hojas_quedan_sin_cuenta_y_siguen_existiendo()
    {
        var reloj = new RelojMovil(Inicio);
        var correo = new CapturadorDeCorreo();
        await using var anfitrion = await Anfitrion(reloj, correo, new InterruptorDeAuditoria());
        var compartido = $"c{Guid.NewGuid():N}@example.test";
        var sinVerificar = await SembrarCliente(anfitrion, compartido, verificado: false);
        var marcaDeLaHoja = Guid.NewGuid().ToString("N")[..12];
        var registro = await RegistrarHoja(anfitrion, ActorActual.DeUsuario(sinVerificar, Roles.Cliente, null), compartido, marcaDeLaHoja);
        Assert.True(registro.EsExito, registro.Error?.Clave);
        Assert.Equal(sinVerificar, await bd.Consultar<Guid>($"SELECT cuenta_id FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marcaDeLaHoja})%'"));

        var versiones = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IVersionesDeTextosVigentes>().Obtener());
        var alta = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IRegistroDeCuentas>().Registrar(
            new SolicitudDeAltaDeCuenta("Cliente Nuevo", compartido, S(Frase), versiones.Terminos, versiones.Privacidad, true)));


        Assert.True(alta.EsExito, alta.Error?.Clave);
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM identidad.usuarios WHERE id = '{sinVerificar}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM identidad.usuarios WHERE correo = '{compartido}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marcaDeLaHoja})%'"));
        Assert.Equal(0L, await HojasVinculadas(marcaDeLaHoja));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-71")]
    public async Task El_host_real_resuelve_un_unico_receptor_de_cambios_de_cuenta_el_de_reclamaciones()
    {
        await using var anfitrion = await Anfitrion(new RelojMovil(Inicio), new CapturadorDeCorreo(), new InterruptorDeAuditoria());

        var modulos = await anfitrion.Como(ActorActual.DelSistema(), sp => Task.FromResult(sp.GetServices<IReceptorDeCambiosDeCuenta>().Select(r => r.Modulo).ToArray()));

        Assert.Equal(["reclamaciones"], modulos);
    }
}
