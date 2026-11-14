using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Web.Errores;

/// <summary>Redirección que entiende htmx: <c>200</c> con la cabecera <c>HX-Redirect</c>, porque htmx no sigue un <c>302</c> dentro de un fragmento.</summary>
public sealed class HxRedirectResult(string ruta) : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.HttpContext.Response.StatusCode = StatusCodes.Status200OK;
        context.HttpContext.Response.Headers["HX-Redirect"] = ruta;
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return Task.CompletedTask;
    }
}

/// <summary>Convierte una <see cref="RespuestaDePantalla"/> en la respuesta HTTP: la redirección al ingreso o el componente <c>bh-estado</c> con el código que corresponde.</summary>
public static class ResultadosDePantalla
{
    public static bool EsHtmx(this HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return string.Equals(solicitud.Headers["HX-Request"], "true", StringComparison.Ordinal);
    }

    /// <summary>
    /// Una solicitud no autenticada va al ingreso (con <c>HX-Redirect</c> si es de htmx). Los demás estados se pintan con <c>bh-estado</c>: a una
    /// solicitud de htmx se le responde <c>200</c> para que el fragmento se reemplace (htmx no reemplaza un 4xx ni un 5xx).
    /// </summary>
    public static IActionResult ComoResultado(this RespuestaDePantalla respuesta, ControllerBase controlador)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentNullException.ThrowIfNull(controlador);

        var htmx = controlador.Request.EsHtmx();
        if (respuesta.RedirigirA is { } destino)
        {
            return htmx ? new HxRedirectResult(destino) : new LocalRedirectResult(destino);
        }

        if (respuesta.Vista.EsperaSugeridaSegundos is { } espera)
        {
            controlador.Response.Headers["Retry-After"] = espera.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        controlador.Response.Headers.CacheControl = "no-store";
        return new ViewComponentResult
        {
            ViewComponentName = "BhEstado",
            Arguments = new { modelo = respuesta.Vista },
            StatusCode = htmx ? StatusCodes.Status200OK : respuesta.EstadoHttp,
        };
    }
}
