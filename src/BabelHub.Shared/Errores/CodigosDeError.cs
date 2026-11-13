namespace BabelHub.Shared.Errores;

/// <summary>
/// Códigos estables de error: <c>&lt;SRV&gt;-&lt;SUFIJO&gt;</c> con <c>ECO</c> (E-commerce) o <c>INV</c> (Inventario). Los códigos
/// específicos de una regla de negocio (por ejemplo <c>INV-STOCK-INSUFICIENTE</c>) los declara cada feature.
/// </summary>
public static class CodigosDeError
{
    public const string Ecommerce = "ECO";
    public const string Inventario = "INV";

    public const string ValidacionInvalida = "VALIDACION-INVALIDA";
    public const string NoAutenticado = "NO-AUTENTICADO";
    public const string Prohibido = "PROHIBIDO";
    public const string NoEncontrado = "NO-ENCONTRADO";
    public const string Conflicto = "CONFLICTO";
    public const string ReglaDeNegocioIncumplida = "REGLA-DE-NEGOCIO-INCUMPLIDA";
    public const string LimiteExcedido = "LIMITE-EXCEDIDO";
    public const string ErrorInesperado = "ERROR-INESPERADO";
    public const string ServicioNoDisponible = "SERVICIO-NO-DISPONIBLE";
    public const string VerificacionRequerida = "VERIFICACION-REQUERIDA";

    /// <summary>El código completo de un sufijo para un servicio.</summary>
    public static string De(string servicio, string sufijo) => $"{servicio}-{sufijo}";
}
