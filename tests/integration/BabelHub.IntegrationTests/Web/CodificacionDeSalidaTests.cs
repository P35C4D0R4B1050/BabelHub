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
        ["BabelHub.Ecommerce/BabelHub.Ecommerce.Api/Web/Layout/IconoTagHelper.cs"] = "el trazado de un icono: constantes de una lista cerrada (Iconos.Trazado lanza si el nombre no está), sin ningún valor de la vista",
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

    [Fact]
    public void LaRevisionVeLosArchivosDeLaPresentacion()
    {
        Assert.Contains(ArchivosDeLaPresentacion(), a => a.EndsWith("Plataforma/ManejoDeErrores.cs", StringComparison.Ordinal));
        Assert.All(Permitidos.Keys, p => Assert.Contains(p, ArchivosDeLaPresentacion()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-02")]
    public void NingunArchivoDeLaPresentacion_EscribeMarcadoSinCodificarNiPoneValoresDelModeloEnScripts()
    {
        var raiz = BuscarRaiz();
        var archivos = ArchivosDeLaPresentacion().ToList();
        var infractores = new List<string>();
        foreach (var archivo in archivos)
        {
            var texto = File.ReadAllText(Path.Combine(raiz, "src", archivo));

            if (!Permitidos.ContainsKey(archivo) && SalidaSinCodificar().IsMatch(texto))
            {
                infractores.Add($"{archivo} escribe marcado sin codificar.");
            }

            if (ValorDelModeloEnScriptOManejador().IsMatch(texto))
            {
                infractores.Add($"{archivo} pone un valor del modelo en un script o en un manejador de evento.");
            }
        }

        // Sin archivos que revisar, la prueba no estaría comprobando nada.
        Assert.True(archivos.Count > 100, $"Solo se listaron {archivos.Count} archivos de la presentación.");
        Assert.True(infractores.Count == 0, $"{infractores.Count} infractor(es):\n" + string.Join('\n', infractores));
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
