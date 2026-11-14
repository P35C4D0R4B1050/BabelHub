using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Estados;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Controllers;

/// <summary>Una pantalla de estado completa (error, sin permiso, servicio no disponible): el componente <c>bh-estado</c> dentro del layout.</summary>
public sealed class PantallaDeEstadoVista : ModeloBaseDePagina
{
    public EstadoVista Estado { get; set; } = EstadoVista.ErrorGenerico(null);
}

/// <summary>
/// Las pantallas de error, sin permiso y servicio no disponible (WEB-03, WEB-07, WEB-26, T26): <b>único dueño</b> de esos tres estados. Existen en los
/// dos hosts. Son los destinos de las redirecciones de la web: solo se leen y no tienen efectos.
/// </summary>
public sealed class EstadosController(ContextoDePagina pagina) : Controller
{
    /// <summary>Un fallo inesperado: mensaje simple y reintento a la página de inicio, sin detalles técnicos.</summary>
    [HttpGet("/error")]
    public async Task<IActionResult> Error(CancellationToken cancelacion) =>
        await Mostrar("Algo salió mal", EstadoVista.ErrorGenerico("/"), StatusCodes.Status500InternalServerError, cancelacion);

    /// <summary>El rol no permite la pantalla (WEB-03): se dice sin revelar si el recurso existe y se ofrecen salidas a lo que sí puede usar.</summary>
    [HttpGet("/sin-permiso")]
    public async Task<IActionResult> SinPermiso(CancellationToken cancelacion) =>
        await Mostrar("Sin permiso", EstadoVista.SinPermiso([new EnlaceDeSalida("Ir al inicio", "/"), new EnlaceDeSalida("Ver mi cuenta", "/cuenta")]), StatusCodes.Status403Forbidden, cancelacion);

    /// <summary>Un servicio o módulo no responde (WEB-26): el reintento es un enlace <c>GET</c> a esta misma pantalla, sin destino en la dirección.</summary>
    [HttpGet("/no-disponible")]
    public async Task<IActionResult> NoDisponible(CancellationToken cancelacion) =>
        await Mostrar("Servicio no disponible", EstadoVista.ServicioNoDisponible(TextosDeInterfaz.NoDisponibleMensajeGeneral, "/no-disponible"), StatusCodes.Status503ServiceUnavailable, cancelacion);

    private async Task<IActionResult> Mostrar(string titulo, EstadoVista estado, int codigo, CancellationToken cancelacion)
    {
        var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = estado }, titulo, cancelacion: cancelacion);
        Response.StatusCode = codigo;
        Response.Headers.CacheControl = "no-store";
        return View("Pantalla", modelo);
    }
}
