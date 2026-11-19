using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>Los motivos de alerta técnica que Pedidos publica (ECO-6b a ECO-6c; AUD-12, AUD-29): están en el catálogo técnico y en el contrato compartido (el texto en español está en el presentador de la Api).</summary>
public sealed class AlertasDePedidosTests
{
    public static TheoryData<string> Motivos => new()
    {
        "pedido-operacion-fallida",
        "pedido-liberacion-no-recogido-ya-cerrada",
        "pedido-liberacion-de-anulacion-ya-cerrada",
        "pedido-estado-de-traslado-de-un-pedido-o-linea-inexistente",
        "pedido-estado-de-traslado-de-una-linea-anulada",
    };

    [Theory]
    [MemberData(nameof(Motivos))]
    [Trait("Criterio", "AC-AUD-12")]
    public void CadaMotivoDePedidos_EstaEnElCatalogoTecnicoYEnElContratoCompartido(string motivo)
    {
        var catalogo = CatalogoDeAlertasDeAuditoria.Crear();

        Assert.True(catalogo.Contiene(motivo), motivo);
        Assert.Equal(ClaseDeAlerta.Tecnica, catalogo.Obtener(motivo).Clase);
        Assert.Contains(motivo, BabelHub.Shared.Contratos.Auditoria.MotivosDeAlertaDeAuditoria.Todos);
        Assert.Matches("^[a-z][a-z0-9-]{1,63}$", motivo);
    }
}
