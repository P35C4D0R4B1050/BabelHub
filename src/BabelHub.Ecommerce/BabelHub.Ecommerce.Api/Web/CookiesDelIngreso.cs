using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// La cookie del desafío del ingreso (WEB-34): <c>__Host-bh-desafio</c>, <c>HttpOnly</c>, <c>Secure</c>, <c>SameSite=Lax</c>, ruta <c>/</c>, <b>no persistente</b> y de un solo uso. Lleva un
/// valor opaco que emite Identidad (nunca va en la URL ni en un campo oculto) y se borra al completar el paso, al fallar o al vencer (5 minutos, que cuenta Identidad).
/// </summary>
public static partial class CookieDelDesafio
{
    public const string Nombre = "__Host-bh-desafio";

    public static CookieOptions Opciones() => new() { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true };

    /// <summary>El valor de la cookie si tiene la forma de un identificador opaco (43 caracteres base64url); un valor ajeno se trata como si no hubiera desafío.</summary>
    public static string? Leer(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.Cookies.TryGetValue(Nombre, out var valor) && valor is not null && Forma().IsMatch(valor) ? valor : null;
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

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex Forma();
}

/// <summary>
/// La marca de dispositivo (WEB-37, WEB-39, D12): <c>__Host-bh-dispositivo</c>, 256 bits opacos, persistente 180 días, que la web emite solo tras un ingreso completo (sin rotar una válida) y entrega a
/// Identidad, que <b>decide</b> si el dispositivo es reconocido: la web no guarda ni afirma «reconocida».
/// </summary>
public static partial class CookieDeDispositivo
{
    public const string Nombre = "__Host-bh-dispositivo";

    public static readonly TimeSpan Duracion = TimeSpan.FromDays(180);

    public static CookieOptions Opciones() => new() { Path = "/", Secure = true, HttpOnly = true, SameSite = SameSiteMode.Lax, IsEssential = true, MaxAge = Duracion };

    /// <summary>La marca presente y con forma válida (43 caracteres base64url = 256 bits), o <see langword="null"/>.</summary>
    public static string? Leer(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        return solicitud.Cookies.TryGetValue(Nombre, out var valor) && valor is not null && Forma().IsMatch(valor) ? valor : null;
    }

    public static void Escribir(HttpResponse respuesta, string marca)
    {
        ArgumentNullException.ThrowIfNull(respuesta);
        ArgumentException.ThrowIfNullOrEmpty(marca);
        respuesta.Cookies.Append(Nombre, marca, Opciones());
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{43}$", RegexOptions.CultureInvariant)]
    private static partial Regex Forma();
}

/// <summary>Lo que el borde y el navegador aportan sobre el dispositivo en un ingreso y que la web entrega a Identidad: la marca (si es válida) y la región del borde.</summary>
public sealed record DatosDelDispositivo(string? Marca, string? Region)
{
    public override string ToString() => $"DatosDelDispositivo {{ TieneMarca = {Marca is not null}, Region = {Region} }}";
}

public static partial class DatosDelBorde
{
    /// <summary>
    /// La cabecera con el país del cliente que <b>Nginx fija siempre</b> desde la de Cloudflare (<c>infra/nginx/nginx.conf</c>): lo que el cliente envíe con ese nombre nunca llega a la aplicación (WEB-39).
    /// Esta es la única fuente de la región y solo se acepta con la forma de un código de país de dos letras.
    /// </summary>
    public const string CabeceraDeRegion = "X-Region-Del-Borde";

    public static DatosDelDispositivo Leer(HttpRequest solicitud)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var regiones = solicitud.Headers[CabeceraDeRegion];
        var region = regiones.Count == 1 && regiones[0] is { } texto && FormaDeLaRegion().IsMatch(texto) ? texto : null;
        return new DatosDelDispositivo(CookieDeDispositivo.Leer(solicitud), region);
    }

    [GeneratedRegex("^[A-Z]{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDeLaRegion();
}
