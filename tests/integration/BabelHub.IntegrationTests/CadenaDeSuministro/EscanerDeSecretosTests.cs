using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.CadenaDeSuministro;

/// <summary>
/// PLA-28 y AC-PLA-28: el escáner de secretos rechaza un cambio y una imagen con un secreto de prueba, sin copiarlo a su salida, y deja
/// pasar lo que no tiene secretos. El secreto se genera en cada ejecución (nunca hay uno fijo en el repositorio). Ejecuta
/// <c>scripts/escanear-secretos.sh</c> con gitleaks y Trivy en contenedores, así que necesita Docker, como las demás pruebas de integración.
/// </summary>
public sealed partial class EscanerDeSecretosTests : IDisposable
{
    private const string AlfabetoDeAws = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    private readonly string _carpeta = Path.Combine(Path.GetTempPath(), "escaner-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _imagenes = [];

    public EscanerDeSecretosTests()
    {
        Directory.CreateDirectory(_carpeta);
    }

    public void Dispose()
    {
        foreach (var imagen in _imagenes)
        {
            Ejecutar("docker", ["rmi", "--force", imagen]);
        }

        try
        {
            Directory.Delete(_carpeta, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza de una carpeta temporal: no debe hacer fallar la prueba.
        }
        catch (UnauthorizedAccessException)
        {
            // Los objetos de git de solo lectura en Windows.
        }
    }

    /// <summary>Un identificador de clave de acceso de AWS con la forma real y valor distinto en cada ejecución.</summary>
    private static string SecretoGenerado()
    {
        var caracteres = new char[16];
        for (var i = 0; i < caracteres.Length; i++)
        {
            caracteres[i] = AlfabetoDeAws[RandomNumberGenerator.GetInt32(AlfabetoDeAws.Length)];
        }

        return "AKIA" + new string(caracteres);
    }

    private string NuevaCarpeta(string nombre)
    {
        var ruta = Path.Combine(_carpeta, nombre);
        Directory.CreateDirectory(ruta);
        return ruta;
    }

    private static string RutaDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static string Bash()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach (var candidato in new[] { @"C:\Program Files\Git\bin\bash.exe", @"C:\Program Files (x86)\Git\bin\bash.exe" })
            {
                if (File.Exists(candidato))
                {
                    return candidato;
                }
            }
        }

        return "bash";
    }

