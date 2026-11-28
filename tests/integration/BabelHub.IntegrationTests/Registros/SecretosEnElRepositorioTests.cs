using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Registros;

/// <summary>
/// SEG-11 y AC-SEG-25: el repositorio no contiene ningún secreto ni clave. Revisa los archivos versionados (y los nuevos sin ignorar):
/// que no haya archivos de claves o de entorno reales, y que ningún archivo de texto lleve claves privadas, credenciales de
/// proveedores o contraseñas con valor. Es una red de seguridad barata que corre con cada prueba; el escáner de secretos de
/// <c>transversal-plataforma</c> (que también revisa el historial y las imágenes) es la revisión de fondo.
/// </summary>
public sealed partial class SecretosEnElRepositorioTests
{
    private const long TamanoMaximo = 2 * 1024 * 1024;

    private static readonly string[] CarpetasIgnoradas = ["bin/", "obj/", ".git/", "node_modules/", "TestResults/", "coverage/"];

    private static readonly string[] ExtensionesDeTexto =
    [
        ".cs", ".csproj", ".props", ".targets", ".sln", ".json", ".md", ".yml", ".yaml", ".xml", ".sql", ".conf", ".sh", ".ps1", ".cmd",
        ".txt", ".html", ".cshtml", ".css", ".js", ".ts", ".toml", ".ini", ".config", ".example", ".env", ".lua", ".pptx.txt",
    ];

    /// <summary>Archivos que por su nombre solo pueden ser un secreto o una clave; se prohíben aunque estén vacíos.</summary>
    private static readonly Regex ArchivosProhibidos = ArchivoDeSecreto();

