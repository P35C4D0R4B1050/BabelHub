using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T20: recorre <b>todas</b> las rutas de la Api en <b>ambos</b> hosts, anónimo y con sesión, y comprueba lo que debe valer en cada respuesta: las cuatro cabeceras también en 4xx y 5xx, los
/// atributos de todas las cookies, la CSP por ruta, <c>no-store</c> en el HTML y ningún <c>unsafe-inline</c>; y que el regreso de la pasarela conserva la sesión (AC-WEB-14).
/// </summary>
public sealed class RecorridoDeRutasTests
{
    private const string Pasarela = "https://checkout.pasarela.example";

    private static readonly Regex CspBase = new(
        "^default-src 'self'; script-src 'self' 'nonce-[A-Za-z0-9+/=]{22,}'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'$",
        RegexOptions.CultureInvariant);

    private sealed record Ruta(string Metodo, string Url, bool DeterminadaPorAtributo);

    private static async Task<AnfitrionWeb> Iniciar(HostWeb host, SesionesWebDePrueba sesiones) =>
        await AnfitrionConSesion.Iniciar(
            sesiones,
            host,
            servicios: s => s.AgregarCabecerasDeSeguridad(new OpcionesDeContenidoWeb([Pasarela], "https://almacen.babelhub.example")),
            antesDeLasRutas: app =>
            {
                app.UsarCabecerasDeSeguridad();
                app.UseStaticFiles();
            });

