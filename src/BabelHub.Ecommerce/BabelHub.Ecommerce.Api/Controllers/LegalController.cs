using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Controllers;

/// <summary>Un texto legal listo para mostrar.</summary>
public sealed class TextoLegalVista : ModeloBaseDePagina
{
    public TextoLegal Texto { get; set; } = null!;
}

/// <summary>
/// Los términos, la privacidad y el reembolso con su versión y su fecha de vigencia visibles (WEB-29, AC-WEB-24). La versión la informa
/// Identidad; el cuerpo, los recursos de la web. Existen en los dos hosts y los lee cualquier persona, también con sesión y textos pendientes.
/// </summary>
[PermitidaConPasoPendiente]
public sealed class LegalController(ITextosLegales textos, ContextoDePagina pagina) : Controller
{
    [HttpGet("/legal/{texto}")]
    [PermitidaConTextosPendientes(CapacidadWeb.TextosLegales)]
    public async Task<IActionResult> Ver(string texto, CancellationToken cancelacion)
    {
        if (!TiposDeTextoLegal.TryParse(texto, out var tipo))
        {
            return await Estado(EstadoVista.Vacio("No encontramos ese texto", "Revisa el enlace o vuelve al inicio.", "Ir al inicio", "/"), "No encontrado", StatusCodes.Status404NotFound, cancelacion);
        }

        var resultado = await textos.Obtener(tipo, cancelacion);
        if (!resultado.EsExito)
        {
            return resultado.Error!.Categoria == CategoriaDeError.NoEncontrado
                ? await Estado(EstadoVista.Vacio("No encontramos ese texto", "Revisa el enlace o vuelve al inicio.", "Ir al inicio", "/"), "No encontrado", StatusCodes.Status404NotFound, cancelacion)
                : await Estado(EstadoVista.ServicioNoDisponible("No podemos mostrar este texto ahora.", Request.Path), "Texto legal", StatusCodes.Status503ServiceUnavailable, cancelacion);
        }

        Response.Headers.CacheControl = "no-store";
        return View("Texto", await pagina.Completar(new TextoLegalVista { Texto = resultado.Valor }, resultado.Valor.Titulo, cancelacion: cancelacion));
    }

    private async Task<IActionResult> Estado(EstadoVista estado, string titulo, int codigo, CancellationToken cancelacion)
    {
        Response.StatusCode = codigo;
        return View("~/Views/Estados/Pantalla.cshtml", await pagina.Completar(new PantallaDeEstadoVista { Estado = estado }, titulo, cancelacion: cancelacion));
    }
}
