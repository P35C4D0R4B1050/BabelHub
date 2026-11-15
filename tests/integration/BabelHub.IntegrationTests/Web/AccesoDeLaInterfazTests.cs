using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Un <see cref="IDesafioDelIngreso"/> con el paso que fije la prueba (y que puede fallar), que anota lo que se le pidió.</summary>
internal sealed class DesafioDePrueba : IDesafioDelIngreso
{
    public PasoDelIngresoWeb Paso { get; set; } = PasoDelIngresoWeb.PresentarSegundoFactor;

    public bool Falla { get; set; }

    public List<string> Consultados { get; } = [];

    public List<string> Cancelados { get; } = [];

    public Task<PasoDelIngresoWeb> PasoPendiente(string valorDelDesafio, CancellationToken cancelacion = default)
    {
        Consultados.Add(valorDelDesafio);
        return Falla ? throw new InvalidOperationException("Identidad no responde") : Task.FromResult(Paso);
    }

    public Task Cancelar(string valorDelDesafio, CancellationToken cancelacion = default)
    {
        Cancelados.Add(valorDelDesafio);
        return Falla ? throw new InvalidOperationException("Identidad no responde") : Task.CompletedTask;
    }
}

/// <summary>
/// T18: los filtros de área y de paso pendiente del ingreso, <c>POST /ingreso/cancelar</c>, la cookie del desafío y los datos del dispositivo y de la región del borde
/// (WEB-02, WEB-03, WEB-34, WEB-37, WEB-39; AC-WEB-02, AC-WEB-28).
/// </summary>
public sealed class AccesoDeLaInterfazTests
{
    private const string Desafio = "Qw3ErTy5UiOp7AsDf9GhJk1LzXc2VbNm4QwErTyUiOp";

    private static async Task<(AnfitrionWeb Anfitrion, SesionesWebDePrueba Sesiones, DesafioDePrueba Desafios, DenegacionesDePrueba Denegaciones)> Armar(HostWeb host = HostWeb.Tienda, SesionesWebDePrueba? sesiones = null)
    {
        sesiones ??= new SesionesWebDePrueba();
        var desafios = new DesafioDePrueba();
        var denegaciones = new DenegacionesDePrueba();
        var anfitrion = await AnfitrionConSesion.Iniciar(
            sesiones,
            host,
            servicios: s =>
            {
                s.AddSingleton<IDesafioDelIngreso>(desafios);
                s.AddSingleton<BabelHub.Ecommerce.Application.Auditoria.IDenegacionDeAcceso>(denegaciones);
            });
        return (anfitrion, sesiones, desafios, denegaciones);
    }

    private static Task<HttpResponseMessage> Pedir(AnfitrionWeb anfitrion, string ruta, string? cookies = null, bool htmx = false, HttpMethod? metodo = null)
    {
        var solicitud = new HttpRequestMessage(metodo ?? HttpMethod.Get, ruta);
        if (cookies is not null)
        {
            solicitud.Headers.Add("Cookie", cookies);
        }

        if (htmx)
        {
            solicitud.Headers.Add("HX-Request", "true");
        }

        return anfitrion.Cliente.SendAsync(solicitud);
    }

    private static string ConDesafio(string? otras = null) => string.Join("; ", new[] { $"{CookieDelDesafio.Nombre}={Desafio}", otras }.Where(c => c is not null));

    // ---- Área (WEB-02, WEB-03) ----

