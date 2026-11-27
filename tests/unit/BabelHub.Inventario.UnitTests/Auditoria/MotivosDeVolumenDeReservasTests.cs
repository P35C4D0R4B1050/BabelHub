using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// Los motivos de alerta técnica que publican las reservas y las bajas de titular constan en el catálogo cerrado y en el contrato de <c>Shared</c> (hallazgo de INV-2b): el aviso de un
/// volumen anómalo ya no sale como «motivo no catalogado». El contrato de <c>Shared</c> vs. el catálogo lo vigila <c>ContratoCompartidoDeAuditoriaTests</c>.
/// </summary>
public sealed class MotivosDeVolumenDeReservasTests
{
    [Theory]
    [Trait("Criterio", "AC-SEL-06")]
    [Trait("Criterio", "AC-SEL-53")]
    [InlineData(VolumenDeOperacionesDeReservas.MotivoDeConfirmaciones)]
    [InlineData(VolumenDeOperacionesDeReservas.MotivoDeLiberaciones)]
    [InlineData(VolumenDeBajasDeTitular.Motivo)]
    [InlineData(ConcentracionDeReservas.MotivoDeLaAlerta)]
    public void ElMotivoDeVolumenAnomalo_ConstaEnElCatalogoComoAlertaTecnica_Y_EnElContrato(string motivo)
    {
        var catalogo = CatalogoDeAlertasDeAuditoria.Crear();

        Assert.True(catalogo.Contiene(motivo), motivo);
        Assert.Equal(ClaseDeAlerta.Tecnica, catalogo.Obtener(motivo).Clase);
        Assert.Contains(motivo, BabelHub.Shared.Contratos.Auditoria.MotivosDeAlertaDeAuditoria.Todos);
    }

    [Fact]
    public void ElMotivoDeLimiteDeReemplazosPorPedido_EstaEnElContratoYEnLosEventosDeSeguridad()
    {
        Assert.Contains(BabelHub.Shared.Contratos.Inventario.MotivosDeLimiteDeReservas.ReemplazosPorPedido, BabelHub.Shared.Contratos.Inventario.MotivosDeLimiteDeReservas.Todos);
        Assert.Contains(BabelHub.Shared.Contratos.Inventario.MotivosDeLimiteDeReservas.ReemplazosPorPedido, CatalogoDeAccionesDeAuditoria.MotivosDeLimiteExcedido);
    }
}
