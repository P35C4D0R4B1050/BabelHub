using System.Xml.Linq;

namespace BabelHub.ArchitectureTests;

/// <summary>Lee los archivos .csproj del repositorio para comprobar sus referencias.</summary>
internal sealed record Proyecto(string Nombre, IReadOnlySet<string> ProyectosReferenciados, IReadOnlySet<string> Paquetes);

internal static class Proyectos
{
    private static readonly Lazy<IReadOnlyList<Proyecto>> Src = new(() => Cargar("src"));
    private static readonly Lazy<IReadOnlyList<Proyecto>> Herramientas = new(() => Cargar("tools"));

    public static IReadOnlyList<Proyecto> DeSrc => Src.Value;

    /// <summary>Los proyectos de <c>tools/</c>: herramientas operativas que no son parte de los servicios.</summary>
    public static IReadOnlyList<Proyecto> DeTools => Herramientas.Value;

    public static string Raiz { get; } = EncontrarRaiz();

    private static string EncontrarRaiz()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "BabelHub.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("No se encontró BabelHub.sln subiendo desde el directorio de las pruebas.");
    }

    private static List<Proyecto> Cargar(string carpeta)
    {
        var ruta = Path.Combine(Raiz, carpeta);
        if (!Directory.Exists(ruta))
        {
            return [];
        }

        return Directory.EnumerateFiles(ruta, "*.csproj", SearchOption.AllDirectories)
            .Where(r => !r.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(r =>
            {
                var xml = XDocument.Load(r);
                var referencias = xml.Descendants("ProjectReference")
                    .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
                    .Select(i => Path.GetFileNameWithoutExtension(i.Replace('\\', '/')))
                    .ToHashSet(StringComparer.Ordinal);
                var paquetes = xml.Descendants("PackageReference")
                    .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
                    .ToHashSet(StringComparer.Ordinal);
                return new Proyecto(Path.GetFileNameWithoutExtension(r), referencias, paquetes);
            })
            .OrderBy(p => p.Nombre, StringComparer.Ordinal)
            .ToList();
    }
}
