using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Rutas de prueba de las cabeceras y de la política de contenido por ruta (T17): cada una en la ruta exacta que le corresponde.</summary>
public sealed class ControladorDeCabecerasDePrueba : Controller
{
    [HttpGet("/ingreso")]
    [VerificacionDePersona(AccionProtegida.Ingreso)]
    public IActionResult Ingreso() => Content("<p>ingreso</p>", "text/html");

    [HttpGet("/checkout/pago")]
    [PoliticaDeContenido(ContenidoDeTerceros.Pasarela)]
    public IActionResult Pago() => Content("<p>pago</p>", "text/html");

    [HttpGet("/biblioteca")]
    [PoliticaDeContenido(ContenidoDeTerceros.DescargaDeLibro)]
    public IActionResult Biblioteca() => Content("<p>biblioteca</p>", "text/html");

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
