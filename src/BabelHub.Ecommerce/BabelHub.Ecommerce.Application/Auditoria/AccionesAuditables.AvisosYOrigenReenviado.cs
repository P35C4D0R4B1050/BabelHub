namespace BabelHub.Ecommerce.Application.Auditoria;

public static partial class AccionesAuditables
{
    /// <summary>
    /// Se suprimió un aviso de seguridad por correo: por la cuota, por un tope por cuenta o por origen, o por un correo sin verificar (AUD-43,
    /// IDE-79). Guarda el identificador de la cuenta y el motivo, nunca el correo de destino ni el contenido del aviso.
    /// </summary>
    public static readonly AccionAuditable AvisoDeSeguridadSuprimido = new(nameof(AvisoDeSeguridadSuprimido), aislada: true);

    /// <summary>
    /// Una llamada al Inventario trajo una dirección reenviada sin la credencial de alcance «origen reenviado» (AUD-44, SEG-32). El origen del evento es
    /// solo la dirección directa del llamante: la dirección reenviada, completa o en parte, nunca se guarda.
    /// </summary>
    public static readonly AccionAuditable OrigenReenviadoSinCredencial = new(nameof(OrigenReenviadoSinCredencial));
}
