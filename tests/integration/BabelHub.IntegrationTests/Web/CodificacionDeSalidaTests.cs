using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// SEG-02: toda salida hacia la interfaz se codifica según su contexto. Razor codifica por defecto, así que la regla es no saltársela:
/// ni la Api ni sus vistas escriben marcado sin codificar ni ponen un valor del modelo dentro de un script o un manejador de evento.
/// Recorre los archivos de la presentación de los dos servicios; el cierre con pantallas reales (el marcado se muestra como texto) es de E2E.
/// </summary>
public sealed partial class CodificacionDeSalidaTests
{
    /// <summary>Lo único que escribe marcado sin codificar, con el motivo; un caso nuevo se discute antes de agregarlo aquí.</summary>
    private static readonly Dictionary<string, string> Permitidos = new(StringComparer.Ordinal)
    {
        ["BabelHub.Ecommerce/BabelHub.Ecommerce.Api/Plataforma/VerificacionDePersonaWeb.cs"] = "el script de Turnstile: marcado fijo sin ningún valor de la vista",
    };

    [GeneratedRegex(@"Html\.Raw|\bHtmlString\b|\bMarkupString\b|\.AppendHtml\(|\bWriteLiteral\(|\bContent\.Raw\b|\bSetHtmlContent\(|\bHtmlContentBuilder\b|\bInnerHtml\b", RegexOptions.CultureInvariant)]
    private static partial Regex SalidaSinCodificar();

    // Un valor del modelo dentro de un bloque <script> inline o de un manejador de evento (onclick="@…"): otro contexto, otra codificación.
    [GeneratedRegex(@"<script(?![^>]*\bsrc=)[^>]*>[^<]*@|\son[a-z]+\s*=\s*""[^""]*@", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex ValorDelModeloEnScriptOManejador();

    public static IEnumerable<string> ArchivosDeLaPresentacion()
    {
        var src = Path.Combine(BuscarRaiz(), "src");
        foreach (var servicio in new[] { "Ecommerce", "Inventario" })
        {
            var proyecto = $"BabelHub.{servicio}/BabelHub.{servicio}.Api";
            var carpeta = Path.Combine(src, proyecto);
            foreach (var archivo in Directory.EnumerateFiles(carpeta, "*.*", SearchOption.AllDirectories))
            {
                var relativa = Path.GetRelativePath(carpeta, archivo).Replace('\\', '/');
                if (relativa.StartsWith("bin/", StringComparison.Ordinal) || relativa.StartsWith("obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (archivo.EndsWith(".cs", StringComparison.Ordinal) || archivo.EndsWith(".cshtml", StringComparison.Ordinal))
                {
                    yield return $"{proyecto}/{relativa}";
                }
            }
        }
    }

    public static TheoryData<string> Archivos => new([.. ArchivosDeLaPresentacion()]);

    [Fact]
    public void LaRevisionVeLosArchivosDeLaPresentacion()
    {
        Assert.Contains(ArchivosDeLaPresentacion(), a => a.EndsWith("Plataforma/ManejoDeErrores.cs", StringComparison.Ordinal));
        Assert.All(Permitidos.Keys, p => Assert.Contains(p, ArchivosDeLaPresentacion()));
    }

    [Theory]
    [MemberData(nameof(Archivos))]
    [Trait("Criterio", "AC-SEG-02")]
    public void UnArchivoDeLaPresentacion_NoEscribeMarcadoSinCodificarNiValoresDelModeloEnScripts(string archivo)
    {
        var texto = File.ReadAllText(Path.Combine(BuscarRaiz(), "src", archivo));

        if (!Permitidos.ContainsKey(archivo))
        {
            Assert.False(SalidaSinCodificar().IsMatch(texto), $"{archivo} escribe marcado sin codificar.");
        }

        Assert.False(ValorDelModeloEnScriptOManejador().IsMatch(texto), $"{archivo} pone un valor del modelo en un script o en un manejador de evento.");
    }

    [Theory]
    [InlineData("@Html.Raw(Model.Comentario)")]
    [InlineData("output.Content.AppendHtml(comentario);")]
    [InlineData("var x = new HtmlString(nombre);")]
    [InlineData("<script>var nombre = '@Model.Nombre';</script>")]
    [InlineData("<button onclick=\"borrar('@Model.Id')\">")]
    [Trait("Criterio", "AC-SEG-02")]
    public void LaRevision_DetectaLasFormasDeSaltarseLaCodificacion(string muestra)
    {
        Assert.True(SalidaSinCodificar().IsMatch(muestra) || ValorDelModeloEnScriptOManejador().IsMatch(muestra));
    }

    [Theory]
    [InlineData("<p>@Model.Comentario</p>")]
    [InlineData("<script src=\"/js/app.js\"></script>")]
    [InlineData("<a href=\"@Url.Action(\"Ver\")\">@Model.Nombre</a>")]
    public void LaRevision_NoMarcaLaSalidaCodificadaPorOmision(string muestra)
    {
        Assert.False(SalidaSinCodificar().IsMatch(muestra) || ValorDelModeloEnScriptOManejador().IsMatch(muestra));
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
