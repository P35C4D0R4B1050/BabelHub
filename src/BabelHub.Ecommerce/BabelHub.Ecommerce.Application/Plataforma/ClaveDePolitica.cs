using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Clave estable y tipada de una política de límite. Los demás módulos nombran las políticas con ella: los valores
/// (máximo, ventana, bloqueo) son datos de la plataforma (D04) y no salen de su Dominio.
/// </summary>
public readonly partial record struct ClaveDePolitica
{
    public ClaveDePolitica(string valor)
    {
        if (string.IsNullOrEmpty(valor) || !Forma().IsMatch(valor))
        {
            throw new ArgumentException("La clave de política es un identificador estable en minúsculas, con guiones.", nameof(valor));
        }

        Valor = valor;
    }

    public string Valor { get; }

    public override string ToString() => Valor;

    [GeneratedRegex("^[a-z][a-z0-9-]{1,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex Forma();
}

/// <summary>Claves de las políticas que usan los módulos del E-commerce (los valores están en el Dominio de Plataforma).</summary>
public static class PoliticasDeLimite
{
    public static ClaveDePolitica IngresosFallidos { get; } = new("ingresos-fallidos");

    public static ClaveDePolitica CodigosPorCorreo { get; } = new("codigos-por-correo");

    public static ClaveDePolitica CodigosPorOrigen { get; } = new("codigos-por-origen");

    public static ClaveDePolitica SegundoFactorPorOrigen { get; } = new("segundo-factor-por-origen");

    public static ClaveDePolitica SegundoFactorPorCuenta { get; } = new("segundo-factor-por-cuenta");

    public static ClaveDePolitica RegistrosPorOrigen { get; } = new("registros-por-origen");

    /// <summary>3 hojas de reclamación por hora y por origen (REC-11).</summary>
    public static ClaveDePolitica HojasPorOrigen { get; } = new("hojas-por-origen");

    public static ClaveDePolitica SolicitudesPreviasPorOrigen { get; } = new("solicitudes-previas-por-origen");

    public static ClaveDePolitica SolicitudesDelOrigenDesconocido { get; } = new("solicitudes-del-origen-desconocido");

    public static ClaveDePolitica SolicitudesSinSesionPorOrigen { get; } = new("solicitudes-sin-sesion-por-origen");

    public static ClaveDePolitica SolicitudesPorUsuario { get; } = new("solicitudes-por-usuario");

    public static ClaveDePolitica SolicitudesPorHuellaDirecta { get; } = new("solicitudes-por-huella-directa");

    /// <summary>Los límites generales de uso (con respaldo local), aparte de las políticas críticas de <see cref="Todas"/>.</summary>
    public static IReadOnlyList<ClaveDePolitica> Generales { get; } =
        [SolicitudesPreviasPorOrigen, SolicitudesDelOrigenDesconocido, SolicitudesSinSesionPorOrigen, SolicitudesPorUsuario, SolicitudesPorHuellaDirecta, HojasPorOrigen];

    public static IReadOnlyList<ClaveDePolitica> Todas { get; } =
    [
        IngresosFallidos,
        CodigosPorCorreo,
        CodigosPorOrigen,
        SegundoFactorPorOrigen,
        SegundoFactorPorCuenta,
        RegistrosPorOrigen,
    ];
}
