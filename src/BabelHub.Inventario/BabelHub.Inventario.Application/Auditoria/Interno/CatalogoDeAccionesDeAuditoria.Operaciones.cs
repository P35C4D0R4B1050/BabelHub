using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>
    /// Las operaciones de AUD-02: precios, anulaciones, ajustes, ingresos, reembolsos y los estados de pedido, envío, devolución y traslado, con su
    /// solicitud y su rechazo (un rechazo es un registro con resultado «rechazado», no otra acción). Los objetos son pedidos, ediciones, devoluciones
    /// o traslados (identificadores opacos), nunca una persona; el detalle lleva importes, cantidades y estados, jamás datos de tarjeta ni direcciones
    /// de entrega (la lista blanca no las declara).
    /// </summary>
    private static IEnumerable<AccionDelCatalogo> AccionesDeOperaciones()
    {
        yield return Auditada(nameof(AccionesAuditables.PrecioDeEdicionCambiado), false, false, Clave("precioAnterior"), Clave("precioNuevo"), Clave("moneda"));
        yield return Auditada(nameof(AccionesAuditables.PedidoAnulado), false, false, Clave("estadoAnterior"), Clave("causa"));
        yield return Auditada(nameof(AccionesAuditables.AjusteRegistrado), false, false, Clave("edicionId"), Clave("cantidadAnterior"), Clave("cantidadPosterior"), Clave("causa"));
        yield return Auditada(nameof(AccionesAuditables.IngresoDeMercaderiaRegistrado), false, false, Clave("edicionId"), Clave("cantidad"), Clave("referencia"));
        yield return Auditada(nameof(AccionesAuditables.ReingresoPorDevolucion), false, false, Clave("edicionId"), Clave("cantidad"), Clave("devolucionId"));
        yield return Auditada(nameof(AccionesAuditables.ReembolsoRegistrado), false, false, Clave("monto"), Clave("moneda"), Clave("modalidad"), Clave("medio"));
        yield return Auditada(nameof(AccionesAuditables.EstadoDePedidoCambiado), false, false, Clave("estadoAnterior"), Clave("estadoNuevo"));
        yield return Auditada(nameof(AccionesAuditables.EstadoDeEnvioCambiado), false, false, Clave("estadoAnterior"), Clave("estadoNuevo"));
        yield return Auditada(nameof(AccionesAuditables.DevolucionSolicitada), false, false, Clave("pedidoId"), Clave("cantidadDeLineas"));
        yield return Auditada(nameof(AccionesAuditables.EstadoDeDevolucionCambiado), false, false, Clave("estadoAnterior"), Clave("estadoNuevo"));
        yield return Auditada(nameof(AccionesAuditables.TrasladoSolicitado), false, false, Clave("edicionId"), Clave("cantidad"), Clave("causa"));
        yield return Auditada(nameof(AccionesAuditables.EstadoDeTrasladoCambiado), false, false, Clave("estadoAnterior"), Clave("estadoNuevo"));
    }
}
