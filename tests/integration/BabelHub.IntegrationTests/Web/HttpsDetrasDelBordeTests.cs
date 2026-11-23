using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// HTTPS detrás de Cloudflare, el túnel y Nginx (PAG-36; AC-PAG-31): la aplicación recibe HTTP y confía solo en <c>X-Forwarded-Proto</c> para ver el esquema real; lo que el borde declara HTTP se redirige a HTTPS aunque la regla «Always Use HTTPS»
/// de Cloudflare no esté activa (la salud y lo que no pasó por un proxy, no); toda respuesta lleva HSTS; las cookies de la sesión van <c>Secure</c>; y ninguna vista ni recurso propio pide nada por <c>http://</c> (sin contenido mixto).
/// </summary>
public sealed partial class HttpsDetrasDelBordeTests
{
    private static async Task<(WebApplication App, HttpClient Cliente)> Iniciar(bool conRedireccion = true)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarEsquemaDelBorde();
        var app = builder.Build();
        app.UseForwardedHeaders();
        if (conRedireccion)
        {
            app.UsarRedireccionAHttps();
        }

        app.Run(async contexto => await contexto.Response.WriteAsync($"{contexto.Request.Scheme}|{contexto.Request.IsHttps}|{contexto.Request.Path}"));
        await app.StartAsync();
        return (app, app.GetTestClient());
    }

    private static HttpRequestMessage Pedir(HttpMethod metodo, string ruta, string? protoReenviado, string? host = null)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta);
        if (protoReenviado is not null)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Forwarded-Proto", protoReenviado);
        }

        if (host is not null)
        {
            solicitud.Headers.Host = host;
        }

        return solicitud;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-31")]
    public async Task Lo_que_el_borde_declara_https_la_aplicacion_lo_ve_como_https()
    {
        var (app, cliente) = await Iniciar();
        await using var _ = app;

        using var respuesta = await cliente.SendAsync(Pedir(HttpMethod.Get, "/tienda", "https"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("https|True|/tienda", await respuesta.Content.ReadAsStringAsync());
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-31")]
    [InlineData("GET")]
    [InlineData("HEAD")]
    [InlineData("POST")]
    public async Task Lo_que_el_borde_declara_http_se_redirige_a_https_conservando_ruta_y_consulta_con_308(string metodo)
    {
        var (app, cliente) = await Iniciar();
        await using var _ = app;

        using var respuesta = await cliente.SendAsync(Pedir(new HttpMethod(metodo), "/catalogo/obra?id=a%20b&x=1", "http", host: "babel-hub.shop"));

        Assert.Equal(HttpStatusCode.PermanentRedirect, respuesta.StatusCode);
        Assert.Equal("https://babel-hub.shop/catalogo/obra?id=a%20b&x=1", respuesta.Headers.Location?.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-31")]
    public async Task La_redireccion_no_toma_el_destino_de_otra_cabecera_y_codifica_la_ruta()
    {
        var (app, cliente) = await Iniciar();
        await using var _ = app;

        // El host del destino es el de la propia solicitud: un X-Forwarded-Host o un Host de otro servidor no cambian a dónde va. Y una ruta con saltos de línea decodificados no llega cruda a la cabecera.
        using var solicitud = Pedir(HttpMethod.Get, "/a%0d%0aSet-Cookie:x=1", "http", host: "babel-hub.shop");
        solicitud.Headers.TryAddWithoutValidation("X-Forwarded-Host", "malo.example");
        using var respuesta = await cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.PermanentRedirect, respuesta.StatusCode);
        var destino = respuesta.Headers.Location!.OriginalString;
        Assert.StartsWith("https://babel-hub.shop/", destino, StringComparison.Ordinal);
        Assert.DoesNotContain("malo.example", destino, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', destino);
        Assert.DoesNotContain('\n', destino);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-31")]
    [InlineData("/health")]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task La_salud_no_se_redirige_aunque_el_borde_declare_http(string ruta)
    {
        var (app, cliente) = await Iniciar();
        await using var _ = app;

        using var respuesta = await cliente.SendAsync(Pedir(HttpMethod.Get, ruta, "http"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-31")]
    public async Task Una_solicitud_que_no_paso_por_un_proxy_no_se_redirige_la_salud_local_y_el_https_entre_servicios()
    {
        var (app, cliente) = await Iniciar();
        await using var _ = app;

        using var respuesta = await cliente.SendAsync(Pedir(HttpMethod.Get, "/cualquier/ruta", protoReenviado: null));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("http|False|/cualquier/ruta", await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-31")]
    public async Task Sin_la_redireccion_la_aplicacion_no_redirige_nada()
    {
        var (app, cliente) = await Iniciar(conRedireccion: false);
        await using var _ = app;

        using var respuesta = await cliente.SendAsync(Pedir(HttpMethod.Get, "/tienda", "http"));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-31")]
    public async Task Las_cabeceras_de_reenvio_del_cliente_no_cambian_nada_mas_que_el_esquema()
    {
        var (app, cliente) = await Iniciar();
        await using var _ = app;

        using var solicitud = Pedir(HttpMethod.Get, "/tienda", "https", host: "babel-hub.shop");
        solicitud.Headers.TryAddWithoutValidation("X-Forwarded-Host", "malo.example");
        solicitud.Headers.TryAddWithoutValidation("X-Forwarded-For", "203.0.113.9");
        using var respuesta = await cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("https|True|/tienda", await respuesta.Content.ReadAsStringAsync());
    }

    // ---------- HSTS y cookies Secure aunque la aplicación vea HTTP ----------

    [Theory]
    [Trait("Criterio", "AC-PAG-31")]
    [InlineData(null)]
    [InlineData("https")]
    public async Task Toda_respuesta_lleva_HSTS_y_las_cookies_de_sesion_van_Secure_aunque_la_aplicacion_vea_http(string? protoReenviado)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarEsquemaDelBorde();
        builder.Services.AgregarCabecerasDeSeguridad();
        await using var app = builder.Build();
        app.UseForwardedHeaders();
        app.UsarRedireccionAHttps();
        app.UsarCabecerasDeSeguridad();
        app.Run(contexto =>
        {
            // Las cookies de la tienda con el prefijo __Host-: el navegador las exige Secure, y la aplicación las marca así aun cuando ve HTTP (detrás del proxy sin X-Forwarded-Proto).
            CookieDeSesionWeb.Escribir(contexto.Response, BabelHub.Ecommerce.Application.Web.HostWeb.Tienda, "valor-opaco");
            CookieDelDesafio.Escribir(contexto.Response, new string('a', 43));
            return Task.CompletedTask;
        });
        await app.StartAsync();

        using var respuesta = await app.GetTestClient().SendAsync(Pedir(HttpMethod.Get, "/", protoReenviado));

        Assert.Equal("max-age=31536000", respuesta.Headers.GetValues("Strict-Transport-Security").Single());
        var cookies = respuesta.Headers.GetValues("Set-Cookie").ToList();
        Assert.Equal(2, cookies.Count);
        Assert.All(cookies, c =>
        {
            Assert.StartsWith("__Host-", c, StringComparison.Ordinal);
            Assert.Contains("secure", c, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("httponly", c, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("path=/", c, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("domain=", c, StringComparison.OrdinalIgnoreCase);
        });
    }

    // ---------- Sin contenido mixto ----------

    [GeneratedRegex(@"(?<![A-Za-z0-9])http://[^\s""'<>)]+", RegexOptions.CultureInvariant)]
    private static partial Regex UrlHttp();

    [Fact]
    [Trait("Criterio", "AC-PAG-31")]
    public void Ninguna_vista_ni_recurso_propio_de_la_tienda_pide_nada_por_http()
    {
        var api = RaizDelRepositorio.Api;
        var archivos = Directory.EnumerateFiles(api, "*.*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !f.Contains($"{Path.DirectorySeparatorChar}lib{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && Path.GetExtension(f) is ".cshtml" or ".cs" or ".js" or ".css" or ".html" or ".json")
            .Where(f => !f.EndsWith(Path.Combine("wwwroot", "css", "app.css"), StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(archivos);

        // Los espacios de nombres XML de SVG no son una petición de red: el navegador no los descarga.
        var infractores = archivos
            .SelectMany(f => UrlHttp().Matches(File.ReadAllText(f)).Select(m => (Archivo: Path.GetRelativePath(api, f), Url: m.Value)))
            .Where(x => !x.Url.StartsWith("http://www.w3.org/", StringComparison.Ordinal))
            .ToList();

        Assert.True(infractores.Count == 0, "Referencias http:// (contenido mixto): " + string.Join(", ", infractores.Select(x => $"{x.Archivo} → {x.Url}")));
    }
}
