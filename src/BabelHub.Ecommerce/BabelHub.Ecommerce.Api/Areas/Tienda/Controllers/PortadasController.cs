using BabelHub.Ecommerce.Api.Catalogo;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Catalogo;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// Sirve la portada de una obra en <c>GET /portadas/{huella}</c> (D06, CAT-16, R06): una sola URL por portada, con el tipo detectado, <c>nosniff</c>, una política de contenido que no carga nada y caché inmutable de un año; una huella que no es de una portada
/// (la de un libro incluida) responde 404 <b>sin</b> caché inmutable. La cadena de consulta se ignora (no cambia el contenido ni abre otra entrada de caché: la clave de la ruta es la huella). No hay lógica: la Aplicación decide qué se sirve.
/// </summary>
[Area("Tienda")]
public sealed class PortadasController(IArchivosDelCatalogo archivos, OpcionesDeErrores errores) : Controller
{
    [HttpGet("/portadas/{huella}")]
    public async Task<IActionResult> Obtener(string huella, CancellationToken cancelacion)
    {
        var resultado = await archivos.ObtenerPortada(huella, cancelacion);
        if (!resultado.EsExito)
        {
            Response.Headers.CacheControl = "no-store";
            await ProblemasDelCatalogo.Escribir(HttpContext, errores, resultado.Error!);
            return new EmptyResult();
        }

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'";
        Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        // El flujo lo cierra la respuesta al terminar.
        return new FileStreamResult(resultado.Valor.Contenido, resultado.Valor.TipoDeMedio);
    }
}
