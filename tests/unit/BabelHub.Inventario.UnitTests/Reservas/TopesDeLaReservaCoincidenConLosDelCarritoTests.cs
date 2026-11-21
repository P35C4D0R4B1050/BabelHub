using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>Los topes de una reserva (DD-16: 30 líneas y 20 unidades por línea) son los del carrito del E-commerce (CAR-38): el carrito los fija con los mismos valores en `TopesDelCarritoCoincidenConLosDeLaReservaTests`.</summary>
public sealed class TopesDeLaReservaCoincidenConLosDelCarritoTests
{
    [Fact]
    public void Los_topes_de_la_reserva_son_30_lineas_y_20_unidades_y_las_lineas_coinciden_con_el_contrato()
    {
        Assert.Equal(30, LimitesDeReservas.MaximoDeLineas);
        Assert.Equal(20, LimitesDeReservas.MaximoDeCantidad);
        Assert.Equal(LimitesDeContrato.MaximoDeLineasPorReserva, LimitesDeReservas.MaximoDeLineas);
    }
}
