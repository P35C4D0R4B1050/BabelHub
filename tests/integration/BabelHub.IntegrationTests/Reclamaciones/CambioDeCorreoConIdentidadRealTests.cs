using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.IntegrationTests.Pedidos;
using Microsoft.Extensions.DependencyInjection;
using Roles = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>
/// T35 de <c>ecommerce-libro-reclamaciones</c> (AC-REC-23, AC-REC-27) con <b>Identidad real</b> y el receptor real en el host compuesto: un cliente que cambia su correo vincula antes las hojas de su correo anterior (sin eso, el correo anterior,
/// al liberarse, las dejaría visibles para quien lo verifique después) y el cambio no se aplica si el receptor no puede vincular.
/// </summary>
[Collection(ColeccionDelEcommerceCompleto.Nombre)]
public sealed class CambioDeCorreoConIdentidadRealTests(PostgresDelEcommerceCompletoFixture bd)
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

    private static ValorSecreto S(string valor) => new(valor);

    private async Task<Guid> SembrarCliente(AnfitrionRealDelEcommerce anfitrion, string correo)
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
              VALUES ('{id}', 'Cliente Original', '{correo}', true, 'cliente', true, '{resumen}', DATE '2026-10-01');
            """);
        return id;
    }

    [Fact]
    [Trait("Criterio", "AC-REC-23")]
    public async Task Un_cliente_que_cambia_su_correo_con_Identidad_real_vincula_antes_las_hojas_de_su_correo_anterior()
    {
        var reloj = new RelojMovil(Inicio);
        var correo = new CapturadorDeCorreo();
        await using var anfitrion = bd.Anfitrion(s =>
        {
            s.AddSingleton<IReloj>(reloj);
            s.AddSingleton<IEnviadorDeCorreo>(correo);
        });
        var anterior = $"c{Guid.NewGuid():N}@example.test";
        var nuevo = $"nuevo-c{Guid.NewGuid():N}@example.test";
        var id = await SembrarCliente(anfitrion, anterior);
        var actor = ActorActual.DeUsuario(id, Roles.Cliente, null);
        var marca = (await anfitrion.Como(actor, sp => sp.GetRequiredService<IDispositivosDeLaCuenta>().RegistrarElIngresoCompleto(id, null, "PE"))).Valor.MarcaNueva!.Revelar();
        reloj.Ahora = Inicio.AddHours(25);

        // Una hoja de un visitante con el correo anterior (sin cuenta).
        var marcaDeLaHoja = Guid.NewGuid().ToString("N")[..12];
        var registro = await anfitrion.Como(ActorActual.Anonimo(), sp => sp.GetRequiredService<IReclamaciones>().Registrar(
            new RegistrarHojaEntrada("Ana Pérez Quispe", "12345678", "Av. Los Olivos 123, Ayacucho", anterior, "Libro", 49.90m, "reclamo", $"Llegó dañado ({marcaDeLaHoja}).", null, $"clave-del-formulario-{marcaDeLaHoja}", null)));
        Assert.True(registro.EsExito, registro.Error?.Clave);
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marcaDeLaHoja})%' AND cuenta_id IS NOT NULL"));

        var pedido = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().Solicitar(id, nuevo, S(Frase), S(marca), "PE"));
        Assert.True(pedido.EsExito, pedido.Error?.Clave);
        var codigo = Assert.Single(correo.Codigos, c => c.Destinatario == nuevo).Codigo.Revelar();

        var verificado = await anfitrion.Como(actor, sp => sp.GetRequiredService<ICambioDeCorreo>().VerificarElCorreoNuevo(id, S(codigo)));

        Assert.True(verificado.EsExito, verificado.Error?.Clave);
        Assert.Equal(nuevo, await bd.Consultar<string>($"SELECT correo FROM identidad.usuarios WHERE id = '{id}'"));
        Assert.Equal(id, await bd.Consultar<Guid>($"SELECT cuenta_id FROM reclamaciones.hojas_reclamacion WHERE detalle LIKE '%({marcaDeLaHoja})%'"));
    }
}
