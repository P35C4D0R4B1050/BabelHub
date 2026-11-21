using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// La parte de Pedidos del conteo de pendientes de una cuenta (T23; PED-31, IDE-27; AC-PED-26): <see cref="Contar"/> devuelve <b>la suma</b> (para Identidad) y <see cref="ContarPorTipo"/> los <b>dos números</b> que pide PED-31, separados. Cuenta los pedidos de esa cuenta con alguna línea impresa sin entregar ni anular (también las no recogidas, hasta que se anulen (un no recogido no se entrega, DD-20); un pedido solo digital no cuenta) y de los reembolsos pendientes de anulación y de cancelación del cliente
/// (los pide a <see cref="IReembolsos"/>: Pagos es su dueño). No devuelve más datos ni nada de otra cuenta. Corre dentro de la transacción de Identidad y <b>falla cerrado</b>: si no puede contar, lanza <see cref="PendientesNoDisponiblesException"/>; nunca se asume cero.
/// </summary>
internal sealed class ContarPendientesDePedidos(IRepositorioDePedidos pedidos, IReembolsos reembolsos) : IPendientesDeCuentaPorTipo
{
    public async Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default)
        => (await ContarPorTipo(usuarioId, cancelacion).ConfigureAwait(false)).Total;

    /// <summary>Los dos números de PED-31 por separado: pedidos con una impresa sin cerrar y reembolsos pendientes (anulación y cancelación del cliente).</summary>
    public async Task<PendientesDeCuentaPorTipo> ContarPorTipo(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        try
        {
            var conImpresas = await pedidos.ContarPedidosConImpresaSinCerrar(usuarioId, cancelacion).ConfigureAwait(false);
            var deAnulacion = await reembolsos.ContarPendientes(usuarioId, OrigenDelReembolso.Anulacion, cancelacion).ConfigureAwait(false);
            var deCancelacion = await reembolsos.ContarPendientes(usuarioId, OrigenDelReembolso.CancelacionDelCliente, cancelacion).ConfigureAwait(false);
            return new PendientesDeCuentaPorTipo(conImpresas, checked(deAnulacion + deCancelacion));
        }
        catch (Exception excepcion) when (excepcion is not (OperationCanceledException or PendientesNoDisponiblesException))
        {
            throw new PendientesNoDisponiblesException("No se pudieron contar los pendientes de los pedidos de la cuenta.", excepcion);
        }
    }
}
