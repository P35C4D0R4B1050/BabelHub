using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El orden del pipeline real del E-commerce para el índice infinito (web-indice-infinito T09, T13; revisión de arquitectura y seguridad): los hosts de prueba arman su propio pipeline, así que esta prueba lee <c>UsarEcommerce</c> y comprueba que las cabeceras
/// del índice van <b>después</b> de las de seguridad y <b>antes</b> del manejo de errores, del tope previo y de los límites (para cubrir también el 429 y los errores del servidor), y que ninguna ruta exenta de límites cubre el índice (IND-09).
/// Una regresión del orden o una exención nueva hacen fallar la prueba sin necesidad de Docker.
/// </summary>
public sealed partial class IndiceInfinitoPipelineRealTests
{
    [GeneratedRegex(@"public static WebApplication UsarEcommerce\(this WebApplication app\)\s*\{(?<cuerpo>.*?)\n    \}", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Metodo();

    private static string FuenteDelAnfitrion() => File.ReadAllText(RaizDelRepositorio.DeLaApi("Plataforma/AnfitrionDelEcommerce.cs"));

    [Fact]
    [Trait("Criterio", "AC-IND-04")]
    public void Las_cabeceras_del_indice_van_tras_las_de_seguridad_y_antes_de_los_errores_el_tope_previo_y_los_limites()
    {
        var cuerpo = Metodo().Match(FuenteDelAnfitrion()).Groups["cuerpo"].Value;
        Assert.NotEmpty(cuerpo);

        var orden = new[] { "UsarCabecerasDeSeguridad", "UsarCabecerasDelIndiceInfinito", "UsarManejoDeErrores", "UseStaticFiles", "UsarTopePrevio", "UsarLimitesGenerales", "UsarPoliticaDeTextosPendientes", "MapDefaultControllerRoute" }
            .Select(llamada => (llamada, posicion: cuerpo.IndexOf($"app.{llamada}(", StringComparison.Ordinal)))
            .ToList();

        Assert.All(orden, o => Assert.True(o.posicion >= 0, $"UsarEcommerce no llama a {o.llamada}."));
        Assert.Equal(orden.OrderBy(o => o.posicion).Select(o => o.llamada), orden.Select(o => o.llamada));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-07")]
    public void Ninguna_ruta_exenta_de_limites_del_host_real_cubre_el_indice()
    {
        var fuente = FuenteDelAnfitrion();
        var exentas = Regex.Match(fuente, @"RutasExentas = \[(?<lista>[^\]]*)\]", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)).Groups["lista"].Value;

        Assert.NotEmpty(exentas);
        Assert.DoesNotContain("indice", exentas, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IndiceInfinito", exentas, StringComparison.Ordinal);
    }
}
