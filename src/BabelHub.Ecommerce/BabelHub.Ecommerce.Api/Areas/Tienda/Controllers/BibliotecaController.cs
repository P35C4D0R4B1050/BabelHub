using System.Security.Cryptography;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Api.Web.Estados;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// El botón «Descargar» de un libro digital: un formulario <c>POST</c> con una <b>clave de un solo uso</b> generada al renderizarlo (D18, SEG-22). Dos envíos
/// con la misma clave (un doble clic, un reenvío) llegan a la Biblioteca con la misma clave y consumen una sola de las cinco descargas.
/// </summary>
public sealed class BhBotonDeDescargaViewComponent : ViewComponent
{
    public IViewComponentResult Invoke(Guid accesoId) =>
        View(new BotonDeDescargaVista(accesoId, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))));
}

/// <summary>Lo que pinta el botón: el acceso y la clave de un solo uso de este formulario.</summary>
public sealed record BotonDeDescargaVista(Guid AccesoId, string Clave);

/// <summary>
/// <c>POST /biblioteca/{id}/descargar</c> (WEB-15, D18): pide el enlace firmado a la Biblioteca digital y redirige el navegador a él con <c>302</c> y
/// <c>Cache-Control: no-store</c>. La capa web <b>nunca transmite el archivo</b>, no guarda ni registra la URL firmada (SEG-10) y la descarga no es un
/// <c>GET</c> que un prefetch pueda gastar de las cinco descargas. La titularidad, el cupo y la idempotencia son de la Biblioteca.
/// </summary>
[Area("Tienda")]
[RequestSizeLimit(4096)]
public sealed partial class BibliotecaController(IDescargasDeLaBiblioteca biblioteca, ContextoDePagina pagina, OpcionesDeErrores opciones) : Controller
{
    [GeneratedRegex("^[0-9a-f]{32}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDeLaClave();

    [HttpPost("/biblioteca/{id:guid}/descargar")]
    [RequiereSesion]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Descargar(Guid id, [FromForm] string? clave, CancellationToken cancelacion)
    {
        if (clave is null || !FormaDeLaClave().IsMatch(clave))
        {
            var problema = new ProblemaDto { Code = $"{opciones.Servicio}-{CodigosDeError.ValidacionInvalida}", Status = StatusCodes.Status400BadRequest, TraceId = HttpContext.TraceIdentifier };
            return await Estado(TraductorDeErrores.Traducir(problema), cancelacion);
        }

        var resultado = await biblioteca.SolicitarEnlace(id, clave, cancelacion);
        if (!resultado.EsExito)
        {
            var problema = Biblioteca.ProblemasDeBiblioteca.Crear(HttpContext, opciones, resultado.Error!);
            return await Estado(Biblioteca.PantallasDeBiblioteca.Traducir(problema, resultado.Error!, Biblioteca.PantallasDeBiblioteca.RutaDeMiBiblioteca, "No podemos preparar tu descarga ahora."), cancelacion);
        }

        // Solo se redirige a un enlace https: nunca a otro esquema, aunque el módulo lo entregara (defensa en profundidad).
        var enlace = resultado.Valor.Url;
        if (!enlace.IsAbsoluteUri || enlace.Scheme != Uri.UriSchemeHttps)
        {
            return await Estado(
                TraductorDeErrores.Traducir(new HttpRequestException("Enlace de descarga no válido."), parteAfectada: "No podemos preparar tu descarga ahora."),
                cancelacion);
        }

        // No se usa Redirect(): su ejecutor registra el destino y la URL firmada no puede quedar en ningún registro (SEG-10).
        return new RedireccionSinRegistro(enlace.AbsoluteUri);
    }

    private async Task<IActionResult> Estado(RespuestaDePantalla respuesta, CancellationToken cancelacion)
    {
        var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = respuesta.Vista }, "Descarga", cancelacion: cancelacion);
        Response.StatusCode = respuesta.EstadoHttp;
        Response.Headers.CacheControl = "no-store";
        return View("~/Views/Estados/Pantalla.cshtml", modelo);
    }
}
