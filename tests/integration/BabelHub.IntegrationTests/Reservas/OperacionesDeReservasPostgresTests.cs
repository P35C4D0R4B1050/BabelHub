using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Shared.Contratos;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>Las pruebas que cierran los huecos de la revisión de INV-2b con PostgreSQL real: el orden líneas → filas con ediciones cruzadas, el envío de <c>ya-cerrada</c> y el no recogido con stock real.</summary>
public sealed class OperacionesDeReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<long> Interbloqueos() => await bd.Consultar<long>("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()");

    private static Task<Resultado<ReservaDelPedido>> Confirmar(PilaDeReservas pila, Guid pedido) =>
        pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(pedido, Guid.NewGuid().ToString("N")));

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task LiberarYCerrarConEdicionesCruzadasEnOrdenContrario_NoSeInterbloquean_YTodoQuedaCoherente()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var a = await bd.AgregarEdicion();
        var b = await bd.AgregarEdicion();
        await bd.PonerStock(lima, a, 1000, 0);
        await bd.PonerStock(lima, b, 1000, 0);
        using var pila = new PilaDeReservas(bd);
        var pedidos = new List<ReservarEntrada>();
        for (var i = 0; i < 16; i++)
        {
            var entrada = ARecojo("lima", i % 2 == 0 ? [Linea(a, 2), Linea(b, 3)] : [Linea(b, 2), Linea(a, 3)]);
            Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
            Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);
            pedidos.Add(entrada);
        }

        var antes = await Interbloqueos();
        var compuerta = new TaskCompletionSource();
        var tareas = pedidos.Select((entrada, i) => Task.Run(async () =>
        {
            await compuerta.Task;
            if (i % 2 == 0)
            {
                return (await pila.Ejecutar<LiberarReservasEntrada, ReservaDelPedido>(Servicio, new LiberarReservasEntrada(entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado, null))).EsExito;
            }

            var encargado = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, lima);
            return (await pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(encargado, new CerrarReservasEntrada(entrada.PedidoId, MotivoDeCierreDeReserva.Entrega, [.. entrada.Lineas.Select(l => l.LineaId)], null, Guid.NewGuid().ToString("N")))).EsExito;
        })).ToList();
        compuerta.SetResult();

        var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromSeconds(120));

        Assert.All(resultados, ok => Assert.True(ok));
        Assert.Equal(8L, await bd.Consultar<long>("SELECT count(DISTINCT pedido_id) FROM reservas.reserva_lineas WHERE estado = 'liberada'"));
        Assert.Equal(8L, await bd.Consultar<long>("SELECT count(DISTINCT pedido_id) FROM reservas.reserva_lineas WHERE estado = 'cerrada'"));
        Assert.Equal(await bd.ReservadoDeLaRed(a), await bd.UnidadesDeLineasActivas(a));
        Assert.Equal(await bd.ReservadoDeLaRed(b), await bd.UnidadesDeLineasActivas(b));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0, await Interbloqueos() - antes);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-22")]
    public async Task UnPedidoNoRecogido_LiberaLaConfirmada_ElFisicoSigueEnNueveYElVendibleSubeEnUno()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 9, 0);
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);
        Assert.Equal((9, 1), await bd.StockDe(lima, libro));

        var resultado = await pila.Ejecutar<LiberarReservasEntrada, ReservaDelPedido>(Servicio, new LiberarReservasEntrada(entrada.PedidoId, MotivoDeLiberacionDeReserva.NoRecogido, null));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((9, 0), await bd.StockDe(lima, libro));
        Assert.Equal("liberada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Equal("no-recogido", Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaLiberada").Detalle["causa"]);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-39")]
    public async Task EnElReparto_YaCerradaExigeElMismoEnvio_UnoDistintoEsEstadoInvalido()
    {
        var cusco = await bd.AgregarFuente("cusco");
        await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 9, 0);
        using var pila = new PilaDeReservas(bd);
        var entrada = ADomicilio("cusco", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);
        var logistica = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica);
        var envio = Guid.NewGuid();
        var linea = entrada.Lineas[0].LineaId;

        Task<Resultado<CierreDeReserva>> Cerrar(Guid envioId) =>
            pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(logistica, new CerrarReservasEntrada(entrada.PedidoId, MotivoDeCierreDeReserva.Reparto, [linea], envioId, Guid.NewGuid().ToString("N")));

        Assert.True((await Cerrar(envio)).EsExito);
        var mismo = await Cerrar(envio);
        var otro = await Cerrar(Guid.NewGuid());

        Assert.Equal([true], mismo.Valor.Lineas.Select(l => l.YaCerrada));
        Assert.Equal("reserva-estado-invalido", otro.Error!.Clave);
        Assert.Equal((8, 0), await bd.StockDe(cusco, libro));
        Assert.Equal(envio, await bd.Consultar<Guid>($"SELECT envio_id FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }
}
