namespace BabelHub.Ecommerce.Application.Auditoria;

public static partial class AccionesAuditables
{
    // ---------- Operaciones del mínimo irrenunciable (AUD-02, AUD-26) ----------

    /// <summary>Cambió el precio de una edición (precio anterior y nuevo, CAT-11).</summary>
    public static readonly AccionAuditable PrecioDeEdicionCambiado = new(nameof(PrecioDeEdicionCambiado));

    /// <summary>Se anuló un pedido (la causa figura en el detalle).</summary>
    public static readonly AccionAuditable PedidoAnulado = new(nameof(PedidoAnulado));

    /// <summary>Un ajuste manual de stock (cantidad anterior y posterior, con la fuente en la sucursal de origen del registro). La nota libre no se audita.</summary>
    public static readonly AccionAuditable AjusteRegistrado = new(nameof(AjusteRegistrado));

    /// <summary>Un ingreso de mercadería en la fuente de recepción (una sola entrada para el ingreso y su ajuste, AJU-13). La referencia libre no se audita.</summary>
    public static readonly AccionAuditable IngresoDeMercaderiaRegistrado = new(nameof(IngresoDeMercaderiaRegistrado));

    /// <summary>Un cliente inició un cobro (un intento con un medio): el importe y el medio, sin token ni datos de pago (PAG-11).</summary>
    public static readonly AccionAuditable CobroIniciado = new(nameof(CobroIniciado));

    /// <summary>Se resolvió un cobro: aprobado, rechazado (con su motivo de la lista cerrada) o pendiente de confirmación (PAG-11).</summary>
    public static readonly AccionAuditable CobroResuelto = new(nameof(CobroResuelto));

    /// <summary>El aviso firmado de la pasarela adelantó la consulta de un intento sin resolver (PAG-19): sin el cuerpo del aviso ni datos de pago.</summary>
    public static readonly AccionAuditable AvisoDeLaPasarelaRecibido = new(nameof(AvisoDeLaPasarelaRecibido));

    /// <summary>El Catálogo concilió un ingreso de mercadería pendiente con los ingresos del Inventario (registrado, fallido o discrepancia; sin la nota ni la referencia, CAT-22).</summary>
    public static readonly AccionAuditable IngresoPendienteConciliado = new(nameof(IngresoPendienteConciliado));

    /// <summary>El trabajo de fondo anonimizó los ingresos solicitados de más de 2 años (cantidad, sin datos personales).</summary>
    public static readonly AccionAuditable IngresosSolicitadosAnonimizados = new(nameof(IngresosSolicitadosAnonimizados));

    /// <summary>El trabajo de fondo desvinculó las intenciones de pago cerradas hace más de 2 años (cantidad, sin datos personales, PAG-26).</summary>
    public static readonly AccionAuditable IntencionesDePagoDesvinculadas = new(nameof(IntencionesDePagoDesvinculadas));

    /// <summary>Un reembolso de Pagos se concluyó o quedó pendiente (con su motivo de lista cerrada); el actor es el administrador central o el sistema. Sin datos de pago.</summary>
    public static readonly AccionAuditable ReembolsoResuelto = new(nameof(ReembolsoResuelto));

    /// <summary>Un paso de la compensación de un cobro se hizo (abrir, liberar reservas, registrar el reembolso o cerrar el checkout), con su motivo de lista cerrada.</summary>
    public static readonly AccionAuditable PasoDeCompensacionHecho = new(nameof(PasoDeCompensacionHecho));

    /// <summary>Pagos eliminó el contacto de entrega de una o varias intenciones (cantidad, sin datos personales, PAG-27).</summary>
    public static readonly AccionAuditable ContactoDeEntregaLiberado = new(nameof(ContactoDeEntregaLiberado));

    /// <summary>Carrito aplicó una fusión del carrito de visitante a la cuenta (cuántas líneas aportó y los motivos de los ajustes; sin identificar al visitante).</summary>
    public static readonly AccionAuditable FusionDeCarritoAplicada = new(nameof(FusionDeCarritoAplicada));

    /// <summary>La baja de ediciones se aplicó a los carritos: cuántas ediciones, carritos afectados y líneas quitadas o anotadas con retiro pendiente (sin identificar a nadie).</summary>
    public static readonly AccionAuditable EdicionesRetiradasDeCarritos = new(nameof(EdicionesRetiradasDeCarritos));

    /// <summary>El checkout terminó por voluntad de la persona (volver o reiniciar), con su motivo de lista cerrada.</summary>
    public static readonly AccionAuditable CheckoutTerminado = new(nameof(CheckoutTerminado));

    /// <summary>Identidad pidió eliminar el carrito y los datos de entrega de una cuenta (reemplazo, cancelación o cuenta inactiva); sin datos personales.</summary>
    public static readonly AccionAuditable CarritoEliminadoPorIdentidad = new(nameof(CarritoEliminadoPorIdentidad));

