using BabelHub.Ecommerce.Api.Web.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>La respuesta de la web cuando la sesión terminó sin que la persona lo pidiera (vencimiento o renovación rechazada) o no hay sesión: al ingreso, con el navegador limpio si hacía falta.</summary>
public static class RespuestasDeSesion
{
    public const string RutaDelIngreso = "/ingreso";
    public const string RutaSinPermiso = "/sin-permiso";

    /// <summary>
    /// Lleva al ingreso (<c>HX-Redirect</c> si la solicitud es de htmx, <c>302</c> si no). Si la sesión terminó en esta solicitud (venció, se rechazó su renovación o ya estaba cerrada) pide además al
    /// navegador vaciar su caché y su almacenamiento: lo que había en pantalla era de esa sesión (WEB-28, WEB-40). Una visita que nunca tuvo sesión no necesita esa limpieza. La cookie de sesión ya la
    /// borró <see cref="SesionWebHandler"/>.
    /// </summary>
    public static IActionResult RedirigirAlIngreso(this HttpContext contexto, bool? limpiarElNavegador = null)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var limpiar = limpiarElNavegador ?? contexto.EstadoDeLaSesion() is EstadoDeLaSesionEnLaSolicitud.Vencida or EstadoDeLaSesionEnLaSolicitud.RenovacionRechazada or EstadoDeLaSesionEnLaSolicitud.Cerrada;
        if (limpiar)
        {
            contexto.Response.LimpiarElNavegador();
        }
        else
        {
            contexto.Response.Headers.CacheControl = "no-store";
        }

        return contexto.Request.EsHtmx() ? new HxRedirectResult(RutaDelIngreso) : new LocalRedirectResult(RutaDelIngreso);
    }
}
