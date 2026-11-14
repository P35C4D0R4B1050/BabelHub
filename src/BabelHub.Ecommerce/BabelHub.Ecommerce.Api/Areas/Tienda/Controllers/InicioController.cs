using BabelHub.Ecommerce.Api.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>Modelo de la portada de la tienda.</summary>
public sealed class InicioVista : ModeloBaseDePagina;

/// <summary>Portada de la tienda (<c>GET /</c>). Presentación pura: el catálogo, el carrito y el resto de las pantallas los agregan sus features.</summary>
[Area("Tienda")]
public sealed class InicioController(ContextoDePagina pagina) : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => View(pagina.Completar(new InicioVista(), "Inicio"));
}
