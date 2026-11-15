using BabelHub.Inventario.Application.Fuentes;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// <see cref="IOrigenDeUnidadesVendidas"/>: las ventas atribuidas que <c>Fuentes</c> necesita para las unidades vendidas de los últimos 30 días (FUE-21, FUE-22). Una venta es una línea de reserva confirmada o cerrada
/// (por entrega, reparto o traslado) creada por el pedido, no por un traslado, y no anulada: la línea que un traslado recrea en el destino no cuenta (no es otra venta) ni la de un pedido anulado. En el recojo se atribuye a la
/// sucursal de recojo, aunque el libro esté en el origen o en tránsito; a domicilio, a la fuente de la línea. Cada pedido y edición cuenta una sola vez por fuente. Solo lee: la autorización es de quien consulta (<c>Fuentes</c>).
/// </summary>
internal sealed class OrigenDeUnidadesVendidas(IRepositorioDeReservas reservas) : IOrigenDeUnidadesVendidas
{
    public async Task<IReadOnlyList<VentaAtribuida>> Ventas(Guid? fuenteId, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default)
    {
        if (hasta < desde)
        {
            throw new ArgumentException("La ventana de ventas termina antes de empezar.", nameof(hasta));
        }

        return await reservas.VentasAtribuidas(fuenteId, desde, hasta, cancelacion).ConfigureAwait(false);
    }
}
