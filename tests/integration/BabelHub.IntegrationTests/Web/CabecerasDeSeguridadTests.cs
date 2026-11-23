using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>T17: cabeceras de seguridad en toda respuesta, CSP con nonce y por ruta exacta, <c>no-store</c> y configuración de htmx (WEB-18, WEB-40, AC-WEB-14).</summary>
public sealed class CabecerasDeSeguridadTests
{
    private const string Pasarela = "https://checkout.pasarela.example";
    private const string Almacen = "https://almacen.babelhub.example";

    private static readonly OpcionesDeContenidoWeb Opciones = new([Pasarela, "https://api.pasarela.example"], Almacen);

    private static Task<AnfitrionWeb> Iniciar(SesionesWebDePrueba? sesiones = null, OpcionesDeContenidoWeb? opciones = null) =>
        AnfitrionConSesion.Iniciar(
            sesiones ?? new SesionesWebDePrueba(),
            servicios: s => s.AgregarCabecerasDeSeguridad(opciones ?? Opciones),
            antesDeLasRutas: app =>
            {
                app.UsarCabecerasDeSeguridad();
                app.UseStaticFiles();
            });

    private static string Politica(HttpResponseMessage respuesta) => respuesta.Headers.GetValues("Content-Security-Policy").Single();

    private static string Directiva(string politica, string nombre) =>
        politica.Split("; ").SingleOrDefault(d => d.StartsWith(nombre + " ", StringComparison.Ordinal)) ?? string.Empty;

