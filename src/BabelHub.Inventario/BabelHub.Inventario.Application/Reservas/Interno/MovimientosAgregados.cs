using BabelHub.Inventario.Application.Fuentes;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// La regla única de cómo se aplican varios cambios de stock: <b>agregados por pareja (fuente, edición) y ordenados por (edición, fuente)</b>, el orden único de bloqueo (D04). La usan reservar, devolver lo liberado y
/// descontar al cerrar, de modo que ningún caso de uso arme su propio orden.
/// </summary>
internal static class MovimientosAgregados
{
    /// <summary>Un movimiento por pareja con la suma de las cantidades multiplicada por los signos dados al físico y a lo reservado (por ejemplo, 0 y +1 para reservar; −1 y −1 para cerrar).</summary>
    public static IEnumerable<MovimientoDeStock> Ordenados(IEnumerable<(Guid FuenteId, Guid EdicionId, int Cantidad)> lineas, int signoDelFisico, int signoDeLoReservado)
    {
        ArgumentNullException.ThrowIfNull(lineas);

        return lineas
            .GroupBy(l => (l.FuenteId, l.EdicionId))
            .Select(g =>
            {
                var total = checked(g.Sum(l => l.Cantidad));
                return new MovimientoDeStock(g.Key.FuenteId, g.Key.EdicionId, signoDelFisico * total, signoDeLoReservado * total);
            })
            .OrderBy(m => m.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(m => m.FuenteId, OrdenDeBloqueo.DeIdentificadores);
    }
}
