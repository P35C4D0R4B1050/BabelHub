using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// Devuelve al vendible las unidades de unas líneas liberadas (el reemplazo de una reserva y la liberación por motivo): <c>Aplicar(0, −c)</c> <b>agregado y ordenado por (edición, fuente)</b> con las filas ya bloqueadas, y una
/// <c>ReservaLiberada</c> por línea con su causa, <b>en la misma transacción</b>: si no se puede auditar, la excepción revierte todo y las líneas siguen como estaban (AUD-04). El físico no se toca.
/// </summary>
internal sealed class DevolucionDeUnidades(IMovimientosDeStock stock, IAuditoria auditoria, AutorizadorDeReservas autorizador)
{
    public async Task<ErrorDeAplicacion?> Devolver(IReadOnlyList<LineaLiberada> liberadas, string causa, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(liberadas);

        var movimientos = liberadas
            .GroupBy(l => (l.FuenteId, l.EdicionId))
            .Select(g => new MovimientoDeStock(g.Key.FuenteId, g.Key.EdicionId, 0, -checked(g.Sum(l => l.Cantidad))))
            .OrderBy(m => m.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(m => m.FuenteId, OrdenDeBloqueo.DeIdentificadores);

        foreach (var movimiento in movimientos)
        {
            var resultado = await stock.Aplicar(movimiento, cancelacion).ConfigureAwait(false);
            if (resultado.Resultado == ResultadoDeMovimiento.DeniegadoPorAlcance)
            {
                return await autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false);
            }

            if (!resultado.Aplicado)
            {
                // Devolver lo que esta misma transacción tenía reservado nunca debería fallar: un estado imposible aborta (la transacción se revierte).
                throw new InvalidOperationException("No se pudo devolver al vendible lo liberado.");
            }
        }

        foreach (var linea in liberadas)
        {
            await Auditar(linea, causa, cancelacion).ConfigureAwait(false);
        }

        return null;
    }

    /// <summary>Audita una línea liberada (o, con una causa propia, una venta anulada) con su causa estable.</summary>
    public Task Auditar(LineaLiberada linea, string causa, CancellationToken cancelacion) =>
        auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.ReservaLiberada)
            {
                ObjetoTipo = "pedido",
                ObjetoId = linea.PedidoId.ToString("D"),
                SucursalOrigen = linea.FuenteId,
                Detalle = new Dictionary<string, string?>
                {
                    ["edicionId"] = linea.EdicionId.ToString("D"),
                    ["cantidad"] = linea.Cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["causa"] = causa,
                },
            },
            cancelacion);
}
