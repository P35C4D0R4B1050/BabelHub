using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T21: <c>IOrigenDeUnidadesVendidas</c> con PostgreSQL real: la atribución, la ventana de 30 días y que el adaptador real reemplaza al nulo de <c>Fuentes</c> (FUE-21, FUE-22).</summary>
public sealed class OrigenDeUnidadesVendidasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class ReceptorSinEfecto : IAlLiberarPedido
    {
        public Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private static Task<IReadOnlyList<VentaAtribuida>> Ventas(PilaDeReservas pila, Guid? fuente) =>
        pila.EnUnAmbito(Servicio, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IOrigenDeUnidadesVendidas>().Ventas(fuente, pila.Reloj.Ahora.AddDays(-30), pila.Reloj.Ahora, token)));

    private static Task<Resultado<ReservaDelPedido>> Confirmar(PilaDeReservas pila, Guid pedido) =>
        pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(pedido, Guid.NewGuid().ToString("N")));

    [Fact]
    [Trait("Criterio", "AC-FUE-21")]
    public async Task ElAdaptadorRealReemplazaAlNuloDeFuentes()
    {
        using var pila = new PilaDeReservas(bd);
        using var ambito = pila.Proveedor.CreateScope();

        Assert.IsType<OrigenDeUnidadesVendidas>(ambito.ServiceProvider.GetRequiredService<IOrigenDeUnidadesVendidas>());
        await Task.CompletedTask;
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-21")]
    public async Task ElRecojoSeAtribuyeALaSucursal_ADomicilioALaFuente_ConLaVentanaYElFiltro()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 0, 0);
        await bd.PonerStock(cusco, libro, 20, 0);
        await bd.DefinirRuta(cusco, lima, 2);
        using var pila = new PilaDeReservas(bd, s => s.AddScoped<IGeneradorDeTrasladosDePedido>(_ => new TrasladosDeIntegracionDoble(bd)));
        var recojo = ARecojo("lima", [Linea(libro, 2)]);
        var domicilio = ADomicilio("cusco", [Linea(libro, 3)]);
        Assert.True((await pila.Reservar(Cliente(), recojo)).EsExito);
        Assert.True((await pila.Reservar(Cliente(), domicilio)).EsExito);
        Assert.True((await Confirmar(pila, recojo.PedidoId)).EsExito);
        Assert.True((await Confirmar(pila, domicilio.PedidoId)).EsExito);

        var red = await Ventas(pila, null);
        var deLima = await Ventas(pila, lima);
        var deCusco = await Ventas(pila, cusco);

        Assert.Equal(new[] { new VentaAtribuida(recojo.PedidoId, libro, lima, 2), new VentaAtribuida(domicilio.PedidoId, libro, cusco, 3) }.OrderBy(v => v.PedidoId), red.OrderBy(v => v.PedidoId));
        Assert.Equal([new VentaAtribuida(recojo.PedidoId, libro, lima, 2)], deLima);
        Assert.Equal([new VentaAtribuida(domicilio.PedidoId, libro, cusco, 3)], deCusco);

        // A los 31 días ya no entran.
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddDays(31);
        Assert.Empty(await Ventas(pila, null));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-22")]
    public async Task ElTrasladoDeReposicionNoEsUnaVenta_ElPedidoCuentaUnaSolaVezEnLaSucursalDeRecojo()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 0, 0);
        await bd.PonerStock(cusco, libro, 9, 0);
        await bd.DefinirRuta(cusco, lima, 2);
        var traslados = new TrasladosDeIntegracionDoble(bd);
        using var pila = new PilaDeReservas(bd, s =>
        {
            s.AddScoped<IGeneradorDeTrasladosDePedido>(_ => traslados);
            s.AddSingleton<IAlLiberarPedido>(new ReceptorSinEfecto());
        });
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        var confirmada = await Confirmar(pila, entrada.PedidoId);
        Assert.True(confirmada.EsExito, confirmada.Error?.Clave);
        var traslado = confirmada.Valor.Traslados.Single().TrasladoId;
        var encargadoDeCusco = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, cusco);
        var encargadoDeLima = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, lima);

        // En el origen y en tránsito: ya es una venta de Lima.
        Assert.Equal([new VentaAtribuida(entrada.PedidoId, libro, lima, 1)], await Ventas(pila, null));

        await pila.EnUnAmbito(encargadoDeCusco, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IReservasParaTraslados>().ConsumirEnElOrigen(entrada.PedidoId, cusco, libro, token)));
        Assert.Equal([new VentaAtribuida(entrada.PedidoId, libro, lima, 1)], await Ventas(pila, lima));

        // Ya recreada en el destino: sigue siendo una sola venta.
        await pila.EnUnAmbito(encargadoDeLima, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
            await sp.GetRequiredService<IReservasParaTraslados>().RecrearEnElDestino(entrada.PedidoId, lima, libro, 1, traslado, token)));
        Assert.Equal([new VentaAtribuida(entrada.PedidoId, libro, lima, 1)], await Ventas(pila, null));
        Assert.Empty(await Ventas(pila, cusco));
    }
}
