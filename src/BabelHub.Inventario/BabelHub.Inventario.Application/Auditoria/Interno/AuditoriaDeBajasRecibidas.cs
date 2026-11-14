using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

/// <summary>Implementa <see cref="IAuditoriaDeBajasRecibidas"/> sobre <see cref="IAuditoria"/>: una auditoría por baja nueva, con el actor «integración» del contexto verificado.</summary>
public sealed class AuditoriaDeBajasRecibidas(IAuditoria auditoria, IActorActual actores) : IAuditoriaDeBajasRecibidas
{
    public async Task Registrar(BajaDeTitularRecibida baja, bool esNueva, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(baja);

        // La baja solo llega con la credencial de servicio: un usuario, un anónimo o la ausencia de contexto no la pueden entregar (AUD-23, SEG-15).
        if (actores.Actual is not { Tipo: TipoDeActor.Integracion })
        {
            throw new InvalidOperationException("La baja de un titular llega con la credencial de servicio: el actor debe ser una integración.");
        }

        if (!esNueva)
        {
            return;
        }

        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.BajaDeTitularProcesada)
            {
                ObjetoTipo = "titular",
                ObjetoId = baja.Identificador.ToString("D"),
            },
            cancelacion).ConfigureAwait(false);
    }
}
