using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T20: <c>IReservasParaTraslados</c> con PostgreSQL real, el rol real de la aplicación y la política de filas (SEL-11, SEL-28, SEL-29).</summary>
public sealed class ReservasParaTrasladosPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>Una pieza que se parece a <c>Traslados</c>: usa <c>IReservasParaTraslados</c> junto a las piezas de stock de <c>Fuentes</c>. Sirve para comprobar que el contenedor resuelve sin ciclos.</summary>
    private sealed class TrasladosDeEjemplo(IReservasParaTraslados reservas, IMovimientosDeStock movimientos, IAplicadorDeMovimientos aplicador, IExistencias existencias, IReservasDeStock reservasDeStock)
    {
        public IReservasParaTraslados Reservas => reservas;

        public int Piezas => new object[] { movimientos, aplicador, existencias, reservasDeStock }.Length;
    }

    private sealed class ReceptorSinEfecto : IAlLiberarPedido
    {
        public Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed record Escenario(Guid Lima, Guid Cusco, Guid Libro, ReservarEntrada Entrada, ActorActual EncargadoDeCusco, ActorActual EncargadoDeLima);

    /// <summary>Una reserva de recojo en Lima confirmada con la línea en Cusco (físico 9, reservado 1) y el traslado vinculado.</summary>
    private async Task<(PilaDeReservas Pila, Escenario Datos, Guid Traslado)> Preparar()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 0, 0);
        await bd.PonerStock(cusco, libro, 9, 0);
        await bd.DefinirRuta(cusco, lima, 2);
        var traslados = new TrasladosDeIntegracionDoble(bd);
        var pila = new PilaDeReservas(bd, s =>
        {
            s.AddScoped<IGeneradorDeTrasladosDePedido>(_ => traslados);
            s.AddSingleton<IAlLiberarPedido>(new ReceptorSinEfecto());
        });
        var entrada = ARecojo("lima", [Linea(libro, 1)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        var confirmada = await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")));
        Assert.True(confirmada.EsExito, confirmada.Error?.Clave);
        var datos = new Escenario(lima, cusco, libro, entrada, ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, cusco), ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, lima));
        return (pila, datos, confirmada.Valor.Traslados.Single().TrasladoId);
    }

    private static Task<ResultadoDeMovimientoDeReserva> EnUnaTransaccion(PilaDeReservas pila, ActorActual actor, Func<IReservasParaTraslados, Task<ResultadoDeMovimientoDeReserva>> operacion) =>
        pila.EnUnAmbito(actor, sp => sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(async token =>
        {
            var resultado = await operacion(sp.GetRequiredService<IReservasParaTraslados>());

            // Quien llama traduce un rechazo a un fallo y la transacción se revierte: aquí se imita lanzando.
            return resultado == ResultadoDeMovimientoDeReserva.Aplicado ? resultado : throw new ResultadoRechazado(resultado);
        })).ContinueWith(t => t.IsFaulted && t.Exception!.GetBaseException() is ResultadoRechazado r ? r.Resultado : t.GetAwaiter().GetResult());

    private sealed class ResultadoRechazado(ResultadoDeMovimientoDeReserva resultado) : Exception
    {
        public ResultadoDeMovimientoDeReserva Resultado { get; } = resultado;
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task ElTrasladoCompleto_ConsumeEnCuscoYRecreaEnLima_ElEncargadoDeCadaSucursalEscribeLaSuya()
    {
        var (pila, d, traslado) = await Preparar();
        using (pila)
        {
            var consumo = await EnUnaTransaccion(pila, d.EncargadoDeCusco, r => r.ConsumirEnElOrigen(d.Entrada.PedidoId, d.Cusco, d.Libro));
            Assert.Equal(ResultadoDeMovimientoDeReserva.Aplicado, consumo);
            Assert.Equal((8, 0), await bd.StockDe(d.Cusco, d.Libro));

            var recreacion = await EnUnaTransaccion(pila, d.EncargadoDeLima, r => r.RecrearEnElDestino(d.Entrada.PedidoId, d.Lima, d.Libro, 1, traslado));
            Assert.Equal(ResultadoDeMovimientoDeReserva.Aplicado, recreacion);

            Assert.Equal((1, 1), await bd.StockDe(d.Lima, d.Libro));
            Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{d.Entrada.PedidoId}'"));
            Assert.Equal(await bd.ReservadoDeLaRed(d.Libro), await bd.UnidadesDeLineasActivas(d.Libro));

            // La entrega en la sucursal de recojo descuenta el físico allí (AC-SEL-11).
            var entrega = await pila.Ejecutar<CerrarReservasEntrada, CierreDeReserva>(d.EncargadoDeLima, new CerrarReservasEntrada(d.Entrada.PedidoId, MotivoDeCierreDeReserva.Entrega, [d.Entrada.Lineas[0].LineaId], null, Guid.NewGuid().ToString("N")));
            Assert.True(entrega.EsExito, entrega.Error?.Clave);
            Assert.Equal((0, 0), await bd.StockDe(d.Lima, d.Libro));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task ElEncargadoDeOtraSucursalNoEscribeLaFila_DenegadoPorAlcance_YLaTransaccionLoDeshace()
    {
        var (pila, d, _) = await Preparar();
        using (pila)
        {
            var resultado = await EnUnaTransaccion(pila, d.EncargadoDeLima, r => r.ConsumirEnElOrigen(d.Entrada.PedidoId, d.Cusco, d.Libro));

            Assert.Equal(ResultadoDeMovimientoDeReserva.DeniegadoPorAlcance, resultado);
            Assert.Equal((9, 1), await bd.StockDe(d.Cusco, d.Libro));
            Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{d.Entrada.PedidoId}'"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task LaAnulacionYElConsumoALaVez_AplicanSoloLaPrimera()
    {
        var (pila, d, _) = await Preparar();
        using (pila)
        {
            var compuerta = new TaskCompletionSource();
            var consumo = Task.Run(async () =>
            {
                await compuerta.Task;
                return await EnUnaTransaccion(pila, d.EncargadoDeCusco, r => r.ConsumirEnElOrigen(d.Entrada.PedidoId, d.Cusco, d.Libro));
            });
            var anulacion = Task.Run(async () =>
            {
                await compuerta.Task;
                return (await pila.Ejecutar<LiberarReservasEntrada, ReservaDelPedido>(Servicio, new LiberarReservasEntrada(d.Entrada.PedidoId, MotivoDeLiberacionDeReserva.PedidoAnulado, null))).Error?.Clave;
            });
            compuerta.SetResult();
            await Task.WhenAll(consumo, anulacion).WaitAsync(TimeSpan.FromSeconds(60));

            // O el consumo gana (la línea queda cerrada por traslado y la anulación marca su venta anulada, con el receptor sin componer la anulación falla a la vista)
            // o la anulación gana (liberada) y el consumo es estado inválido: nunca ambas con el físico bajando.
            var estado = await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{d.Entrada.PedidoId}'");
            Assert.Contains(estado, new[] { "cerrada", "liberada", "confirmada" });
            Assert.Equal(await bd.ReservadoDeLaRed(d.Libro), await bd.UnidadesDeLineasActivas(d.Libro));
            var fisico = (await bd.StockDe(d.Cusco, d.Libro))!.Value.Fisico;
            Assert.Equal(estado == "cerrada" ? 8 : 9, fisico);
        }
    }
}
