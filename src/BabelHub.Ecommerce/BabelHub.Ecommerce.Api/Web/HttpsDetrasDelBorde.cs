using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// El esquema HTTPS detrás del borde (Cloudflare termina TLS; Nginx y el túnel hablan HTTP con la aplicación). Se toma <b>solo</b> <c>X-Forwarded-Proto</c> (el origen del cliente lo fija <c>OrigenDelBorde</c> con la lista de proxies
/// conocidos, y la aplicación solo es alcanzable desde la red del borde, SEG-17), de modo que las cookies <c>__Host-</c>, el antiforgery y los enlaces vean HTTPS. Con <see cref="UsarRedireccionAHttps"/>, una solicitud que el borde declara
/// como HTTP se redirige a HTTPS aunque la regla «Always Use HTTPS» de Cloudflare no esté activa: la aplicación no depende de que el borde esté bien configurado. HSTS lo envía <c>CabecerasDeSeguridad</c> en toda respuesta.
/// </summary>
public static class HttpsDetrasDelBorde
{
    /// <summary>La cabecera que <c>UseForwardedHeaders</c> escribe con el esquema original cuando el proxy informó el suyo (<see cref="ForwardedHeadersOptions.OriginalProtoHeaderName"/>).</summary>
    public const string CabeceraDelEsquemaOriginal = "X-Original-Proto";

    /// <summary>Solo el esquema (<c>X-Forwarded-Proto</c>): sin lista de proxies del marco (la aplicación solo es alcanzable desde el borde, y la dirección del cliente no sale de aquí).</summary>
    public static IServiceCollection AgregarEsquemaDelBorde(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedProto;
            o.OriginalProtoHeaderName = CabeceraDelEsquemaOriginal;
            o.KnownIPNetworks.Clear();
            o.KnownProxies.Clear();
        });
        return servicios;
    }

    /// <summary>Va justo después de <c>UseForwardedHeaders</c>: redirige a HTTPS lo que el borde declara como HTTP. No toca lo que no pasó por un proxy (la salud local del contenedor ni el HTTPS entre servicios por el 8443) ni la salud.</summary>
    public static IApplicationBuilder UsarRedireccionAHttps(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<RedireccionAHttpsMiddleware>();
    }
}

/// <summary>
/// Redirige con <c>308</c> (conserva el método) a <c>https://</c> la solicitud que el borde declaró HTTP (<c>X-Forwarded-Proto: http</c>). Solo actúa si el proxy informó su esquema (<c>X-Original-Proto</c> lo deja <c>UseForwardedHeaders</c>):
/// sin proxy no hay a quién creerle y la solicitud sigue. El host del destino es el de la propia solicitud, que el filtro de hosts del servidor ya validó contra <c>BABELHUB__Web__HostsPermitidos</c>; no se toma de ninguna otra cabecera.
/// Las comprobaciones de salud no se redirigen.
/// </summary>
public sealed class RedireccionAHttpsMiddleware(RequestDelegate siguiente)
{
    public Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        var solicitud = contexto.Request;
        if (!solicitud.IsHttps
            && solicitud.Headers.ContainsKey(HttpsDetrasDelBorde.CabeceraDelEsquemaOriginal)
            && !solicitud.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase))
        {
            contexto.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
            // La ruta se vuelve a codificar (un %0d%0a decodificado no puede llegar a la cabecera) y la consulta ya viene codificada.
            contexto.Response.Headers.Location = $"https://{solicitud.Host.Host}{solicitud.PathBase.ToUriComponent()}{solicitud.Path.ToUriComponent()}{solicitud.QueryString.ToUriComponent()}";
            return Task.CompletedTask;
        }

        return siguiente(contexto);
    }
}
