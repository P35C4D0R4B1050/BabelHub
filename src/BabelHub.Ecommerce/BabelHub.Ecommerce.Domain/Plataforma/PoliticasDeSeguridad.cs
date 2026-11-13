namespace BabelHub.Ecommerce.Domain.Plataforma;

/// <summary>
/// Políticas de límite de uso de la identidad (SEG-06, SEG-19, SEG-20, SEG-24 y SEG-29). Los valores son los iniciales de
/// la spec; la Aplicación los sobrescribe con la configuración cuando se ajustan. Ninguna cae al motor local (SEG-26):
/// ingreso, registro y códigos fallan cerrados cuando el almacén de contadores no responde.
/// </summary>
public static class PoliticasDeSeguridad
{
    /// <summary>5 ingresos fallidos de una combinación cuenta-origen en 15 minutos bloquean 15 minutos (SEG-06). Un ingreso correcto devuelve el cupo.</summary>
    public static PoliticaDeLimite IngresosFallidos { get; } = new(
        "ingresos-fallidos", 5, TimeSpan.FromMinutes(15), TipoDeSujetoDeLimite.Combinacion,
        duracionDeBloqueo: TimeSpan.FromMinutes(15), liberable: true);

    /// <summary>3 solicitudes de código por hora y por correo destino, exista o no la cuenta (SEG-19).</summary>
    public static PoliticaDeLimite CodigosPorCorreo { get; } = new(
        "codigos-por-correo", 3, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    /// <summary>3 solicitudes de código por hora y por origen (SEG-19).</summary>
    public static PoliticaDeLimite CodigosPorOrigen { get; } = new(
        "codigos-por-origen", 3, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen);

    /// <summary>5 segundos factores incorrectos de una combinación cuenta-origen en 15 minutos bloquean 15 minutos (SEG-20).</summary>
    public static PoliticaDeLimite SegundoFactorPorOrigen { get; } = new(
        "segundo-factor-por-origen", 5, TimeSpan.FromMinutes(15), TipoDeSujetoDeLimite.Combinacion,
        duracionDeBloqueo: TimeSpan.FromMinutes(15), liberable: true);

    /// <summary>10 segundos factores incorrectos por cuenta y hora, desde cualquier origen, bloquean 15 minutos (SEG-29).</summary>
    public static PoliticaDeLimite SegundoFactorPorCuenta { get; } = new(
        "segundo-factor-por-cuenta", 10, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario,
        duracionDeBloqueo: TimeSpan.FromMinutes(15), liberable: true);

    /// <summary>3 cuentas nuevas por hora y por origen, con una ventana deslizante (SEG-24).</summary>
    public static PoliticaDeLimite RegistrosPorOrigen { get; } = new(
        "registros-por-origen", 3, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen);

    public static IReadOnlyList<PoliticaDeLimite> Todas { get; } =
    [
        IngresosFallidos,
        CodigosPorCorreo,
        CodigosPorOrigen,
        SegundoFactorPorOrigen,
        SegundoFactorPorCuenta,
        RegistrosPorOrigen,
    ];
}