    [Theory]
    [Trait("Criterio", "AC-WEB-14")]
    [Trait("Criterio", "AC-COM-05")]
    [InlineData("/prueba/cabeceras/pagina", HttpStatusCode.OK)]
    [InlineData("/prueba/cabeceras/json", HttpStatusCode.OK)]
    [InlineData("/prueba/cabeceras/prohibido", HttpStatusCode.Forbidden)]
    [InlineData("/prueba/cabeceras/error", HttpStatusCode.InternalServerError)]
    [InlineData("/contacto", HttpStatusCode.OK)]
    [InlineData("/legal/reembolso", HttpStatusCode.MovedPermanently)]
    [InlineData("/no/existe", HttpStatusCode.NotFound)]
    [InlineData("/js/bh.js", HttpStatusCode.OK)]
    [InlineData("/error", HttpStatusCode.InternalServerError)]
    public async Task ToraRespuesta_LlevaLasCuatroCabecerasYLaCsp_AunLas4xxLas5xxYLosEstaticos(string ruta, HttpStatusCode estado)
    {
        await using var anfitrion = await Iniciar();

        var respuesta = await anfitrion.Cliente.GetAsync(ruta);

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Equal("max-age=31536000", respuesta.Headers.GetValues("Strict-Transport-Security").Single());
        Assert.Equal("nosniff", respuesta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("strict-origin-when-cross-origin", respuesta.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("camera=()", respuesta.Headers.GetValues("Permissions-Policy").Single(), StringComparison.Ordinal);
        var politica = Politica(respuesta);
        Assert.DoesNotContain("unsafe-inline", politica, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", politica, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task LaCspBase_EsLaDeLaSeccion6_ConUnNonceDistintoEnCadaRespuesta()
    {
        await using var anfitrion = await Iniciar();

        var uno = Politica(await anfitrion.Cliente.GetAsync("/prueba/cabeceras/pagina"));
        var dos = Politica(await anfitrion.Cliente.GetAsync("/prueba/cabeceras/pagina"));
        var nonce = Regex.Match(uno, "'nonce-([A-Za-z0-9+/=]{22,})'");

        Assert.True(nonce.Success);
        Assert.NotEqual(uno, dos);
        Assert.Equal(
            $"default-src 'self'; script-src 'self' 'nonce-{nonce.Groups[1].Value}'; style-src 'self'; img-src 'self' data:; font-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'",
            uno);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task Turnstile_SoloSeAbreEnLaRutaQueLoDeclara()
    {
        await using var anfitrion = await Iniciar();

        var ingreso = Politica(await anfitrion.Cliente.GetAsync("/ingreso"));
        var otra = Politica(await anfitrion.Cliente.GetAsync("/prueba/cabeceras/pagina"));

        Assert.Contains("https://challenges.cloudflare.com", Directiva(ingreso, "script-src"), StringComparison.Ordinal);
        Assert.Equal("frame-src https://challenges.cloudflare.com", Directiva(ingreso, "frame-src"));
        Assert.DoesNotContain("cloudflare", otra, StringComparison.Ordinal);
        Assert.Equal(string.Empty, Directiva(otra, "frame-src"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task LaPasarela_SoloSeAbreEnCheckoutPago_YNuncaConviveConTurnstile()
    {
        await using var anfitrion = await Iniciar();

        var pago = Politica(await anfitrion.Cliente.GetAsync("/checkout/pago"));
        var otra = Politica(await anfitrion.Cliente.GetAsync("/ingreso"));

        foreach (var directiva in new[] { "script-src", "frame-src", "connect-src", "form-action" })
        {
            Assert.Contains(Pasarela, Directiva(pago, directiva), StringComparison.Ordinal);
            Assert.Contains("https://api.pasarela.example", Directiva(pago, directiva), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("cloudflare", pago, StringComparison.Ordinal);
        Assert.DoesNotContain("pasarela", otra, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe", pago, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task ElOrigenDelAlmacen_SoloEstaEnElFormActionDeLaPaginaDeLaDescarga()
    {
        await using var anfitrion = await Iniciar();

        var biblioteca = Politica(await anfitrion.Cliente.GetAsync("/biblioteca"));
        var otra = Politica(await anfitrion.Cliente.GetAsync("/prueba/cabeceras/pagina"));

        Assert.Equal($"form-action 'self' {Almacen}", Directiva(biblioteca, "form-action"));
        Assert.Equal("form-action 'self'", Directiva(otra, "form-action"));
        Assert.DoesNotContain(Almacen, biblioteca.Replace($"form-action 'self' {Almacen}", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task SinOrigenesConfigurados_LaRutaNoAbreNada()
    {
        await using var anfitrion = await Iniciar(opciones: OpcionesDeContenidoWeb.Vacias);

        var pago = Politica(await anfitrion.Cliente.GetAsync("/checkout/pago"));
        var biblioteca = Politica(await anfitrion.Cliente.GetAsync("/biblioteca"));

        Assert.Equal("connect-src 'self'", Directiva(pago, "connect-src"));
        Assert.Equal("form-action 'self'", Directiva(pago, "form-action"));
        Assert.Equal("form-action 'self'", Directiva(biblioteca, "form-action"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task ElHtmlAutenticado_YToraRespuestaConSetCookie_LlevanNoStore_PeroUnJsonAnonimoNo()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await Iniciar(sesiones);
        var autenticada = new HttpRequestMessage(HttpMethod.Get, "/prueba/cabeceras/pagina");
        autenticada.Headers.Add("Cookie", AnfitrionConSesion.Cookie());

        var conSesion = await anfitrion.Cliente.SendAsync(autenticada);
        var conCookie = await anfitrion.Cliente.GetAsync("/prueba/cabeceras/cookie");
        var anonimo = await anfitrion.Cliente.GetAsync("/prueba/cabeceras/json");
        var estatico = await anfitrion.Cliente.GetAsync("/js/bh.js");

        Assert.Equal("no-store", conSesion.Headers.CacheControl!.ToString());
        Assert.Equal("no-store", conCookie.Headers.CacheControl!.ToString());
        Assert.Null(anonimo.Headers.CacheControl);
        Assert.NotEqual("no-store", estatico.Headers.CacheControl?.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public void ElNonce_EsElMismoDentroDeUnaRespuesta_YDistintoEntreRespuestas()
    {
        var uno = new DefaultHttpContext();
        var dos = new DefaultHttpContext();

        Assert.Equal(uno.NonceDeLaRespuesta(), uno.NonceDeLaRespuesta());
        Assert.NotEqual(uno.NonceDeLaRespuesta(), dos.NonceDeLaRespuesta());
        Assert.Equal(24, uno.NonceDeLaRespuesta().Length);
    }

    // ---- Las rutas exactas ----

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task LasRutasDeLaAplicacion_DeclaranElContenidoDeTercerosSoloDondeCorresponde()
    {
        await using var anfitrion = await Iniciar();

        RutasConContenidoDeTerceros.Validar(anfitrion.Servicios.GetRequiredService<EndpointDataSource>().Endpoints);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-14")]
    [InlineData("/checkout/pago/otra")]
    [InlineData("/checkout")]
    [InlineData("/Checkout/Pago")]
    [InlineData("/checkout/{paso}")]
    public void LaPasarelaEnOtraRuta_HaceFallarElArranque(string patron)
    {
        var endpoint = Endpoint(patron, new PoliticaDeContenidoAttribute(ContenidoDeTerceros.Pasarela));

        Assert.Throws<InvalidOperationException>(() => RutasConContenidoDeTerceros.Validar([endpoint]));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-14")]
    [InlineData(AccionProtegida.Ingreso, "/registro")]
    [InlineData(AccionProtegida.Registro, "/ingreso")]
    [InlineData(AccionProtegida.Reserva, "/checkout/pago")]
    [InlineData(AccionProtegida.Asistente, "/asistente/{id}")]
    [InlineData(AccionProtegida.HojaDeReclamacion, "/reclamaciones")]
    public void TurnstileEnOtraRuta_HaceFallarElArranque(AccionProtegida accion, string patron)
    {
        var endpoint = Endpoint(patron, new VerificacionDePersonaAttribute(accion));

        Assert.Throws<InvalidOperationException>(() => RutasConContenidoDeTerceros.Validar([endpoint]));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-14")]
    [InlineData(AccionProtegida.Ingreso, "/ingreso")]
    [InlineData(AccionProtegida.Registro, "/registro")]
    [InlineData(AccionProtegida.HojaDeReclamacion, "/reclamaciones/nueva")]
    [InlineData(AccionProtegida.Reserva, "/checkout")]
    [InlineData(AccionProtegida.Asistente, "/asistente")]
    public void LasCincoRutasDeTurnstile_SonLasExactas(AccionProtegida accion, string patron)
    {
        RutasConContenidoDeTerceros.Validar([Endpoint(patron, new VerificacionDePersonaAttribute(accion))]);
    }

    private static RouteEndpoint Endpoint(string patron, params object[] metadatos) =>
        new(_ => Task.CompletedTask, Microsoft.AspNetCore.Routing.Patterns.RoutePatternFactory.Parse(patron), 0, new EndpointMetadataCollection(metadatos), patron);

    [Theory]
    [Trait("Criterio", "AC-WEB-14")]
    [InlineData("http://pasarela.example")]
    [InlineData("https://*.pasarela.example")]
    [InlineData("https://pasarela.example/pago")]
    [InlineData("https://pasarela.example/")]
    [InlineData("pasarela.example")]
    public void UnOrigenDeLaPasarelaMalFormado_HaceFallarElArranque(string origen)
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Web:OrigenesDeLaPasarela"] = origen }).Build();

        var error = Assert.Throws<ArranqueRechazadoException>(() => OpcionesDeContenidoWeb.Leer(configuracion));
        Assert.Contains("BABELHUB__Web__OrigenesDeLaPasarela", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public void LosOrigenesConfiguradosSeLeen_YSinConfiguracionNoHayNinguno()
    {
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Web:OrigenesDeLaPasarela"] = $"{Pasarela}, https://api.pasarela.example",
            ["Web:OrigenDelAlmacen"] = Almacen,
        }).Build();

        var opciones = OpcionesDeContenidoWeb.Leer(configuracion);

        Assert.Equal([Pasarela, "https://api.pasarela.example"], opciones.OrigenesDeLaPasarela);
        Assert.Equal(Almacen, opciones.OrigenDelAlmacen);
        Assert.Empty(OpcionesDeContenidoWeb.Leer(new ConfigurationBuilder().Build()).OrigenesDeLaPasarela);
    }

    // ---- htmx sin eval, sin historial y sin hx-on ----

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public async Task ElLayout_ConfiguraHtmxSinEvalNiHistorialNiScriptsEnRespuestas()
    {
        await using var anfitrion = await Iniciar();

        var html = await anfitrion.Cliente.GetStringAsync("/legal/terminos");
        var configuracion = Regex.Match(html, "<meta name=\"htmx-config\" content='([^']+)'");

        Assert.True(configuracion.Success);
        var json = System.Text.Json.JsonDocument.Parse(configuracion.Groups[1].Value).RootElement;
        Assert.False(json.GetProperty("allowEval").GetBoolean());
        Assert.False(json.GetProperty("allowScriptTags").GetBoolean());
        Assert.Equal(0, json.GetProperty("historyCacheSize").GetInt32());
        Assert.True(json.GetProperty("selfRequestsOnly").GetBoolean());
        Assert.False(json.GetProperty("includeIndicatorStyles").GetBoolean());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-14")]
    public void NingunaVista_UsaHxOnNiScriptsNiEstilosEnLinea()
    {
        var vistas = Directory.EnumerateFiles(RaizDelRepositorio.Api, "*.cshtml", SearchOption.AllDirectories)
            .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !a.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(vistas);
        foreach (var vista in vistas)
        {
            var texto = File.ReadAllText(vista);
            Assert.DoesNotContain("hx-on", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"<script(?![^>]*\bsrc=)(?![^>]*bh-nonce)[^>]*>", texto);
            Assert.DoesNotMatch(@"<style(?![^>]*bh-nonce)[^>]*>", texto);
            Assert.DoesNotMatch(@"\sstyle\s*=\s*""", texto);
            Assert.DoesNotMatch(@"\son(click|load|change|submit|error)\s*=", texto);
        }
    }
}
