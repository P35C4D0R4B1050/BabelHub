using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// Extiende una sola vez la reserva de los impresos de un pago pendiente (PAG-13): <b>fuera de toda transacción</b> pide <c>reservas.extender</c> y, con el instante real que devuelve el Inventario, lo anota bajo el bloqueo de
/// la intención (marca de la cuenta primero, D24). Lo usan el cobro que queda pendiente y el trabajo de consulta que reintenta si la primera extensión falló. Devuelve si ya no queda nada por extender.
/// </summary>
internal sealed class ExtenderReservaDelPago(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, ICuentaDelPagador cuentas, IReservasDelPago reservas)
{
    public async Task<bool> Extender(Guid pedidoId)
    {
        try
        {
            var intencion = await unidad.Ejecutar(token => repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, token), CancellationToken.None).ConfigureAwait(false);
            if (intencion is not { TieneImpresos: true, ReservaExtendida: false })
            {
                return true;
            }

            var extension = await reservas.Extender(pedidoId, CancellationToken.None).ConfigureAwait(false);
            if (!extension.EsCorrecta || extension.Valor is null)
            {
                return false;
            }

            await unidad.Ejecutar(
                async token =>
                {
                    // El mismo orden de bloqueo (D24): la marca de cancelación de la cuenta y después la intención.
                    if (intencion.CuentaId is { } cuentaId)
                    {
                        await cuentas.CancelacionEnEjecucion(cuentaId, bloquear: true, token).ConfigureAwait(false);
                    }

                    var actual = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: true, token).ConfigureAwait(false);
                    if (actual is not null && actual.ExtenderReserva(extension.Valor.ExpiraEn))
                    {
                        await repositorio.GuardarIntencion(actual, token).ConfigureAwait(false);
                    }

                    return true;
                },
                CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El inventario o la base no respondieron: la reserva se extiende en la siguiente consulta del trabajo.
            return false;
        }
    }
}
