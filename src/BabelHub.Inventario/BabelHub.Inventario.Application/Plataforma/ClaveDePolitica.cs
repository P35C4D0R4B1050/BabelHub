using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Application.Plataforma;

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

/// <summary>Claves de las políticas del Inventario (los valores están en el Dominio de Plataforma).</summary>
public static class PoliticasDeLimite
{
    public static ClaveDePolitica AjustesDelPersonal { get; } = new("ajustes-del-personal");

    public static ClaveDePolitica TrasladosDelPersonal { get; } = new("traslados-del-personal");

    public static ClaveDePolitica SolicitudesPorUsuario { get; } = new("solicitudes-por-usuario");

    /// <summary>Los límites generales de uso (con respaldo local), aparte de las políticas críticas de <see cref="Todas"/>.</summary>
    public static IReadOnlyList<ClaveDePolitica> Generales { get; } = [SolicitudesPorUsuario];

    public static IReadOnlyList<ClaveDePolitica> Todas { get; } = [AjustesDelPersonal, TrasladosDelPersonal];
}
