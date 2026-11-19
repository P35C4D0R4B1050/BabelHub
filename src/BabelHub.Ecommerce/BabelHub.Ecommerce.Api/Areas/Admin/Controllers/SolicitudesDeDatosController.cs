using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Admin.Controllers;

public enum PantallaDeSolicitudes
{
    Lista,
    Detalle,
}

/// <summary>Lo que pinta la administración de solicitudes sobre datos personales: la lista ordenada por fecha límite (con las vencidas sin atender marcadas) o el detalle de una; el aviso de lo que pasó es de lista cerrada. Los textos del cliente salen como dato, nunca como HTML.</summary>
public sealed class SolicitudesDeDatosVista : ModeloBaseDePagina
{
    public PantallaDeSolicitudes Pantalla { get; set; }

    public IReadOnlyList<SolicitudSobreDatos> Solicitudes { get; set; } = [];

    public DetalleDeSolicitud? Detalle { get; set; }

    public string? Mensaje { get; set; }

    public bool EsError { get; set; }
}

/// <summary>
/// Las solicitudes sobre datos personales con sus fechas límite (T30; WEB-35, AC-WEB-29): <c>GET /administracion/solicitudes</c> (ordenadas por fecha límite, con las vencidas sin atender señaladas), el detalle de una y <c>POST …/responder</c> con la resolución (en atención, atendida o rechazada), la respuesta y, si la rechaza, el motivo. <b>Solo la administración central</b>
/// (la decide Identidad). <b>Presentación pura</b>: el esquema del cuerpo es estricto y nada de lo escrito vuelve en un error.
/// </summary>
[Area("Admin")]
public sealed class SolicitudesDeDatosController(ISolicitudesDeDatos solicitudes, ContextoDePagina pagina) : Controller
{
    public const string Ruta = "/administracion/solicitudes";

    [HttpGet(Ruta)]
    public async Task<IActionResult> Lista([FromQuery] string? aviso, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext, ["aviso"]) is not null)
        {
            return await Pintar(new SolicitudesDeDatosVista { Mensaje = "La consulta no es válida.", EsError = true }, StatusCodes.Status400BadRequest, cancelacion);
        }

        if (HttpContext.ActorDeLaSolicitud() is not { } actor)
        {
            return HttpContext.RedirigirAlIngreso();
        }

        var resultado = await Llamar(() => solicitudes.Listar(actor.UsuarioId, cancelacion), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        var vista = new SolicitudesDeDatosVista { Pantalla = PantallaDeSolicitudes.Lista, Solicitudes = resultado.Valor, Mensaje = aviso == "respondida" ? "Registramos la respuesta." : null };
        return await Pintar(vista, StatusCodes.Status200OK, cancelacion);
    }

    [HttpGet(Ruta + "/{id:guid}")]
    public async Task<IActionResult> Detalle(Guid id, CancellationToken cancelacion)
    {
        if (await EntradasHttpDelCarrito.RechazarConsulta(HttpContext) is not null)
        {
            return await Pintar(new SolicitudesDeDatosVista { Mensaje = "La consulta no es válida.", EsError = true }, StatusCodes.Status400BadRequest, cancelacion);
        }

        if (HttpContext.ActorDeLaSolicitud() is not { } actor)
        {
            return HttpContext.RedirigirAlIngreso();
        }

        var resultado = await Llamar(() => solicitudes.Consultar(actor.UsuarioId, id, cancelacion), cancelacion);
        return resultado.EsExito
            ? await Pintar(new SolicitudesDeDatosVista { Pantalla = PantallaDeSolicitudes.Detalle, Detalle = resultado.Valor }, StatusCodes.Status200OK, cancelacion)
            : await Falla(resultado.Error!, cancelacion);
    }

    [HttpPost(Ruta + "/{id:guid}/responder")]
    public async Task<IActionResult> Responder(Guid id, CancellationToken cancelacion)
    {
        if (HttpContext.ActorDeLaSolicitud() is not { } actor)
        {
            return HttpContext.RedirigirAlIngreso();
        }

        var (campos, _) = await EntradasHttpDeAdministracion.Leer(HttpContext, ["resolucion", "respuesta", "motivo"], cancelacion);
        if (campos is null || Array.IndexOf(Enum.GetNames<ResolucionDeSolicitud>(), campos.GetValueOrDefault("resolucion")) < 0 || !Enum.TryParse<ResolucionDeSolicitud>(campos.GetValueOrDefault("resolucion"), ignoreCase: false, out var resolucion)
            || campos.GetValueOrDefault("respuesta") is { Length: > 2000 } || campos.GetValueOrDefault("motivo") is { Length: > 500 })
        {
            return await Pintar(new SolicitudesDeDatosVista { Mensaje = "Revisa la resolución, la respuesta y el motivo.", EsError = true }, StatusCodes.Status400BadRequest, cancelacion);
        }

        var respuesta = campos.GetValueOrDefault("respuesta");
        var motivo = campos.GetValueOrDefault("motivo");
        var resultado = await Llamar(() => solicitudes.Responder(actor.UsuarioId, id, resolucion, string.IsNullOrWhiteSpace(respuesta) ? null : respuesta, string.IsNullOrWhiteSpace(motivo) ? null : motivo, cancelacion), cancelacion);
        if (!resultado.EsExito)
        {
            return await Falla(resultado.Error!, cancelacion);
        }

        Response.Headers.CacheControl = "no-store";
        if (Request.EsHtmx())
        {
            return new HxRedirectResult(Ruta + "?aviso=respondida");
        }

        Response.StatusCode = StatusCodes.Status303SeeOther;
        Response.Headers.Location = Ruta + "?aviso=respondida";
        return new EmptyResult();
    }

    private static async Task<Resultado<T>> Llamar<T>(Func<Task<Resultado<T>>> llamada, CancellationToken cancelacion)
    {
        try
        {
            return await llamada();
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        }
    }

    private async Task<IActionResult> Falla(ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        var (texto, estado) = error.Categoria switch
        {
            CategoriaDeError.Prohibido or CategoriaDeError.NoAutenticado => ("No tienes permiso para ver las solicitudes sobre datos personales.", StatusCodes.Status403Forbidden),
            CategoriaDeError.NoEncontrado => ("No encontramos esa solicitud.", StatusCodes.Status404NotFound),
            CategoriaDeError.Conflicto => ("La solicitud ya cambió. Vuelve a abrirla.", StatusCodes.Status409Conflict),
            CategoriaDeError.NoDisponible => ("No podemos mostrar las solicitudes ahora. Inténtalo en unos minutos.", StatusCodes.Status503ServiceUnavailable),
            _ => ("Revisa los datos: alguno falta o no es válido.", StatusCodes.Status400BadRequest),
        };
        return await Pintar(new SolicitudesDeDatosVista { Mensaje = texto, EsError = true }, estado, cancelacion);
    }

    private async Task<IActionResult> Pintar(SolicitudesDeDatosVista vista, int estado, CancellationToken cancelacion)
    {
        await pagina.Completar(vista, "Solicitudes sobre datos personales", AreaDeLaPagina.Admin, cancelacion);
        Response.Headers.CacheControl = "no-store";
        Response.StatusCode = Request.EsHtmx() ? StatusCodes.Status200OK : estado;
        return View("Index", vista);
    }
}