    /// <summary>Las rutas reales de la Api (no las de prueba), con valores de ejemplo para sus parámetros.</summary>
    private static IReadOnlyList<Ruta> RutasDeLaApi(AnfitrionWeb anfitrion) =>
    [
        .. anfitrion.Servicios.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is { } accion && accion.ControllerTypeInfo.Assembly == AssemblyMarker.Assembly)
            .SelectMany(e => (e.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"]).Select(metodo =>
                new Ruta(metodo, Sustituir(e.RoutePattern.RawText!), e.Metadata.GetMetadata<PoliticaDeContenidoAttribute>() is not null || e.Metadata.GetMetadata<BabelHub.Ecommerce.Api.Plataforma.VerificacionDePersonaAttribute>() is not null))),
    ];

    private static string Sustituir(string patron)
    {
        var url = Regex.Replace(patron, @"\{[^}:]+:guid\}", Guid.NewGuid().ToString("D"));
        url = Regex.Replace(url, @"\{texto\}", "terminos");
        url = Regex.Replace(url, @"\{[^}]+\}", "1");
        return "/" + url.TrimStart('/');
    }

    private static async Task<HttpResponseMessage> Enviar(AnfitrionWeb anfitrion, Ruta ruta, string? cookie)
    {
        var solicitud = new HttpRequestMessage(new HttpMethod(ruta.Metodo), ruta.Url);
        if (cookie is not null)
        {
            solicitud.Headers.Add("Cookie", cookie);
        }

        return await anfitrion.Cliente.SendAsync(solicitud);
    }

    public static TheoryData<HostWeb, bool> Escenarios => new()
    {
        { HostWeb.Tienda, false },
        { HostWeb.Tienda, true },
        { HostWeb.Gestion, false },
        { HostWeb.Gestion, true },
    };

    [Theory]
    [Trait("Criterio", "AC-WEB-14")]
    [MemberData(nameof(Escenarios))]
    public async Task TodaRutaDeLaApi_EnCadaHost_LlevaLasCabecerasLaCspYLosAtributosDeSusCookies(HostWeb host, bool conSesion)
    {
        var sesiones = conSesion ? new SesionesWebDePrueba().ConSesionValida(host == HostWeb.Tienda ? RolWeb.Cliente : RolWeb.AdministradorCentral, host == HostWeb.Gestion ? Guid.NewGuid() : null) : new SesionesWebDePrueba();
        await using var anfitrion = await Iniciar(host, sesiones);
        var rutas = RutasDeLaApi(anfitrion);
        Assert.NotEmpty(rutas);
        var cookie = conSesion ? AnfitrionConSesion.Cookie(host) : null;
        var fallos = new List<string>();

        foreach (var ruta in rutas)
        {
            var respuesta = await Enviar(anfitrion, ruta, cookie);
            var etiqueta = $"{host} {(conSesion ? "con sesión" : "anónimo")} {ruta.Metodo} {ruta.Url} → {(int)respuesta.StatusCode}";

            // Las cuatro cabeceras y la CSP salen en todas, también en 4xx y 5xx.
            foreach (var cabecera in new[] { "Strict-Transport-Security", "X-Content-Type-Options", "Referrer-Policy", "Permissions-Policy", "Content-Security-Policy" })
            {
                if (!respuesta.Headers.Contains(cabecera))
                {
                    fallos.Add($"{etiqueta}: falta {cabecera}");
                }
            }

            if (!respuesta.Headers.TryGetValues("Content-Security-Policy", out var politicas))
            {
                continue;
            }

            var politica = politicas.Single();
            if (politica.Contains("unsafe-inline", StringComparison.Ordinal) || politica.Contains("unsafe-eval", StringComparison.Ordinal))
            {
                fallos.Add($"{etiqueta}: la CSP trae unsafe-*");
            }

            if (!ruta.DeterminadaPorAtributo && !CspBase.IsMatch(politica))
            {
                fallos.Add($"{etiqueta}: la CSP no es la base: {politica}");
            }

            if (respuesta.Content.Headers.ContentType?.MediaType == "text/html" && respuesta.Headers.CacheControl?.NoStore != true)
            {
                fallos.Add($"{etiqueta}: el HTML no es no-store");
            }

            fallos.AddRange(AtributosDeLasCookies(respuesta).Select(f => $"{etiqueta}: {f}"));
        }

        Assert.True(fallos.Count == 0, string.Join(Environment.NewLine, fallos));
    }

    private static IEnumerable<string> AtributosDeLasCookies(HttpResponseMessage respuesta)
    {
        if (!respuesta.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            yield break;
        }

        foreach (var cookie in cookies)
        {
            var partes = cookie.Split(';', StringSplitOptions.TrimEntries);
            var nombre = partes[0].Split('=')[0];
            var atributos = partes.Skip(1).Select(a => a.ToLowerInvariant()).ToList();

            if (!atributos.Contains("secure"))
            {
                yield return $"la cookie {nombre} no es Secure";
            }

            if (!atributos.Contains("samesite=lax"))
            {
                yield return $"la cookie {nombre} no es SameSite=Lax";
            }

            if (atributos.Any(a => a.StartsWith("domain", StringComparison.Ordinal)))
            {
                yield return $"la cookie {nombre} lleva Domain";
            }

            if (nombre.StartsWith("__Host-", StringComparison.Ordinal))
            {
                if (!atributos.Contains("httponly"))
                {
                    yield return $"la cookie {nombre} no es HttpOnly";
                }

                if (!atributos.Contains("path=/"))
                {
                    yield return $"la cookie {nombre} no es de la ruta /";
                }
            }
            else if (nombre is not ("bh-tema" or "bh-sucursal"))
            {
                yield return $"la cookie {nombre} no está en la lista de cookies de la web";
            }
        }
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-14")]
    [InlineData("/prueba/cabeceras/error", HttpStatusCode.InternalServerError)]
    [InlineData("/prueba/cabeceras/prohibido", HttpStatusCode.Forbidden)]
    [InlineData("/ruta/que/no/existe", HttpStatusCode.NotFound)]
    public async Task LasRespuestasDeError_EnAmbosHosts_LlevanLasCuatroCabecerasYLaCsp(string ruta, HttpStatusCode estado)
    {
        foreach (var host in new[] { HostWeb.Tienda, HostWeb.Gestion })
        {
            await using var anfitrion = await Iniciar(host, new SesionesWebDePrueba());

            var respuesta = await anfitrion.Cliente.GetAsync(ruta);

            Assert.Equal(estado, respuesta.StatusCode);
            Assert.True(respuesta.Headers.Contains("Strict-Transport-Security") && respuesta.Headers.Contains("X-Content-Type-Options") && respuesta.Headers.Contains("Referrer-Policy") && respuesta.Headers.Contains("Permissions-Policy"));
            Assert.Matches(CspBase, respuesta.Headers.GetValues("Content-Security-Policy").Single());
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task TodoPostDeLaApi_SinToken_SeRechazaConLasCabeceras_EnAmbosHosts()
    {
        foreach (var host in new[] { HostWeb.Tienda, HostWeb.Gestion })
        {
            await using var anfitrion = await Iniciar(host, new SesionesWebDePrueba().ConSesionValida());
            foreach (var ruta in RutasDeLaApi(anfitrion).Where(r => r.Metodo == "POST"))
            {
                var respuesta = await Enviar(anfitrion, ruta, AnfitrionConSesion.Cookie(host));

                Assert.True(respuesta.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.NotFound, $"{host} POST {ruta.Url} → {(int)respuesta.StatusCode}");
                Assert.True(respuesta.Headers.Contains("Content-Security-Policy"), $"{host} POST {ruta.Url} sin CSP");
            }
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task ElRegresoDeLaPasarela_UnGetSinEfectosDeOtroSitio_ConservaLaSesion_YNuncaUnPost()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await Iniciar(HostWeb.Tienda, sesiones);
        var retorno = new HttpRequestMessage(HttpMethod.Get, "/prueba/sesion/yo?estado=pagado&token=ajeno");
        retorno.Headers.Add("Cookie", AnfitrionConSesion.Cookie());
        retorno.Headers.Add("Sec-Fetch-Site", "cross-site");
        retorno.Headers.Add("Sec-Fetch-Mode", "navigate");
        retorno.Headers.Add("Referer", Pasarela + "/pago");

        var respuesta = await anfitrion.Cliente.SendAsync(retorno);
        var yo = JsonDePrueba.Leer(await respuesta.Content.ReadAsStringAsync());

        // La navegación de regreso es un GET de nivel superior: la cookie SameSite=Lax viaja y la sesión sigue siendo la misma.
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(yo.GetProperty("autenticada").GetBoolean());
        Assert.Equal(sesiones.UsuarioId.ToString("D"), yo.GetProperty("usuarioId").GetString());
        Assert.Equal(SameSiteMode.Lax, CookieDeSesionWeb.Opciones().SameSite);

        // Un POST de otro sitio (un formulario ajeno que apunta a la web) no pasa: Origin y Sec-Fetch-Site lo rechazan antes de ejecutar nada.
        var ajeno = await AnfitrionConSesion.Post(anfitrion, "/prueba/sesion/accion", AnfitrionConSesion.Cookie());
        ajeno.Headers.Add("Origin", Pasarela);
        ajeno.Headers.Add("Sec-Fetch-Site", "cross-site");
        Assert.Equal(HttpStatusCode.Forbidden, (await anfitrion.Cliente.SendAsync(ajeno)).StatusCode);
    }
}
