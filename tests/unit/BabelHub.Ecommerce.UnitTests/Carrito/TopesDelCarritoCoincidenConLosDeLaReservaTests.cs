using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Shared.Contratos;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// Los topes del carrito (CAR-38; DD-16) son los de una reserva del Inventario: si divergieran, un carrito válido no se podría reservar al pagar. El lado del Inventario lo fija
/// <c>TopesDeLaReservaCoincidenConLosDelCarritoTests</c> en sus pruebas con los mismos valores.
/// </summary>
public sealed class TopesDelCarritoCoincidenConLosDeLaReservaTests
{
    [Fact]
    [Trait("Criterio", "AC-CAR-32")]
    public void Los_topes_del_carrito_son_30_lineas_y_20_unidades_y_las_lineas_coinciden_con_el_contrato_de_la_reserva()
    {
        Assert.Equal(30, Domain.Carrito.Carrito.MaximoDeLineas);
        Assert.Equal(20, Cantidad.Maxima);
        Assert.Equal(LimitesDeContrato.MaximoDeLineasPorReserva, Domain.Carrito.Carrito.MaximoDeLineas);
    }
}
