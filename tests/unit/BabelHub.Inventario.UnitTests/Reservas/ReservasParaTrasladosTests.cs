using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Soporte;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T20: <c>IReservasParaTraslados</c> (SEL-11, SEL-28, SEL-29): consultar la confirmada, consumirla en el origen y recrearla en el destino, con el bloqueo del pedido y el estado inválido ante lo que ya no está confirmado.</summary>
public sealed class ReservasParaTrasladosTests
{
    private static EntornoDeReservas Entorno() => new(ActorDePrueba.DeAdministradorCentral());

    /// <summary>Un pedido de recojo en Lima con una línea confirmada de 1 unidad que sale de Cusco (físico 9, reservado 1) y la sucursal de recojo con físico 5.</summary>
    private static (EntornoDeReservas E, ReservaDePedido Reserva, Guid Pedido, Guid Libro, Guid Traslado) Confirmada()
    {
        var e = Entorno();
        var libro = e.Impresa();
        var pedido = Guid.NewGuid();
        var reserva = e.Pendiente(pedido, [(libro, e.Base.Cusco, 1)]);
        Assert.True(reserva.Confirmar(e.Base.Reloj.Ahora));
        var traslado = Guid.NewGuid();
        reserva.Lineas.Single().VincularTraslado(traslado);
        e.Base.Existencias.Poner(e.Base.Cusco.Id, libro, 9, 1);
        e.Base.Existencias.Poner(e.Base.Lima.Id, libro, 5, 0);
        return (e, reserva, pedido, libro, traslado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task ConsultarConfirmada_DevuelveLaReservaDeLaFuenteYLaEdicion_ONadaSiNoEsLaConfirmada()
    {
        var (e, _, pedido, libro, traslado) = Confirmada();

        var hay = await e.ParaTraslados().ConsultarConfirmada(pedido, e.Base.Cusco.Id, libro);
        var otraFuente = await e.ParaTraslados().ConsultarConfirmada(pedido, e.Base.Lima.Id, libro);
        var otroPedido = await e.ParaTraslados().ConsultarConfirmada(Guid.NewGuid(), e.Base.Cusco.Id, libro);

        Assert.Equal(new ReservaConfirmadaDeLaFuente(pedido, e.Base.Cusco.Id, libro, 1, traslado), hay);
        Assert.Null(otraFuente);
        Assert.Null(otroPedido);
        Assert.Equal(3, e.Bloqueos.Pedidos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task ConsumirEnElOrigen_BajaElFisicoYLoReservado_YCierraLaLineaPorTraslado_SinRecrear()
    {
        var (e, _, pedido, libro, _) = Confirmada();

        var resultado = await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);

        Assert.Equal(ResultadoDeMovimientoDeReserva.Aplicado, resultado);
        Assert.Equal((8, 0), e.FilaDe(e.Base.Cusco, libro));
        var linea = e.Reservas.Guardadas.Single().Lineas.Single();
        Assert.Equal((EstadoDeLinea.Cerrada, CausaDeCierre.Traslado, false), (linea.Estado, linea.CerradaPor, linea.RecreadaEnDestino));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task ConsumirEnElOrigen_SigueLaJerarquiaDeBloqueos_PedidoLineasYFilas()
    {
        var (e, _, pedido, libro, _) = Confirmada();

        await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);

        var orden = e.Base.Bitacora.Llamadas;
        Assert.True(orden.IndexOf("bloqueo.Pedido") < orden.IndexOf("reservas.BloquearLineas"));
        Assert.True(orden.IndexOf("reservas.BloquearLineas") < orden.IndexOf("existencias.BloquearFilas"));
        Assert.True(orden.IndexOf("existencias.BloquearFilas") < orden.IndexOf("existencias.Aplicar(-1,-1)"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-28")]
    [InlineData("liberada")]
    [InlineData("expirada")]
    [InlineData("pendiente")]
    [InlineData("cerrada-por-entrega")]
    [InlineData("ya-consumida")]
    public async Task ConsumirUnaReservaQueNoEstaConfirmada_EsEstadoInvalido_YElStockNoCambia(string estado)
    {
        var (e, reserva, pedido, libro, _) = Confirmada();
        var linea = reserva.Lineas.Single();
        var ahora = e.Base.Reloj.Ahora;
        var guardada = e.Reservas.Guardadas.Single().Lineas.Single();
        switch (estado)
        {
            case "liberada":
                guardada.Liberar(ahora);
                break;
            case "expirada":
            case "pendiente":
                e.Reservas.Guardadas.Clear();
                var otra = e.Pendiente(pedido, [(libro, e.Base.Cusco, 1)]);
                if (estado == "expirada")
                {
                    otra.Lineas.Single().Expirar(ahora);
                }

                break;
            case "cerrada-por-entrega":
                guardada.Cerrar(CausaDeCierre.Entrega, ahora);
                break;
            default:
                Assert.Equal(ResultadoDeMovimientoDeReserva.Aplicado, await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro));
                break;
        }

        var antes = e.FilaDe(e.Base.Cusco, libro);
        var resultado = await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);

        Assert.Equal(ResultadoDeMovimientoDeReserva.EstadoInvalido, resultado);
        Assert.Equal(antes, e.FilaDe(e.Base.Cusco, libro));
        Assert.NotNull(linea);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-28")]
    public async Task ConsumirSinReservaOSinLaPareja_EsNoEncontrada()
    {
        var (e, _, pedido, libro, _) = Confirmada();

        Assert.Equal(ResultadoDeMovimientoDeReserva.NoEncontrada, await e.ParaTraslados().ConsumirEnElOrigen(Guid.NewGuid(), e.Base.Cusco.Id, libro));
        Assert.Equal(ResultadoDeMovimientoDeReserva.NoEncontrada, await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Ayacucho.Id, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task LaPoliticaDeFilasRechazaLaEscritura_SePropagaDenegadoPorAlcance()
    {
        var (e, _, pedido, libro, _) = Confirmada();
        e.Base.Existencias.FuentesFueraDelAlcance.Add(e.Base.Cusco.Id);

        var resultado = await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);

        Assert.Equal(ResultadoDeMovimientoDeReserva.DeniegadoPorAlcance, resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task RecrearEnElDestino_SubeElFisicoYLoReservado_MarcaLaLineaDeOrigenYCreaLaDelDestino()
    {
        var (e, _, pedido, libro, traslado) = Confirmada();
        await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);

        var resultado = await e.ParaTraslados().RecrearEnElDestino(pedido, e.Base.Lima.Id, libro, 1, traslado);

        Assert.Equal(ResultadoDeMovimientoDeReserva.Aplicado, resultado);
        Assert.Equal((6, 1), e.FilaDe(e.Base.Lima, libro));
        var lineas = e.Reservas.Guardadas.Single().Lineas;
        Assert.Equal(2, lineas.Count);
        var origen = lineas.Single(l => l.FuenteId == e.Base.Cusco.Id);
        var destino = lineas.Single(l => l.FuenteId == e.Base.Lima.Id);
        Assert.True(origen.RecreadaEnDestino);
        Assert.Equal((EstadoDeLinea.Confirmada, true, traslado, origen.LineaId), (destino.Estado, destino.CreadaPorTraslado, destino.TrasladoId, destino.LineaId));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-29")]
    [InlineData("anulada")]
    [InlineData("ya-recreada")]
    [InlineData("otra-cantidad")]
    public async Task RecrearSiYaNoCorresponde_EsEstadoInvalido_SinTocarElStock(string caso)
    {
        var (e, _, pedido, libro, traslado) = Confirmada();
        await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);
        var origen = e.Reservas.Guardadas.Single().Lineas.Single();
        if (caso == "anulada")
        {
            origen.Anular(e.Base.Reloj.Ahora);
        }
        else if (caso == "ya-recreada")
        {
            origen.MarcarRecreadaEnDestino();
        }

        var resultado = await e.ParaTraslados().RecrearEnElDestino(pedido, e.Base.Lima.Id, libro, caso == "otra-cantidad" ? 2 : 1, traslado);

        Assert.Equal(ResultadoDeMovimientoDeReserva.EstadoInvalido, resultado);
        Assert.Equal((5, 0), e.FilaDe(e.Base.Lima, libro));
        Assert.Single(e.Reservas.Guardadas.Single().Lineas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task RecrearUnTrasladoQueNoEsDeLaReserva_EsNoEncontrada()
    {
        var (e, _, pedido, libro, _) = Confirmada();
        await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);

        Assert.Equal(ResultadoDeMovimientoDeReserva.NoEncontrada, await e.ParaTraslados().RecrearEnElDestino(pedido, e.Base.Lima.Id, libro, 1, Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-11")]
    public async Task LaEntregaTrasElTraslado_DescuentaElFisicoEnLaSucursalDeRecojo()
    {
        var (e, _, pedido, libro, traslado) = Confirmada();
        await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);
        await e.ParaTraslados().RecrearEnElDestino(pedido, e.Base.Lima.Id, libro, 1, traslado);
        var lineaDelPedido = e.Reservas.Guardadas.Single().Lineas.First().LineaId;
        e.Base.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, e.Base.Lima.Id));

        var resultado = await e.Cerrar().Ejecutar(new CerrarReservasEntrada(pedido, MotivoDeCierreDeReserva.Entrega, [lineaDelPedido], null, EntornoDeReservas.ClaveNueva()));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((5, 0), e.FilaDe(e.Base.Lima, libro));
        Assert.Equal((8, 0), e.FilaDe(e.Base.Cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-29")]
    public async Task LaAnulacionDelPedidoTrasConsumirEnElOrigen_MarcaLaVentaAnulada_YElDestinoNoLaRecrea()
    {
        var (e, _, pedido, libro, traslado) = Confirmada();
        await e.ParaTraslados().ConsumirEnElOrigen(pedido, e.Base.Cusco.Id, libro);
        e.Base.Actor.Establecer(ActorActual.DeIntegracion("expiracion-de-reservas"));

        var anulado = await e.Liberar().Ejecutar(new LiberarReservasEntrada(pedido, MotivoDeLiberacionDeReserva.PedidoAnulado, null));
        var recrear = await e.ParaTraslados().RecrearEnElDestino(pedido, e.Base.Lima.Id, libro, 1, traslado);

        Assert.True(anulado.EsExito, anulado.Error?.Clave);
        Assert.NotNull(e.Reservas.Guardadas.Single().Lineas.Single().AnuladaEn);
        Assert.Equal(ResultadoDeMovimientoDeReserva.EstadoInvalido, recrear);
    }
}
