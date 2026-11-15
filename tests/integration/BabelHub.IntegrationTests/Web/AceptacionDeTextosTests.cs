using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Web.Errores;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Shared.Errores;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>T38: la pantalla mínima de aceptación (<c>GET</c> y <c>POST /textos/aceptar</c>) y la traducción de <c>ECO-TEXTOS-PENDIENTES</c> (WEB-34, WEB-41, WEB-48, AC-WEB-34, AC-WEB-35).</summary>
public sealed class AceptacionDeTextosTests
{
    private static string Cookie() => AnfitrionConSesion.Cookie();

    private static async Task<string> VersionVigente(AnfitrionWeb a)
    {
        using var ambito = a.Servicios.CreateScope();
        return (await ambito.ServiceProvider.GetRequiredService<IAceptacionDeTextos>().VersionVigente()).Valor.Identificador;
    }

    private static async Task<HttpResponseMessage> Ver(AnfitrionWeb a, string? cookie) =>
        await a.Cliente.SendAsync(Con(new HttpRequestMessage(HttpMethod.Get, "/textos/aceptar"), cookie));

    private static HttpRequestMessage Con(HttpRequestMessage solicitud, string? cookie)
    {
        if (cookie is not null)
        {
            solicitud.Headers.Add("Cookie", cookie);
        }

        return solicitud;
    }

    private static async Task<HttpResponseMessage> Aceptar(AnfitrionWeb a, string version, bool htmx = false)
    {
        var solicitud = await AnfitrionConSesion.Post(a, "/textos/aceptar", Cookie());
        solicitud.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["version"] = version });
        if (htmx)
        {
            solicitud.Headers.Add("HX-Request", "true");
        }

        return await a.Cliente.SendAsync(solicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task ConTextosPendientes_LaPantallaMuestraLasTresVersiones_ConElFormularioDeAceptar()
    {
        var (a, _, _, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        var vigente = await VersionVigente(a);

        var respuesta = await Ver(a, Cookie());
        var html = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.True(respuesta.Headers.CacheControl!.NoStore);
        foreach (var texto in new[] { "terminos", "privacidad", "reembolso" })
        {
            Assert.Contains($"href=\"/legal/{texto}\"", html, StringComparison.Ordinal);
        }

        Assert.Matches(new Regex("<form method=\"post\" action=\"/textos/aceptar\""), html);
        Assert.Contains($"name=\"version\" value=\"{vigente}\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.Contains("Acepto los textos legales", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task SiNoHayNadaQueAceptar_LaPantallaLlevaALaPortada_YSinSesionAlIngreso()
    {
        var (a, _, estado, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        estado.Estado = EstadoDeAceptacionDeTextos.Aceptados;

        var aceptados = await Ver(a, Cookie());
        var anonima = await Ver(a, null);

        Assert.Equal("/", aceptados.Headers.Location!.OriginalString);
        Assert.Equal("/ingreso", anonima.Headers.Location!.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-35")]
    public async Task AlAceptarLaVersionMostrada_IdentidadLaRegistra_YLaPersonaVuelveAdondeIba_UnaSolaVez()
    {
        var (a, sesiones, estado, aceptacion, _, almacen, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        aceptacion.Vigente = await VersionVigente(a);
        // La persona iba al catálogo cuando se le pidió aceptar: la política guardó su ruta.
        await a.Cliente.SendAsync(Con(new HttpRequestMessage(HttpMethod.Get, "/prueba/politica/catalogo/7"), Cookie()));
        Assert.Single(almacen.Retornos);

        var respuesta = await Aceptar(a, aceptacion.Vigente);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/prueba/politica/catalogo/7", respuesta.Headers.Location!.OriginalString);
        Assert.Equal((sesiones.UsuarioId, aceptacion.Vigente), Assert.Single(aceptacion.Aceptadas));
        Assert.Empty(almacen.Retornos);
        Assert.Equal(EstadoDeAceptacionDeTextos.Aceptados, estado.Estado);

        // Sin retorno guardado, la siguiente vuelve a la portada: el retorno no se reutiliza.
        var segunda = await Aceptar(a, aceptacion.Vigente);
        Assert.Equal("/", segunda.Headers.Location!.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-35")]
    public async Task UnaSolicitudDeHtmx_RecibeHxRedirectAlAceptar()
    {
        var (a, _, _, aceptacion, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        aceptacion.Vigente = await VersionVigente(a);

        var respuesta = await Aceptar(a, aceptacion.Vigente, htmx: true);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("/", respuesta.Headers.GetValues("HX-Redirect").Single());
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-35")]
    [InlineData("terminos:v0|privacidad:v0|reembolso:v0")]
    [InlineData("")]
    [InlineData("<script>alert(1)</script>")]
    public async Task SiLaVersionEnviadaYaNoEsLaVigente_NoSeAcepta_YSeMuestraDeNuevoConUnAviso(string version)
    {
        var (a, _, estado, aceptacion, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        aceptacion.Vigente = await VersionVigente(a);

        var respuesta = await Aceptar(a, version);
        var html = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Los textos cambiaron mientras los leías", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Empty(aceptacion.Aceptadas);
        Assert.Equal(EstadoDeAceptacionDeTextos.Pendientes, estado.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-39")]
    public async Task ConIdentidadCaida_AlAceptar_VaAServicioNoDisponible_SinAceptarNada()
    {
        var (a, _, _, aceptacion, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        aceptacion.Vigente = await VersionVigente(a);
        aceptacion.Caida = true;

        var respuesta = await Aceptar(a, aceptacion.Vigente);

        Assert.Equal("/no-disponible", respuesta.Headers.Location!.OriginalString);
        Assert.Empty(aceptacion.Aceptadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-35")]
    public async Task AceptarSinTokenAntifalsificacion_SeRechaza()
    {
        var (a, _, _, aceptacion, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        var solicitud = Con(new HttpRequestMessage(HttpMethod.Post, "/textos/aceptar"), Cookie());
        solicitud.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["version"] = await VersionVigente(a) });

        var respuesta = await a.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(aceptacion.Aceptadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task ElPersonal_TambienVeLaPantalla_YAlAceptarVuelveASuArea()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida(RolWeb.Encargado, Guid.NewGuid());
        var (a, _, _, aceptacion, _, _, _) = await AnfitrionConPolitica.Iniciar(HostWeb.Gestion, sesiones);
        await using var _ = a;
        aceptacion.Vigente = await VersionVigente(a);

        var respuesta = await Ver(a, AnfitrionConSesion.Cookie(HostWeb.Gestion));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
    }

    // ---- ECO-TEXTOS-PENDIENTES en la traducción de errores ----

    [Fact]
    [Trait("Criterio", "AC-WEB-35")]
    public void UnProblemaConElCodigoDeTextosPendientes_SeTraduceComoInvitacionAAceptar_SinConservarLoEscrito()
    {
        var problema = new ProblemaDto { Code = "ECO-TEXTOS-PENDIENTES", Status = 403, TraceId = "abc" };

        var respuesta = TraductorDeErrores.Traducir(problema);

        Assert.Equal("/textos/aceptar", respuesta.RedirigirA);
        Assert.False(respuesta.ConservaLoEscrito);
        Assert.Equal(403, respuesta.EstadoHttp);
        Assert.DoesNotContain("ECO-TEXTOS-PENDIENTES", respuesta.Vista.Mensaje ?? string.Empty, StringComparison.Ordinal);
    }
}
