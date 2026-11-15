using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T21: <c>IOrigenDeUnidadesVendidas</c> (FUE-21, FUE-22): qué línea es una venta, a qué fuente se atribuye y que cuenta una sola vez.</summary>
public sealed class OrigenDeUnidadesVendidasTests
{
    private static EntornoDeReservas Entorno() => new(ActorDePrueba.DeAdministradorCentral());

    private static OrigenDeUnidadesVendidas Origen(EntornoDeReservas e) => new(e.Reservas);

    private static Task<IReadOnlyList<VentaAtribuida>> Ventas(EntornoDeReservas e, Guid? fuente = null) =>
        Origen(e).Ventas(fuente, e.Base.Reloj.Ahora.AddDays(-30), e.Base.Reloj.Ahora);

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task UnaLineaConfirmada_EnElRecojo_SeAtribuyeALaSucursalDeRecojo_AunqueElLibroEsteEnElOrigen()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Cusco, 2)]);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora.AddDays(-1)));

        var ventas = await Ventas(e);

        Assert.Equal([new VentaAtribuida(pedido, libro, e.Base.Lima.Id, 2)], ventas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task UnPedidoADomicilio_SeAtribuyeALaFuenteDeLaLinea()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Cusco, 1)], recojoEnLima: false);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora.AddDays(-1)));

        var ventas = await Ventas(e);

        Assert.Equal([new VentaAtribuida(pedido, libro, e.Base.Cusco.Id, 1)], ventas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-18")]
    public async Task UnaLineaCerradaPorEntrega_SigueSiendoUnaVenta()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var reserva = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 3)]);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora.AddDays(-2)));
        reserva.Lineas.Single().Cerrar(CausaDeCierre.Entrega, e.Base.Reloj.Ahora);

        var ventas = await Ventas(e);

        Assert.Equal(3, Assert.Single(ventas).Unidades);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-18")]
    public async Task ElTrasladoCuentaUnaSolaVez_LaLineaRecreadaEnElDestinoNoEsOtraVenta()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Cusco, 1)]);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora.AddDays(-1)));
        var origen = reserva.Lineas.Single();
        var traslado = Guid.NewGuid();
        origen.VincularTraslado(traslado);
        origen.Cerrar(CausaDeCierre.Traslado, e.Base.Reloj.Ahora);
        origen.MarcarRecreadaEnDestino();
        var recreada = LineaDeReserva.ConfirmadaPorTraslado(Guid.NewGuid(), origen.LineaId, libro, e.Base.Lima.Id, 1, traslado, e.Base.Reloj.Ahora);
        e.Reservas.Guardadas[0] = ReservaDePedido.Rehidratar(
            reserva.Id, pedido, reserva.ClienteId, reserva.TipoDeEntrega, reserva.SucursalDeRecojoId, reserva.RegionDeEntrega, reserva.IniciadaEn, reserva.FechaDeExpiracion, reserva.Extendida, reserva.Reemplazos, [origen, recreada]);

        var ventas = await Ventas(e);

        Assert.Equal([new VentaAtribuida(pedido, libro, e.Base.Lima.Id, 1)], ventas);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-18")]
    public async Task UnPedidoAnulado_NoEsUnaVenta_NiLaLiberadaNiLaPendienteNiLaExpirada()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var anulada = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Cusco, 1)]);
        Assert.True(anulada.Confirmar(e.Base.Reloj.Ahora.AddDays(-1)));
        anulada.Lineas.Single().Cerrar(CausaDeCierre.Traslado, e.Base.Reloj.Ahora);
        anulada.Lineas.Single().Anular(e.Base.Reloj.Ahora);
        var liberada = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)]);
        Assert.True(liberada.Confirmar(e.Base.Reloj.Ahora.AddDays(-1)));
        liberada.Lineas.Single().Liberar(e.Base.Reloj.Ahora);
        e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)]);
        var expirada = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)]);
        expirada.Lineas.Single().Expirar(e.Base.Reloj.Ahora);

        Assert.Empty(await Ventas(e));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task SoloCuentaLaVentanaDeTreintaDias_ConLosDosExtremosIncluidos()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var ahora = e.Base.Reloj.Ahora;
        var dentro = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)]);
        Assert.True(dentro.Confirmar(ahora.AddDays(-30)));
        var ahoraMismo = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)]);
        Assert.True(ahoraMismo.Confirmar(ahora));
        var fuera = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)]);
        Assert.True(fuera.Confirmar(ahora.AddDays(-30).AddTicks(-1)));
        var futura = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Lima, 1)]);
        Assert.True(futura.Confirmar(ahora.AddTicks(1)));

        var ventas = await Ventas(e);

        Assert.Equal(new[] { dentro.PedidoId, ahoraMismo.PedidoId }.Order(), ventas.Select(v => v.PedidoId).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task ConUnaFuente_SoloLasVentasDeEsaFuente()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var enLima = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Cusco, 1)]);
        Assert.True(enLima.Confirmar(e.Base.Reloj.Ahora));
        var enCusco = e.Pendiente(Guid.NewGuid(), [(libro, e.Base.Cusco, 4)], recojoEnLima: false);
        Assert.True(enCusco.Confirmar(e.Base.Reloj.Ahora));

        Assert.Equal(enLima.PedidoId, Assert.Single(await Ventas(e, e.Base.Lima.Id)).PedidoId);
        Assert.Equal(4, Assert.Single(await Ventas(e, e.Base.Cusco.Id)).Unidades);
        Assert.Equal(2, (await Ventas(e)).Count);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task DosLineasDeLaMismaEdicionEnUnPedido_SeSumanEnUnaSolaVentaAtribuida()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Cusco, 2), (libro, e.Base.Lima, 3)]);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));

        var venta = Assert.Single(await Ventas(e));

        Assert.Equal(new VentaAtribuida(pedido, libro, e.Base.Lima.Id, 5), venta);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-17")]
    public async Task UnaVentanaInvertida_SeRechaza()
    {
        var e = Entorno();

        await Assert.ThrowsAsync<ArgumentException>(() => Origen(e).Ventas(null, e.Base.Reloj.Ahora, e.Base.Reloj.Ahora.AddDays(-1)));
    }
}
