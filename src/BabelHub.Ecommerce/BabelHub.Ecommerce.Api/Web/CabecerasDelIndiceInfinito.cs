namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// Las cabeceras de <b>toda</b> respuesta del índice infinito (web-indice-infinito T09; D06): <c>X-Robots-Tag: noindex, nofollow</c> y <c>Cache-Control: no-store</c> en las respuestas 200, 302, 429 y los errores del servidor (que el manejador de errores
/// limpia, por eso se ponen con <c>Response.OnStarting</c>, como <see cref="CabecerasDeSeguridadMiddleware"/>). El prefijo es <c>/indice-infinito</c> por segmento y sin distinguir mayúsculas, como el enrutado. Ninguna caché compartida debe servir una página al
/// azar a otro visitante ni reutilizar el nonce de la CSP, y un espacio infinito de páginas es una trampa para los rastreadores (el <c>robots.txt</c> estático reduce la carga y esta cabecera cubre a quien llega por un enlace compartido).
/// </summary>
public sealed class CabecerasDelIndiceInfinitoMiddleware(RequestDelegate siguiente)
{
    public const string Prefijo = "/indice-infinito";

    public Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (contexto.Request.Path.StartsWithSegments(Prefijo, StringComparison.OrdinalIgnoreCase))
        {
            contexto.Response.OnStarting(() =>
            {
                contexto.Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
                contexto.Response.Headers.CacheControl = "no-store";
                return Task.CompletedTask;
            });
        }

        return siguiente(contexto);
    }
}

public static class CabecerasDelIndiceInfinitoExtensiones
{
    /// <summary>Va tras <c>UsarCabecerasDeSeguridad</c> y <b>antes</b> del manejo de errores y de los límites, para cubrir también el 429 y los errores del servidor.</summary>
    public static IApplicationBuilder UsarCabecerasDelIndiceInfinito(this IApplicationBuilder app) => app.UseMiddleware<CabecerasDelIndiceInfinitoMiddleware>();
}
