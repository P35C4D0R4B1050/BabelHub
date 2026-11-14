namespace BabelHub.Ecommerce.Application.Auditoria;

public static partial class AccionesAuditables
{
    /// <summary>
    /// El administrador central marcó una alerta de seguridad como revisada hasta el contador que veía (AUD-34): si no se puede registrar, la alerta sigue
    /// pendiente. El objeto es la alerta (su identificador con prefijo de servicio) y el detalle solo lleva el servicio, el motivo del catálogo y el contador.
    /// </summary>
    public static readonly AccionAuditable AlertaMarcadaRevisada = new(nameof(AlertaMarcadaRevisada));

    /// <summary>
    /// Terminó la depuración diaria (AUD-36): con el actor «sistema» y el número de registros anonimizados, aunque sea cero. Sin objeto.
    /// </summary>
    public static readonly AccionAuditable DepuracionDiariaEjecutada = new(nameof(DepuracionDiariaEjecutada));
}
