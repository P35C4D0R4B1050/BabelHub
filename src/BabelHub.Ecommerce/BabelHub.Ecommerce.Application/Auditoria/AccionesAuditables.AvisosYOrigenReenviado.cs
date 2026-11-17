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

    /// <summary>
    /// Una acción directa de la interfaz web bloqueada porque la persona tiene textos legales pendientes (WEB-47): un evento de seguridad propio, no un acceso denegado, de modo que no cuenta para el
    /// umbral de SEG-09. Lleva el motivo del bloqueo, la plantilla de la ruta sin consulta, el método y la cuenta por su identificador opaco; nunca contraseñas ni el identificador de la sesión.
    /// </summary>
    public static readonly AccionAuditable AccesoBloqueadoPorTextosPendientes = new(nameof(AccesoBloqueadoPorTextosPendientes));
}
