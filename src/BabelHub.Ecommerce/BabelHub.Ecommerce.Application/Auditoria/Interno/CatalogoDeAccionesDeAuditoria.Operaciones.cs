using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>
    /// Las operaciones de AUD-02: precios, anulaciones, ajustes, ingresos, reembolsos y los estados de pedido, envío, devolución y traslado, con su
    /// solicitud y su rechazo (un rechazo es un registro con resultado «rechazado», no otra acción). Los objetos son pedidos, ediciones, devoluciones
    /// o traslados (identificadores opacos), nunca una persona; el detalle lleva importes, cantidades y estados, jamás datos de tarjeta ni direcciones
    /// de entrega ni textos libres de un empleado (la nota y la referencia de un ajuste o un ingreso no se auditan: la lista blanca no las declara).
    /// </summary>
    private static IEnumerable<AccionDelCatalogo> AccionesDeOperaciones()
    {
        yield return Auditada(nameof(AccionesAuditables.PrecioDeEdicionCambiado), false, false, Clave("precioAnterior", 16), Clave("precioNuevo", 16), Moneda());
        yield return Auditada(nameof(AccionesAuditables.PedidoAnulado), false, false, Clave("estadoAnterior", 32), Clave("causa", 40));
        yield return Auditada(nameof(AccionesAuditables.IngresoPendienteConciliado), false, false, Clave("edicionId", 40), Clave("cantidad", 12), Clave("intentos", 12), new ClaveDeDetalle("resolucion", personal: false) { Valores = ["registrado", "fallido", "discrepancia"] });
        yield return Auditada(nameof(AccionesAuditables.IngresosSolicitadosAnonimizados), false, false, Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.IntencionesDePagoDesvinculadas), false, false, Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.ReembolsoResuelto), false, false, new ClaveDeDetalle("resultado", personal: false) { Valores = ["reembolsado", "pendiente"] }, Clave("motivo", 40));
        yield return Auditada(nameof(AccionesAuditables.PasoDeCompensacionHecho), false, false, new ClaveDeDetalle("paso", personal: false) { Valores = ["abrir", "liberar-reservas", "registrar-reembolso", "cerrar-checkout"] }, Clave("motivo", 40));
        yield return Auditada(nameof(AccionesAuditables.ContactoDeEntregaLiberado), false, false, new ClaveDeDetalle("origen", personal: false) { Valores = ["pedido", "retencion"] }, Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.CobroIniciado), false, false, new ClaveDeDetalle("medio", personal: false) { Valores = ["yape", "tarjeta"] }, Clave("importe", 12));
        yield return Auditada(nameof(AccionesAuditables.CobroResuelto), false, false, new ClaveDeDetalle("resultado", personal: false) { Valores = ["aprobado", "rechazado", "pendiente"] }, Clave("motivo", 40), new ClaveDeDetalle("duda", personal: false) { Valores = ["referencia", "moneda", "importe"] });
        yield return Auditada(nameof(AccionesAuditables.AvisoDeLaPasarelaRecibido), false, false, new ClaveDeDetalle("resultado", personal: false) { Valores = ["consulta-adelantada"] });
        yield return Auditada(nameof(AccionesAuditables.AjusteRegistrado), false, false, Clave("ajusteId", 40), Clave("edicionId", 40), Clave("cantidadAnterior", 12), Clave("cantidadPosterior", 12), Clave("motivo", 40));
        yield return Auditada(nameof(AccionesAuditables.IngresoDeMercaderiaRegistrado), false, false, Clave("ajusteId", 40), Clave("ingresoId", 40), Clave("edicionId", 40), Clave("cantidadAnterior", 12), Clave("cantidadPosterior", 12));
        yield return Auditada(nameof(AccionesAuditables.ReingresoPorDevolucion), false, false, Clave("ajusteId", 40), Clave("devolucionId", 40), Clave("edicionId", 40), Clave("cantidadAnterior", 12), Clave("cantidadPosterior", 12));
        yield return Auditada(nameof(AccionesAuditables.ReembolsoRegistrado), false, false, Clave("monto", 16), Moneda(), Modalidad(), Clave("medio", 32));
        yield return Auditada(nameof(AccionesAuditables.EstadoDePedidoCambiado), false, false, Clave("estadoAnterior", 32), Clave("estadoNuevo", 32));
        yield return Auditada(nameof(AccionesAuditables.EstadoDeEnvioCambiado), false, false, Clave("estadoAnterior", 32), Clave("estadoNuevo", 32));
        yield return Auditada(nameof(AccionesAuditables.DevolucionSolicitada), false, false, Clave("pedidoId", 40), Clave("cantidadDeLineas", 8));
        yield return Auditada(nameof(AccionesAuditables.EstadoDeDevolucionCambiado), false, false, Clave("estadoAnterior", 32), Clave("estadoNuevo", 32));

        // Traslados: lo que pide el plan de Traslados (número, edición, cantidad, estados anterior y nuevo, si es vinculado y el pedido opaco), nunca el motivo.
        yield return Auditada(nameof(AccionesAuditables.TrasladoSolicitado), false, false, Clave("numero", 24), Clave("edicionId", 40), Clave("cantidad", 12), Clave("vinculado", 8), Clave("pedidoId", 40));
        yield return Traslado(nameof(AccionesAuditables.TrasladoAprobado));
        yield return Traslado(nameof(AccionesAuditables.TrasladoRechazado));
        yield return Traslado(nameof(AccionesAuditables.TrasladoCancelado));
        yield return Traslado(nameof(AccionesAuditables.TrasladoDespachado));
        yield return Traslado(nameof(AccionesAuditables.TrasladoRecibido));
        yield return Auditada(nameof(AccionesAuditables.GuiaDeTrasladoRegistrada), false, false, Clave("numero", 24), Clave("guiaNueva", 40), Clave("reemplazoOtraGuia", 8));
    }

    private static AccionDelCatalogo Traslado(string identificador) =>
        Auditada(identificador, false, false, Clave("numero", 24), Clave("edicionId", 40), Clave("cantidad", 12), Clave("estadoAnterior", 32), Clave("estadoNuevo", 32), Clave("vinculado", 8), Clave("pedidoId", 40));
}
