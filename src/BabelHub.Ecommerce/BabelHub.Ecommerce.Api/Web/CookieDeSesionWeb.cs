using BabelHub.Ecommerce.Application.Web;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// La cookie de la sesión web (WEB-04, WEB-19, WEB-28): una por host, con el prefijo <c>__Host-</c> (el navegador exige <c>Secure</c>, ruta <c>/</c> y ningún
/// <c>Domain</c>), <c>HttpOnly</c>, <c>SameSite=Lax</c>, de sesión (sin <c>Max-Age</c> ni <c>Expires</c>) y <c>Secure</c> aun cuando la aplicación vea HTTP detrás del
/// proxy. Lleva solo un identificador opaco: el valor no dice nada de la persona (D01).
/// </summary>
public static class CookieDeSesionWeb
{
    public static string Nombre(HostWeb host) => host switch
    {
        HostWeb.Tienda => "__Host-bh-tienda",
        HostWeb.Gestion => "__Host-bh-gestion",
        _ => throw new ArgumentOutOfRangeException(nameof(host)),
    };

    /// <summary>Los atributos de la cookie; también los que necesita su borrado (un <c>__Host-</c> solo se borra con los mismos atributos).</summary>
    public static CookieOptions Opciones() => new()
    {
        Path = "/",
        Secure = true,
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        IsEssential = true,
    };

    public static string? Leer(HttpRequest solicitud, HostWeb host)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.Cookies.TryGetValue(Nombre(host), out var valor) && !string.IsNullOrEmpty(valor) ? valor : null;
    }

    public static void Escribir(HttpResponse respuesta, HostWeb host, string valor)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentException.ThrowIfNullOrEmpty(valor);
        respuesta.Cookies.Append(Nombre(host), valor, Opciones());
    }

    public static void Borrar(HttpResponse respuesta, HostWeb host)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        respuesta.Cookies.Delete(Nombre(host), Opciones());
    }
}
