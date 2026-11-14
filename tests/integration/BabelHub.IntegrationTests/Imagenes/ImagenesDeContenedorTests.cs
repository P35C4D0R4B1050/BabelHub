using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Imagenes;

/// <summary>
/// PLA-03 y AC-PLA-03: cada imagen propia tiene su <c>Dockerfile</c> (varias etapas, sin privilegios, con comprobación de salud y
/// construida sin emulación para las dos arquitecturas) y cada imagen de terceros existe para ARM64 y x86_64 o consta como excepción
/// registrada. Las pruebas de humo que arrancan las imágenes en las dos arquitecturas están en <see cref="HumoDeImagenesTests"/>.
/// </summary>
public sealed partial class ImagenesDeContenedorTests
{
    private const string Digest = "sha256:d3244e8ce3f165ee3c93d9c4a4b1d0fe48b3a89e2287b717fdcbbde92d405065";

    private static string Raiz()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }

    private static string Leer(string ruta) => File.ReadAllText(Path.Combine(Raiz(), ruta));

    private static IEnumerable<string> Archivos(string carpeta, string patron) =>
        Directory.EnumerateFiles(Path.Combine(Raiz(), carpeta), patron, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Raiz(), f).Replace('\\', '/'));

    private static IEnumerable<string> DockerfilesDeServicios() => Archivos("src", "Dockerfile*");

    // ---------- Imágenes propias ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public void CadaApiTieneSuDockerfile()
    {
        var apis = Directory.EnumerateDirectories(Path.Combine(Raiz(), "src"), "*.Api", SearchOption.AllDirectories)
            .Select(d => Path.GetRelativePath(Raiz(), d).Replace('\\', '/')).ToList();
        var conDockerfile = DockerfilesDeServicios().Select(f => f[..f.LastIndexOf('/')]).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(2, apis.Count);
        Assert.All(apis, a => Assert.Contains(a, conDockerfile));
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-03")]
    [InlineData("Ecommerce")]
    [InlineData("Inventario")]
    public void ElDockerfileDeUnServicio_EsDeVariasEtapasSinPrivilegiosConSaludYConstruyeSinEmulacion(string servicio)
    {
        var texto = Leer($"src/BabelHub.{servicio}/BabelHub.{servicio}.Api/Dockerfile");
        var sinComentarios = Regex.Replace(texto, @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        // Dos etapas: la de compilación en la arquitectura de quien construye y la final en la de destino.
        Assert.Equal(2, Regex.Matches(sinComentarios, @"(?m)^FROM ", RegexOptions.None, TimeSpan.FromSeconds(2)).Count);
        Assert.Matches(@"(?m)^FROM --platform=\$BUILDPLATFORM \S+ AS publicar$", sinComentarios);
        Assert.Contains("--locked-mode", sinComentarios, StringComparison.Ordinal);
        Assert.Contains("/p:UseAppHost=false", sinComentarios, StringComparison.Ordinal);

        // Sin privilegios: el último USER no es root, y el servicio escucha en un puerto alto.
        var usuarios = Regex.Matches(sinComentarios, @"(?m)^USER (\S+)$", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(usuarios);
        Assert.DoesNotContain(usuarios[^1], new[] { "root", "0", "0:0" });
        Assert.Contains("ASPNETCORE_HTTP_PORTS=8080", sinComentarios, StringComparison.Ordinal);

        // Con comprobación de salud sobre /health/live y el arranque en forma exec.
        Assert.Contains("HEALTHCHECK", sinComentarios, StringComparison.Ordinal);
        Assert.Contains("/health/live", sinComentarios, StringComparison.Ordinal);
        Assert.Contains($"ENTRYPOINT [\"dotnet\", \"BabelHub.{servicio}.Api.dll\"]", sinComentarios, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public void LosDockerfilesDeLosDosServicios_SonElMismoSalvoElNombreDelServicio()
    {
        var ecommerce = Leer("src/BabelHub.Ecommerce/BabelHub.Ecommerce.Api/Dockerfile");
        var inventario = Leer("src/BabelHub.Inventario/BabelHub.Inventario.Api/Dockerfile");

        Assert.Equal(ecommerce.Replace("Ecommerce", "Servicio", StringComparison.Ordinal), inventario.Replace("Inventario", "Servicio", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public void NingunDockerfileCopiaSecretosNiDescargaDeLaRedNiUsaLatest()
    {
        foreach (var ruta in DockerfilesDeServicios().Concat(Archivos("infra", "Dockerfile*")))
        {
            var texto = Regex.Replace(Leer(ruta), @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

            Assert.DoesNotMatch(@"(?mi)^(COPY|ADD)\s+.*(\.env|\.pem|\.key|\.pfx|secrets\.json)", texto);
            Assert.DoesNotMatch(@"(?mi)^ADD\s+https?://", texto);
            Assert.DoesNotMatch(@"(?mi)(curl|wget)\s.*\|\s*(sh|bash)", texto);
            Assert.DoesNotContain(":latest", texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public void ElContextoDeConstruccionExcluyeLoQueNoDebeEntrarEnUnaImagen()
    {
        var ignorado = Leer(".dockerignore").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToHashSet(StringComparer.Ordinal);

        foreach (var debe in new[] { "**/bin/", "**/obj/", ".git/", "tests/", "docs/", "progress/", ".env", ".env.*", "**/*.pem", "**/*.key", "**/*.pfx", "**/secrets.json" })
        {
            Assert.Contains(debe, ignorado);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public void LaImagenDePostgreSqlIncluyeAgeYElClienteS3EnVersionesExactasYCorreSinPrivilegios()
    {
        var texto = Regex.Replace(Leer("infra/postgres/Dockerfile"), @"(?m)^\s*#.*$", string.Empty, RegexOptions.None, TimeSpan.FromSeconds(2));

        Assert.Matches(@"(?m)^FROM postgres:17-alpine@sha256:[0-9a-f]{64}$", texto);
        var paquetes = Regex.Match(texto, @"apk add --no-cache (.+)").Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains(paquetes, p => p.StartsWith("age=", StringComparison.Ordinal));
        Assert.Contains(paquetes, p => p.StartsWith("rclone=", StringComparison.Ordinal));
        Assert.All(paquetes, p => Assert.Matches(@"^[a-z0-9-]+=\d[\w.]*-r\d+$", p));
        Assert.Matches(@"(?s)USER root.*USER postgres\s*$", texto);
    }

    // ---------- Imágenes de terceros ----------

    private static IEnumerable<string> ImagenesDeTerceros()
    {
        var referencias = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ruta in Archivos("infra", "*.yml").Concat(Archivos("infra", "*.yaml")))
        {
            foreach (Match m in ImagenDeCompose().Matches(Leer(ruta)))
            {
                referencias.Add(m.Groups["ref"].Value);
            }
        }

        foreach (var ruta in DockerfilesDeServicios().Concat(Archivos("infra", "Dockerfile*")))
        {
            foreach (Match m in DesdeEnDockerfile().Matches(Leer(ruta)))
            {
                if (!string.Equals(m.Groups["ref"].Value, "scratch", StringComparison.OrdinalIgnoreCase))
                {
                    referencias.Add(m.Groups["ref"].Value);
                }
            }
        }

        foreach (var ruta in Archivos("scripts", "*.sh"))
        {
            foreach (Match m in ImagenDeScript().Matches(Leer(ruta)))
            {
                referencias.Add(m.Groups["ref"].Value);
            }
        }

        return referencias.Order(StringComparer.Ordinal);
    }

    public static TheoryData<string> Terceros => new([.. ImagenesDeTerceros()]);

    private static (int Codigo, string Salida) Docker(params string[] argumentos)
    {
        var informacion = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argumento in argumentos)
        {
            informacion.ArgumentList.Add(argumento);
        }

        using var proceso = Process.Start(informacion)!;
        // Cada flujo se lee entero y por separado: el JSON de un manifiesto no debe mezclarse con los avisos de stderr.
        var salida = proceso.StandardOutput.ReadToEndAsync();
        var errores = proceso.StandardError.ReadToEndAsync();
        if (!proceso.WaitForExit(TimeSpan.FromMinutes(2)))
        {
            proceso.Kill(entireProcessTree: true);
            throw new TimeoutException("docker no terminó a tiempo.");
        }

        proceso.WaitForExit();
        return (proceso.ExitCode, proceso.ExitCode == 0 ? salida.Result : salida.Result + errores.Result);
    }

    [Fact]
    public void ElRepositorioTieneImagenesDeTercerosQueRevisar()
    {
        var imagenes = ImagenesDeTerceros().ToList();

        Assert.Contains(imagenes, i => i.StartsWith("postgres:17-alpine@", StringComparison.Ordinal));
        Assert.Contains(imagenes, i => i.StartsWith("redis:7-alpine@", StringComparison.Ordinal));
        Assert.Contains(imagenes, i => i.StartsWith("mcr.microsoft.com/dotnet/aspnet:10.0@", StringComparison.Ordinal));
        Assert.Contains(imagenes, i => i.StartsWith("mcr.microsoft.com/dotnet/sdk:10.0@", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Terceros))]
    [Trait("Criterio", "AC-PLA-03")]
    public void UnaImagenDeTerceros_ExisteParaArm64YX86OConstaComoExcepcionRegistrada(string imagen)
    {
        var (codigo, indice) = Docker("buildx", "imagetools", "inspect", "--raw", imagen);
        Assert.True(codigo == 0, $"No se pudo leer el manifiesto de {imagen}: {indice}");

        var excepciones = ArquitecturasDeImagenes.LeerExcepciones(Leer("infra/excepciones-de-arquitectura.json"));
        var incumplimientos = ArquitecturasDeImagenes.Incumplimientos(imagen, ArquitecturasDeImagenes.DeIndice(indice), excepciones);

        Assert.True(incumplimientos.Count == 0, string.Join("\n", incumplimientos));
    }

    private const string IndiceDeDosArquitecturas = """{"manifests":[{"platform":{"os":"linux","architecture":"amd64"}},{"platform":{"os":"linux","architecture":"arm64","variant":"v8"}},{"platform":{"os":"unknown","architecture":"unknown"}}]}""";
    private const string IndiceSoloX86 = """{"manifests":[{"platform":{"os":"linux","architecture":"amd64"}},{"platform":{"os":"unknown","architecture":"unknown"}}]}""";

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public void UnaImagenSinVersionParaArm64YSinExcepcion_FallaSenalandolaImagenYLaArquitectura()
    {
        var plataformas = ArquitecturasDeImagenes.DeIndice(IndiceSoloX86);

        var incumplimientos = ArquitecturasDeImagenes.Incumplimientos($"tercero/solo-x86@{Digest}", plataformas, []);

        var error = Assert.Single(incumplimientos);
        Assert.Contains("linux/arm64", error, StringComparison.Ordinal);
        Assert.Contains("solo-x86", error, StringComparison.Ordinal);
        Assert.Equal(["linux/amd64"], plataformas);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public void UnaImagenSinVersionParaArm64PeroConExcepcionRegistrada_Pasa()
    {
        var imagen = $"tercero/solo-x86@{Digest}";
        var excepciones = ArquitecturasDeImagenes.LeerExcepciones(
            $$"""{"excepciones":[{"imagen":"{{imagen}}","arquitectura":"linux/arm64","vm":"VM-BORDE","motivo":"solo corre en la VM AMD"}]}""");

        Assert.Empty(ArquitecturasDeImagenes.Incumplimientos(imagen, ArquitecturasDeImagenes.DeIndice(IndiceSoloX86), excepciones));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-03")]
    public void UnaExcepcionDeUnaImagenQueYaExisteParaLaArquitecturaSeRetiraYUnaSinMotivoFalla()
    {
        var imagen = $"tercero/ya-tiene-arm@{Digest}";
        var innecesaria = ArquitecturasDeImagenes.LeerExcepciones(
            $$"""{"excepciones":[{"imagen":"{{imagen}}","arquitectura":"linux/arm64","vm":"VM-VENTAS","motivo":"antes no existía"}]}""");
        var sinMotivo = ArquitecturasDeImagenes.LeerExcepciones(
            $$"""{"excepciones":[{"imagen":"{{imagen}}","arquitectura":"linux/arm64","vm":"VM-VENTAS","motivo":" "}]}""");
        var plataformas = ArquitecturasDeImagenes.DeIndice(IndiceDeDosArquitecturas);

        Assert.Contains("ya no hace falta", Assert.Single(ArquitecturasDeImagenes.Incumplimientos(imagen, plataformas, innecesaria)), StringComparison.Ordinal);
        Assert.Contains(ArquitecturasDeImagenes.Incumplimientos(imagen, plataformas, sinMotivo), e => e.Contains("no dice su motivo", StringComparison.Ordinal));
        Assert.Empty(ArquitecturasDeImagenes.Incumplimientos(imagen, plataformas, []));
    }

    [Fact]
    public void ElRegistroDeExcepcionesDelRepositorio_EsUnJsonValido()
    {
        var excepciones = ArquitecturasDeImagenes.LeerExcepciones(Leer("infra/excepciones-de-arquitectura.json"));

        Assert.All(excepciones, e => Assert.False(string.IsNullOrWhiteSpace(e.Motivo)));
    }

    [GeneratedRegex(@"(?m)^\s*image:\s*(?<ref>[^\s#]+)", RegexOptions.None, 2000)]
    private static partial Regex ImagenDeCompose();

    [GeneratedRegex(@"(?m)^FROM\s+(?:--platform=\S+\s+)?(?<ref>\S+)(?:\s+AS\s+\S+)?\s*$", RegexOptions.IgnoreCase, 2000)]
    private static partial Regex DesdeEnDockerfile();

    [GeneratedRegex(@"(?m)^\s*[A-Z_]*IMAGE=""\$\{[A-Z_]+:-(?<ref>[^}]+)\}""", RegexOptions.None, 2000)]
    private static partial Regex ImagenDeScript();
}
