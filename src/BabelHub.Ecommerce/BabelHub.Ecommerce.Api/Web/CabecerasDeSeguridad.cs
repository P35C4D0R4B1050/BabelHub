using System.Security.Cryptography;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.AspNetCore.Routing;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>Qué contenido de terceros abre la política de contenido de una ruta (WEB-18, WEB-40). Solo existe en las rutas exactas de <see cref="RutasConContenidoDeTerceros"/>.</summary>
public enum ContenidoDeTerceros
{
    /// <summary>La pasarela de pago: <c>script-src</c>, <c>frame-src</c>, <c>connect-src</c> y <c>form-action</c> con sus orígenes configurados (solo <c>/checkout/pago</c>).</summary>
    Pasarela,

    /// <summary>La página que lleva el formulario de descarga: <c>form-action</c> con el origen configurado del almacén de objetos (Chrome aplica <c>form-action</c> también a la redirección de la respuesta).</summary>
    DescargaDeLibro,
}

/// <summary>
/// Abre, <b>solo en esta ruta</b>, el contenido de terceros indicado en la política de contenido de la respuesta (WEB-40). Turnstile no se declara aquí: lo abre
/// <see cref="VerificacionDePersonaAttribute"/>, que la política ya consulta. Una prueba y el arranque exigen que solo las rutas exactas de
/// <see cref="RutasConContenidoDeTerceros"/> lo declaren.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public sealed class PoliticaDeContenidoAttribute(ContenidoDeTerceros contenido) : Attribute
{
    public ContenidoDeTerceros Contenido { get; } = contenido;
}

/// <summary>
/// Los orígenes de terceros que la política puede abrir (configuración, bloqueo B05): los de la pasarela (<c>BABELHUB__Web__OrigenesDeLaPasarela</c>, separados por coma) y el del
/// almacén de objetos de los libros (<c>BABELHUB__Web__OrigenDelAlmacen</c>). Cada uno es un origen <c>https</c> exacto (esquema, host y puerto opcional): sin comodines ni rutas.
/// Sin ellos la ruta correspondiente no abre nada (falla cerrado).
/// </summary>
public sealed record OpcionesDeContenidoWeb(IReadOnlyList<string> OrigenesDeLaPasarela, string? OrigenDelAlmacen)
{
    public static readonly OpcionesDeContenidoWeb Vacias = new([], null);

