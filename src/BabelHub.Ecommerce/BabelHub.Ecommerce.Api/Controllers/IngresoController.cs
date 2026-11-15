using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Controllers;

/// <summary>
/// Lo que la estructura de la web aporta del ingreso (WEB-34, T18): cancelarlo. Las pantallas de ingreso, segundo factor y contraseña temporal son de WEB-2 (T28) y se declararán
/// <see cref="PermitidaConPasoPendienteAttribute"/>. Existe en los dos hosts.
/// </summary>
[PermitidaConPasoPendiente]
public sealed class IngresoController(IDesafioDelIngreso desafios) : Controller
{
    /// <summary>
    /// <c>POST /ingreso/cancelar</c>: descarta el desafío abierto (Identidad) y borra su cookie. La cookie se borra aunque Identidad no responda: quien cancela siempre puede salir, y el desafío
    /// vence solo a los 5 minutos.
    /// </summary>
    [HttpPost("/ingreso/cancelar")]
    public async Task<IActionResult> Cancelar(CancellationToken cancelacion)
    {
        if (CookieDelDesafio.Leer(Request) is { } desafio)
        {
            try
            {
                await desafios.Cancelar(desafio, cancelacion);
            }
            catch (Exception) when (!cancelacion.IsCancellationRequested)
            {
                // El desafío caduca solo; no se retiene a la persona.
            }
        }

        CookieDelDesafio.Borrar(Response);
        Response.Headers.CacheControl = "no-store";
        return Request.EsHtmx() ? new HxRedirectResult(RespuestasDeSesion.RutaDelIngreso) : new LocalRedirectResult(RespuestasDeSesion.RutaDelIngreso);
    }
}
