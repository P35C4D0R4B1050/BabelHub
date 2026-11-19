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

    /// <summary>El Catálogo concilió un ingreso de mercadería pendiente con los ingresos del Inventario (registrado, fallido o discrepancia; sin la nota ni la referencia, CAT-22).</summary>
    public static readonly AccionAuditable IngresoPendienteConciliado = new(nameof(IngresoPendienteConciliado));

    /// <summary>El trabajo de fondo anonimizó los ingresos solicitados de más de 2 años (cantidad, sin datos personales).</summary>
    public static readonly AccionAuditable IngresosSolicitadosAnonimizados = new(nameof(IngresosSolicitadosAnonimizados));

    /// <summary>Un reingreso de stock por una devolución recibida.</summary>
    public static readonly AccionAuditable ReingresoPorDevolucion = new(nameof(ReingresoPorDevolucion));

    /// <summary>Un reembolso, manual (administración) o automático (actor «sistema»); nunca el número de tarjeta.</summary>
    public static readonly AccionAuditable ReembolsoRegistrado = new(nameof(ReembolsoRegistrado));

    public static readonly AccionAuditable EstadoDePedidoCambiado = new(nameof(EstadoDePedidoCambiado));

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
