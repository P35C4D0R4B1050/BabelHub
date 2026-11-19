using BabelHub.IntegrationTests.Imagenes;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// Los cerrojos <b>estáticos</b> del sembrador de sesiones de desarrollo: solo leen texto fuente, así que compilan y corren también en Release (las pruebas del comportamiento del sembrador viven en
/// <c>SembradorDeSesionesTests</c>, bajo <c>#if DEBUG</c>, porque dependen de tipos que solo existen en Debug).
/// </summary>
public sealed class CerrojosDelSembradorDeSesionesTests
{
    private static readonly string[] ArchivosDelSembrador =
    [
        Path.Combine("src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Infrastructure", "Composicion", "SembradorDeSesionesDeDesarrollo.cs"),
        Path.Combine("src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Api", "Web", "SembradorDeSesionesEnLaApi.cs"),
    ];

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public void ElSembrador_SoloExisteEnUnaCompilacionDebug_YSusUsosEstanBajoElMismoCerrojo()
    {
        var raiz = ComposeDeVms.Raiz();
        foreach (var relativo in ArchivosDelSembrador)
        {
            var lineas = File.ReadAllLines(Path.Combine(raiz, relativo)).Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
            Assert.Equal("#if DEBUG", lineas[0]);
            Assert.Equal("#endif", lineas[^1]);
            Assert.Single(lineas, l => l.StartsWith("#if", StringComparison.Ordinal));
        }

        // Las únicas referencias a esas piezas fuera de sus archivos están dentro de un bloque #if DEBUG.
        var anfitrion = File.ReadAllText(Path.Combine(raiz, "src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Api", "Plataforma", "AnfitrionDelEcommerce.cs"));
        foreach (var uso in new[] { "AgregarSembradorDeSesiones", "MapearSembradorDeSesiones" })
        {
            var posicion = anfitrion.IndexOf(uso, StringComparison.Ordinal);
            Assert.True(posicion > 0);
            var antes = anfitrion[..posicion];
            Assert.True(antes.LastIndexOf("#if DEBUG", StringComparison.Ordinal) > antes.LastIndexOf("#endif", StringComparison.Ordinal), $"{uso} está fuera de #if DEBUG.");
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public void LaImagenDeProduccion_SePublicaEnRelease_YFallaSiElBinarioLleva_ElSembrador()
    {
        var raiz = ComposeDeVms.Raiz();
        var dockerfile = File.ReadAllText(Path.Combine(raiz, "src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Api", "Dockerfile"));
        Assert.Contains("-c Release", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("-c Debug", dockerfile, StringComparison.Ordinal);

        // Tras publicar, el Dockerfile niega (`!`) que algún ensamblado publicado (los .pdb listan los nombres de los archivos fuente aunque su código esté excluido) nombre al sembrador: si un cambio de configuración lo colara en la imagen, la construcción falla.
        var publicar = dockerfile.IndexOf("dotnet publish", StringComparison.Ordinal);
        var verificacion = dockerfile.IndexOf("! grep -rl --include=*.dll SembradorDeSesiones /app", StringComparison.Ordinal);
        Assert.True(publicar > 0 && verificacion > publicar, "Falta la verificación posterior a la publicación.");
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public void NingunProyectoNiPropiedadComunRedefineElSimboloDeDepuracion()
    {
        var raiz = ComposeDeVms.Raiz();
        var proyectos = Directory.EnumerateFiles(Path.Combine(raiz, "src"), "*.csproj", SearchOption.AllDirectories)
            .Concat(Directory.EnumerateFiles(raiz, "Directory.Build.*", SearchOption.TopDirectoryOnly));
        foreach (var archivo in proyectos)
        {
            var texto = File.ReadAllText(archivo);
            Assert.False(texto.Contains("DefineConstants", StringComparison.Ordinal) && texto.Contains("DEBUG", StringComparison.Ordinal), $"{Path.GetFileName(archivo)} define el símbolo DEBUG.");
        }
    }
}
