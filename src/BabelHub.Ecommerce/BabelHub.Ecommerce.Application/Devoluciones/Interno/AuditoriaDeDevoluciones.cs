using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// Las entradas de auditoría de los cambios de una devolución (DEV-12, AC-DEV-11): el pedido, la devolución y el monto (en soles, con dos decimales) en cada paso, <b>nunca el motivo ni el texto</b>. El usuario o el actor «sistema» los pone la Auditoría desde el actor.
/// </summary>
internal static class AuditoriaDeDevoluciones
{
    public const string Moneda = "PEN";

    public static SolicitudDeRegistro Solicitada(Devolucion d) =>
        new(AccionesAuditables.DevolucionSolicitada)
        {
            ObjetoTipo = "devolucion",
            ObjetoId = d.Id.ToString(),
            Detalle = new Dictionary<string, string?>
            {
                ["pedidoId"] = d.PedidoId.ToString(),
                ["cantidadDeLineas"] = d.Lineas.Count.ToString(CultureInfo.InvariantCulture),
                ["monto"] = Monto(d.Lineas.Sum(l => l.Monto)),
                ["moneda"] = Moneda,
            },
        };

    /// <summary>
    /// El cambio de estado: el anterior y el nuevo, el pedido, la devolución y el monto. Con <paramref name="lineaDePedidoId"/> el monto es el de esa línea (el reembolso es por línea); sin ella, el de toda la devolución.
    /// </summary>
    public static SolicitudDeRegistro Cambio(Devolucion d, EstadoDeLineaDeDevolucion anterior, EstadoDeLineaDeDevolucion nuevo, string? estadoNuevoPropio = null, Guid? lineaDePedidoId = null) =>
        new(AccionesAuditables.EstadoDeDevolucionCambiado)
        {
            ObjetoTipo = "devolucion",
            ObjetoId = d.Id.ToString(),
            Detalle = new Dictionary<string, string?>
            {
                ["estadoAnterior"] = anterior.Codigo(),
                ["estadoNuevo"] = estadoNuevoPropio ?? nuevo.Codigo(),
                ["pedidoId"] = d.PedidoId.ToString(),
                ["devolucionId"] = d.Id.ToString(),
                ["monto"] = Monto(lineaDePedidoId is { } id ? d.Lineas.Single(l => l.LineaDePedidoId == id).Monto : d.Lineas.Sum(l => l.Monto)),
                ["moneda"] = Moneda,
            },
        };

    /// <summary>El reingreso al Inventario confirmado: el pedido, la devolución y el valor de lo reingresado.</summary>
    public static SolicitudDeRegistro Reingreso(Devolucion d) =>
        new(AccionesAuditables.ReingresoPorDevolucion)
        {
            ObjetoTipo = "devolucion",
            ObjetoId = d.Id.ToString(),
            Detalle = new Dictionary<string, string?>
            {
                ["devolucionId"] = d.Id.ToString(),
                ["pedidoId"] = d.PedidoId.ToString(),
                ["monto"] = Monto(d.Lineas.Sum(l => l.Monto)),
                ["moneda"] = Moneda,
            },
        };

    private static string Monto(decimal monto) => monto.ToString("0.00", CultureInfo.InvariantCulture);
}

