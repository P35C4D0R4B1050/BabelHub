using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BabelHub.IntegrationTests.CadenaDeSuministro;

/// <summary>
/// Las reglas de la cadena de suministro (PLA-26, D04): todo lo que el sistema usa queda fijado a una versión exacta y su contenido no
/// cambia sin un cambio revisado. Cada regla recibe el texto de un archivo y devuelve un mensaje por incumplimiento, con el archivo, la
/// línea y lo que se encontró; una lista vacía significa que todo está fijado.
/// </summary>
internal static partial class VersionesFijadas
{
    /// <summary>Una acción de GitHub se fija por el SHA de 40 caracteres de un commit, no por una etiqueta o rama que pueda moverse.</summary>
    public static List<string> Acciones(string ruta, string texto)
    {
        var incumplimientos = new List<string>();
        var lineas = texto.Split('\n');
        for (var i = 0; i < lineas.Length; i++)
        {
            var coincidencia = UsoDeAccion().Match(lineas[i]);
            if (!coincidencia.Success)
            {
                continue;
            }

            var referencia = coincidencia.Groups["ref"].Value.Trim('"', '\'');
            if (referencia.StartsWith("./", StringComparison.Ordinal))
            {
                continue;
            }

            var fijada = referencia.StartsWith("docker://", StringComparison.Ordinal)
                ? ImagenConDigest().IsMatch(referencia["docker://".Length..])
                : ShaDeAccion().IsMatch(referencia);
            if (!fijada)
            {
                incumplimientos.Add($"{ruta}:{i + 1}: la acción {referencia} no está fijada por el SHA de un commit.");
            }
        }

        return incumplimientos;
    }

    /// <summary>
    /// Una imagen se fija por etiqueta y por <c>digest</c> (<c>nombre:etiqueta@sha256:…</c>): el contenido de una etiqueta puede
    /// cambiar, el <c>digest</c> no. Revisa archivos de Compose (<c>image:</c>), Dockerfiles (<c>FROM</c>) y las variables
    /// <c>*_IMAGE="${VAR:-imagen}"</c> de los scripts.
    /// </summary>
    public static List<string> Imagenes(string ruta, string texto)
    {
        var incumplimientos = new List<string>();
        var lineas = texto.Split('\n');
        var etapas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < lineas.Length; i++)
        {
            string? imagen = null;
            if (ruta.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || ruta.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
            {
                imagen = ImagenDeCompose().Match(lineas[i]) is { Success: true } c ? c.Groups["ref"].Value.Trim('"', '\'') : null;
            }
            else if (Path.GetFileName(ruta).StartsWith("Dockerfile", StringComparison.OrdinalIgnoreCase))
            {
                if (DesdeEnDockerfile().Match(lineas[i]) is { Success: true } d)
                {
                    var origen = d.Groups["ref"].Value;
                    if (!string.Equals(origen, "scratch", StringComparison.OrdinalIgnoreCase) && !etapas.Contains(origen))
                    {
                        imagen = origen;
                    }

                    if (d.Groups["etapa"].Success)
                    {
                        etapas.Add(d.Groups["etapa"].Value);
                    }
                }
            }
            else if (ruta.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
            {
                imagen = ImagenDeScript().Match(lineas[i]) is { Success: true } s ? s.Groups["ref"].Value : null;
            }

            if (imagen is null || (ruta.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) && ImagenPropiaPorVariable().IsMatch(imagen)))
            {
                continue;
            }

            if (imagen.EndsWith(":latest", StringComparison.OrdinalIgnoreCase) || imagen.Contains(":latest@", StringComparison.OrdinalIgnoreCase))
            {
                incumplimientos.Add($"{ruta}:{i + 1}: la imagen {imagen} usa la etiqueta flotante latest.");
            }
            else if (!ImagenConDigest().IsMatch(imagen))
            {
                incumplimientos.Add($"{ruta}:{i + 1}: la imagen {imagen} no tiene su contenido fijado por digest (@sha256:…).");
            }
        }

        return incumplimientos;
    }

    /// <summary>Las versiones de <c>Directory.Packages.props</c> son exactas (ni rangos ni comodines) y la gestión es central.</summary>
    public static List<string> VersionesCentralizadas(string ruta, string texto)
    {
        var incumplimientos = new List<string>();
        var documento = XDocument.Parse(texto);

        if (!documento.Descendants("ManagePackageVersionsCentrally").Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            incumplimientos.Add($"{ruta}: las versiones de los paquetes no se gestionan de forma central (ManagePackageVersionsCentrally).");
        }

        foreach (var paquete in documento.Descendants("PackageVersion"))
        {
            var nombre = (string?)paquete.Attribute("Include") ?? "(sin nombre)";
            var version = (string?)paquete.Attribute("Version") ?? string.Empty;
            if (!VersionExacta().IsMatch(version))
            {
                incumplimientos.Add($"{ruta}: el paquete {nombre} tiene la versión «{version}», que no es una versión exacta.");
            }
        }

        return incumplimientos;
    }

