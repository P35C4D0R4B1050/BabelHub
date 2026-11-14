using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Catalogo;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// «Sorpréndeme» (WEB-17, <c>GET /sorprendeme</c>): la elección del libro disponible al azar es del Catálogo (<see cref="IConsultaDeCatalogo"/>); aquí no hay
/// lógica propia, solo la redirección a la ficha o el estado que corresponde. Es un <c>GET</c> sin efectos.
/// </summary>
[Area("Tienda")]
public sealed class SorprendemeController(IConsultaDeCatalogo catalogo, ContextoDePagina pagina) : Controller
{
    [HttpGet("/sorprendeme")]
    public async Task<IActionResult> Index(CancellationToken cancelacion)
    {
        var resultado = await catalogo.ObtenerObraAlAzar(cancelacion);

        if (!resultado.EsExito)
        {
            return await Estado(EstadoVista.ServicioNoDisponible("No podemos elegir un libro ahora.", "/sorprendeme"), "Sorpréndeme", StatusCodes.Status503ServiceUnavailable, cancelacion);
        }

        if (resultado.Valor is not { } obra)
        {
            return await Estado(
                EstadoVista.Vacio("Por ahora no hay libros para sorprenderte", "No encontramos ningún libro disponible en este momento. Explora el catálogo y vuelve a intentarlo luego.", "Ir al catálogo", "/catalogo"),
                "Sorpréndeme",
                StatusCodes.Status200OK,
                cancelacion);
        }

        // La ficha es del Catálogo (GET /catalogo/obras/{id}); la dirección solo lleva el identificador de la obra elegida.
        return LocalRedirect($"/catalogo/obras/{obra.ObraId:D}");
    }

    private async Task<IActionResult> Estado(EstadoVista estado, string titulo, int codigo, CancellationToken cancelacion)
    {
        Response.StatusCode = codigo;
        Response.Headers.CacheControl = "no-store";
        return View("~/Views/Estados/Pantalla.cshtml", await pagina.Completar(new PantallaDeEstadoVista { Estado = estado }, titulo, cancelacion: cancelacion));
    }
}
