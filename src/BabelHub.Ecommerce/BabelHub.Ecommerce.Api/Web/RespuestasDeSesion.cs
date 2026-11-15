using BabelHub.Ecommerce.Api.Web.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>La respuesta de la web cuando la sesión terminó sin que la persona lo pidiera (vencimiento o renovación rechazada): al ingreso, con el navegador limpio.</summary>
public static class RespuestasDeSesion
{
    public const string RutaDelIngreso = "/ingreso";

    /// <summary>
    /// Lleva al ingreso (<c>HX-Redirect</c> si la solicitud es de htmx, <c>302</c> si no) y pide al navegador vaciar su caché y su almacenamiento: lo que había en pantalla era de la
    /// sesión que terminó (WEB-28, WEB-40). La cookie de sesión ya la borró <see cref="SesionWebHandler"/>.
    /// </summary>
    public static IActionResult RedirigirAlIngreso(this HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        contexto.Response.LimpiarElNavegador();
        return contexto.Request.EsHtmx() ? new HxRedirectResult(RutaDelIngreso) : new LocalRedirectResult(RutaDelIngreso);
    }
}
