using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// T16: antiforgery global (htmx desde el <c>&lt;meta&gt;</c>, cookie propia <c>__Host-</c>), <c>Origin</c> y <c>Sec-Fetch-Site</c> con los hosts configurados, rechazos
/// registrados en Auditoría y captura del borrador solo con un token válido para la sesión que venció (WEB-05, WEB-25, AC-WEB-03, AC-WEB-20).
/// </summary>
public sealed class ProteccionContraFalsificacionTests
{
    private const string Ruta = "/prueba/sesion/sin-atributo";

    private static async Task<(AnfitrionWeb Anfitrion, SesionesWebDePrueba Sesiones, DenegacionesDePrueba Denegaciones)> Armar(
        IReadOnlyList<string>? hosts = null,
        BorradoresDePrueba? borradores = null,
        Action<IServiceCollection>? mas = null)
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        var denegaciones = new DenegacionesDePrueba();
        var anfitrion = await AnfitrionConSesion.Iniciar(
            sesiones,
            servicios: s =>
            {
                s.AddSingleton<BabelHub.Ecommerce.Application.Auditoria.IDenegacionDeAcceso>(denegaciones);
                if (borradores is not null)
                {
                    s.AddSingleton<IBorradoresDePantalla>(borradores);
                }

                mas?.Invoke(s);
            },
            hostsPermitidos: hosts);
        return (anfitrion, sesiones, denegaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task UnPostSinToken_SeRechazaAunSinAtributo_YQuedaRegistrado()
    {
        var (anfitrion, _, denegaciones) = await Armar();
        await using var _ = anfitrion;

        var respuesta = await anfitrion.Cliente.SendAsync(new HttpRequestMessage(HttpMethod.Post, Ruta));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType!.MediaType);
        var registro = Assert.Single(denegaciones.Registradas);
        Assert.Equal("sin-sesion", registro.Motivo);
        Assert.Equal(Ruta, registro.Ruta);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task UnPostConElToken_Pasa_ElCampoOLaCabeceraDeHtmx()
    {
        var (anfitrion, _, denegaciones) = await Armar();
        await using var _ = anfitrion;

        var conCabecera = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie());
        var respuesta = await anfitrion.Cliente.SendAsync(conCabecera);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Empty(denegaciones.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task LaRutaConIgnoreAntiforgeryToken_NoPideToken()
    {
        var (anfitrion, _, _) = await Armar();
        await using var _ = anfitrion;

        var respuesta = await anfitrion.Cliente.PostAsync("/prueba/sesion/ignorada", null);

        Assert.True(respuesta.StatusCode == HttpStatusCode.OK, await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task LaRutaQueIgnoraElToken_AunComprueba_Origin_YSecFetchSite()
    {
        var (anfitrion, _, _) = await Armar();
        await using var _ = anfitrion;
        var ajeno = new HttpRequestMessage(HttpMethod.Post, "/prueba/sesion/ignorada");
        ajeno.Headers.Add("Origin", "https://atacante.example");
        var deOtroSitio = new HttpRequestMessage(HttpMethod.Post, "/prueba/sesion/ignorada");
        deOtroSitio.Headers.Add("Sec-Fetch-Site", "cross-site");

        Assert.Equal(HttpStatusCode.Forbidden, (await anfitrion.Cliente.SendAsync(ajeno)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anfitrion.Cliente.SendAsync(deOtroSitio)).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task UnGet_NoPideToken_NiComprueba_Origin()
    {
        var (anfitrion, _, _) = await Armar();
        await using var _ = anfitrion;
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/prueba/sesion/yo");
        solicitud.Headers.Add("Origin", "https://otro.example");
        solicitud.Headers.Add("Sec-Fetch-Site", "cross-site");

        Assert.Equal(HttpStatusCode.OK, (await anfitrion.Cliente.SendAsync(solicitud)).StatusCode);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData("https://atacante.example")]
    [InlineData("null")]
    [InlineData("http://tienda.babelhub.pe")]
    [InlineData("https://tienda.babelhub.pe.atacante.example")]
    [InlineData("https://tienda.babelhub.pe/ruta")]
    [InlineData("https://usuario@tienda.babelhub.pe")]
    [InlineData("https://tienda.babelhub.pe:8443")]
    public async Task UnOrigenQueNoEsElDelSitio_SeRechazaConToken_YQuedaRegistrado(string origen)
    {
        var (anfitrion, _, denegaciones) = await Armar(hosts: ["tienda.babelhub.pe", "localhost"]);
        await using var _ = anfitrion;
        var solicitud = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie());
        solicitud.Headers.Add("Origin", origen);

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Single(denegaciones.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task UnOrigenDeLosHostsConfigurados_Pasa()
    {
        var (anfitrion, _, _) = await Armar(hosts: ["tienda.babelhub.pe", "www.babelhub.pe", "localhost"]);
        await using var _ = anfitrion;
        var solicitud = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie());
        solicitud.Headers.Add("Origin", "https://www.babelhub.pe");
        solicitud.Headers.Add("Sec-Fetch-Site", "same-origin");

        // El esquema del origen coincide con el de la solicitud (https).
        Assert.Equal(HttpStatusCode.OK, (await anfitrion.Cliente.SendAsync(solicitud)).StatusCode);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    [InlineData("none")]
    public async Task UnaSolicitudDeOtroSitioSegunElNavegador_SeRechaza(string sitio)
    {
        var (anfitrion, _, denegaciones) = await Armar();
        await using var _ = anfitrion;
        var solicitud = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie());
        solicitud.Headers.Add("Sec-Fetch-Site", sitio);

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Single(denegaciones.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task SinHostsConfigurados_ElOrigenDebeSerElDeLaPropiaSolicitud()
    {
        var (anfitrion, _, _) = await Armar();
        await using var _ = anfitrion;
        var propio = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie());
        propio.Headers.Add("Origin", "https://localhost");
        var ajeno = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie());
        ajeno.Headers.Add("Origin", "https://otro.example");

        Assert.Equal(HttpStatusCode.OK, (await anfitrion.Cliente.SendAsync(propio)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anfitrion.Cliente.SendAsync(ajeno)).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task SiAuditoriaFalla_ElRechazoSigueYNoSeConvierteEnUnError()
    {
        var (anfitrion, _, denegaciones) = await Armar();
        await using var _ = anfitrion;
        denegaciones.Falla = true;

        var respuesta = await anfitrion.Cliente.SendAsync(new HttpRequestMessage(HttpMethod.Post, Ruta));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Single(denegaciones.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task ElTokenEstaLigadoALaSesion_UnoDeOtraSesionOUnoAnonimoNoSirve()
    {
        var (anfitrion, _, _) = await Armar();
        await using var _ = anfitrion;
        var deOtraSesion = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie(valor: "Zz9YxW8vU7tS6rQ5pO4nM3lK2jI1hG0fEdCbAaQqRr1"));
        var anonimo = await AnfitrionConSesion.Post(anfitrion, Ruta);
        // El token de la sesión A usado con la cookie de la sesión B (los dos identificadores son válidos para el módulo).
        var deA = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie());
        var cabeceras = deOtraSesion.Headers.GetValues("RequestVerificationToken").Single();
        deA.Headers.Remove("RequestVerificationToken");
        deA.Headers.Add("RequestVerificationToken", cabeceras);
        // Un token anonimo usado con la sesión.
        var anonimoConSesion = await AnfitrionConSesion.Post(anfitrion, Ruta);
        anonimoConSesion.Headers.Remove("Cookie");
        anonimoConSesion.Headers.Add("Cookie", string.Join("; ", anonimo.Headers.GetValues("Cookie").Append(AnfitrionConSesion.Cookie())));

        Assert.Equal(HttpStatusCode.BadRequest, (await anfitrion.Cliente.SendAsync(deA)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anfitrion.Cliente.SendAsync(anonimoConSesion)).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task LaCookieDelAntiforgery_EsPropiaConPrefijoHost_YDeSeguridad()
    {
        var (anfitrion, _, _) = await Armar();
        await using var _ = anfitrion;

        var respuesta = await anfitrion.Cliente.GetAsync("/prueba/sesion/token");
        var cookie = respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-bh-antiforgery=", StringComparison.Ordinal));
        var atributos = cookie.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(a => a.ToLowerInvariant()).ToArray();

        Assert.Contains("path=/", atributos);
        Assert.Contains("secure", atributos);
        Assert.Contains("httponly", atributos);
        Assert.Contains("samesite=lax", atributos);
        Assert.DoesNotContain(atributos, a => a.StartsWith("domain", StringComparison.Ordinal));
        Assert.False(respuesta.Headers.Contains("X-Frame-Options"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task ElLayout_LlevaElTokenEnUnMeta_YBhJsLoEnviaEnCadaSolicitudDeHtmx()
    {
        var (anfitrion, _, _) = await Armar();
        await using var _ = anfitrion;

        var html = await anfitrion.Cliente.GetStringAsync("/legal/terminos");
        var coincidencia = System.Text.RegularExpressions.Regex.Match(html, "<meta name=\"csrf-token\" content=\"([^\"]+)\"");
        var js = await File.ReadAllTextAsync(Path.Combine(RaizDelRepositorio.Api, "wwwroot", "js", "bh.js"));

        Assert.True(coincidencia.Success);
        Assert.True(coincidencia.Groups[1].Value.Length > 20);
        Assert.Contains("htmx:configRequest", js, StringComparison.Ordinal);
        Assert.Contains("RequestVerificationToken", js, StringComparison.Ordinal);
    }

    // ---- Captura del borrador al vencer la sesión (WEB-25, D06) ----

    private static async Task<HttpRequestMessage> PostDeFormulario(AnfitrionWeb anfitrion, string ruta, string nombre, string otro)
    {
        var solicitud = await AnfitrionConSesion.Post(anfitrion, ruta, AnfitrionConSesion.Cookie());
        solicitud.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Nombre"] = nombre, ["Otro"] = otro, ["Texto"] = "x" });
        return solicitud;
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task AlVencerLaSesion_SeConservanSoloLosCamposMarcados_ConUnTokenValidoParaEsaSesion_YSeLlevaAlIngreso()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        var borradores = new BorradoresDePrueba(sesiones.UsuarioId);
        var (anfitrion, sesionesDePrueba, _) = await Armar(borradores: borradores);
        await using var _ = anfitrion;
        // El token se emitió con la sesión viva; después la sesión vence (la solicitud ya llega anónima).
        var solicitud = await PostDeFormulario(anfitrion, "/cuenta/datos", "Ana", "no-se-conserva");
        sesionesDePrueba.ConCarga(EstadoDeCarga.VencidaPorInactividad);
        borradores.UsuarioRetenido = sesionesDePrueba.UsuarioId;

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.True(respuesta.Headers.Contains("Clear-Site-Data"));
        var (plantilla, campos) = Assert.Single(borradores.Guardados);
        Assert.Equal("/cuenta/datos", plantilla);
        Assert.Equal(new Dictionary<string, string> { ["Nombre"] = "Ana" }, campos);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task ElTokenDeOtraPersona_NoPermiteConservarNada_PeroLaPersonaVaAlIngreso()
    {
        var (anfitrion, sesiones, _) = await Armar(borradores: new BorradoresDePrueba(Guid.NewGuid()));
        await using var _ = anfitrion;
        var borradores = anfitrion.Servicios.GetRequiredService<IBorradoresDePantalla>() as BorradoresDePrueba;
        var solicitud = await PostDeFormulario(anfitrion, "/cuenta/datos", "Ana", "x");
        sesiones.ConCarga(EstadoDeCarga.VencidaPorInactividad);

        // La fila retenida es de otro usuario: el token emitido para la persona original no vale para ella.
        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal(1, borradores!.Intentos);
        Assert.Empty(borradores.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task UnTokenAnonimoOAjenoALaSesion_NoConservaNada()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        var borradores = new BorradoresDePrueba(sesiones.UsuarioId);
        var (anfitrion, sesionesDePrueba, _) = await Armar(borradores: borradores);
        await using var _ = anfitrion;
        sesionesDePrueba.ConCarga(EstadoDeCarga.VencidaPorInactividad);
        borradores.UsuarioRetenido = sesionesDePrueba.UsuarioId;

        // Un token pedido sin sesión (anónimo) y enviado con la cookie vencida de otra persona.
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/cuenta/datos");
        solicitud.Headers.Remove("Cookie");
        solicitud.Headers.Add("Cookie", string.Join("; ", [AnfitrionConSesion.Cookie()]));
        var anonimo = await AnfitrionConSesion.Post(anfitrion, "/cuenta/datos");
        solicitud.Headers.Remove("RequestVerificationToken");
        solicitud.Headers.Add("RequestVerificationToken", anonimo.Headers.GetValues("RequestVerificationToken").Single());
        solicitud.Headers.Remove("Cookie");
        solicitud.Headers.Add("Cookie", string.Join("; ", anonimo.Headers.GetValues("Cookie").Append(AnfitrionConSesion.Cookie())));
        solicitud.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["Nombre"] = "Plantado" });

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Empty(borradores.Guardados);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task UnaRutaSinCamposConservables_NoIntentaGuardarNada()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        var borradores = new BorradoresDePrueba(sesiones.UsuarioId);
        var (anfitrion, sesionesDePrueba, _) = await Armar(borradores: borradores);
        await using var _ = anfitrion;
        var solicitud = await PostDeFormulario(anfitrion, "/reclamaciones/nueva", "x", "y");
        sesionesDePrueba.ConCarga(EstadoDeCarga.VencidaPorInactividad);

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal(0, borradores.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task TrasUnCierreVoluntario_NoSeConservaNada_YElPostSinSesionSeRechaza()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        var borradores = new BorradoresDePrueba(sesiones.UsuarioId);
        var (anfitrion, sesionesDePrueba, _) = await Armar(borradores: borradores);
        await using var _ = anfitrion;
        var solicitud = await PostDeFormulario(anfitrion, "/cuenta/datos", "Ana", "x");
        sesionesDePrueba.ConCarga(EstadoDeCarga.Cerrada);

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal(0, borradores.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-20")]
    public async Task UnaAccionConSesionVencida_NoSeEjecuta_AunSinBorradores()
    {
        var (anfitrion, sesiones, _) = await Armar();
        await using var _ = anfitrion;
        var solicitud = await AnfitrionConSesion.Post(anfitrion, Ruta, AnfitrionConSesion.Cookie());
        sesiones.ConCarga(EstadoDeCarga.VencidaPorTopeAbsoluto);

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
    }

    // ---- El comprobador de Origin, sin servidor ----

    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData(null, null, ResultadoDeOrigen.Valido)]
    [InlineData("https://www.babelhub.pe", "same-origin", ResultadoDeOrigen.Valido)]
    [InlineData("https://WWW.BabelHub.pe", null, ResultadoDeOrigen.Valido)]
    [InlineData("https://www.babelhub.pe", "cross-site", ResultadoDeOrigen.DeOtroSitio)]
    [InlineData("https://evil.example", "same-origin", ResultadoDeOrigen.OrigenNoPermitido)]
    [InlineData(null, "cross-site", ResultadoDeOrigen.DeOtroSitio)]
    [InlineData("null", null, ResultadoDeOrigen.OrigenNoPermitido)]
    public void ElComprobadorDeOrigen_AplicaLasDosCabeceras(string? origen, string? sitio, ResultadoDeOrigen esperado)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Scheme = "https";
        contexto.Request.Host = new HostString("www.babelhub.pe");
        if (origen is not null)
        {
            contexto.Request.Headers["Origin"] = origen;
        }

        if (sitio is not null)
        {
            contexto.Request.Headers["Sec-Fetch-Site"] = sitio;
        }

        Assert.Equal(esperado, ComprobadorDeOrigen.Comprobar(contexto.Request, ["www.babelhub.pe"]));
    }
}