    /// <summary>Un proyecto no fija versiones de paquetes por su cuenta: las toma de las versiones centralizadas.</summary>
    public static List<string> ReferenciasDeProyecto(string ruta, string texto)
    {
        var incumplimientos = new List<string>();
        foreach (var referencia in XDocument.Parse(texto).Descendants("PackageReference"))
        {
            var nombre = (string?)referencia.Attribute("Include") ?? "(sin nombre)";
            if (referencia.Attribute("Version") is not null || referencia.Attribute("VersionOverride") is not null)
            {
                incumplimientos.Add($"{ruta}: el paquete {nombre} fija su propia versión en vez de usar la centralizada.");
            }
        }

        return incumplimientos;
    }

    /// <summary>El bloqueo de paquetes está activado y obligatorio en CI, y la versión del SDK es exacta.</summary>
    public static List<string> Bloqueo(string propiedades, string globalJson)
    {
        var incumplimientos = new List<string>();
        var documento = XDocument.Parse(propiedades);

        if (!documento.Descendants("RestorePackagesWithLockFile").Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)))
        {
            incumplimientos.Add("Directory.Build.props: falta RestorePackagesWithLockFile en true (bloqueo de paquetes).");
        }

        if (!documento.Descendants("RestoreLockedMode").Any(e => string.Equals(e.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)
                                                              && ((string?)e.Attribute("Condition") ?? string.Empty).Contains("CI", StringComparison.Ordinal)))
        {
            incumplimientos.Add("Directory.Build.props: falta RestoreLockedMode en true cuando corre CI (el restore debe fallar si el bloqueo cambia).");
        }

        if (!VersionDeSdk().IsMatch(globalJson))
        {
            incumplimientos.Add("global.json: la versión del SDK no es exacta.");
        }

        return incumplimientos;
    }

    /// <summary>Todo proyecto tiene su <c>packages.lock.json</c> al lado.</summary>
    public static List<string> ArchivosDeBloqueo(IEnumerable<string> proyectos, Func<string, bool> existe) =>
        [.. proyectos
            .Where(p => !existe(Path.Combine(Path.GetDirectoryName(p) ?? string.Empty, "packages.lock.json")))
            .Select(p => $"{p}: no tiene su packages.lock.json.")];

    /// <summary>
    /// Una imagen propia de un Compose llega por <c>${IMAGEN_NOMBRE:?mensaje}</c>: su digest lo pone el flujo de lanzamiento con la imagen
    /// que publicó y <c>desplegar.sh</c> rechaza un valor sin <c>@sha256:</c> (T23). La parte «:?» obliga a darla: sin ella no se levanta nada.
    /// </summary>
    [GeneratedRegex(@"^\$\{IMAGEN_[A-Z0-9_]+:\?[^}]+\}$", RegexOptions.None, 2000)]
    private static partial Regex ImagenPropiaPorVariable();

    [GeneratedRegex(@"^\s*-?\s*uses:\s*(?<ref>\S+)", RegexOptions.None, 2000)]
    private static partial Regex UsoDeAccion();

    [GeneratedRegex(@"^[\w.-]+/[\w./-]+@[0-9a-f]{40}$", RegexOptions.None, 2000)]
    private static partial Regex ShaDeAccion();

    [GeneratedRegex(@"^\s*image:\s*(?<ref>[^#\r\n]+?)\s*(?:#.*)?$", RegexOptions.None, 2000)]
    private static partial Regex ImagenDeCompose();

    [GeneratedRegex(@"^\s*FROM\s+(?:--platform=\S+\s+)?(?<ref>\S+)(?:\s+AS\s+(?<etapa>\S+))?\s*$", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex DesdeEnDockerfile();

    [GeneratedRegex(@"^\s*[A-Z_]*IMAGE=""\$\{[A-Z_]+:-(?<ref>[^}]+)\}""", RegexOptions.None, 2000)]
    private static partial Regex ImagenDeScript();

    [GeneratedRegex(@"^[\w./-]+(?::[\w.-]+)?@sha256:[0-9a-f]{64}$", RegexOptions.None, 2000)]
    private static partial Regex ImagenConDigest();

    [GeneratedRegex(@"^\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z.-]+)?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.None, 2000)]
    private static partial Regex VersionExacta();

    [GeneratedRegex(@"""version""\s*:\s*""\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?""", RegexOptions.None, 2000)]
    private static partial Regex VersionDeSdk();
}
