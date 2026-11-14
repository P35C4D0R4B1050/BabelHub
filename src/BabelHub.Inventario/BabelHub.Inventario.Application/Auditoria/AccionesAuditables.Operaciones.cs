namespace BabelHub.Inventario.Application.Auditoria;

public static partial class AccionesAuditables
{
    // ---------- Operaciones del mínimo irrenunciable (AUD-02, AUD-26) ----------

    /// <summary>Cambió el precio de una edición (precio anterior y nuevo, CAT-11).</summary>
    public static readonly AccionAuditable PrecioDeEdicionCambiado = new(nameof(PrecioDeEdicionCambiado));

    /// <summary>Se anuló un pedido (la causa figura en el detalle).</summary>
    public static readonly AccionAuditable PedidoAnulado = new(nameof(PedidoAnulado));

    /// <summary>Un ajuste manual de stock (cantidad anterior y posterior, con la fuente en la sucursal de origen del registro).</summary>
    public static readonly AccionAuditable AjusteRegistrado = new(nameof(AjusteRegistrado));

    /// <summary>Un ingreso de mercadería en la fuente de recepción (una sola entrada para el ingreso y su ajuste, AJU-13).</summary>
    public static readonly AccionAuditable IngresoDeMercaderiaRegistrado = new(nameof(IngresoDeMercaderiaRegistrado));

    /// <summary>Un reingreso de stock por una devolución recibida.</summary>
    public static readonly AccionAuditable ReingresoPorDevolucion = new(nameof(ReingresoPorDevolucion));

    /// <summary>Un reembolso, manual (administración) o automático (actor «sistema»); nunca el número de tarjeta.</summary>
    public static readonly AccionAuditable ReembolsoRegistrado = new(nameof(ReembolsoRegistrado));

    public static readonly AccionAuditable EstadoDePedidoCambiado = new(nameof(EstadoDePedidoCambiado));

    public static readonly AccionAuditable EstadoDeEnvioCambiado = new(nameof(EstadoDeEnvioCambiado));

    public static readonly AccionAuditable DevolucionSolicitada = new(nameof(DevolucionSolicitada));

    /// <summary>El estado de una devolución cambió (aprobada, rechazada, recibida, reembolsada…).</summary>
    public static readonly AccionAuditable EstadoDeDevolucionCambiado = new(nameof(EstadoDeDevolucionCambiado));

    public static readonly AccionAuditable TrasladoSolicitado = new(nameof(TrasladoSolicitado));

    /// <summary>El estado de un traslado cambió (aprobado, rechazado, en tránsito, recibido…), con la fuente de origen y la de destino (TRA-13).</summary>
    public static readonly AccionAuditable EstadoDeTrasladoCambiado = new(nameof(EstadoDeTrasladoCambiado));
}
