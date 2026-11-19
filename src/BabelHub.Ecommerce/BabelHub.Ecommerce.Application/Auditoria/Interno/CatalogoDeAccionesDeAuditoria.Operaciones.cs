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
        yield return Auditada(nameof(AccionesAuditables.ReingresoPorDevolucion), false, false, Clave("ajusteId", 40), Clave("devolucionId", 40), Clave("edicionId", 40), Clave("cantidadAnterior", 12), Clave("cantidadPosterior", 12), Clave("pedidoId", 40), Clave("monto", 16), Moneda());
        yield return Auditada(nameof(AccionesAuditables.ReembolsoRegistrado), false, false, Clave("monto", 16), Moneda(), Modalidad(), Clave("medio", 32));
        yield return Auditada(nameof(AccionesAuditables.EstadoDePedidoCambiado), false, false, Clave("estadoAnterior", 32), Clave("estadoNuevo", 32), ClavePersonal("iniciadaPor"));
        yield return Auditada(nameof(AccionesAuditables.ContactoDePedidoConsultado), false, false, Clave("rol", 32));
        yield return Auditada(nameof(AccionesAuditables.OperacionDePedidoIniciada), false, false, new ClaveDeDetalle("operacion", personal: false) { Valores = ["anulacion", "entrega"] }, ClavePersonal("iniciadaPor"));
        yield return Auditada(nameof(AccionesAuditables.PedidoCanceladoPorElCliente), false, false, Clave("estadoAnterior", 32));
        yield return Auditada(nameof(AccionesAuditables.ReembolsoDePedidoReintentado), false, false, new ClaveDeDetalle("resultado", personal: false) { Valores = ["reembolsado", "pendiente"] }, Clave("monto", 16), Moneda());
        yield return Auditada(nameof(AccionesAuditables.LineaDePedidoEntregada), false, false, Clave("lineaId", 40), ClavePersonal("iniciadaPor"));
        yield return Auditada(nameof(AccionesAuditables.ContactoDePedidoAnonimizado), false, false, Clave("causa"), Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.PedidosDepurados), false, false, Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.DevolucionesDepuradas), false, false, Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.DevolucionesDeCuentaRedactadas), false, false, Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.EstadoDeEnvioCambiado), false, false, Clave("estadoAnterior", 32), Clave("estadoNuevo", 32), Clave("pedidoId", 40), Clave("numeroDeGuia", 40), Clave("operador", 32), Clave("fuente", 40), Clave("fecha", 32));
        yield return Auditada(nameof(AccionesAuditables.DevolucionSolicitada), false, false, Clave("pedidoId", 40), Clave("cantidadDeLineas", 8), Clave("monto", 16), Moneda());
        yield return Auditada(nameof(AccionesAuditables.EstadoDeDevolucionCambiado), false, false, Clave("estadoAnterior", 32), Clave("estadoNuevo", 32), Clave("pedidoId", 40), Clave("devolucionId", 40), Clave("monto", 16), Moneda());

        // Traslados: lo que pide el plan de Traslados (número, edición, cantidad, estados anterior y nuevo, si es vinculado y el pedido opaco), nunca el motivo.
        yield return Auditada(nameof(AccionesAuditables.TrasladoSolicitado), false, false, Clave("numero", 24), Clave("edicionId", 40), Clave("cantidad", 12), Clave("vinculado", 8), Clave("pedidoId", 40));
        yield return Traslado(nameof(AccionesAuditables.TrasladoAprobado));
        yield return Traslado(nameof(AccionesAuditables.TrasladoRechazado));
        yield return Traslado(nameof(AccionesAuditables.TrasladoCancelado));
        yield return Traslado(nameof(AccionesAuditables.TrasladoDespachado));
        yield return Traslado(nameof(AccionesAuditables.TrasladoRecibido));
        foreach (var carrito in AccionesDeCarrito())
        {
            yield return carrito;
        }
        yield return Auditada(nameof(AccionesAuditables.GuiaDeTrasladoRegistrada), false, false, Clave("numero", 24), Clave("guiaNueva", 40), Clave("reemplazoOtraGuia", 8));
    }

    /// <summary>Las acciones de Carrito (plan de carrito-checkout §6): en la misma transacción del caso de uso; sin datos personales, sin la cookie del visitante ni su huella, sin identificar al visitante ni al pedido.</summary>
    private static IEnumerable<AccionDelCatalogo> AccionesDeCarrito()
    {
        yield return Auditada(nameof(AccionesAuditables.FusionDeCarritoAplicada), false, false, Clave("lineasDelVisitante", 12), Clave("ajustes", 120));
        yield return Auditada(nameof(AccionesAuditables.EdicionesRetiradasDeCarritos), false, false, Clave("ediciones", 12), Clave("carritosAfectados", 12), Clave("lineasQuitadas", 12), Clave("lineasAnotadas", 12));
        yield return Auditada(nameof(AccionesAuditables.CheckoutTerminado), false, false, Clave("motivo", 40));
        yield return Auditada(nameof(AccionesAuditables.CarritoEliminadoPorIdentidad), false, false, new ClaveDeDetalle("motivo", personal: false) { Valores = ["reemplazo", "cancelacion", "cuenta-inactiva"] });
        yield return Auditada(nameof(AccionesAuditables.PurgaDeCarritosRealizada), false, false, Clave("trabajo", 40), Clave("cantidad", 12));
        yield return Auditada(nameof(AccionesAuditables.PasoAlPagoAbortado), false, false, Clave("motivo", 40));
        yield return Auditada(nameof(AccionesAuditables.PasoAlPagoIniciado), false, false, new ClaveDeDetalle("conImpresos", personal: false) { Valores = ["true", "false"] }, new ClaveDeDetalle("aDomicilio", personal: false) { Valores = ["true", "false"] });
    }

    private static AccionDelCatalogo Traslado(string identificador) =>
        Auditada(identificador, false, false, Clave("numero", 24), Clave("edicionId", 40), Clave("cantidad", 12), Clave("estadoAnterior", 32), Clave("estadoNuevo", 32), Clave("vinculado", 8), Clave("pedidoId", 40));
}
