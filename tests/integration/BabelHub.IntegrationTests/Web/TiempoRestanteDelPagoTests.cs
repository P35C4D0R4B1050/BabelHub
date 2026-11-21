using BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

namespace BabelHub.IntegrationTests.Web;

/// <summary>CAR-25 y AC-CAR-23: el tiempo restante hasta los 30 minutos se dice en minutos enteros hacia arriba, nunca menos de uno.</summary>
public sealed class TiempoRestanteDelPagoTests
{
    [Theory]
    [Trait("Criterio", "AC-CAR-23")]
    [InlineData(30 * 60, "Te quedan 30 min.")]
    [InlineData(29 * 60 + 1, "Te quedan 30 min.")]
    [InlineData(90, "Te quedan 2 min.")]
    [InlineData(1, "Te quedan 1 min.")]
    public void El_restante_se_redondea_hacia_arriba_con_un_minimo_de_uno(int segundos, string esperado) =>
        Assert.Equal(esperado, TextosDelPago.Restante(TimeSpan.FromSeconds(segundos)));
}
