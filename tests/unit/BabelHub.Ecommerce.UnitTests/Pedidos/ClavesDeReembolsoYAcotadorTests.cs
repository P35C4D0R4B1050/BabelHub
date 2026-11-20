using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>Dos piezas pequeñas de Pedidos que otros módulos asumen: la clave natural del reembolso (la valida Pagos) y el acotador de alertas.</summary>
public sealed class ClavesDeReembolsoYAcotadorTests
{
    [Fact]
    [Trait("Criterio", "AC-PED-14")]
    public void ClaveDelReembolso_CoincideConLaQueValidaPagos()
    {
        var pedido = Guid.NewGuid();

        Assert.Equal(ClaveDeReembolso.Anulacion(pedido), OrquestadorDeAnulaciones.ClaveDelReembolso(OrigenDeLaAnulacion.Administracion, pedido));
        Assert.Equal(ClaveDeReembolso.Cancelacion(pedido), OrquestadorDeAnulaciones.ClaveDelReembolso(OrigenDeLaAnulacion.Cliente, pedido));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public void Acotador_UnRelojQueRetrocede_NoSuprimeLaAlerta()
    {
        var acotador = new AcotadorDeAlertasDePedidos();
        var ahora = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.True(acotador.Permitir("motivo", ahora));
        Assert.False(acotador.Permitir("motivo", ahora.AddMinutes(1)));
        Assert.True(acotador.Permitir("motivo", ahora.AddHours(-3)));
    }
}
