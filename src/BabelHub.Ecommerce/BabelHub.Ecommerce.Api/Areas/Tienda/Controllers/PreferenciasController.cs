using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Mvc;

namespace BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;

/// <summary>
/// El tema y la sucursal preferida (WEB-09, WEB-16, D16): cookies persistentes sin datos sensibles. El valor se valida (el tema contra la lista de dos
/// temas, la sucursal contra la lista de sucursales) antes de guardarse y el retorno es siempre una ruta local sin parámetros.
/// </summary>
[Area("Tienda")]
public sealed class PreferenciasController(IPreferenciaDeSucursal sucursales) : Controller
{
    private static readonly TimeSpan UnAnio = TimeSpan.FromDays(365);

    [HttpPost("/preferencias/tema")]
    [ValidateAntiForgeryToken]
    public IActionResult Tema([FromForm] string? tema)
    {
        var valido = Temas.Validar(tema);
        if (valido is null)
        {
            // «Dispositivo»: sin elección manda la preferencia del dispositivo.
            Response.Cookies.Delete(Temas.Cookie, OpcionesDeCookie(TimeSpan.Zero));
        }
        else
        {
            Response.Cookies.Append(Temas.Cookie, valido, OpcionesDeCookie(UnAnio));
        }

        return VolverAtras();
    }

    [HttpPost("/preferencias/sucursal")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Sucursal([FromForm] string? sucursal, CancellationToken cancelacion)
    {
        var valida = await sucursales.Validar(sucursal, cancelacion);
        if (valida is null)
        {
            // Una sucursal que no está en la lista (o sin lista conocida) no se guarda nunca (D16).
            var problema = new ProblemaDto { Code = "ECO-VALIDACION-INVALIDA", Status = StatusCodes.Status400BadRequest, TraceId = HttpContext.TraceIdentifier };
            return TraductorDeErrores.Traducir(problema, rutaDeReintento: null).ComoResultado(this);
        }

        Response.Cookies.Append(PreferenciasDeLaPagina.CookieDeSucursal, valida, OpcionesDeCookie(UnAnio));
        return VolverAtras();
    }

    private static CookieOptions OpcionesDeCookie(TimeSpan duracion) => new()
    {
        Path = "/",
        Secure = true,
        HttpOnly = false,
        SameSite = SameSiteMode.Lax,
        MaxAge = duracion,
        IsEssential = true,
    };

    /// <summary>Vuelve a la página de la que se envió el formulario si su dirección es del mismo sitio; si no, al inicio. Nunca usa un destino enviado por el cliente.</summary>
    private LocalRedirectResult VolverAtras()
    {
        var referente = Request.Headers.Referer.ToString();
        if (Uri.TryCreate(referente, UriKind.Absolute, out var uri)
            && string.Equals(uri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith('/')
            && !uri.AbsolutePath.StartsWith("//", StringComparison.Ordinal))
        {
            return LocalRedirect(uri.AbsolutePath);
        }

        return LocalRedirect("/");
    }
}
