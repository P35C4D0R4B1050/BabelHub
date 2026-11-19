using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Http.Features;

namespace BabelHub.Ecommerce.Api.Carrito;

/// <summary>
/// El tope del cuerpo de los <c>POST</c> de <c>/carrito</c> y <c>/checkout</c> (SEG-05, API4): 16 KiB, mucho más que cualquier carrito o checkout legítimo, <b>antes</b> de que el filtro global de antifalsificación (que corre primero y ya lee el formulario) o el lector estricto toquen el cuerpo.
/// Rechaza con 413 un <c>Content-Length</c> mayor y fija el tope del servidor para un cuerpo sin longitud (<c>chunked</c>); y solo admite <c>application/x-www-form-urlencoded</c> y <c>application/json</c> (un <c>multipart</c> o cualquier otro tipo es 415). Un cuerpo vacío sin tipo es válido
/// (<c>quitar</c>, <c>volver</c> y <c>reiniciar</c> no llevan campos). El anfitrión lo agrega al canal de la Api antes del antifalsificación.
/// </summary>
public sealed class LimiteDeCuerpoDelCarrito(RequestDelegate siguiente, OpcionesDeErrores errores)
{
    public static bool Aplica(HttpRequest solicitud)
    {
        if (!HttpMethods.IsPost(solicitud.Method))
        {
            return false;
        }

        var ruta = solicitud.Path;
        return ruta.StartsWithSegments("/carrito", StringComparison.OrdinalIgnoreCase) || ruta.StartsWithSegments("/checkout", StringComparison.OrdinalIgnoreCase);
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        if (!Aplica(contexto.Request))
        {
            await siguiente(contexto);
            return;
        }

        var solicitud = contexto.Request;
        if (solicitud.ContentLength is { } longitud && longitud > EntradasHttpDelCarrito.TopeDelCuerpo)
        {
            await Rechazar(contexto, StatusCodes.Status413PayloadTooLarge, "cuerpo-demasiado-grande");
            return;
        }

        var hayCuerpo = (solicitud.ContentLength ?? 0) > 0 || !string.IsNullOrEmpty(solicitud.ContentType) || solicitud.Headers.ContainsKey("Transfer-Encoding");
        if (hayCuerpo && !(EsUrlEncoded(solicitud) || solicitud.HasJsonContentType()))
        {
            await Rechazar(contexto, StatusCodes.Status415UnsupportedMediaType, "tipo-de-contenido");
            return;
        }

        if (contexto.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } tope)
        {
            tope.MaxRequestBodySize = EntradasHttpDelCarrito.TopeDelCuerpo;
        }

        await siguiente(contexto);
    }

    private static bool EsUrlEncoded(HttpRequest solicitud) =>
        solicitud.ContentType is { } tipo && tipo.StartsWith("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase);

    private async Task Rechazar(HttpContext contexto, int estado, string clave)
    {
        contexto.Response.Headers.CacheControl = "no-store";
        var problema = ProblemasHttp.Crear(contexto, errores, CategoriaDeError.Validacion, clave, []) with { Status = estado };
        await ProblemasHttp.Escribir(contexto, problema).ConfigureAwait(false);
    }
}

public static class LimiteDeCuerpoDelCarritoExtensiones
{
    /// <summary>Agrega el tope del cuerpo de Carrito y del checkout; va <b>antes</b> del antifalsificación.</summary>
    public static IApplicationBuilder UsarLimiteDeCuerpoDelCarrito(this IApplicationBuilder app) => app.UseMiddleware<LimiteDeCuerpoDelCarrito>();
}
