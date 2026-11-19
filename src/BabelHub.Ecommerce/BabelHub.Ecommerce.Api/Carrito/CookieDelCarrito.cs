using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Api.Carrito;

/// <summary>
/// La cookie del carrito de un visitante (CAR-16, CAR-35; T27): <c>__Host-bh-carrito</c>, opaca (256 bits en base64url, 43 caracteres), <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Lax</c>, ruta <c>/</c> y <b>sin <c>Domain</c></b> (el prefijo <c>__Host-</c> lo
/// exige), persistente 30 días. Solo la Presentación escribe su valor en claro; el servidor guarda su SHA-256. <b>Se rota</b> al fusionar el carrito con la cuenta (se borra o se reemplaza) y un valor ajeno o mal formado se trata como «sin carrito».
/// El valor nunca va en una URL, un registro ni un mensaje de error.
/// </summary>
public static partial class CookieDelCarrito
{
    public const string Nombre = "__Host-bh-carrito";

    public static readonly TimeSpan Duracion = TimeSpan.FromDays(30);

    public static CookieOptions Opciones() => new() { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = Duracion };

    /// <summary>El valor de la cookie si tiene la forma de la cookie del carrito (43 caracteres base64url); si no, <see langword="null"/>.</summary>
    public static string? Leer(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.Cookies.TryGetValue(Nombre, out var valor) && valor is not null && Forma().IsMatch(valor) ? valor : null;
    }

    /// <summary>Hay una cookie con ese nombre aunque su forma no sea válida (para borrarla en vez de reenviarla).</summary>
    public static bool HayCookie(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.Cookies.ContainsKey(Nombre);
    }

    public static void Escribir(HttpResponse respuesta, string valor)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentException.ThrowIfNullOrEmpty(valor);
        respuesta.Cookies.Append(Nombre, valor, Opciones());
    }

    public static void Borrar(HttpResponse respuesta)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        respuesta.Cookies.Delete(Nombre, Opciones());
    }

    /// <summary>Aplica lo que pidió la Aplicación: escribir la cookie nueva, borrarla o no tocarla.</summary>
    public static void Aplicar(HttpResponse respuesta, string? cookieNueva, bool borrar)
    {
        if (cookieNueva is not null)
        {
            Escribir(respuesta, cookieNueva);
        }
        else if (borrar)
        {
            Borrar(respuesta);
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex Forma();
}
