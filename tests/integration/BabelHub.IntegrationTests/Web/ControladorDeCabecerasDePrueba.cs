using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Rutas de prueba de las cabeceras (T17). La página de la descarga (<c>/biblioteca</c>, la única que abre <c>form-action</c> al almacén) es la real de Biblioteca (<c>MiBibliotecaController</c>), no un doble.</summary>
public sealed class ControladorDeCabecerasDePrueba : Controller
{
    [HttpGet("/prueba/cabeceras/pagina")]
    public IActionResult Pagina() => Content("<p>hola</p>", "text/html");

    [HttpGet("/prueba/cabeceras/json")]
    public IActionResult Json() => Content("{}", "application/json");

    [HttpGet("/prueba/cabeceras/cookie")]
    public IActionResult ConCookie()
    {
        Response.Cookies.Append("bh-prueba", "1");
        return Content("{}", "application/json");
    }

    [HttpGet("/prueba/cabeceras/error")]
    public IActionResult Falla() => StatusCode(500);

    [HttpGet("/prueba/cabeceras/prohibido")]
    public IActionResult Prohibido() => StatusCode(403);
}

/// <summary>Rutas de prueba de los filtros de acceso (T18).</summary>
public sealed class ControladorDeAccesoDePrueba : Controller
{
    [HttpGet("/prueba/acceso/publica")]
    public IActionResult Publica() => Content("<p>publica</p>", "text/html");

    [HttpGet("/prueba/acceso/cuenta")]
    [RequiereSesion]
    public IActionResult Cuenta() => Content("<p>cuenta</p>", "text/html");
}
