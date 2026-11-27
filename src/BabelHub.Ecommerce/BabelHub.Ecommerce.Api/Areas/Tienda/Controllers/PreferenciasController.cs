using BabelHub.Ecommerce.Api.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// El tema (WEB-09, D16): una cookie persistente sin datos sensibles. El valor se valida contra la lista de dos
/// temas antes de guardarse y el retorno es siempre una ruta local sin parámetros. La sucursal preferida no tiene ruta propia: la escribe el alta de un impreso en el carrito (WEB-16).
/// </summary>
[Area("Tienda")]
public sealed class PreferenciasController : Controller
{
    [HttpPost("/preferencias/tema")]
    [ValidateAntiForgeryToken]
    public IActionResult Tema([FromForm] string? tema)
    {
        var valido = Temas.Validar(tema);
        if (valido is null)
        {
            // «Dispositivo»: sin elección manda la preferencia del dispositivo.
            Response.Cookies.Delete(Temas.Cookie, PreferenciasDeLaPagina.OpcionesDeCookie(TimeSpan.Zero));
        }
        else
        {
            Response.Cookies.Append(Temas.Cookie, valido, PreferenciasDeLaPagina.OpcionesDeCookie());
        }

        return VolverAtras();
    }

    /// <summary>Vuelve a la página de la que se envió el formulario si su dirección es del mismo sitio; si no, al inicio. Nunca usa un destino enviado por el cliente.</summary>
    private LocalRedirectResult VolverAtras()
    {
        var referente = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referente, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith('/')
            && !uri.AbsolutePath.StartsWith("//", StringComparison.Ordinal))
        {
            return LocalRedirect(uri.AbsolutePath);
        }

        return LocalRedirect("/");
    }
}
