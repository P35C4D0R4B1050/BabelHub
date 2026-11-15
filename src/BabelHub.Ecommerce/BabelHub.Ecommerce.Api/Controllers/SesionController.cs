using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Controllers;

/// <summary>
/// Cerrar la sesión (WEB-19, WEB-28): <c>POST /sesion/cerrar</c> cierra la de este navegador y <c>POST /sesion/cerrar-todas</c> las de la persona (por ejemplo, tras
/// sospechar de un robo). Ambas son <c>POST</c> con antiforgery, existen en los dos hosts, borran la cookie, piden al navegador vaciar su caché y su almacenamiento
/// (<c>Clear-Site-Data</c>) y llevan al ingreso. Sin reglas de negocio: el cierre, la revocación de la renovación en Identidad y el borrado de los secretos son del módulo
/// <c>Web</c> (<see cref="ISesionesWeb"/>).
/// </summary>
public sealed class SesionController(ISesionesWeb sesiones, OpcionesDelHostWeb host) : Controller
{
    private const string Ingreso = "/ingreso";

    /// <summary>Cierre voluntario: invalida la sesión de este navegador en el servidor aunque la revocación de la renovación en Identidad quede pendiente.</summary>
    [HttpPost("/sesion/cerrar")]
    [PermitidaConTextosPendientes(CapacidadWeb.CerrarSesion)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cerrar(CancellationToken cancelacion)
    {
        if (CookieDeSesionWeb.Leer(Request, host.Host) is { } valor)
        {
            await sesiones.Cerrar(valor, cancelacion);
        }

        return Salir();
    }

    /// <summary>Cierra todas las sesiones de la persona y las renovaciones en Identidad. Sin una sesión válida no hay de quién cerrarlas: solo se limpia este navegador.</summary>
    [HttpPost("/sesion/cerrar-todas")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CerrarTodas(CancellationToken cancelacion)
    {
        if (HttpContext.ActorDeLaSolicitud() is { } actor)
        {
            await sesiones.CerrarTodasDeUnUsuario(actor.UsuarioId, cancelacion);
        }

        return Salir();
    }

    private IActionResult Salir()
    {
        CookieDeSesionWeb.Borrar(Response, host.Host);
        Response.LimpiarElNavegador();
        return Request.EsHtmx() ? new HxRedirectResult(Ingreso) : new LocalRedirectResult(Ingreso);
    }
}
