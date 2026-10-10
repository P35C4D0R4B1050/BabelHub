using System.Xml.Linq;

namespace BabelHub.ArchitectureTests;

/// <summary>Lee los archivos .csproj del repositorio para comprobar sus referencias.</summary>
internal sealed record Proyecto(string Nombre, IReadOnlySet<string> ProyectosReferenciados, IReadOnlySet<string> Paquetes);

internal static class Proyectos
{
    private static readonly Lazy<IReadOnlyList<Proyecto>> Todos = new(Cargar);

    public static IReadOnlyList<Proyecto> DeSrc => Todos.Value;

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

    private static IReadOnlyList<Proyecto> Cargar()
    {
        var src = Path.Combine(Raiz, "src");
        return Directory.EnumerateFiles(src, "*.csproj", SearchOption.AllDirectories)
            .Select(ruta =>
            {
                var xml = XDocument.Load(ruta);
                var referencias = xml.Descendants("ProjectReference")
                    .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
                    .Select(i => Path.GetFileNameWithoutExtension(i.Replace('\\', '/')))
                    .ToHashSet(StringComparer.Ordinal);
                var paquetes = xml.Descendants("PackageReference")
                    .Select(e => (string?)e.Attribute("Include") ?? string.Empty)
                    .ToHashSet(StringComparer.Ordinal);
                return new Proyecto(Path.GetFileNameWithoutExtension(ruta), referencias, paquetes);
            })
            .OrderBy(p => p.Nombre, StringComparer.Ordinal)
            .ToList();
    }
}
