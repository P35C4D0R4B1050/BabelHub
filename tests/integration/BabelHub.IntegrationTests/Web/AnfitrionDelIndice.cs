using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// Un anfitrión del índice infinito (web-indice-infinito T10 a T13): el MVC real de la Api con sus vistas y el pipeline que el índice necesita (cabeceras de seguridad, cabeceras del índice, manejo de errores y archivos estáticos), en memoria y sin Docker.
/// La prueba puede sumar servicios (límites, sumidero de registros) y middleware propio antes de las rutas.
/// </summary>
internal static partial class AnfitrionDelIndice
{
    /// <summary>La forma exacta de lo que el índice pone en <c>Location</c> (plan §1).</summary>
    [GeneratedRegex(@"^/indice-infinito/[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    public static partial Regex FormaDeLaUbicacion();

    [GeneratedRegex("<pre[^>]*>(?<texto>.*?)</pre>", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Pre();

    public static Task<AnfitrionWeb> Iniciar(Action<IServiceCollection>? servicios = null, Action<WebApplication>? antesDelPipeline = null, Action<WebApplication>? despuesDelPipeline = null) =>
        AnfitrionWeb.Iniciar(
            s =>
            {
                s.AgregarCabecerasDeSeguridad();
                servicios?.Invoke(s);
            },
            app =>
            {
                antesDelPipeline?.Invoke(app);
                app.UsarCabecerasDeSeguridad();
                app.UsarCabecerasDelIndiceInfinito();
                app.UsarManejoDeErrores();
                app.UseStaticFiles();
                despuesDelPipeline?.Invoke(app);
            },
            despuesDelEnrutado: app => app.MapGet("/indice-infinito/acciones/error-forzado", (Func<IResult>)(() => throw new InvalidOperationException("Error forzado de la prueba."))));

    /// <summary>El texto de la página generada (el contenido del <c>&lt;pre&gt;</c>, ya decodificado).</summary>
    public static string TextoDeLaPagina(string html)
    {
        var m = Pre().Match(html);
        Assert.True(m.Success, "La página no tiene el bloque <pre> de la página generada.");
        return WebUtility.HtmlDecode(m.Groups["texto"].Value);
    }

    /// <summary>El valor del campo de solo lectura con la dirección de la página mostrada.</summary>
    public static string DireccionDeLaPagina(string html)
    {
        var m = CampoDeDireccion().Match(html);
        Assert.True(m.Success, "La página no muestra su dirección.");
        return WebUtility.HtmlDecode(m.Groups["valor"].Value);
    }

    [GeneratedRegex("id=\"direccion-de-la-pagina\"[^>]*value=\"(?<valor>[^\"]*)\"", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CampoDeDireccion();
}
