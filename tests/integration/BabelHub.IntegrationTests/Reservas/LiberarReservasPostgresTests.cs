using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T16: <c>LiberarReservas</c> con PostgreSQL real (SEL-07, SEL-17, SEL-32, D15).</summary>
public sealed class LiberarReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class ReceptorDePrueba : IAlLiberarPedido
    {
        private readonly object _candado = new();

        public List<(Guid Pedido, MotivoDeLiberacionDeReserva Motivo, Guid[] Traslados)> Recibidos { get; } = [];

        public Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default)
        {
            lock (_candado)
            {
                Recibidos.Add((pedidoId, motivo, [.. trasladoIds]));
            }

            return Task.CompletedTask;
        }
    }

    private static Task<Resultado<ReservaDelPedido>> Liberar(PilaDeReservas pila, Guid pedido, MotivoDeLiberacionDeReserva motivo, params Guid[] lineas) =>
        pila.Ejecutar<LiberarReservasEntrada, ReservaDelPedido>(Servicio, new LiberarReservasEntrada(pedido, motivo, lineas.Length == 0 ? null : lineas));

    private static Task<Resultado<ReservaDelPedido>> Confirmar(PilaDeReservas pila, Guid pedido) =>
        pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(pedido, Guid.NewGuid().ToString("N")));

    private async Task<(Guid Lima, Guid Libro)> Preparar(int stock = 10)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, stock, 0);
        return (lima, libro);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task CheckoutCancelado_LiberaLaPendienteYDevuelveElVendible_SinTocarElFisico_YRepetirNoLoCambia()
    {
        var (lima, libro) = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 3)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);

        var primera = await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.CheckoutCancelado);
        var repetida = await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.CheckoutCancelado);

        Assert.True(primera.EsExito, primera.Error?.Clave);
        Assert.True(repetida.Valor.EsRepeticion);
        Assert.Equal((10, 0), await bd.StockDe(lima, libro));
        Assert.Equal("liberada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        var auditada = Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaLiberada");
        Assert.Equal(("checkout-cancelado", "3"), (auditada.Detalle["causa"], auditada.Detalle["cantidad"]));
        Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-17")]
    public async Task PedidoAnulado_LiberaLaConfirmada_ConAvisoALosReceptoresAntesDeTocarNada()
    {
        var (lima, libro) = await Preparar();
        var receptor = new ReceptorDePrueba();
        using var pila = new PilaDeReservas(bd, s => s.AddSingleton<IAlLiberarPedido>(receptor));
        var entrada = ARecojo("lima", [Linea(libro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);

        var resultado = await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((10, 0), await bd.StockDe(lima, libro));
        var recibido = Assert.Single(receptor.Recibidos);
        Assert.Equal((entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado), (recibido.Pedido, recibido.Motivo));
        Assert.Equal(await bd.ReservadoDeLaRed(libro), await bd.UnidadesDeLineasActivas(libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-17")]
    public async Task CheckoutCanceladoSobreUnPedidoConfirmado_SeRechazaSinCancelarTrasladosNiTocarNada()
    {
        var (lima, libro) = await Preparar();
        var receptor = new ReceptorDePrueba();
        using var pila = new PilaDeReservas(bd, s => s.AddSingleton<IAlLiberarPedido>(receptor));
        var entrada = ARecojo("lima", [Linea(libro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);

        var resultado = await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.CheckoutCancelado);

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Empty(receptor.Recibidos);
        Assert.Equal((10, 2), await bd.StockDe(lima, libro));
        Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task DiezLiberacionesSimultaneas_DevuelvenLasUnidadesUnaSolaVez()
    {
        var (lima, libro) = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 4)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        var compuerta = new TaskCompletionSource();
        var tareas = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await compuerta.Task;
            return await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.CheckoutCancelado);
        })).ToList();
        compuerta.SetResult();

        var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(1, resultados.Count(r => !r.Valor.EsRepeticion));
        Assert.Equal((10, 0), await bd.StockDe(lima, libro));
        Assert.Equal(1, pila.Auditoria.Contar("ReservaLiberada"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task LoLiberadoVuelveAEstarDisponibleParaOtrosClientes()
    {
        var (lima, libro) = await Preparar(stock: 3);
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 3)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.Equal("stock-insuficiente", (await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 1)]))).Error!.Clave);

        Assert.True((await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.CheckoutCancelado)).EsExito);

        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro, 3)]))).EsExito);
        Assert.Equal((3, 3), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task SiLaAuditoriaFalla_LaLiberacionSeRevierteEntera()
    {
        var (lima, libro) = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        pila.Auditoria.Falla = true;

        await Assert.ThrowsAsync<BabelHub.Inventario.Application.Auditoria.AuditoriaNoDisponibleException>(() => Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.CheckoutCancelado));

        Assert.Equal((10, 2), await bd.StockDe(lima, libro));
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public async Task LiberarSoloUnaLineaDeLaReserva_DejaLaOtraIntacta()
    {
        var (lima, a) = await Preparar();
        var b = await bd.AgregarEdicion();
        await bd.PonerStock(lima, b, 10, 0);
        using var pila = new PilaDeReservas(bd);
        var la = Linea(a, 2);
        var lb = Linea(b, 3);
        var entrada = ARecojo("lima", [la, lb]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);

        var resultado = await Liberar(pila, entrada.PedidoId, MotivoDeLiberacionDeReserva.CheckoutCancelado, la.LineaId);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((10, 0), await bd.StockDe(lima, a));
        Assert.Equal((10, 3), await bd.StockDe(lima, b));
        Assert.Equal(["liberada", "pendiente"], (await bd.Consultar<string>($"SELECT string_agg(estado, ',' ORDER BY estado) FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"))!.Split(','));
    }
}