    public static OpcionesDeContenidoWeb Leer(IConfiguration configuracion)
    {
        ArgumentNullException.ThrowIfNull(configuracion);
        var pasarela = (configuracion["Web:OrigenesDeLaPasarela"] ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(o => Validar(o, "BABELHUB__Web__OrigenesDeLaPasarela"))
            .ToList();
        var almacen = configuracion["Web:OrigenDelAlmacen"];
        return new OpcionesDeContenidoWeb(pasarela, string.IsNullOrWhiteSpace(almacen) ? null : Validar(almacen.Trim(), "BABELHUB__Web__OrigenDelAlmacen"));
    }

    private static string Validar(string origen, string variable)
    {
        if (!Uri.TryCreate(origen, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.UserInfo.Length > 0
            || origen.Contains('*', StringComparison.Ordinal) || origen.EndsWith('/'))
        {
            throw new ArranqueRechazadoException($"{variable} debe listar orígenes https exactos (esquema y host, sin ruta ni comodines).");
        }

        return origen;
    }
}

/// <summary>Las rutas <b>exactas</b> (nunca por prefijo) en las que se permite cada contenido de terceros (plan §4): script de Turnstile solo en cinco y la pasarela solo en <c>/checkout/pago</c>.</summary>
public static class RutasConContenidoDeTerceros
{
    public static readonly IReadOnlyDictionary<AccionProtegida, string> Turnstile = new Dictionary<AccionProtegida, string>
    {
        [AccionProtegida.Ingreso] = "/ingreso",
        [AccionProtegida.Registro] = "/registro",
        [AccionProtegida.HojaDeReclamacion] = "/reclamaciones/nueva",
        [AccionProtegida.Reserva] = "/checkout",
        [AccionProtegida.Asistente] = "/asistente",
    };

    public static readonly IReadOnlyDictionary<ContenidoDeTerceros, IReadOnlyList<string>> PorContenido = new Dictionary<ContenidoDeTerceros, IReadOnlyList<string>>
    {
        [ContenidoDeTerceros.Pasarela] = ["/checkout/pago"],
        // Las páginas de la biblioteca digital las define su plan; cada una se agrega aquí por decisión explícita.
        [ContenidoDeTerceros.DescargaDeLibro] = ["/biblioteca"],
    };

    /// <summary>
    /// Falla si un endpoint declara <see cref="PoliticaDeContenidoAttribute"/> o <see cref="VerificacionDePersonaAttribute"/> en una ruta que no es la exacta que le corresponde: otra
    /// página no puede abrir la política. Se llama una vez, con los endpoints ya mapeados.
    /// </summary>
    public static void Validar(IEnumerable<Endpoint> endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        foreach (var endpoint in endpoints)
        {
            var ruta = PlantillasDeRuta.De(endpoint);
            if (endpoint.Metadata.GetMetadata<PoliticaDeContenidoAttribute>() is { } politica
                && (ruta is null || !PorContenido[politica.Contenido].Contains(ruta, StringComparer.Ordinal)))
            {
                throw new InvalidOperationException($"«{endpoint.DisplayName}» declara [PoliticaDeContenido({politica.Contenido})] en la ruta «{ruta}», que no es una de las exactas permitidas.");
            }

            if (endpoint.Metadata.GetMetadata<VerificacionDePersonaAttribute>() is { } verificacion
                && (ruta is null || !string.Equals(Turnstile[verificacion.Accion], ruta, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException($"«{endpoint.DisplayName}» declara [VerificacionDePersona({verificacion.Accion})] en la ruta «{ruta}», que no es la exacta ({Turnstile[verificacion.Accion]}).");
            }
        }
    }
}

public static class PlantillasDeRuta
{
    /// <summary>La plantilla de la ruta del endpoint (nunca la dirección con sus valores), siempre con «/» inicial.</summary>
    public static string? De(Endpoint? endpoint) =>
        (endpoint as RouteEndpoint)?.RoutePattern.RawText is { Length: > 0 } texto ? "/" + texto.TrimStart('/') : null;
}

/// <summary>La política de contenido de una respuesta (WEB-18, WEB-40): la base de la §6 con un nonce por respuesta, y nada más que lo que la ruta declara. Nunca <c>unsafe-inline</c> ni <c>unsafe-eval</c>.</summary>
public static class PoliticaDeContenidoWeb
{
    private static readonly object ClaveDelNonce = new();

    /// <summary>El nonce de esta respuesta (16 bytes aleatorios en base64): solo los scripts y estilos que lo lleven corren; no se guarda en caché.</summary>
    public static string NonceDeLaRespuesta(this HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        if (contexto.Items.TryGetValue(ClaveDelNonce, out var existente) && existente is string nonce)
        {
            return nonce;
        }

        nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        contexto.Items[ClaveDelNonce] = nonce;
        return nonce;
    }

    public static string Construir(HttpContext contexto, OpcionesDeContenidoWeb opciones)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(opciones);

        var scripts = new List<string> { "'self'", $"'nonce-{contexto.NonceDeLaRespuesta()}'" };
        var marcos = new List<string>();
        var conexiones = new List<string> { "'self'" };
        var formularios = new List<string> { "'self'" };

        if (contexto.OrigenParaLaPoliticaDeContenido() is { } turnstile)
        {
            scripts.Add(turnstile);
            marcos.Add(turnstile);
        }

        switch (contexto.GetEndpoint()?.Metadata.GetMetadata<PoliticaDeContenidoAttribute>()?.Contenido)
        {
            case ContenidoDeTerceros.Pasarela:
                scripts.AddRange(opciones.OrigenesDeLaPasarela);
                marcos.AddRange(opciones.OrigenesDeLaPasarela);
                conexiones.AddRange(opciones.OrigenesDeLaPasarela);
                formularios.AddRange(opciones.OrigenesDeLaPasarela);
                break;
            case ContenidoDeTerceros.DescargaDeLibro when opciones.OrigenDelAlmacen is { } almacen:
                formularios.Add(almacen);
                break;
        }

        var directivas = new List<string>
        {
            "default-src 'self'",
            "script-src " + string.Join(' ', scripts.Distinct(StringComparer.Ordinal)),
            "style-src 'self'",
            "img-src 'self' data:",
            "font-src 'self'",
            "connect-src " + string.Join(' ', conexiones.Distinct(StringComparer.Ordinal)),
        };
        if (marcos.Count > 0)
        {
            directivas.Add("frame-src " + string.Join(' ', marcos.Distinct(StringComparer.Ordinal)));
        }

        directivas.Add("frame-ancestors 'none'");
        directivas.Add("base-uri 'none'");
        directivas.Add("form-action " + string.Join(' ', formularios.Distinct(StringComparer.Ordinal)));
        directivas.Add("object-src 'none'");
        return string.Join("; ", directivas);
    }
}

/// <summary>
/// Las cabeceras de seguridad de <b>toda</b> respuesta, también 4xx, 5xx, archivos estáticos y salud (WEB-18, WEB-40): <c>Content-Security-Policy</c> con nonce y por ruta,
/// <c>Strict-Transport-Security</c> propio (el del marco no actúa detrás del proxy, donde la aplicación ve HTTP), <c>X-Content-Type-Options</c>, <c>Referrer-Policy</c> y una
/// <c>Permissions-Policy</c> mínima; y <c>Cache-Control: no-store</c> en todo HTML o fragmento autenticado y en toda respuesta con <c>Set-Cookie</c> (el nonce no debe cachearse).
/// Se escriben al empezar la respuesta, cuando ya se conoce el endpoint, y reemplazan lo que otro componente hubiera puesto. Va <b>primero</b> en el pipeline.
/// </summary>
public sealed class CabecerasDeSeguridadMiddleware(RequestDelegate siguiente, OpcionesDeContenidoWeb opciones)
{
    public const string Hsts = "max-age=31536000";
    public const string PoliticaDePermisos = "accelerometer=(), camera=(), geolocation=(), gyroscope=(), microphone=(), usb=()";

    public Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        contexto.Response.OnStarting(() =>
        {
            Aplicar(contexto);
            return Task.CompletedTask;
        });
        return siguiente(contexto);
    }

    private void Aplicar(HttpContext contexto)
    {
        var cabeceras = contexto.Response.Headers;
        cabeceras["Content-Security-Policy"] = PoliticaDeContenidoWeb.Construir(contexto, opciones);
        cabeceras["Strict-Transport-Security"] = Hsts;
        cabeceras["X-Content-Type-Options"] = "nosniff";
        cabeceras["Referrer-Policy"] = "strict-origin-when-cross-origin";
        cabeceras["Permissions-Policy"] = PoliticaDePermisos;

        if ((EsHtml(contexto.Response.ContentType) && contexto.User.Identity?.IsAuthenticated == true) || cabeceras.ContainsKey("Set-Cookie"))
        {
            cabeceras.CacheControl = "no-store";
        }
    }

    private static bool EsHtml(string? tipo) => tipo is not null && tipo.StartsWith("text/html", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Pone el nonce de la respuesta en un <c>&lt;script bh-nonce&gt;</c> o <c>&lt;style bh-nonce&gt;</c> (los únicos en línea que la política admite).</summary>
[HtmlTargetElement("script", Attributes = "bh-nonce")]
[HtmlTargetElement("style", Attributes = "bh-nonce")]
public sealed class NonceTagHelper : TagHelper
{
    [ViewContext]
    [HtmlAttributeNotBound]
    public Microsoft.AspNetCore.Mvc.Rendering.ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.Attributes.RemoveAll("bh-nonce");
        output.Attributes.SetAttribute("nonce", ViewContext.HttpContext.NonceDeLaRespuesta());
    }
}

public static class CabecerasDeSeguridadExtensiones
{
    public static IServiceCollection AgregarCabecerasDeSeguridad(this IServiceCollection servicios, OpcionesDeContenidoWeb? opciones = null)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AddSingleton(opciones ?? OpcionesDeContenidoWeb.Vacias);
        return servicios;
    }

    /// <summary>Va <b>primero</b>: sus cabeceras salen también en lo que responden el manejador de errores y los archivos estáticos.</summary>
    public static IApplicationBuilder UsarCabecerasDeSeguridad(this IApplicationBuilder app) => app.UseMiddleware<CabecerasDeSeguridadMiddleware>();
}