    private static (int Codigo, string Salida) Ejecutar(string programa, IEnumerable<string> argumentos, string? directorio = null)
    {
        var informacion = new ProcessStartInfo(programa)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = directorio ?? Environment.CurrentDirectory,
        };
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        using var proceso = Process.Start(informacion)!;
        var salida = new StringBuilder();
        proceso.OutputDataReceived += (_, e) => salida.AppendLine(e.Data);
        proceso.ErrorDataReceived += (_, e) => salida.AppendLine(e.Data);
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();
        if (!proceso.WaitForExit(TimeSpan.FromMinutes(4)))
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException($"{programa} {string.Join(' ', argumentos)} no terminó a tiempo.");
        }

        proceso.WaitForExit();
        return (proceso.ExitCode, salida.ToString());
    }

    private static (int Codigo, string Salida) Escanear(params string[] argumentos) =>
        Ejecutar(Bash(), [Path.Combine("scripts", "escanear-secretos.sh").Replace('\\', '/'), .. argumentos], RutaDelRepositorio());

    private static void Git(string carpeta, params string[] argumentos)
    {
        var (codigo, salida) = Ejecutar("git", ["-c", "user.name=Prueba", "-c", "user.email=prueba@example.com", "-c", "commit.gpgsign=false", .. argumentos], carpeta);
        Assert.True(codigo == 0, salida);
    }

    /// <summary>Construye una imagen mínima cuyo archivo de configuración lleva (o no) el secreto.</summary>
    private string ConstruirImagen(string nombre, string? secreto)
    {
        var contexto = NuevaCarpeta("imagen-" + nombre);
        File.WriteAllText(
            Path.Combine(contexto, "Dockerfile"),
            "FROM alpine:3.20\nCOPY configuracion.env /app/configuracion.env\n");
        File.WriteAllText(
            Path.Combine(contexto, "configuracion.env"),
            secreto is null ? "modo=produccion\n" : $"aws_access_key_id={secreto}\n");

        var etiqueta = "babelhub-escaner-prueba-" + Guid.NewGuid().ToString("N")[..12];
        var (codigo, salida) = Ejecutar("docker", ["build", "--quiet", "--tag", etiqueta, contexto]);
        Assert.True(codigo == 0, salida);
        _imagenes.Add(etiqueta);
        return etiqueta;
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-28")]
    public void UnCambioConUnSecretoDePrueba_SeRechazaYElSecretoNoApareceEnLaSalida()
    {
        var secreto = SecretoGenerado();
        var carpeta = NuevaCarpeta("con-secreto");
        File.WriteAllText(Path.Combine(carpeta, "configuracion.env"), $"aws_access_key_id={secreto}\n");

        var (codigo, salida) = Escanear("repositorio", carpeta);

        Assert.Equal(1, codigo);
        Assert.DoesNotContain(secreto, salida, StringComparison.Ordinal);
        Assert.DoesNotContain(secreto[4..], salida, StringComparison.Ordinal);
        Assert.Contains("REDACTED", salida, StringComparison.Ordinal);
        Assert.Contains("configuracion.env", salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-28")]
    public void UnCambioSinSecretos_Pasa()
    {
        var carpeta = NuevaCarpeta("sin-secreto");
        File.WriteAllText(Path.Combine(carpeta, "configuracion.env"), "modo=produccion\nregion=lima\n");

        var (codigo, salida) = Escanear("repositorio", carpeta);

        Assert.True(codigo == 0, salida);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-28")]
    public void UnSecretoQuitadoDelArbolPeroPresenteEnElHistorial_SoloLoDetectaLaRevisionDelHistorial()
    {
        var secreto = SecretoGenerado();
        var carpeta = NuevaCarpeta("historial");
        Git(carpeta, "init", "--quiet");
        File.WriteAllText(Path.Combine(carpeta, "configuracion.env"), $"aws_access_key_id={secreto}\n");
        Git(carpeta, "add", "-A");
        Git(carpeta, "commit", "--quiet", "-m", "agrega la configuración");
        File.WriteAllText(Path.Combine(carpeta, "configuracion.env"), "modo=produccion\n");
        Git(carpeta, "commit", "--quiet", "-am", "quita el secreto");

        var delArbol = Escanear("repositorio", carpeta);
        var delHistorial = Escanear("repositorio", carpeta, "--historial");

        Assert.True(delArbol.Codigo == 0, delArbol.Salida);
        Assert.Equal(1, delHistorial.Codigo);
        Assert.DoesNotContain(secreto, delHistorial.Salida, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-28")]
    public void UnaImagenConUnSecretoDePrueba_SeRechazaYElSecretoNoApareceEnLaSalida()
    {
        var secreto = SecretoGenerado();
        var imagen = ConstruirImagen("con-secreto", secreto);

        var (codigo, salida) = Escanear("imagen", imagen);

        Assert.Equal(1, codigo);
        Assert.DoesNotContain(secreto, salida, StringComparison.Ordinal);
        Assert.DoesNotContain(secreto[4..], salida, StringComparison.Ordinal);
        Assert.Contains("configuracion.env", salida, StringComparison.Ordinal);
        Assert.Matches(@"\*{8,}", salida);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-28")]
    public void UnaImagenSinSecretos_Pasa()
    {
        var imagen = ConstruirImagen("sin-secreto", null);

        var (codigo, salida) = Escanear("imagen", imagen);

        Assert.True(codigo == 0, salida);
    }

    [Fact]
    public void ElEscaner_RechazaUnUsoInvalido()
    {
        Assert.Equal(2, Escanear().Codigo);
        Assert.Equal(2, Escanear("otra-cosa").Codigo);
        Assert.Equal(2, Escanear("repositorio", Path.Combine(_carpeta, "no-existe")).Codigo);
        Assert.Equal(2, Escanear("imagen").Codigo);
    }

    public static TheoryData<string> Flujos => new([.. Directory
        .EnumerateFiles(Path.Combine(RutaDelRepositorio(), ".github", "workflows"), "*.yml")
        .Select(f => Path.GetFileName(f))]);

    [Theory]
    [MemberData(nameof(Flujos))]
    [Trait("Criterio", "AC-PLA-28")]
    public void UnFlujoDeCI_NoUsaPullRequestTargetYDeclaraPermisosMinimos(string flujo)
    {
        // Sin los comentarios: un comentario puede nombrar lo que el flujo prohíbe.
        var texto = Regex.Replace(File.ReadAllText(Path.Combine(RutaDelRepositorio(), ".github", "workflows", flujo)), @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.DoesNotContain("pull_request_target", texto, StringComparison.Ordinal);
        Assert.Matches(PermisosAlNivelSuperior(), texto);
        Assert.DoesNotMatch(PermisoDeEscritura(), texto);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-28")]
    public void ElFlujoDeSecretos_NoUsaSecretosDelRepositorioYRevisaElHistorialYLasImagenes()
    {
        var texto = File.ReadAllText(Path.Combine(RutaDelRepositorio(), ".github", "workflows", "secretos.yml"));

        Assert.DoesNotContain("secrets.", texto, StringComparison.Ordinal);
        Assert.Contains("fetch-depth: 0", texto, StringComparison.Ordinal);
        Assert.Contains("--historial", texto, StringComparison.Ordinal);
        Assert.Contains("escanear-secretos.sh imagen", texto, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(texto, "persist-credentials: false", RegexOptions.None, TimeSpan.FromSeconds(2)).Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-28")]
    public void ElRepositorioActual_NoTieneSecretos()
    {
        var (codigo, salida) = Escanear("repositorio", ".");

        Assert.True(codigo == 0, salida);
    }

    [GeneratedRegex(@"(?m)^permissions:\s*\n\s+contents: read\s*$", RegexOptions.None, 2000)]
    private static partial Regex PermisosAlNivelSuperior();

    [GeneratedRegex(@"(?m)^\s+(contents|packages|pull-requests|issues|actions|id-token|statuses|checks|deployments): write", RegexOptions.None, 2000)]
    private static partial Regex PermisoDeEscritura();
}
