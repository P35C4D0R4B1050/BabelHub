using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;
using static BabelHub.IntegrationTests.Traslados.EscenarioDeTraslados;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// T15: solicitudes simultáneas reales detrás de una barrera, con PostgreSQL real, el rol real de la aplicación y <c>Reservas</c> real. Cada aserción vale en cualquier orden de llegada: el despacho frente a la anulación del
/// pedido (gana la primera que confirma y la anulación nunca falla), dos despachos sobre un vendible que solo alcanza para uno, dos aprobaciones y dos recepciones del mismo traslado, y la recepción frente a la reserva de otro
/// cliente en el destino. Ningún caso deja un interbloqueo y lo reservado de la red siempre coincide con las líneas activas.
/// </summary>
public sealed class ConcurrenciaDeTrasladosTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Criterio", "AC-TRA-22")]
    public async Task ElDespachoFrenteALaAnulacionDelPedido_SeAplicaSoloUna_YLaAnulacionNuncaFalla()
    {
        for (var ronda = 0; ronda < 8; ronda++)
        {
            await bd.Limpiar();
            using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 1);
            var (pedido, traslado) = await escenario.VinculadoAprobado();
            Assert.Equal((1, 1), await bd.StockDe(escenario.Cusco, escenario.Libro));
            var antes = await escenario.Interbloqueos();

            var tareas = await Simultaneas<object>(
                async () => await escenario.Ejecutar(Encargado(escenario.Cusco), new DespacharTrasladoEntrada(traslado)),
                async () => await escenario.AnularElPedido(pedido));
            var despacho = (BabelHub.Inventario.Application.Plataforma.Resultado<TrasladoRegistrado>)tareas[0];
            var anulacion = (BabelHub.Inventario.Application.Plataforma.Resultado<ReservaDelPedido>)tareas[1];

            Assert.True(anulacion.EsExito, anulacion.Error?.Clave);
            Assert.Equal(antes, await escenario.Interbloqueos());
            if (despacho.EsExito)
            {
                Assert.Equal(("en-transito", true), (await escenario.EstadoDe(traslado), await escenario.PedidoAnuladoDe(traslado)));
                Assert.Equal((0, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
            }
            else
            {
                Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, despacho.Error!.Clave);
                Assert.Equal(("cancelado", false), (await escenario.EstadoDe(traslado), await escenario.PedidoAnuladoDe(traslado)));
                Assert.Equal("pedido-anulado", await bd.Consultar<string>($"SELECT motivo_de_cierre FROM traslados.traslados WHERE id = '{traslado}'"));
                Assert.Equal((1, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
            }

            Assert.Equal(await bd.ReservadoDeLaRed(escenario.Libro), await bd.UnidadesDeLineasActivas(escenario.Libro));
        }

    }

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task DosTrasladosDe2UnidadesSobreUnVendibleDe3_DespachadosALaVez_SoloUnoSale_ElOtroSigueAprobado()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 3);
        var uno = await escenario.ReposicionAprobada(2, destino: escenario.Ayacucho);
        var dos = await escenario.ReposicionAprobada(2, destino: escenario.Lima);
        var antes = await escenario.Interbloqueos();

        var resultados = await Simultaneas(
            () => escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(uno.TrasladoId)),
            () => escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(dos.TrasladoId)));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        var perdedor = resultados.Single(r => !r.EsExito);
        Assert.Equal(ClavesDeTraslados.StockInsuficiente, perdedor.Error!.Clave);
        Assert.Equal((1, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados WHERE estado = 'en-transito'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados WHERE estado = 'aprobado'"));
        Assert.Equal(1, escenario.Pila.Auditoria.Contar("TrasladoDespachado"));
        Assert.Equal(antes, await escenario.Interbloqueos());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task DosAprobacionesDelMismoTraslado_ProducenUnaSolaTransicion()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar();
        var solicitado = await escenario.SolicitarComoAdministracion();
        var antes = await escenario.Interbloqueos();

        var resultados = await Simultaneas(
            () => escenario.Ejecutar(Administracion(), new AprobarTrasladoEntrada(solicitado.TrasladoId)),
            () => escenario.Ejecutar(Encargado(escenario.Cusco), new AprobarTrasladoEntrada(solicitado.TrasladoId)));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, resultados.Single(r => !r.EsExito).Error!.Clave);
        Assert.Equal(("aprobado", 1L), (await escenario.EstadoDe(solicitado.TrasladoId), await escenario.VersionDe(solicitado.TrasladoId)));
        Assert.Equal(1, escenario.Pila.Auditoria.Contar("TrasladoAprobado"));
        Assert.Equal(antes, await escenario.Interbloqueos());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-23")]
    public async Task DosRecepcionesDelMismoTraslado_SumanUnaSolaVez()
    {
        using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeCusco: 10, stockDeAyacucho: 2);
        var enTransito = await escenario.ReposicionEnTransito(4);
        var antes = await escenario.Interbloqueos();

        var resultados = await Simultaneas(
            () => escenario.Ejecutar(Administracion(), new RecibirTrasladoEntrada(enTransito.TrasladoId)),
            () => escenario.Ejecutar(Encargado(escenario.Ayacucho), new RecibirTrasladoEntrada(enTransito.TrasladoId)),
            () => escenario.Ejecutar(Administracion(), new RecibirTrasladoEntrada(enTransito.TrasladoId)));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal(ClavesDeTraslados.TrasladoEstadoInvalido, r.Error!.Clave));
        Assert.Equal((6, 0), await bd.StockDe(escenario.Ayacucho, escenario.Libro));
        Assert.Equal((6, 0), await bd.StockDe(escenario.Cusco, escenario.Libro));
        Assert.Equal(1, escenario.Pila.Auditoria.Contar("TrasladoRecibido"));
        Assert.Equal(antes, await escenario.Interbloqueos());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-24")]
    public async Task LaRecepcionFrenteALaReservaDeOtrosClientesEnElDestino_LasUnidadesRecibidasQuedanParaElPedido()
    {
        for (var ronda = 0; ronda < 4; ronda++)
        {
            await bd.Limpiar();
            using var escenario = await new EscenarioDeTraslados(bd).Preparar(stockDeLima: 0, stockDeCusco: 1);
            var (pedido, traslado) = await escenario.VinculadoAprobado();
            var despachado = await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));
            Assert.True(despachado.EsExito, despachado.Error?.Clave);
            await bd.PonerStock(escenario.Lima, escenario.Libro, 1, 0);
            var otroUno = ARecojo("lima", [Linea(escenario.Libro, 1)]);
            var otroDos = ARecojo("lima", [Linea(escenario.Libro, 1)]);
            var antes = await escenario.Interbloqueos();

            var tareas = await Simultaneas<object>(
                async () => await escenario.Ejecutar(Administracion(), new RecibirTrasladoEntrada(traslado)),
                async () => await escenario.Pila.Reservar(Cliente(), otroUno),
                async () => await escenario.Pila.Reservar(Cliente(), otroDos));
            var recepcion = (BabelHub.Inventario.Application.Plataforma.Resultado<TrasladoRegistrado>)tareas[0];
            var reservas = tareas.Skip(1).Cast<BabelHub.Inventario.Application.Plataforma.Resultado<ReservaDelPedido>>().ToList();

            Assert.True(recepcion.EsExito, recepcion.Error?.Clave);
            Assert.Equal(antes, await escenario.Interbloqueos());
            Assert.Equal("recibido", await escenario.EstadoDe(traslado));

            // El pedido del traslado tiene su unidad confirmada en Lima (creada por el traslado) y el vendible de Lima nunca fue de otro cliente que ya no lo tuviera.
            Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}' AND fuente_id = '{escenario.Lima}' AND estado = 'confirmada' AND creada_por_traslado"));
            var (fisico, reservado) = (await bd.StockDe(escenario.Lima, escenario.Libro))!.Value;
            Assert.Equal(2, fisico);
            Assert.Equal(1, reservas.Count(r => r.EsExito));
            Assert.Equal(1 + reservas.Count(r => r.EsExito), reservado);
            Assert.Equal(await bd.ReservadoDeLaRed(escenario.Libro), await bd.UnidadesDeLineasActivas(escenario.Libro));
        }
    }
}
