using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;
using static BabelHub.IntegrationTests.Traslados.EscenarioDeTraslados;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// T16: el módulo <c>Traslados</c> con PostgreSQL real y <c>Reservas</c> real, de la confirmación del pago a la recepción: los traslados que genera la confirmación (uno por fuente y edición, ninguno si el pedido está pendiente
/// o vence), la estimación fijada con la ruta de ese momento, el despacho que consume la reserva del origen, la recepción que la recrea en el destino con el vendible sin cambio, y la anulación del pedido en cada fase.
/// </summary>
public sealed class TrasladosConReservasRealesTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task AlConfirmarElPago_SeCreaUnTrasladoPorFuenteYEdicion_ConLaEstimacionDeLaRutaDeEseMomento()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 5, stockDeAyacucho: 0);
        var otroLibro = await bd.AgregarEdicion();
        await bd.PonerStock(escenario.Cusco, otroLibro, 4, 0);
        await bd.PonerStock(escenario.Ayacucho, otroLibro, 0, 0);
        await bd.PonerStock(escenario.Lima, otroLibro, 0, 0);
        var entrada = ARecojo("lima", [Linea(escenario.Libro, 2), Linea(otroLibro, 1)]);
        Assert.True((await escenario.Pila.Reservar(Cliente(), entrada)).EsExito);

        var confirmada = await escenario.Pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")));
        await bd.DefinirRuta(escenario.Cusco, escenario.Lima, 9);
        var repetida = await escenario.Pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")));

        Assert.True(confirmada.EsExito, confirmada.Error?.Clave);
        Assert.Equal(2, confirmada.Valor.Traslados.Count);
        Assert.All(confirmada.Valor.Traslados, t => Assert.Equal(("cusco", "lima", "solicitado"), (t.CodigoDeOrigen, t.CodigoDeDestino, t.Estado)));
        var esperada = escenario.Pila.Reloj.HoyEnLima.AddDays(2);
        Assert.All(confirmada.Valor.Traslados, t => Assert.Equal(esperada, t.FechaEstimadaDeLlegada));
        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.traslados WHERE fecha_estimada = '{esperada:yyyy-MM-dd}'"));
        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.traslados WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.True(repetida.EsExito);
        Assert.Equal(2L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.traslados WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Equal((2L, 2L), (await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE tipo = 'aviso' AND sucursal_codigo = 'cusco'"), await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE tipo = 'aviso' AND sucursal_codigo = 'lima'")));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task UnaRutaSinDefinirDejaLaEstimacionPorConfirmar()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 5);
        await bd.Ejecutar("DELETE FROM fuentes.rutas");
        var (_, reserva, traslado) = await escenario.PedidoConTraslado();

        Assert.Null(reserva.Traslados.Single().FechaEstimadaDeLlegada);
        Assert.True(await bd.Consultar<bool>($"SELECT estimada_por_confirmar FROM traslados.traslados WHERE id = '{traslado}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task UnPedidoPendienteOQueVenceNoGeneraTraslados()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 5);
        var entrada = ARecojo("lima", [Linea(escenario.Libro, 1)]);
        Assert.True((await escenario.Pila.Reservar(Cliente(), entrada)).EsExito);

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));

        escenario.Pila.Reloj.Ahora = escenario.Pila.Reloj.Ahora.AddMinutes(40);
        var vencida = await escenario.Pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")));

        Assert.False(vencida.EsExito);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-12")]
    public async Task ElDespachoDeUnVinculadoConsumeLaReservaDelOrigen_ElOrigenQuedaEnFisico0YReservado0()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 1);
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        Assert.Equal((1, 1), await bd.StockDe(escenario.Cusco, escenario.Libro));

        var despachado = await escenario.Ejecutar(Encargado(escenario.Cusco), new DespacharTrasladoEntrada(traslado));

        Assert.True(despachado.EsExito, despachado.Error?.Clave);
        Assert.Equal("en-transito", despachado.Valor.Estado);
        Assert.Equal((0, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
        Assert.Equal("cerrada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND fuente_id = '{escenario.Cusco}'"));
        Assert.Equal(await bd.ReservadoDeLaRed(escenario.Libro), await bd.UnidadesDeLineasActivas(escenario.Libro));
        Assert.Equal(3L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.mensajes_salientes WHERE clave_de_evento = 'traslado:{traslado:D}:en-transito'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-13")]
    public async Task LaRecepcionRecreaLaReservaEnElDestino_ElFisicoYLoReservadoSuben_ElVendibleNoCambia()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeLima: 0, stockDeCusco: 1);
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));
        var vendibleAntes = (await bd.StockDe(escenario.Lima, escenario.Libro))!.Value;

        var recibido = await escenario.Ejecutar(Encargado(escenario.Lima), new RecibirTrasladoEntrada(traslado));

        Assert.True(recibido.EsExito, recibido.Error?.Clave);
        var (fisico, reservado) = (await bd.StockDe(escenario.Lima, escenario.Libro))!.Value;
        Assert.Equal((1, 1), (fisico, reservado));
        Assert.Equal(vendibleAntes.Fisico - vendibleAntes.Reservado, fisico - reservado);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND fuente_id = '{escenario.Lima}' AND estado = 'confirmada' AND creada_por_traslado"));
        Assert.Equal(await bd.ReservadoDeLaRed(escenario.Libro), await bd.UnidadesDeLineasActivas(escenario.Libro));
        Assert.Equal(3L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.mensajes_salientes WHERE clave_de_evento = 'traslado:{traslado:D}:recibido'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.mensajes_salientes WHERE tipo = 'estado-de-pedido' AND clave_de_evento = 'traslado:{traslado:D}:recibido' AND sucursal_codigo = 'lima'"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public async Task AnularElPedidoConElTrasladoAprobado_LoCancelaConElMotivoPedidoAnulado_YYaNoSePuedeDespachar()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 1);
        var (pedido, traslado) = await escenario.VinculadoAprobado();

        var anulado = await escenario.AnularElPedido(pedido);
        var despacho = await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));

        Assert.True(anulado.EsExito, anulado.Error?.Clave);
        Assert.Equal(("cancelado", "pedido-anulado"), (await escenario.EstadoDe(traslado), await bd.Consultar<string>($"SELECT motivo_de_cierre FROM traslados.traslados WHERE id = '{traslado}'")));
        Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, despacho.Error!.Clave);
        Assert.Equal((1, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
        Assert.Equal(3L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.mensajes_salientes WHERE clave_de_evento = 'traslado:{traslado:D}:cancelado'"));
        Assert.Equal(1, escenario.Pila.Auditoria.Contar("TrasladoCancelado"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-18")]
    [Trait("Criterio", "AC-TRA-16")]
    public async Task AnularElPedidoConElTrasladoEnTransito_LoMarca_ElPedidoSeAnulaSinFallar_YAlRecibirseLasUnidadesQuedanLibres()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeLima: 0, stockDeCusco: 1);
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));
        Assert.Equal((0, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));

        var anulado = await escenario.AnularElPedido(pedido);

        Assert.True(anulado.EsExito, anulado.Error?.Clave);
        Assert.Equal(("en-transito", true), (await escenario.EstadoDe(traslado), await escenario.PedidoAnuladoDe(traslado)));

        var recibido = await escenario.Ejecutar(Encargado(escenario.Lima), new RecibirTrasladoEntrada(traslado));

        Assert.True(recibido.EsExito, recibido.Error?.Clave);
        Assert.Equal((1, 0), await bd.StockDe(escenario.Lima, escenario.Libro));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND fuente_id = '{escenario.Lima}' AND estado = 'confirmada'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM traslados.mensajes_salientes WHERE tipo = 'estado-de-pedido' AND clave_de_evento = 'traslado:{traslado:D}:recibido'"));
        Assert.Equal(await bd.ReservadoDeLaRed(escenario.Libro), await bd.UnidadesDeLineasActivas(escenario.Libro));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-13")]
    [Trait("Criterio", "AC-TRA-16")]
    public async Task AnularElPedidoDespuesDeRecibirSusUnidades_LiberaLaLineaConfirmadaDelDestino_ElTrasladoSigueRecibido_YElStockQuedaLibre()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeLima: 0, stockDeCusco: 1);
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        Assert.True((await escenario.Ejecutar(Encargado(escenario.Cusco), new DespacharTrasladoEntrada(traslado))).EsExito);
        Assert.True((await escenario.Ejecutar(Encargado(escenario.Lima), new RecibirTrasladoEntrada(traslado))).EsExito);
        Assert.Equal((1, 1), await bd.StockDe(escenario.Lima, escenario.Libro));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND fuente_id = '{escenario.Lima}' AND estado = 'confirmada' AND creada_por_traslado"));

        var anulado = await escenario.AnularElPedido(pedido);

        Assert.True(anulado.EsExito, anulado.Error?.Clave);
        // La línea que recreó el destino se libera como cualquier reserva confirmada (SEL-07): el físico llegó y ya nadie lo retiene.
        Assert.Equal((1, 0), await bd.StockDe(escenario.Lima, escenario.Libro));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND fuente_id = '{escenario.Lima}' AND estado = 'confirmada'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND fuente_id = '{escenario.Lima}' AND estado = 'liberada'"));
        Assert.Equal("recibido", await escenario.EstadoDe(traslado));
        Assert.Equal((0, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
        Assert.Equal(await bd.ReservadoDeLaRed(escenario.Libro), await bd.UnidadesDeLineasActivas(escenario.Libro));
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-15")]
    [InlineData(MotivoDeLiberacionDeReserva.CanceladoPorElCliente)]
    [InlineData(MotivoDeLiberacionDeReserva.FallaPosteriorAlPago)]
    public async Task TodosLosMotivosQueAnulanElPedidoCancelanElTrasladoSolicitado(MotivoDeLiberacionDeReserva motivo)
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 1);
        var (pedido, _, traslado) = await escenario.PedidoConTraslado();

        var anulado = await escenario.AnularElPedido(pedido, motivo);

        Assert.True(anulado.EsExito, anulado.Error?.Clave);
        Assert.Equal("cancelado", await escenario.EstadoDe(traslado));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-15")]
    public async Task ElCheckoutCanceladoNoTocaUnTrasladoDeUnPedidoYaPagado()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 1);
        var (pedido, _, traslado) = await escenario.PedidoConTraslado();

        var liberada = await escenario.AnularElPedido(pedido, MotivoDeLiberacionDeReserva.CheckoutCancelado);

        Assert.False(liberada.EsExito);
        Assert.Equal("solicitado", await escenario.EstadoDe(traslado));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-13")]
    public async Task ElCicloCompletoDeUnVinculado_DejaLaReservaDelPedidoEnElDestino_ConElStockCuadrado()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeLima: 0, stockDeCusco: 1);
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        var totalAntes = await bd.Consultar<long>($"SELECT sum(fisico) FROM fuentes.stock WHERE edicion_id = '{escenario.Libro}'");

        Assert.True((await escenario.Ejecutar(Encargado(escenario.Cusco), new DespacharTrasladoEntrada(traslado))).EsExito);
        Assert.True((await escenario.Ejecutar(Logistica(), new RegistrarGuiaDeTrasladoEntrada(traslado, "CA-771204"))).EsExito);
        Assert.True((await escenario.Ejecutar(Encargado(escenario.Lima), new RecibirTrasladoEntrada(traslado))).EsExito);

        Assert.Equal(totalAntes, await bd.Consultar<long>($"SELECT sum(fisico) FROM fuentes.stock WHERE edicion_id = '{escenario.Libro}'"));
        Assert.Equal((1, 1), await bd.StockDe(escenario.Lima, escenario.Libro));
        Assert.Equal((0, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
        Assert.Equal("recibido", await escenario.EstadoDe(traslado));
        Assert.Equal(await bd.ReservadoDeLaRed(escenario.Libro), await bd.UnidadesDeLineasActivas(escenario.Libro));
        Assert.Equal(pedido, await bd.Consultar<Guid>($"SELECT pedido_id FROM traslados.traslados WHERE id = '{traslado}'"));
    }
}
