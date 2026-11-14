using System.Net;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T15: el handler de la sesión en el pipeline, sobre un módulo de sesiones simulado. Qué actor ve cada solicitud, cuándo se borra la cookie, qué es una consulta
/// automática, qué pasa si Identidad no responde y cómo se cierra la sesión (WEB-04, WEB-19, WEB-22, WEB-27, WEB-28).
/// </summary>
public sealed class SesionWebHandlerTests
{
    private static async Task<HttpResponseMessage> Get(AnfitrionWeb anfitrion, string ruta, string? cookie = null, bool htmx = false)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (cookie is not null)
        {
            solicitud.Headers.Add("Cookie", cookie);
        }

        if (htmx)
        {
            solicitud.Headers.Add("HX-Request", "true");
        }

        return await anfitrion.Cliente.SendAsync(solicitud);
    }

    private static async Task<System.Text.Json.JsonElement> Yo(AnfitrionWeb anfitrion, string? cookie = null) =>
        JsonDePrueba.Leer(await (await Get(anfitrion, "/prueba/sesion/yo", cookie)).Content.ReadAsStringAsync());

    private static string? CookieBorrada(HttpResponseMessage respuesta, HostWeb host = HostWeb.Tienda) =>
        respuesta.Headers.TryGetValues("Set-Cookie", out var valores)
            ? valores.SingleOrDefault(v => v.StartsWith(CookieDeSesionWeb.Nombre(host) + "=;", StringComparison.Ordinal) || v.StartsWith(CookieDeSesionWeb.Nombre(host) + "=", StringComparison.Ordinal) && v.Contains("1970", StringComparison.Ordinal))
            : null;

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task SinCookie_LaSolicitudEsAnonima_YNoSeConsultaLaSesion()
    {
        var sesiones = new SesionesWebDePrueba();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);

        var yo = await Yo(anfitrion);

        Assert.False(yo.GetProperty("autenticada").GetBoolean());
        Assert.Equal("Anonimo", yo.GetProperty("actorTipo").GetString());
        Assert.Equal("SinCookie", yo.GetProperty("estado").GetString());
        Assert.False(yo.GetProperty("tieneToken").GetBoolean());
        Assert.Empty(sesiones.Cargas);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData(RolWeb.Cliente, "cliente", HostWeb.Tienda)]
    [InlineData(RolWeb.Encargado, "encargado-de-sucursal", HostWeb.Gestion)]
    [InlineData(RolWeb.Logistica, "personal-de-logistica", HostWeb.Gestion)]
    [InlineData(RolWeb.AdministradorCentral, "administrador-central", HostWeb.Gestion)]
    public async Task ConUnaSesionValida_ElActorSaleDeLaCredencialVerificada(RolWeb rol, string rolEsperado, HostWeb host)
    {
        var sucursal = rol is RolWeb.Encargado or RolWeb.Logistica ? Guid.NewGuid() : (Guid?)null;
        var sesiones = new SesionesWebDePrueba().ConSesionValida(rol, sucursal);
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones, host);

        var yo = await Yo(anfitrion, AnfitrionConSesion.Cookie(host));

        Assert.True(yo.GetProperty("autenticada").GetBoolean());
        Assert.Equal(sesiones.UsuarioId.ToString("D"), yo.GetProperty("usuarioId").GetString());
        Assert.Equal(rolEsperado, yo.GetProperty("rol").GetString());
        Assert.Equal("Usuario", yo.GetProperty("actorTipo").GetString());
        Assert.Equal(rolEsperado, yo.GetProperty("actorRol").GetString());
        Assert.Equal(sesiones.UsuarioId, yo.GetProperty("actorUsuarioId").GetGuid());
        Assert.Equal(sucursal?.ToString("D"), yo.TryGetProperty("sucursal", out var s) && s.ValueKind == System.Text.Json.JsonValueKind.String ? s.GetString() : null);
        Assert.Equal("Asegurada", yo.GetProperty("estado").GetString());
        Assert.True(yo.GetProperty("tieneToken").GetBoolean());
        var carga = Assert.Single(sesiones.Cargas);
        Assert.Equal(host, carga.Host);
        Assert.Equal(SesionesWebDePrueba.ValorDeLaCookie, carga.Cookie);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task UnaAccionDeLaPersona_CargaLaSesionComoAccion_YUnaConsultaAutomaticaComoConsulta()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);

        await Get(anfitrion, "/prueba/sesion/yo", AnfitrionConSesion.Cookie());
        await Get(anfitrion, "/prueba/sesion/sondeo", AnfitrionConSesion.Cookie());

        Assert.Equal([OrigenDeLaAccion.AccionDelUsuario, OrigenDeLaAccion.ConsultaAutomatica], sesiones.Cargas.Select(c => c.Origen).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task UnaCabeceraDelCliente_NoPuedeHacerPasarUnaAccionPorConsultaAutomatica()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/prueba/sesion/yo");
        solicitud.Headers.Add("Cookie", AnfitrionConSesion.Cookie());
        solicitud.Headers.Add("HX-Trigger", "every 20s");
        solicitud.Headers.Add("X-Consulta-Automatica", "true");

        await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(OrigenDeLaAccion.AccionDelUsuario, Assert.Single(sesiones.Cargas).Origen);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-23")]
    [InlineData(EstadoDeCarga.Inexistente, EstadoDeLaSesionEnLaSolicitud.Inexistente)]
    [InlineData(EstadoDeCarga.Cerrada, EstadoDeLaSesionEnLaSolicitud.Cerrada)]
    [InlineData(EstadoDeCarga.VencidaPorInactividad, EstadoDeLaSesionEnLaSolicitud.Vencida)]
    [InlineData(EstadoDeCarga.VencidaPorTopeAbsoluto, EstadoDeLaSesionEnLaSolicitud.Vencida)]
    [InlineData(EstadoDeCarga.OtroHost, EstadoDeLaSesionEnLaSolicitud.OtroHost)]
    public async Task UnaSesionQueNoSirve_BorraLaCookie_YSigueComoVisitaAnonima(EstadoDeCarga carga, EstadoDeLaSesionEnLaSolicitud esperado)
    {
        var sesiones = new SesionesWebDePrueba().ConCarga(carga);
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);

        var respuesta = await Get(anfitrion, "/prueba/sesion/yo", AnfitrionConSesion.Cookie());
        var yo = JsonDePrueba.Leer(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.False(yo.GetProperty("autenticada").GetBoolean());
        Assert.Equal("Anonimo", yo.GetProperty("actorTipo").GetString());
        Assert.Equal(esperado.ToString(), yo.GetProperty("estado").GetString());
        Assert.NotNull(CookieBorrada(respuesta));
        Assert.Empty(sesiones.Aseguradas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task SiIdentidadRechazaLaRenovacion_LaCookieSeBorra_YLaPersonaQuedaSinSesion()
    {
        var sesiones = new SesionesWebDePrueba().ConCredencial(EstadoDeCredencial.SesionCerrada);
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);

        var respuesta = await Get(anfitrion, "/prueba/sesion/yo", AnfitrionConSesion.Cookie());
        var yo = JsonDePrueba.Leer(await respuesta.Content.ReadAsStringAsync());

        Assert.False(yo.GetProperty("autenticada").GetBoolean());
        Assert.Equal("RenovacionRechazada", yo.GetProperty("estado").GetString());
        Assert.False(yo.GetProperty("tieneToken").GetBoolean());
        Assert.NotNull(CookieBorrada(respuesta));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-26")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SiLaCredencialNoPuedeVerificarse_NoSeCierraNadaYUnaPantallaVaAServicioNoDisponible(bool alCargar)
    {
        var sesiones = alCargar ? new SesionesWebDePrueba().ConCarga(EstadoDeCarga.ServicioNoDisponible) : new SesionesWebDePrueba().ConCredencial(EstadoDeCredencial.ServicioNoDisponible);
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);

        var respuesta = await Get(anfitrion, "/prueba/sesion/yo", AnfitrionConSesion.Cookie());

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/no-disponible", respuesta.Headers.Location!.OriginalString);
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
        Assert.Null(CookieBorrada(respuesta));
        Assert.Empty(sesiones.Cierres);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task ConElServicioNoDisponible_HtmxRecibeHxRedirect_YUnaAccionUnProblemaDe503()
    {
        var sesiones = new SesionesWebDePrueba().ConCredencial(EstadoDeCredencial.ServicioNoDisponible);
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);

        var htmx = await Get(anfitrion, "/prueba/sesion/yo", AnfitrionConSesion.Cookie(), htmx: true);
        Assert.Equal(HttpStatusCode.OK, htmx.StatusCode);
        Assert.Equal("/no-disponible", htmx.Headers.GetValues("HX-Redirect").Single());

        var accion = new HttpRequestMessage(HttpMethod.Post, "/prueba/sesion/accion");
        accion.Headers.Add("Cookie", AnfitrionConSesion.Cookie());
        var respuesta = await anfitrion.Cliente.SendAsync(accion);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType!.MediaType);
        Assert.Contains("ECO-SERVICIO-NO-DISPONIBLE", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(sesiones.Cierres);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-26")]
    [InlineData("/no-disponible")]
    [InlineData("/error")]
    public async Task LosDestinosDeLasRedirecciones_NoConsultanLaSesion_PorqueNoHayBucle(string ruta)
    {
        var sesiones = new SesionesWebDePrueba().ConCredencial(EstadoDeCredencial.ServicioNoDisponible);
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);

        var respuesta = await Get(anfitrion, ruta, AnfitrionConSesion.Cookie());

        Assert.Empty(sesiones.Cargas);
        Assert.NotEqual(HttpStatusCode.Found, respuesta.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task LaCookieDelOtroHost_NoSeLee()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones, HostWeb.Tienda);

        var yo = await Yo(anfitrion, AnfitrionConSesion.Cookie(HostWeb.Gestion));

        Assert.False(yo.GetProperty("autenticada").GetBoolean());
        Assert.Equal("SinCookie", yo.GetProperty("estado").GetString());
        Assert.Empty(sesiones.Cargas);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-23")]
    [InlineData(ModoDeArranque.Ensayo)]
    [InlineData(ModoDeArranque.Restauracion)]
    public async Task EnEnsayoYRestauracion_NoSeCargaNingunaSesion(ModoDeArranque modo)
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones, modo: modo);

        var yo = await Yo(anfitrion, AnfitrionConSesion.Cookie());

        Assert.False(yo.GetProperty("autenticada").GetBoolean());
        Assert.Equal("NoSeCargo", yo.GetProperty("estado").GetString());
        Assert.Empty(sesiones.Cargas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task ElActorYLaIdentidad_NoSeFiltranDeUnaSolicitudALaSiguiente()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);

        var conSesion = await Yo(anfitrion, AnfitrionConSesion.Cookie());
        var sinSesion = await Yo(anfitrion);

        Assert.True(conSesion.GetProperty("autenticada").GetBoolean());
        Assert.False(sinSesion.GetProperty("autenticada").GetBoolean());
        Assert.Equal("Anonimo", sinSesion.GetProperty("actorTipo").GetString());
    }
}
