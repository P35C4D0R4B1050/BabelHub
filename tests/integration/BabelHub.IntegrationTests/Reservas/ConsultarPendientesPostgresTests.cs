using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T22: <c>ConsultarPendientes</c> con PostgreSQL real (SEL-33, AC-SEL-35): confirmada, pendiente vencida, sin reservas, en tránsito entre sucursales y anulada.</summary>
public sealed class ConsultarPendientesPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    private static readonly ActorActual ConCredencialDePendientes = ActorActual.DeIntegracion("consulta-de-pendientes");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class ReceptorSinEfecto : IAlLiberarPedido
    {
        public Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private static async Task<int> Pendientes(PilaDeReservas pila, Guid titular)
    {
        var resultado = await pila.Ejecutar<ConsultarPendientesEntrada, PendientesDeTitular>(ConCredencialDePendientes, new ConsultarPendientesEntrada(titular));
        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        return resultado.Valor.ReservasActivas;
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task UnaConfirmadaCuenta_UnaPendienteVencidaNo_UnClienteSinReservasTampoco_YSoloConSuCredencial()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 10, 0);
        using var pila = new PilaDeReservas(bd);
        var conConfirmada = Guid.NewGuid();
        var conVencida = Guid.NewGuid();
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(conConfirmada), entrada)).EsExito);
        Assert.True((await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")))).EsExito);
        Assert.True((await pila.Reservar(Cliente(conVencida), ARecojo("lima", [Linea(libro, 1)]))).EsExito);

        // La pendiente vence a los 15 minutos; nadie la barrió todavía y aun así no cuenta (SEL-08, SEL-27).
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);

        Assert.Equal(1, await Pendientes(pila, conConfirmada));
        Assert.Equal(0, await Pendientes(pila, conVencida));
        Assert.Equal(0, await Pendientes(pila, Guid.NewGuid()));

        foreach (var otro in new[] { Cliente(), Servicio, ActorActual.DeIntegracion("baja-de-titular") })
        {
            var rechazo = await pila.Ejecutar<ConsultarPendientesEntrada, PendientesDeTitular>(otro, new ConsultarPendientesEntrada(conConfirmada));
            Assert.Equal(CategoriaDeError.Prohibido, rechazo.Error!.Categoria);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task UnPedidoEnTransito_SigueContando_YElAnuladoYaNo()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 0, 0);
        await bd.PonerStock(cusco, libro, 9, 0);
        await bd.DefinirRuta(cusco, lima, 2);
        using var pila = new PilaDeReservas(bd, s =>
        {
            s.AddScoped<IGeneradorDeTrasladosDePedido>(_ => new TrasladosDeIntegracionDoble(bd));
            s.AddSingleton<IAlLiberarPedido>(new ReceptorSinEfecto());
        });
        var titular = Guid.NewGuid();
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(titular), entrada)).EsExito);
        Assert.True((await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")))).EsExito);
        var encargadoDeCusco = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, cusco);

        // Despachada desde Cusco: la línea del origen quedó cerrada por traslado y todavía no hay otra en el destino.
        await pila.EnUnAmbito(encargadoDeCusco, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IReservasParaTraslados>().ConsumirEnElOrigen(entrada.PedidoId, cusco, libro, token)));
        Assert.Equal("cerrada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Equal(1, await Pendientes(pila, titular));

        // La anulación marca la venta de la línea en tránsito y el pedido deja de contar.
        var anulada = await pila.Ejecutar<LiberarReservasEntrada, ReservaDelPedido>(Servicio, new LiberarReservasEntrada(entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado, null));
        Assert.True(anulada.EsExito, anulada.Error?.Clave);
        Assert.Equal(0, await Pendientes(pila, titular));
    }
}