    /// <summary>Una corrida de purga de Carrito: cuántos registros eliminó o venció de cada tipo (solo conteos, D09).</summary>
    public static readonly AccionAuditable PurgaDeCarritosRealizada = new(nameof(PurgaDeCarritosRealizada));

    /// <summary>Se abortó el paso al pago, o lo recuperó el trabajo de fondo, con un motivo cerrado (DD-11); sin datos personales ni identificadores.</summary>
    public static readonly AccionAuditable PasoAlPagoAbortado = new(nameof(PasoAlPagoAbortado));

    /// <summary>Se pasó al pago (intención creada y checkout en pago): con o sin impresos y el tipo de entrega; sin contacto, cuenta ni pedido (D13).</summary>
    public static readonly AccionAuditable PasoAlPagoIniciado = new(nameof(PasoAlPagoIniciado));

    /// <summary>Un reingreso de stock por una devolución recibida.</summary>
    public static readonly AccionAuditable ReingresoPorDevolucion = new(nameof(ReingresoPorDevolucion));

    /// <summary>Un reembolso, manual (administración) o automático (actor «sistema»); nunca el número de tarjeta.</summary>
    public static readonly AccionAuditable ReembolsoRegistrado = new(nameof(ReembolsoRegistrado));

    public static readonly AccionAuditable EstadoDePedidoCambiado = new(nameof(EstadoDePedidoCambiado));

    /// <summary>El contacto de entrega de pedidos se anonimizó al vencer su conservación (actor «sistema»; la causa y el conteo, nunca el contacto).</summary>
    public static readonly AccionAuditable ContactoDePedidoAnonimizado = new(nameof(ContactoDePedidoAnonimizado));

    /// <summary>Alguien del personal leyó el detalle de un pedido con el contacto del cliente (el pedido y el rol; nunca el contacto).</summary>
    public static readonly AccionAuditable ContactoDePedidoConsultado = new(nameof(ContactoDePedidoConsultado));

    /// <summary>Una línea de un pedido de recojo se entregó y su reserva se cerró en el Inventario.</summary>
    public static readonly AccionAuditable LineaDePedidoEntregada = new(nameof(LineaDePedidoEntregada));

    /// <summary>Se inició una anulación o una entrega de pedido (primer tiempo, con el actor de la solicitud y su iniciador); sin esta auditoría no hay fila ni llamada al Inventario.</summary>
    public static readonly AccionAuditable OperacionDePedidoIniciada = new(nameof(OperacionDePedidoIniciada));

    /// <summary>El cliente canceló su pedido.</summary>
    public static readonly AccionAuditable PedidoCanceladoPorElCliente = new(nameof(PedidoCanceladoPorElCliente));

    /// <summary>La depuración de pedidos vencidos (actor «sistema» y conteo).</summary>
    public static readonly AccionAuditable PedidosDepurados = new(nameof(PedidosDepurados));

    /// <summary>La administración reintentó el reembolso pendiente de un pedido.</summary>
    public static readonly AccionAuditable ReembolsoDePedidoReintentado = new(nameof(ReembolsoDePedidoReintentado));

    public static readonly AccionAuditable EstadoDeEnvioCambiado = new(nameof(EstadoDeEnvioCambiado));

    public static readonly AccionAuditable DevolucionSolicitada = new(nameof(DevolucionSolicitada));

    /// <summary>El estado de una devolución cambió (aprobada, rechazada, recibida, reembolsada…).</summary>
    public static readonly AccionAuditable EstadoDeDevolucionCambiado = new(nameof(EstadoDeDevolucionCambiado));

    // ---------- Traslados (AUD-02, TRA-13): una acción por transición, con la fuente de origen y la de destino en las sucursales del registro ----------

    public static readonly AccionAuditable TrasladoSolicitado = new(nameof(TrasladoSolicitado));

    public static readonly AccionAuditable TrasladoAprobado = new(nameof(TrasladoAprobado));

    public static readonly AccionAuditable TrasladoRechazado = new(nameof(TrasladoRechazado));

    public static readonly AccionAuditable TrasladoCancelado = new(nameof(TrasladoCancelado));

    public static readonly AccionAuditable TrasladoDespachado = new(nameof(TrasladoDespachado));

    public static readonly AccionAuditable TrasladoRecibido = new(nameof(TrasladoRecibido));

    /// <summary>Se registró o reemplazó la guía de un traslado: no cambia su estado (TRA-12); guarda el valor nuevo y si reemplazó a otra, no la anterior.</summary>
    public static readonly AccionAuditable GuiaDeTrasladoRegistrada = new(nameof(GuiaDeTrasladoRegistrada));
}