    [GeneratedRegex(@"(^|/)(\.env(\.(?!example$)[^/]*)?|secrets\.json|appsettings\.[^/]*\.local\.json|id_(rsa|dsa|ecdsa|ed25519)[^/]*|[^/]+\.(pem|key|pfx|p12|pkcs12|jks|keystore|ppk))$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ArchivoDeSecreto();

    private sealed record Patron(string Nombre, Regex Expresion);

    private static readonly Patron[] Patrones =
    [
        new("clave privada", new Regex(@"-----BEGIN (?:[A-Z]+ )*PRIVATE KEY-----", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))),
        new("clave de acceso de AWS", new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))),
        new("token de GitHub", new Regex(@"\bgh[pousr]_[A-Za-z0-9]{36,}\b", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))),
        new("token de Slack", new Regex(@"\bxox[abprs]-[A-Za-z0-9-]{10,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))),
        new("clave secreta de pasarela en producción", new Regex(@"\b[sr]k_live_[A-Za-z0-9]{16,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))),
        new("clave de API de Anthropic u OpenAI", new Regex(@"\bsk-(?:ant-|proj-)?[A-Za-z0-9_-]{32,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))),
        new("JWT firmado", new Regex(@"\beyJ[A-Za-z0-9_-]{20,}\.eyJ[A-Za-z0-9_-]{20,}\.[A-Za-z0-9_-]{20,}", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))),
        new("URL con usuario y contraseña", new Regex(@"\b[a-z][a-z0-9+.-]*://[^/\s:@]+:(?![\s<$({])[^/\s@]{6,}@(?!localhost|127\.0\.0\.1)[\w.-]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))),
    ];

    private const string ContrasenaConValor = "contraseña con valor en una cadena de conexión o configuración";

    /// <summary>
    /// Lo único que se permite, por archivo y por tipo de hallazgo, con su motivo: semillas falsas para probar el redactor y las
    /// credenciales de un contenedor desechable de pruebas. Un archivo nuevo aquí se discute antes de agregarlo.
    /// </summary>
    private static readonly Dictionary<string, (string[] Patrones, string Motivo)> Permitidos = new(StringComparer.Ordinal)
    {
        ["tests/unit/BabelHub.Ecommerce.UnitTests/Plataforma/DatosProhibidosTests.cs"] = (["clave de acceso de AWS", ContrasenaConValor], "semillas falsas para probar el redactor de datos prohibidos"),
        ["tests/unit/BabelHub.Inventario.UnitTests/Plataforma/DatosProhibidosTests.cs"] = (["clave de acceso de AWS", ContrasenaConValor], "semillas falsas para probar el redactor de datos prohibidos"),
        ["tests/integration/BabelHub.IntegrationTests/Plataforma/PostgresDeRolesFixture.cs"] = ([ContrasenaConValor], "credenciales de un contenedor PostgreSQL desechable de pruebas"),
        ["tests/integration/BabelHub.IntegrationTests/Web/PostgresDeLaWebFixture.cs"] = ([ContrasenaConValor], "contraseña aleatoria de un contenedor PostgreSQL desechable de pruebas"),
    };

    /// <summary>Valores de contraseña que son un marcador y no un secreto.</summary>
    private static readonly Regex ValorDeMarcador = new(
        @"^(?:<.*>|\$\{?.*|%.*%|\{\{.*|cambiar.*|change.*|changeme|example.*|ejemplo.*|xxx+|\*+|test.*|prueba.*|dev.*|local.*|\.\.\.)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [GeneratedRegex(@"(?:password|pwd|passwd|contrase[ñn]a)\s*=\s*(?<valor>[^;\s'""`<>{}$,)]{6,})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 2000)]
    private static partial Regex ContrasenaEnUnaCadena();

    public static IEnumerable<string> ArchivosDelRepositorio()
    {
        var raiz = BuscarRaiz();
        var listados = ListarConGit(raiz) ?? ListarCarpeta(raiz);
        return listados.Where(a => !CarpetasIgnoradas.Any(c => a.StartsWith(c, StringComparison.Ordinal) || a.Contains("/" + c, StringComparison.Ordinal)));
    }

    private static List<string>? ListarConGit(string raiz)
    {
        try
        {
            var informacion = new ProcessStartInfo("git", "ls-files -z --cached --others --exclude-standard")
            {
                WorkingDirectory = raiz,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using var proceso = Process.Start(informacion);
            if (proceso is null)
            {
                return null;
            }

            var salida = proceso.StandardOutput.ReadToEnd();
            proceso.WaitForExit(30_000);
            return proceso.ExitCode == 0
                ? [.. salida.Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(a => File.Exists(Path.Combine(raiz, a))).Select(a => a.Replace('\\', '/'))]
                : null;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static List<string> ListarCarpeta(string raiz) =>
        [.. Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories).Select(a => Path.GetRelativePath(raiz, a).Replace('\\', '/'))];

    private static bool EsTexto(string ruta) =>
        ExtensionesDeTexto.Any(e => ruta.EndsWith(e, StringComparison.OrdinalIgnoreCase)) || Path.GetFileName(ruta) is ".env.example" or ".gitignore" or "Dockerfile";

    /// <summary>Los hallazgos de un texto: cada uno con su motivo, sin copiar el valor.</summary>
    internal static List<string> Revisar(string ruta, string texto)
    {
        var hallazgos = new List<string>();
        foreach (var patron in Patrones)
        {
            if (patron.Expresion.IsMatch(texto))
            {
                hallazgos.Add($"{ruta}: {patron.Nombre}");
            }
        }

        foreach (Match coincidencia in ContrasenaEnUnaCadena().Matches(texto))
        {
            if (!ValorDeMarcador.IsMatch(coincidencia.Groups["valor"].Value))
            {
                hallazgos.Add($"{ruta}: {ContrasenaConValor}");
                break;
            }
        }

        return hallazgos;
    }

    [Fact]
    public void LaRevisionVeElRepositorio()
    {
        var archivos = ArchivosDelRepositorio().ToList();

        Assert.Contains("AGENTS.md", archivos);
        Assert.Contains(".gitignore", archivos);
        Assert.Contains(archivos, a => a.EndsWith(".cs", StringComparison.Ordinal));
        Assert.True(archivos.Count > 100);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void ElRepositorio_NoTieneArchivosDeClavesNiDeEntornoReales()
    {
        var prohibidos = ArchivosDelRepositorio().Where(a => ArchivosProhibidos.IsMatch(a)).ToList();

        Assert.True(prohibidos.Count == 0, "Archivos que solo pueden ser un secreto: " + string.Join(", ", prohibidos));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void ElGitignore_ExcluyeLosArchivosDeSecretos()
    {
        var gitignore = File.ReadAllText(Path.Combine(BuscarRaiz(), ".gitignore"));

        foreach (var regla in new[] { ".env", "*.pem", "*.key", "*.pfx", "secrets.json", "appsettings.*.local.json", "!.env.example" })
        {
            Assert.Contains(regla, gitignore, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void NingunArchivoDeTexto_LlevaClavesPrivadasCredencialesNiContrasenasConValor()
    {
        var raiz = BuscarRaiz();
        var archivos = ArchivosDelRepositorio().Where(EsTexto).ToList();
        var hallazgos = new List<string>();
        foreach (var ruta in archivos)
        {
            var completa = Path.Combine(raiz, ruta);
            if (new FileInfo(completa).Length > TamanoMaximo)
            {
                continue;
            }

            var propios = Revisar(ruta, File.ReadAllText(completa));
            if (Permitidos.TryGetValue(ruta, out var permitido))
            {
                propios = [.. propios.Where(h => !permitido.Patrones.Any(p => h.EndsWith(": " + p, StringComparison.Ordinal)))];
            }

            hallazgos.AddRange(propios);
        }

        // Sin archivos que revisar, la prueba no estaría comprobando nada.
        Assert.True(archivos.Count > 100, $"Solo se listaron {archivos.Count} archivos de texto.");
        Assert.True(hallazgos.Count == 0, $"{hallazgos.Count} hallazgo(s):\n" + string.Join('\n', hallazgos));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void LaRevision_DetectaLosSecretosSembradosYNoMarcaLosMarcadores()
    {
        // Las muestras se arman en pedazos para que este archivo no sea, a su vez, un hallazgo.
        string[] sembrados =
        [
            "-----BEGIN " + "RSA PRIVATE KEY-----\nMIIEow",
            "clave = AK" + "IA" + "ABCDEFGHIJKLMNOP",
            "token: gh" + "p_" + new string('a', 36),
            "slack xo" + "xb-1234567890-abcdefghij",
            "pasarela sk" + "_live_" + "AbCdEfGhIjKlMnOpQr",
            "sk" + "-ant-" + new string('b', 40),
            "jwt: ey" + "JhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9." + "ey" + "JzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4ifQ." + "SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV",
            "Host=db;Username=app;Pass" + "word=Sup3rClaveReal2026;Database=x",
            "postgres://app:Sup3r" + "ClaveReal2026@db.produccion.pe/babelhub",
        ];
        string[] marcadores =
        [
            "Password=cambiar-en-desarrollo",
            "POSTGRES_PASSWORD: ${POSTGRES_ADMIN_PASSWORD:?Define}",
            "Password=<contraseña>",
            "postgres://app:<clave>@db/babelhub",
            "redis://localhost:6379",
            "un texto cualquiera de la documentación",
        ];

        foreach (var muestra in sembrados)
        {
            Assert.NotEmpty(Revisar("muestra.txt", muestra));
        }

        foreach (var muestra in marcadores)
        {
            Assert.Empty(Revisar("muestra.txt", muestra));
        }

        Assert.Matches(ArchivosProhibidos, ".env");
        Assert.Matches(ArchivosProhibidos, "infra/.env.produccion");
        Assert.Matches(ArchivosProhibidos, "claves/servidor.pem");
        Assert.Matches(ArchivosProhibidos, "src/Api/appsettings.Production.local.json");
        Assert.DoesNotMatch(ArchivosProhibidos, ".env.example");
        Assert.DoesNotMatch(ArchivosProhibidos, "src/Api/appsettings.json");
    }

    private static string BuscarRaiz()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
