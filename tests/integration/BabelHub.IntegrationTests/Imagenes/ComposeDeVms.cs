using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>Lo que comparten las pruebas de los archivos de Compose por VM: resolverlos con un entorno de muestra y ejecutar <c>docker compose</c>.</summary>
internal static partial class ComposeDeVms
{
    public static readonly string[] Vms = ["borde", "ventas", "inventario"];

    public const string DigestDeMuestra = "sha256:0000000000000000000000000000000000000000000000000000000000000000";

    public static string Raiz()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    public static string Archivo(string vm) => Path.Combine(Raiz(), "infra", $"docker-compose.{vm}.yml");

    public static string Texto(string vm) => File.ReadAllText(Archivo(vm));

    /// <summary>Los archivos de secretos que el Compose pide dentro de <c>BABELHUB_SECRETOS</c> (variables <c>.env</c> y certificados).</summary>
    public static IReadOnlyList<string> ArchivosDeSecretos(string vm) =>
        [.. SecretoReferenciado().Matches(Texto(vm)).Select(m => m.Groups["archivo"].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Las variables obligatorias (<c>${NOMBRE:?…}</c>) del archivo.</summary>
    public static IReadOnlyList<string> VariablesObligatorias(string vm) =>
        [.. VariableObligatoria().Matches(Texto(vm)).Select(m => m.Groups["nombre"].Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    /// <summary>Un entorno de muestra completo: una carpeta con los archivos de secretos (vacíos) y valores para cada variable obligatoria.</summary>
    public static Dictionary<string, string> EntornoDeMuestra(string vm, string carpetaDeSecretos, string? imagenes = null)
    {
        Directory.CreateDirectory(carpetaDeSecretos);
        foreach (var archivo in ArchivosDeSecretos(vm))
        {
            var ruta = Path.Combine(carpetaDeSecretos, archivo);
            if (!File.Exists(ruta))
            {
                File.WriteAllText(ruta, string.Empty);
            }
        }

        var entorno = new Dictionary<string, string>(StringComparer.Ordinal) { ["BABELHUB_SECRETOS"] = carpetaDeSecretos.Replace('\\', '/') };
        foreach (var variable in VariablesObligatorias(vm).Where(v => v != "BABELHUB_SECRETOS"))
        {
            entorno[variable] = variable.StartsWith("IMAGEN_", StringComparison.Ordinal)
                ? $"{imagenes ?? "registro.ejemplo/babelhub/" + variable[7..].ToLowerInvariant()}@{DigestDeMuestra}"
                : "10.0.0.10";
        }

        return entorno;
    }

    public static (int Codigo, string Salida, string Errores) Docker(TimeSpan plazo, IReadOnlyDictionary<string, string>? entorno, params string[] argumentos)
    {
        var informacion = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.Combine(Raiz(), "infra") };
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        foreach (var (clave, valor) in entorno ?? new Dictionary<string, string>())
        {
            informacion.Environment[clave] = valor;
        }

        using var proceso = Process.Start(informacion)!;
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        if (!proceso.WaitForExit(plazo))
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException($"docker {string.Join(' ', argumentos.Take(4))} no terminó a tiempo.");
        }

        proceso.WaitForExit();
        return (proceso.ExitCode, salida.Result, errores.Result);
    }

    /// <summary>El Compose de la VM ya resuelto, en JSON, con el entorno de muestra.</summary>
    public static string ComposeResuelto(string vm, string carpetaDeSecretos)
    {
        var (codigo, salida, errores) = Docker(TimeSpan.FromMinutes(1), EntornoDeMuestra(vm, carpetaDeSecretos), "compose", "-f", $"docker-compose.{vm}.yml", "config", "--format", "json");
        if (codigo != 0)
        {
            throw new InvalidOperationException($"docker compose config falló para {vm}: {errores}{salida}");
        }

        return salida;
    }

    [GeneratedRegex(@"\$\{BABELHUB_SECRETOS:\?[^}]*\}/(?<archivo>[\w.-]+\.(?:env|pem))", RegexOptions.None, 2000)]
    private static partial Regex SecretoReferenciado();

    [GeneratedRegex(@"\$\{(?<nombre>[A-Z][A-Z0-9_]*):\?", RegexOptions.None, 2000)]
    private static partial Regex VariableObligatoria();
}
