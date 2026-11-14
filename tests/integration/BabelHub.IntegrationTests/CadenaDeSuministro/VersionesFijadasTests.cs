namespace BabelHub.IntegrationTests.CadenaDeSuministro;

/// <summary>
/// AC-PLA-26: un archivo de despliegue o de dependencias con una versión flotante, o una imagen de terceros sin su contenido fijado,
/// hace fallar la verificación señalándolo; con todo fijado, pasa. Cada regla se prueba con muestras y se aplica al repositorio real.
/// </summary>
public sealed class VersionesFijadasTests
{
    private const string Sha = "11bd71901bbe5b1630ceea73d27597364c9af683";
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

    private static IEnumerable<string> Archivos(string carpeta, string patron) =>
        Directory.EnumerateFiles(Path.Combine(Raiz(), carpeta), patron, SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                     && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(Raiz(), f).Replace('\\', '/'));

    private static string Leer(string ruta) => File.ReadAllText(Path.Combine(Raiz(), ruta));

    // ---------- Acciones de GitHub ----------

    [Theory]
    [Trait("Criterio", "AC-PLA-26")]
    [InlineData("      - uses: actions/checkout@v4")]
    [InlineData("      - uses: actions/checkout@main")]
    [InlineData("      - uses: actions/checkout")]
    [InlineData("      - uses: actions/checkout@11bd719")]
    [InlineData("        uses: docker://alpine:3.20")]
    public void UnaAccionSinSuSha_Falla_ySenalaLaLinea(string linea)
    {
        var incumplimientos = VersionesFijadas.Acciones(".github/workflows/x.yml", "name: x\njobs:\n  a:\n    steps:\n" + linea + "\n");

        var error = Assert.Single(incumplimientos);
        Assert.Contains(".github/workflows/x.yml:5", error, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-26")]
    [InlineData($"      - uses: actions/checkout@{Sha} # v4.2.2")]
    [InlineData($"        uses: owner/repo/subcarpeta/accion@{Sha}")]
    [InlineData("      - uses: ./.github/acciones/local")]
    [InlineData($"        uses: docker://alpine:3.20@{Digest}")]
    [InlineData("      # uses: actions/checkout@v4 (un comentario no cuenta)")]
    public void UnaAccionFijadaPorSha_Pasa(string linea)
    {
        Assert.Empty(VersionesFijadas.Acciones("x.yml", linea + "\n"));
    }

    // ---------- Imágenes ----------

    [Theory]
    [Trait("Criterio", "AC-PLA-26")]
    [InlineData("x.yml", "    image: postgres:latest", "latest")]
    [InlineData("x.yml", "    image: redis:7-alpine", "digest")]
    [InlineData("x.yml", "    image: redis", "digest")]
    [InlineData("docker-compose.yml", "    image: \"nginx:1.27-alpine\"", "digest")]
    [InlineData("Dockerfile", "FROM mcr.microsoft.com/dotnet/aspnet:10.0", "digest")]
    [InlineData("Dockerfile", "FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build", "digest")]
    [InlineData("Dockerfile.api", "FROM alpine:latest", "latest")]
    [InlineData("tareas.sh", "TRIVY_IMAGE=\"${TRIVY_IMAGE:-aquasec/trivy:0.58.1}\"", "digest")]
    public void UnaImagenSinSuDigestOConEtiquetaFlotante_Falla_ySenalaLaImagen(string ruta, string linea, string motivo)
    {
        var incumplimientos = VersionesFijadas.Imagenes(ruta, "# cabecera\n" + linea + "\n");

        var error = Assert.Single(incumplimientos);
        Assert.Contains($"{ruta}:2", error, StringComparison.Ordinal);
        Assert.Contains(motivo, error, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-26")]
    [InlineData("x.yml", $"    image: postgres:17-alpine@{Digest}")]
    [InlineData("x.yml", $"    image: postgres@{Digest}")]
    [InlineData("Dockerfile", $"FROM mcr.microsoft.com/dotnet/aspnet:10.0@{Digest}")]
    [InlineData("Dockerfile", "FROM scratch")]
    [InlineData("tareas.sh", $"GITLEAKS_IMAGE=\"${{GITLEAKS_IMAGE:-zricethezav/gitleaks:v8.24.3@{Digest}}}\"")]
    [InlineData("x.yml", "    build: ./api")]
    public void UnaImagenFijadaPorDigest_Pasa(string ruta, string linea)
    {
        Assert.Empty(VersionesFijadas.Imagenes(ruta, linea + "\n"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-26")]
    public void UnaEtapaDeUnDockerfile_PuedePartirDeOtraEtapaYsoloLaImagenExternaNecesitaDigest()
    {
        var dockerfile = $"FROM mcr.microsoft.com/dotnet/sdk:10.0@{Digest} AS build\nRUN dotnet build\nFROM build AS publicar\nFROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final\n";

        var error = Assert.Single(VersionesFijadas.Imagenes("Dockerfile", dockerfile));

        Assert.Contains("Dockerfile:4", error, StringComparison.Ordinal);
        Assert.Contains("aspnet:10.0", error, StringComparison.Ordinal);
    }

    // ---------- Paquetes NuGet ----------

    [Theory]
    [Trait("Criterio", "AC-PLA-26")]
    [InlineData("*")]
    [InlineData("1.*")]
    [InlineData("[1.0,2.0)")]
    [InlineData("1.0.0-*")]
    [InlineData("")]
    [InlineData("latest")]
    public void UnaVersionDePaqueteFlotanteORango_Falla_ySenalaElPaquete(string version)
    {
        var props = $"<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include=\"Paquete.Ejemplo\" Version=\"{version}\" /></ItemGroup></Project>";

        var error = Assert.Single(VersionesFijadas.VersionesCentralizadas("Directory.Packages.props", props));

        Assert.Contains("Paquete.Ejemplo", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-26")]
    public void LasVersionesExactasYLaGestionCentral_Pasan_ySinGestionCentralFalla()
    {
        const string Exactas = "<Project><PropertyGroup><ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally></PropertyGroup><ItemGroup><PackageVersion Include=\"A\" Version=\"10.0.3\" /><PackageVersion Include=\"B\" Version=\"2.9.3-beta.1\" /></ItemGroup></Project>";
        const string SinGestion = "<Project><ItemGroup><PackageVersion Include=\"A\" Version=\"10.0.3\" /></ItemGroup></Project>";

        Assert.Empty(VersionesFijadas.VersionesCentralizadas("Directory.Packages.props", Exactas));
        Assert.Contains("ManagePackageVersionsCentrally", Assert.Single(VersionesFijadas.VersionesCentralizadas("Directory.Packages.props", SinGestion)), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-26")]
    [InlineData("<PackageReference Include=\"Paquete.Ejemplo\" Version=\"1.0.0\" />")]
    [InlineData("<PackageReference Include=\"Paquete.Ejemplo\" VersionOverride=\"1.0.0\" />")]
    public void UnProyectoQueFijaSuPropiaVersion_Falla_ySenalaElPaquete(string referencia)
    {
        var error = Assert.Single(VersionesFijadas.ReferenciasDeProyecto("src/A/A.csproj", $"<Project><ItemGroup>{referencia}</ItemGroup></Project>"));

        Assert.Contains("Paquete.Ejemplo", error, StringComparison.Ordinal);
        Assert.Contains("src/A/A.csproj", error, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-26")]
    public void UnProyectoSinVersionesPropias_Pasa()
    {
        Assert.Empty(VersionesFijadas.ReferenciasDeProyecto("a.csproj", "<Project><ItemGroup><PackageReference Include=\"X\" /><PackageReference Include=\"Y\" PrivateAssets=\"all\" /></ItemGroup></Project>"));
    }

    // ---------- Bloqueo y SDK ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-26")]
    public void SinBloqueoDePaquetesOSinModoBloqueadoEnCI_OConSdkFlotante_Falla()
    {
        const string SinNada = "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>";
        const string Completo = "<Project><PropertyGroup><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile><RestoreLockedMode Condition=\"'$(CI)' == 'true'\">true</RestoreLockedMode></PropertyGroup></Project>";
        const string Sdk = "{ \"sdk\": { \"version\": \"10.0.401\", \"rollForward\": \"latestFeature\" } }";

        Assert.Equal(3, VersionesFijadas.Bloqueo(SinNada, "{ \"sdk\": { \"version\": \"10.0.x\" } }").Count);
        Assert.Empty(VersionesFijadas.Bloqueo(Completo, Sdk));
        Assert.Single(VersionesFijadas.Bloqueo(Completo, "{ \"sdk\": { \"rollForward\": \"latestFeature\" } }"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-26")]
    public void UnProyectoSinSuArchivoDeBloqueo_Falla_ySenalaElProyecto()
    {
        string[] proyectos = ["src/A/A.csproj", "src/B/B.csproj"];

        var error = Assert.Single(VersionesFijadas.ArchivosDeBloqueo(proyectos, ruta => ruta.Replace('\\', '/') == "src/A/packages.lock.json"));

        Assert.Contains("src/B/B.csproj", error, StringComparison.Ordinal);
    }

    // ---------- El repositorio real ----------

    [Fact]
    [Trait("Criterio", "AC-PLA-26")]
    public void LasAccionesDeLosFlujos_EstanFijadasPorSha()
    {
        var incumplimientos = Archivos(".github/workflows", "*.yml").SelectMany(f => VersionesFijadas.Acciones(f, Leer(f))).ToList();

        Assert.True(incumplimientos.Count == 0, string.Join("\n", incumplimientos));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-26")]
    public void LasImagenesDeLosArchivosDeDespliegueYDeLosScripts_EstanFijadasPorDigest()
    {
        var archivos = Archivos("infra", "*.yml").Concat(Archivos("infra", "*.yaml")).Concat(Archivos("infra", "*.sh"))
            .Concat(Archivos("scripts", "*.sh")).Concat(Archivos("src", "Dockerfile*")).Concat(Archivos("infra", "Dockerfile*")).ToList();
        var incumplimientos = archivos.SelectMany(f => VersionesFijadas.Imagenes(f, Leer(f))).ToList();

        Assert.NotEmpty(archivos);
        Assert.True(incumplimientos.Count == 0, string.Join("\n", incumplimientos));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-26")]
    public void LasDependenciasDelRepositorio_TienenVersionExactaGestionCentralYBloqueo()
    {
        var incumplimientos = new List<string>();
        incumplimientos.AddRange(VersionesFijadas.VersionesCentralizadas("Directory.Packages.props", Leer("Directory.Packages.props")));
        incumplimientos.AddRange(VersionesFijadas.Bloqueo(Leer("Directory.Build.props"), Leer("global.json")));

        var proyectos = new[] { "src", "tests", "tools" }.SelectMany(c => Archivos(c, "*.csproj")).ToList();
        foreach (var proyecto in proyectos)
        {
            incumplimientos.AddRange(VersionesFijadas.ReferenciasDeProyecto(proyecto, Leer(proyecto)));
        }

        incumplimientos.AddRange(VersionesFijadas.ArchivosDeBloqueo(proyectos, ruta => File.Exists(Path.Combine(Raiz(), ruta))));

        Assert.True(proyectos.Count > 10);
        Assert.True(incumplimientos.Count == 0, string.Join("\n", incumplimientos));
    }
}
