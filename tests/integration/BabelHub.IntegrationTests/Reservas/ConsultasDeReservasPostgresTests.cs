using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T18: las consultas con PostgreSQL real y las decoraciones de lectura (SEL-20): solo leen y no cuentan para los límites.</summary>
public sealed class ConsultasDeReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Lectura = ActorActual.DeIntegracion("lectura-de-disponibilidad");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task LaConsultaPrevia_DiceLaFuenteSinReservarNiContarParaLosLimites()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 0, 0);
        await bd.PonerStock(cusco, libro, 5, 0);
        await bd.DefinirRuta(cusco, lima, 4);
        using var pila = new PilaDeReservas(bd);

        SeleccionConsultada? ultima = null;
        for (var i = 0; i < 12; i++)
        {
            var resultado = await pila.Ejecutar<ConsultarSeleccionEntrada, SeleccionConsultada>(Lectura, new ConsultarSeleccionEntrada(TipoDeEntregaDeReserva.Recojo, "lima", null, [Linea(libro, 2)]));
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            ultima = resultado.Valor;
        }

        var linea = Assert.Single(ultima!.Lineas);
        Assert.Equal(("cusco", true, 4, 0), (linea.CodigoDeFuente, linea.RequiereTraslado, linea.PlazoEstimadoEnDias, linea.DisponibleEnLaRed));
        Assert.Equal((5, 0), await bd.StockDe(cusco, libro));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM plataforma.eventos_limite"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-20")]
    public async Task ElClienteVeSuReserva_ElOtroNoLaVe_YLaVencidaSeMuestraExpirada()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var duena = Cliente();
        var entrada = ARecojo("lima", [Linea(libro, 2)]);
        Assert.True((await pila.Reservar(duena, entrada)).EsExito);

        var propia = await pila.Ejecutar<ConsultarReservaEntrada, ReservaDelPedido>(duena, new ConsultarReservaEntrada(entrada.PedidoId));
        var ajena = await pila.Ejecutar<ConsultarReservaEntrada, ReservaDelPedido>(Cliente(), new ConsultarReservaEntrada(entrada.PedidoId));
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(20);
        var vencida = await pila.Ejecutar<ConsultarReservaEntrada, ReservaDelPedido>(duena, new ConsultarReservaEntrada(entrada.PedidoId));

        Assert.Equal(EstadoDeReserva.Pendiente, propia.Valor.Estado);
        Assert.Equal(CategoriaDeError.NoEncontrado, ajena.Error!.Categoria);
        Assert.Equal(EstadoDeReserva.Expirada, vencida.Valor.Estado);
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Contains("recurso-ajeno", pila.Denegaciones.Motivos);
    }
}
