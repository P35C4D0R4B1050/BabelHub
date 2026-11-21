using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.IntegrationTests.Pedidos;
using Roles = BabelHub.Shared.Contratos.RolesDeUsuario;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Nombre y correo de todo rol con el host real del E-commerce sobre PostgreSQL (DD-25; AC-IDE-67, AC-IDE-68, AC-IDE-69, AC-AUD-33): cada rol corrige su nombre y deja un registro de auditoría aislado de Identidad
/// sin ningún nombre en el detalle, y el personal cambia su correo con la verificación del nuevo (el anterior sigue vigente hasta entonces). <b>Escritas sin ejecutar</b> mientras Docker estaba ocupado por otra
/// corrida: se ejecutan con Docker antes de pasar la spec a Done (T57 de <c>identidad-seguridad</c>).
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class CuentaDelPersonalConElHostRealTests(PostgresDelEcommerceCompletoFixture bd)
{
    private const string Frase = "una frase larga y segura para el personal";

    private static readonly DateTimeOffset Inicio = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private sealed class RelojMovil(DateTimeOffset inicio) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private static ValorSecreto S(string valor) => new(valor);

    private async Task<Guid> Sembrar(AnfitrionRealDelEcommerce anfitrion, string correo, string rolEnBd)
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
              VALUES ('{id}', 'Persona Original', '{correo}', true, '{rolEnBd}', true, '{resumen}', DATE '2026-10-01');
            """);
        return id;
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-67")]
    [Trait("Criterio", "AC-AUD-33")]
    [InlineData("cliente", Roles.Cliente)]
    [InlineData("personal-de-logistica", Roles.PersonalDeLogistica)]
    [InlineData("administrador-central", Roles.AdministradorCentral)]
    public async Task Cada_rol_corrige_su_nombre_y_deja_un_registro_aislado_sin_el_nombre_en_el_detalle(string rolEnBd, string rolDelActor)
    {
        await using var anfitrion = bd.Anfitrion(s => s.AddSingleton<IReloj>(new RelojMovil(Inicio)));
        var id = await Sembrar(anfitrion, $"n{Guid.NewGuid():N}@example.test", rolEnBd);
        var actor = ActorActual.DeUsuario(id, rolDelActor, null);

        var cambiado = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICuentaDelCliente>().CorregirElNombre(id, "Nombre Corregido"));
        var igual = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICuentaDelCliente>().CorregirElNombre(id, "Nombre Corregido"));

        Assert.True(cambiado.EsExito, cambiado.Error?.Clave);
        Assert.True(igual.EsExito, igual.Error?.Clave);
        Assert.Equal("Nombre Corregido", await bd.Consultar<string>($"SELECT nombre FROM identidad.usuarios WHERE id = '{id}'"));
        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE accion = 'UsuarioActualizado' AND objeto_id = '{id}'"));
        Assert.Equal("usuario", await bd.Consultar<string>($"SELECT objeto_tipo FROM identidad.registros WHERE accion = 'UsuarioActualizado' AND objeto_id = '{id}'"));
        Assert.Equal("{}", await bd.Consultar<string>($"SELECT detalle::text FROM identidad.registros WHERE accion = 'UsuarioActualizado' AND objeto_id = '{id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-67")]
    public async Task La_sexta_correccion_de_la_hora_se_rechaza_con_el_limite_y_no_deja_registro()
    {
        await using var anfitrion = bd.Anfitrion(s => s.AddSingleton<IReloj>(new RelojMovil(Inicio)));
        var id = await Sembrar(anfitrion, $"l{Guid.NewGuid():N}@example.test", "cliente");
        var actor = ActorActual.DeUsuario(id, Roles.Cliente, null);

        for (var i = 1; i <= 5; i++)
        {
            var n = i;
            Assert.True((await anfitrion.Como(actor, sp => sp.GetRequiredService<ICuentaDelCliente>().CorregirElNombre(id, $"Nombre {n}"))).EsExito);
        }

        var sexta = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICuentaDelCliente>().CorregirElNombre(id, "Nombre 6"));

        Assert.False(sexta.EsExito);
        Assert.Equal("Nombre 5", await bd.Consultar<string>($"SELECT nombre FROM identidad.usuarios WHERE id = '{id}'"));
        Assert.Equal(5, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE accion = 'UsuarioActualizado' AND objeto_id = '{id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-67")]
    public async Task Un_actor_distinto_del_titular_recibe_un_acceso_denegado_y_el_nombre_no_cambia()
    {
        await using var anfitrion = bd.Anfitrion(s => s.AddSingleton<IReloj>(new RelojMovil(Inicio)));
        var objetivo = await Sembrar(anfitrion, $"o{Guid.NewGuid():N}@example.test", "personal-de-logistica");
        var administrador = await Sembrar(anfitrion, $"a{Guid.NewGuid():N}@example.test", "administrador-central");
        var actor = ActorActual.DeUsuario(administrador, Roles.AdministradorCentral, null);

        var resultado = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICuentaDelCliente>().CorregirElNombre(objetivo, "Nombre ajeno"));

        Assert.False(resultado.EsExito);
        Assert.Equal("Persona Original", await bd.Consultar<string>($"SELECT nombre FROM identidad.usuarios WHERE id = '{objetivo}'"));
    }

    private sealed class CapturadorDeCorreo : IEnviadorDeCorreo
    {
        public List<CorreoDeCodigo> Codigos { get; } = [];

        public List<CorreoSaliente> Correos { get; } = [];

        public Task<ResultadoDeEnvio> Enviar(CorreoSaliente correo, CancellationToken cancelacion = default)
        {
            lock (Correos)
            {
                Correos.Add(correo);
            }

            return Task.FromResult(ResultadoDeEnvio.Enviado);
        }

        public Task<ResultadoDeEnvio> EnviarCodigo(CorreoDeCodigo correo, CancellationToken cancelacion = default)
        {
            lock (Codigos)
            {
                Codigos.Add(correo);
            }

            return Task.FromResult(ResultadoDeEnvio.Enviado);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-68")]
    [Trait("Criterio", "AC-IDE-69")]
    public async Task El_personal_cambia_su_correo_el_anterior_sigue_vigente_hasta_verificar_y_al_aplicarlo_se_audita_y_se_avisa_a_los_dos()
    {
        var reloj = new RelojMovil(Inicio);
        var correo = new CapturadorDeCorreo();
        await using var anfitrion = bd.Anfitrion(s =>
        {
            s.AddSingleton<IReloj>(reloj);
            s.AddSingleton<IEnviadorDeCorreo>(correo);
        });
        var anterior = $"p{Guid.NewGuid():N}@example.test";
        var nuevo = $"nuevo-p{Guid.NewGuid():N}@example.test";
        var id = await Sembrar(anfitrion, anterior, "personal-de-logistica");
        var actor = ActorActual.DeUsuario(id, Roles.PersonalDeLogistica, null);
        var marca = (await anfitrion.Como(actor, sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().RegistrarElIngresoCompleto(id, null, "PE"))).Valor.MarcaNueva!.Revelar();
        reloj.Ahora = Inicio.AddHours(25);

        var pedido = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().Solicitar(id, nuevo, S(Frase), S(marca), "PE"));

        Assert.True(pedido.EsExito, pedido.Error?.Clave);
        Assert.Equal(anterior, await bd.Consultar<string>($"SELECT correo FROM identidad.usuarios WHERE id = '{id}'"));
        Assert.Equal("esperando-verificacion", await bd.Consultar<string>($"SELECT estado FROM identidad.cambios_de_correo_pendientes WHERE usuario_id = '{id}'"));
        Assert.Equal(0, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE accion = 'CorreoCambiado' AND objeto_id = '{id}'"));
        var codigo = Assert.Single(correo.Codigos, c => c.Destinatario == nuevo).Codigo.Revelar();

        var verificado = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().VerificarElCorreoNuevo(id, S(codigo)));

        Assert.True(verificado.EsExito, verificado.Error?.Clave);
        Assert.Equal(nuevo, await bd.Consultar<string>($"SELECT correo FROM identidad.usuarios WHERE id = '{id}'"));
        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM identidad.registros WHERE accion = 'CorreoCambiado' AND objeto_id = '{id}'"));
        Assert.Equal("{}", await bd.Consultar<string>($"SELECT detalle::text FROM identidad.registros WHERE accion = 'CorreoCambiado' AND objeto_id = '{id}'"));
        Assert.Contains(correo.Correos, c => c.Destinatario == nuevo && c.Plantilla == "correo-cambiado");
        Assert.Contains(correo.Correos, c => c.Destinatario == anterior && c.Plantilla == "correo-anterior-cambiado" && c.Categoria == CategoriaDeCorreo.AlertaCriticaDeCuenta);
        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM identidad.avisos_de_cuenta WHERE usuario_id = '{id}' AND tipo = 'correo-cambiado'"));
    }
}