    [Theory]
    [Trait("Criterio", "AC-WEB-02")]
    [InlineData(RolWeb.Encargado, "/prueba/area/sucursal", HttpStatusCode.OK)]
    [InlineData(RolWeb.Logistica, "/prueba/area/sucursal", HttpStatusCode.OK)]
    [InlineData(RolWeb.AdministradorCentral, "/prueba/area/sucursal", HttpStatusCode.OK)]
    [InlineData(RolWeb.AdministradorCentral, "/prueba/area/admin", HttpStatusCode.OK)]
    [InlineData(RolWeb.Encargado, "/prueba/area/admin", HttpStatusCode.Found)]
    [InlineData(RolWeb.Logistica, "/prueba/area/admin", HttpStatusCode.Found)]
    public async Task UnRol_AbreSoloLasAreasQueSuModuloLePermite(RolWeb rol, string ruta, HttpStatusCode esperado)
    {
        var (anfitrion, _, _, denegaciones) = await Armar(HostWeb.Gestion, new SesionesWebDePrueba().ConSesionValida(rol, Guid.NewGuid()));
        await using var _ = anfitrion;

        var respuesta = await Pedir(anfitrion, ruta, AnfitrionConSesion.Cookie(HostWeb.Gestion));

        Assert.Equal(esperado, respuesta.StatusCode);
        if (esperado == HttpStatusCode.Found)
        {
            Assert.Equal("/sin-permiso", respuesta.Headers.Location!.OriginalString);
            var registro = Assert.Single(denegaciones.Registradas);
            Assert.Equal("fuera-de-rol", registro.Motivo);
            Assert.Equal("ECO-PROHIBIDO", registro.CodigoDeError);
        }
        else
        {
            Assert.Empty(denegaciones.Registradas);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public async Task ElEncargadoQueAbreAdministracion_VeLaPantallaDeSinPermiso_ConSalidas()
    {
        var (anfitrion, _, _, _) = await Armar(HostWeb.Gestion, new SesionesWebDePrueba().ConSesionValida(RolWeb.Encargado, Guid.NewGuid()));
        await using var _ = anfitrion;

        var redireccion = await Pedir(anfitrion, "/prueba/area/admin", AnfitrionConSesion.Cookie(HostWeb.Gestion));
        var pantalla = await Pedir(anfitrion, redireccion.Headers.Location!.OriginalString, AnfitrionConSesion.Cookie(HostWeb.Gestion));

        Assert.Equal(HttpStatusCode.Forbidden, pantalla.StatusCode);
        Assert.Contains("Ir al inicio", await pantalla.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task UnaAccionDirectaFueraDeRol_Recibe403SinRedireccion_YUnaDeHtmxElHxRedirect()
    {
        var (anfitrion, _, _, denegaciones) = await Armar(HostWeb.Gestion, new SesionesWebDePrueba().ConSesionValida(RolWeb.Encargado, Guid.NewGuid()));
        await using var _ = anfitrion;
        var accion = await AnfitrionConSesion.Post(anfitrion, "/prueba/area/admin", AnfitrionConSesion.Cookie(HostWeb.Gestion));

        var directa = await anfitrion.Cliente.SendAsync(accion);
        var htmx = await Pedir(anfitrion, "/prueba/area/admin", AnfitrionConSesion.Cookie(HostWeb.Gestion), htmx: true);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, directa.StatusCode);
        Assert.Equal("/sin-permiso", htmx.Headers.GetValues("HX-Redirect").Single());
        Assert.Single(denegaciones.Registradas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public async Task UnVisitante_VeLaTienda_PeroUnaAreaDelPersonalLoLlevaAlIngresoSinLimpiarElNavegador()
    {
        await using var gestion = (await Armar(HostWeb.Gestion)).Anfitrion;
        await using var tienda = (await Armar(HostWeb.Tienda)).Anfitrion;

        var delPersonal = await Pedir(gestion, "/prueba/area/sucursal");
        var publica = await Pedir(tienda, "/prueba/area/tienda");

        Assert.Equal(HttpStatusCode.Found, delPersonal.StatusCode);
        Assert.Equal("/ingreso", delPersonal.Headers.Location!.OriginalString);
        Assert.False(delPersonal.Headers.Contains("Clear-Site-Data"));
        Assert.Equal(HttpStatusCode.OK, publica.StatusCode);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-22")]
    [InlineData(EstadoDeCarga.VencidaPorInactividad)]
    [InlineData(EstadoDeCarga.VencidaPorTopeAbsoluto)]
    public async Task ConLaSesionVencida_UnaPantallaDelPersonalLlevaAlIngresoYLimpiaElNavegador(EstadoDeCarga carga)
    {
        var (anfitrion, _, _, _) = await Armar(HostWeb.Gestion, new SesionesWebDePrueba().ConCarga(carga));
        await using var _ = anfitrion;

        var respuesta = await Pedir(anfitrion, "/prueba/area/sucursal", AnfitrionConSesion.Cookie(HostWeb.Gestion));

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Equal("\"cache\", \"storage\"", respuesta.Headers.GetValues("Clear-Site-Data").Single());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task UnaRutaQueRequiereSesion_SinSesionLlevaAlIngreso_YConSesionSeAbre()
    {
        var (anfitrion, sesiones, _, _) = await Armar();
        await using var _ = anfitrion;

        var sin = await Pedir(anfitrion, "/prueba/acceso/cuenta");
        sesiones.ConSesionValida();
        var con = await Pedir(anfitrion, "/prueba/acceso/cuenta", AnfitrionConSesion.Cookie());

        Assert.Equal("/ingreso", sin.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, con.StatusCode);
    }

    // ---- Paso pendiente del ingreso (WEB-34) ----

    [Theory]
    [Trait("Criterio", "AC-WEB-28")]
    [InlineData("/prueba/acceso/publica")]
    [InlineData("/prueba/area/tienda")]
    [InlineData("/prueba/acceso/cuenta")]
    [InlineData("/")]
    public async Task ConUnPasoPendiente_CualquierOtraPantallaLlevaAlPasoQueFalta(string ruta)
    {
        var (anfitrion, _, desafios, _) = await Armar();
        await using var _ = anfitrion;

        var respuesta = await Pedir(anfitrion, ruta, ConDesafio());

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Equal([Desafio], desafios.Consultados);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task ConUnPasoPendiente_UnaAccionNoSeEjecuta_AunConTokenValido()
    {
        var (anfitrion, _, _, _) = await Armar();
        await using var _ = anfitrion;
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/prueba/sesion/accion");
        solicitud.Headers.Remove("Cookie");
        solicitud.Headers.Add("Cookie", ConDesafio());

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        // El token de una visita anónima sin la cookie del antiforgery no vale, y aun si valiera, la acción no se ejecuta: va al ingreso.
        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-28")]
    [InlineData("/ingreso")]
    [InlineData("/legal/terminos")]
    [InlineData("/error")]
    [InlineData("/sin-permiso")]
    [InlineData("/no-disponible")]
    public async Task ConUnPasoPendiente_LasRutasPermitidasSeAbren_SinRedirigirlasEnBucle(string ruta)
    {
        var (anfitrion, _, _, _) = await Armar();
        await using var _ = anfitrion;

        var respuesta = await Pedir(anfitrion, ruta, ConDesafio());

        Assert.NotEqual(HttpStatusCode.Found, respuesta.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task SiIdentidadNoResponde_FallaCerradoSinBucle_AlIngresoYElIngresoSeAbre()
    {
        var (anfitrion, _, desafios, _) = await Armar();
        await using var _ = anfitrion;
        desafios.Falla = true;

        var pantalla = await Pedir(anfitrion, "/prueba/acceso/publica", ConDesafio());
        var ingreso = await Pedir(anfitrion, pantalla.Headers.Location!.OriginalString, ConDesafio());

        Assert.Equal("/ingreso", pantalla.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task UnDesafioQueYaNoExiste_SeBorra_YLaSolicitudSigue()
    {
        var (anfitrion, _, desafios, _) = await Armar();
        await using var _ = anfitrion;
        desafios.Paso = PasoDelIngresoWeb.Ninguno;

        var respuesta = await Pedir(anfitrion, "/prueba/acceso/publica", ConDesafio());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains(respuesta.Headers.GetValues("Set-Cookie"), c => c.StartsWith(CookieDelDesafio.Nombre + "=;", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-28")]
    [InlineData("corto")]
    [InlineData("Qw3ErTy5UiOp7AsDf9GhJk1LzXc2VbNm4QwErTyUiO!")]
    public async Task UnValorDeDesafioConFormaInvalida_SeIgnora_YNoSeConsultaAIdentidad(string valor)
    {
        var (anfitrion, _, desafios, _) = await Armar();
        await using var _ = anfitrion;

        var respuesta = await Pedir(anfitrion, "/prueba/acceso/publica", $"{CookieDelDesafio.Nombre}={valor}");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Empty(desafios.Consultados);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task ConUnaSesionValida_ElDesafioNoSeConsulta()
    {
        var (anfitrion, _, desafios, _) = await Armar(sesiones: new SesionesWebDePrueba().ConSesionValida());
        await using var _ = anfitrion;

        var respuesta = await Pedir(anfitrion, "/prueba/acceso/publica", ConDesafio(AnfitrionConSesion.Cookie()));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Empty(desafios.Consultados);
    }

    // ---- POST /ingreso/cancelar ----

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task CancelarElIngreso_DescartaElDesafio_BorraSuCookie_YLlevaAlIngreso()
    {
        var (anfitrion, _, desafios, _) = await Armar();
        await using var _ = anfitrion;
        var token = await AnfitrionConSesion.Post(anfitrion, "/ingreso/cancelar");
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/ingreso/cancelar");
        solicitud.Headers.Add("RequestVerificationToken", token.Headers.GetValues("RequestVerificationToken").Single());
        solicitud.Headers.Add("Cookie", ConDesafio(token.Headers.GetValues("Cookie").Single()));

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Equal([Desafio], desafios.Cancelados);
        Assert.Contains(respuesta.Headers.GetValues("Set-Cookie"), c => c.StartsWith(CookieDelDesafio.Nombre + "=;", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task CancelarElIngreso_ConIdentidadCaida_BorraLaCookieIgual()
    {
        var (anfitrion, _, desafios, _) = await Armar();
        await using var _ = anfitrion;
        desafios.Falla = true;
        var token = await AnfitrionConSesion.Post(anfitrion, "/ingreso/cancelar");
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/ingreso/cancelar");
        solicitud.Headers.Add("RequestVerificationToken", token.Headers.GetValues("RequestVerificationToken").Single());
        solicitud.Headers.Add("Cookie", ConDesafio(token.Headers.GetValues("Cookie").Single()));

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Contains(respuesta.Headers.GetValues("Set-Cookie"), c => c.StartsWith(CookieDelDesafio.Nombre + "=;", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task CancelarElIngreso_SinTokenAntifalsificacion_Rechaza()
    {
        var (anfitrion, _, desafios, _) = await Armar();
        await using var _ = anfitrion;
        var solicitud = new HttpRequestMessage(HttpMethod.Post, "/ingreso/cancelar");
        solicitud.Headers.Add("Cookie", ConDesafio());

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(desafios.Cancelados);
    }

    // ---- Cookie del desafío, marca de dispositivo y región del borde ----

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public void LaCookieDelDesafio_EsDeSesion_ConPrefijoHost_YDeSeguridad()
    {
        var contexto = new DefaultHttpContext();

        CookieDelDesafio.Escribir(contexto.Response, Desafio);
        var atributos = contexto.Response.Headers.SetCookie.Single()!.Split(';', StringSplitOptions.TrimEntries).Select(a => a.ToLowerInvariant()).ToArray();

        Assert.StartsWith("__host-bh-desafio=", atributos[0], StringComparison.Ordinal);
        Assert.Contains("path=/", atributos);
        Assert.Contains("secure", atributos);
        Assert.Contains("httponly", atributos);
        Assert.Contains("samesite=lax", atributos);
        Assert.DoesNotContain(atributos, a => a.StartsWith("domain", StringComparison.Ordinal) || a.StartsWith("max-age", StringComparison.Ordinal) || a.StartsWith("expires", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-33")]
    public void LaMarcaDeDispositivo_EsDe256Bits_PersistenteDe180Dias_YSeguraEnElNavegador()
    {
        var contexto = new DefaultHttpContext();

        CookieDeDispositivo.Escribir(contexto.Response, Desafio);
        var atributos = contexto.Response.Headers.SetCookie.Single()!.Split(';', StringSplitOptions.TrimEntries).Select(a => a.ToLowerInvariant()).ToArray();

        Assert.StartsWith("__host-bh-dispositivo=", atributos[0], StringComparison.Ordinal);
        Assert.Contains($"max-age={(int)TimeSpan.FromDays(180).TotalSeconds}", atributos);
        Assert.Contains("secure", atributos);
        Assert.Contains("httponly", atributos);
        Assert.Contains("samesite=lax", atributos);
        Assert.DoesNotContain(atributos, a => a.StartsWith("domain", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-33")]
    [InlineData("PE", "PE")]
    [InlineData("US", "US")]
    [InlineData("pe", null)]
    [InlineData("PER", null)]
    [InlineData("P1", null)]
    [InlineData("", null)]
    public void LaRegionSoloSeAceptaComoCodigoDePaisDeLaCabeceraDelBorde(string valor, string? esperada)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers[DatosDelBorde.CabeceraDeRegion] = valor;

        Assert.Equal(esperada, DatosDelBorde.Leer(contexto.Request).Region);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-33")]
    public void UnaCabeceraDeRegionRepetida_SeIgnora_YLaMarcaSoloValeConSuForma()
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers[DatosDelBorde.CabeceraDeRegion] = new Microsoft.Extensions.Primitives.StringValues(["PE", "US"]);
        contexto.Request.Headers.Cookie = $"{CookieDeDispositivo.Nombre}=corta";

        var datos = DatosDelBorde.Leer(contexto.Request);

        Assert.Null(datos.Region);
        Assert.Null(datos.Marca);
        Assert.DoesNotContain("corta", datos.ToString(), StringComparison.Ordinal);

        contexto.Request.Headers.Cookie = $"{CookieDeDispositivo.Nombre}={Desafio}";
        Assert.Equal(Desafio, DatosDelBorde.Leer(contexto.Request).Marca);
        Assert.DoesNotContain(Desafio, DatosDelBorde.Leer(contexto.Request).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-33")]
    public void Nginx_SobrescribeSiempreLaRegionDelBorde_ConLaDeCloudflare_EnLosDosHosts()
    {
        var configuracion = File.ReadAllText(Path.Combine(RaizDelRepositorio.Raiz, "infra", "nginx", "nginx.conf"));

        var directivas = Regex.Matches(configuracion, @"proxy_set_header\s+" + DatosDelBorde.CabeceraDeRegion + @"\s+(\S+);");

        // Una por servidor (tienda y gestión); el valor sale solo de la cabecera de Cloudflare, nunca de la del cliente con ese nombre.
        Assert.Equal(2, directivas.Count);
        Assert.All(directivas, d => Assert.Equal("$http_cf_ipcountry", d.Groups[1].Value));
        Assert.Equal(2, Regex.Matches(configuracion, @"server\s*\{").Count);
    }
}
