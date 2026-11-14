using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Web.Errores;

/// <summary>
/// Redirección <c>302</c> que escribe la cabecera <c>Location</c> sin pasar por <c>RedirectResult</c>: el ejecutor del framework registra el destino
/// («redirecting to …») y el enlace firmado de una descarga no puede quedar en ningún registro (SEG-10, AM31). Sin cuerpo y sin guardarse en caché.
/// </summary>
public sealed class RedireccionSinRegistro(string destino) : IActionResult
{
    public Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var respuesta = context.HttpContext.Response;
        respuesta.StatusCode = StatusCodes.Status302Found;
        respuesta.Headers.Location = destino;
        respuesta.Headers.CacheControl = "no-store";
        respuesta.Headers.Pragma = "no-cache";
        respuesta.ContentLength = 0;
        return Task.CompletedTask;
    }
}
