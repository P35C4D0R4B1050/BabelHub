using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Template;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>T15: la cookie por host, el cierre de la sesión (<c>POST /sesion/cerrar</c> y <c>/sesion/cerrar-todas</c>), el mapeo de las áreas por host y <c>[ConsultaAutomatica]</c>.</summary>
public sealed class CookieYCierreDeSesionTests
{
    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData(HostWeb.Tienda, "__Host-bh-tienda")]
    [InlineData(HostWeb.Gestion, "__Host-bh-gestion")]
    public void LaCookie_LlevaElPrefijoHost_ElNombreDeSuHost_YTodosLosAtributosDeSeguridad(HostWeb host, string nombre)
    {
        var contexto = new DefaultHttpContext();

        CookieDeSesionWeb.Escribir(contexto.Response, host, SesionesWebDePrueba.ValorDeLaCookie);
        var encabezado = contexto.Response.Headers.SetCookie.Single()!;

        Assert.Equal(nombre, CookieDeSesionWeb.Nombre(host));
        Assert.StartsWith(nombre + "=" + SesionesWebDePrueba.ValorDeLaCookie, encabezado, StringComparison.Ordinal);
        var atributos = encabezado.Split(';', StringSplitOptions.TrimEntries).Skip(1).Select(a => a.ToLowerInvariant()).ToArray();
        Assert.Contains("path=/", atributos);
        Assert.Contains("secure", atributos);
        Assert.Contains("httponly", atributos);
        Assert.Contains("samesite=lax", atributos);
        // Sin Domain (exigido por __Host-) y sin Max-Age ni Expires: es una cookie de sesión.
        Assert.DoesNotContain(atributos, a => a.StartsWith("domain", StringComparison.Ordinal));
        Assert.DoesNotContain(atributos, a => a.StartsWith("max-age", StringComparison.Ordinal) || a.StartsWith("expires", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void BorrarLaCookie_UsaLosMismosAtributos_ParaQueElNavegadorLaAcepte()
    {
        var contexto = new DefaultHttpContext();

        CookieDeSesionWeb.Borrar(contexto.Response, HostWeb.Gestion);
        var atributos = contexto.Response.Headers.SetCookie.Single()!.Split(';', StringSplitOptions.TrimEntries).Select(a => a.ToLowerInvariant()).ToArray();

        Assert.StartsWith("__host-bh-gestion=;", string.Join("; ", atributos), StringComparison.Ordinal);
        Assert.Contains("path=/", atributos);
        Assert.Contains("secure", atributos);
        Assert.Contains("httponly", atributos);
        Assert.Contains(atributos, a => a.StartsWith("expires=", StringComparison.Ordinal) && a.Contains("1970", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task CerrarLaSesion_ConElTokenAntifalsificacion_LaCierraBorraLaCookieYLlevaAlIngreso()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/sesion/cerrar", AnfitrionConSesion.Cookie());

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Equal(SesionesWebDePrueba.ValorDeLaCookie, Assert.Single(sesiones.Cierres));
        var borrada = respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("__Host-bh-tienda=;", StringComparison.Ordinal));
        Assert.Contains("secure", borrada, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("\"cache\", \"storage\"", respuesta.Headers.GetValues("Clear-Site-Data").Single());
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task CerrarLaSesion_SinElTokenAntifalsificacion_NoCierraNada()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/sesion/cerrar", AnfitrionConSesion.Cookie(), conToken: false);

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(sesiones.Cierres);
        Assert.False(respuesta.Headers.Contains("Clear-Site-Data"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task CerrarLaSesion_ConUnaSolicitudDeHtmx_RedirigeConHxRedirect()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/sesion/cerrar", AnfitrionConSesion.Cookie());
        solicitud.Headers.Add("HX-Request", "true");

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.GetValues("HX-Redirect").Single());
        Assert.True(respuesta.Headers.Contains("Clear-Site-Data"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task CerrarLaSesion_SinCookie_LimpiaElNavegadorSinTocarElModulo()
    {
        var sesiones = new SesionesWebDePrueba();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/sesion/cerrar");

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Empty(sesiones.Cierres);
        Assert.True(respuesta.Headers.Contains("Clear-Site-Data"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-26")]
    public async Task CerrarLaSesion_ConIdentidadCaida_SigueFuncionando()
    {
        // La página se emitió con la sesión sana (el token va ligado a la persona y a su sesión) y después Identidad cae: salir sigue funcionando.
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/sesion/cerrar", AnfitrionConSesion.Cookie());
        sesiones.ConCredencial(EstadoDeCredencial.ServicioNoDisponible);

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Equal(SesionesWebDePrueba.ValorDeLaCookie, Assert.Single(sesiones.Cierres));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task CerrarTodas_CierraLasSesionesDeLaPersona_ConElUsuarioDeLaCredencialVerificada()
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/sesion/cerrar-todas", AnfitrionConSesion.Cookie());

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Equal(sesiones.UsuarioId, Assert.Single(sesiones.CierresDeTodas));
        Assert.True(respuesta.Headers.Contains("Clear-Site-Data"));
        Assert.Contains(respuesta.Headers.GetValues("Set-Cookie"), c => c.StartsWith("__Host-bh-tienda=;", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-23")]
    public async Task CerrarTodas_SinSesionValida_NoCierraLasDeNadie()
    {
        var sesiones = new SesionesWebDePrueba();
        await using var anfitrion = await AnfitrionConSesion.Iniciar(sesiones);
        var solicitud = await AnfitrionConSesion.Post(anfitrion, "/sesion/cerrar-todas");

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Empty(sesiones.CierresDeTodas);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-23")]
    [InlineData("/sesion/cerrar")]
    [InlineData("/sesion/cerrar-todas")]
    public async Task LasRutasDeCierre_NoSeAtiendenPorGet(string ruta)
    {
        await using var anfitrion = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba().ConSesionValida());

        var respuesta = await anfitrion.Cliente.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.MethodNotAllowed, respuesta.StatusCode);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-02")]
    [InlineData(HostWeb.Tienda, "tienda", HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.NotFound)]
    // En gestion las áreas existen y piden ingreso (T18): un visitante va al ingreso en lugar de recibir un 404.
    [InlineData(HostWeb.Gestion, "gestion", HttpStatusCode.NotFound, HttpStatusCode.Found, HttpStatusCode.Found)]
    public async Task LasAreas_SeMapeanSoloEnElHostQueLasSirve(HostWeb host, string nombre, HttpStatusCode tienda, HttpStatusCode sucursal, HttpStatusCode admin)
    {
        await using var anfitrion = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba(), host);

        Assert.Equal(tienda, (await anfitrion.Cliente.GetAsync("/prueba/area/tienda")).StatusCode);
        Assert.Equal(sucursal, (await anfitrion.Cliente.GetAsync("/prueba/area/sucursal")).StatusCode);
        Assert.Equal(admin, (await anfitrion.Cliente.GetAsync("/prueba/area/admin")).StatusCode);
        Assert.NotNull(nombre);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-02")]
    [InlineData(HostWeb.Tienda)]
    [InlineData(HostWeb.Gestion)]
    public async Task LasRutasCompartidas_ExistenEnLosDosHosts(HostWeb host)
    {
        await using var anfitrion = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba(), host);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await anfitrion.Cliente.GetAsync("/no-disponible")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await anfitrion.Cliente.GetAsync("/sin-permiso")).StatusCode);
        Assert.Equal(HttpStatusCode.InternalServerError, (await anfitrion.Cliente.GetAsync("/error")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await anfitrion.Cliente.GetAsync("/legal/terminos")).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await anfitrion.Cliente.GetAsync("/sesion/cerrar")).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public async Task LaPortadaDeLaTienda_NoExisteEnElHostDeGestion()
    {
        await using var tienda = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba(), HostWeb.Tienda);
        await using var gestion = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba(), HostWeb.Gestion);

        Assert.Equal(HttpStatusCode.OK, (await tienda.Cliente.GetAsync("/")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await gestion.Cliente.GetAsync("/")).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public void UnAreaFueraDeLaListaCerrada_HaceFallarElArranque()
    {
        var convencion = new ConvencionDeAreasPorHost(new OpcionesDelHostWeb(HostWeb.Tienda, []), new BabelHub.Ecommerce.Application.Web.Interno.AreasDeInterfaz());
        var modelo = new Microsoft.AspNetCore.Mvc.ApplicationModels.ApplicationModel();
        var controlador = new Microsoft.AspNetCore.Mvc.ApplicationModels.ControllerModel(typeof(ControladorDeAreaTiendaDePrueba).GetTypeInfo(), []);
        controlador.RouteValues["area"] = "Otra";
        modelo.Controllers.Add(controlador);

        Assert.Throws<InvalidOperationException>(() => convencion.Apply(modelo));
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData(null, false, "tienda")]
    [InlineData("gestion", true, "gestion")]
    [InlineData("tienda", true, "tienda")]
    public void ElHost_SeLeeDeLaConfiguracion(string? host, bool produccion, string esperado)
    {
        var opciones = OpcionesDelHostWeb.Leer(Configuracion(host, produccion ? "www.babelhub.pe, gestion.babelhub.pe" : null), produccion);

        Assert.Equal(esperado == "tienda" ? HostWeb.Tienda : HostWeb.Gestion, opciones.Host);
        Assert.Equal(produccion ? ["www.babelhub.pe", "gestion.babelhub.pe"] : [], opciones.HostsPermitidos);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-03")]
    [InlineData(null, "www.babelhub.pe", "BABELHUB__Web__Host")]
    [InlineData("otro", null, "BABELHUB__Web__Host")]
    [InlineData("Tienda", null, "BABELHUB__Web__Host")]
    [InlineData("tienda", null, "BABELHUB__Web__HostsPermitidos")]
    [InlineData("tienda", "*.babelhub.pe", "BABELHUB__Web__HostsPermitidos")]
    public void ElHostMalConfigurado_HaceFallarElArranque_NombrandoLaVariable(string? host, string? permitidos, string variable)
    {
        var error = Assert.Throws<ArranqueRechazadoException>(() => OpcionesDelHostWeb.Leer(Configuracion(host, permitidos), esProduccion: true));

        Assert.Contains(variable, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void UnValorInvalidoDelHost_FallaAunFueraDeProduccion()
    {
        Assert.Throws<ArranqueRechazadoException>(() => OpcionesDelHostWeb.Leer(Configuracion("otro", null), esProduccion: false));
    }

    private static IConfiguration Configuracion(string? host, string? permitidos)
    {
        var valores = new Dictionary<string, string?>();
        if (host is not null)
        {
            valores["Web:Host"] = host;
        }

        if (permitidos is not null)
        {
            valores["Web:HostsPermitidos"] = permitidos;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task UnaConsultaAutomatica_DebeAdmitirSoloGet()
    {
        await using var anfitrion = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba());
        var endpoints = anfitrion.Servicios.GetRequiredService<EndpointDataSource>().Endpoints;

        ConsultasAutomaticas.Validar(endpoints);

        var sondeo = endpoints.Single(e => e.Metadata.GetMetadata<ConsultaAutomaticaAttribute>() is not null);
        Assert.Equal(["GET"], sondeo.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public void UnaConsultaAutomaticaQueAdmiteOtroMetodo_HaceFallarElArranque()
    {
        var endpoint = new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(new ConsultaAutomaticaAttribute(), new HttpMethodMetadata(["GET", "POST"])),
            "sondeo que escribe");

        var error = Assert.Throws<InvalidOperationException>(() => ConsultasAutomaticas.Validar([endpoint]));
        Assert.Contains("GET", error.Message, StringComparison.Ordinal);

        var sinMetodos = new Endpoint(_ => Task.CompletedTask, new EndpointMetadataCollection(new ConsultaAutomaticaAttribute()), "sin métodos");
        Assert.Throws<InvalidOperationException>(() => ConsultasAutomaticas.Validar([sinMetodos]));
    }

    // ---- Todo hx-trigger="every …" de las vistas apunta a una consulta declarada [ConsultaAutomatica] (R03) ----

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task TodoSondeoDeHtmxDeLasVistas_ApuntaAUnaConsultaAutomaticaDeclarada()
    {
        await using var anfitrion = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba());
        var endpoints = anfitrion.Servicios.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var fallos = new List<string>();
        foreach (var archivo in Directory.EnumerateFiles(RaizDelRepositorio.Api, "*.cshtml", SearchOption.AllDirectories)
                     .Where(a => !a.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                                 && !a.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
        {
            fallos.AddRange(ExploradorDeSondeos.Revisar(File.ReadAllText(archivo), endpoints).Select(f => $"{Path.GetFileName(archivo)}: {f}"));
        }

        Assert.True(fallos.Count == 0, string.Join(Environment.NewLine, fallos));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-22")]
    public async Task ElExploradorDeSondeos_FallaSiUnSondeoNoDeclaraLaConsultaAutomatica_OSiNoSePuedeResolver()
    {
        await using var anfitrion = await AnfitrionConSesion.Iniciar(new SesionesWebDePrueba());
        var endpoints = anfitrion.Servicios.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        Assert.Empty(ExploradorDeSondeos.Revisar("""<div hx-get="/prueba/sesion/sondeo" hx-trigger="every 20s"></div>""", endpoints));
        Assert.Empty(ExploradorDeSondeos.Revisar("""<div hx-get="/prueba/sesion/yo" hx-trigger="click"></div>""", endpoints));
        Assert.Single(ExploradorDeSondeos.Revisar("""<div hx-get="/prueba/sesion/yo" hx-trigger="every 20s"></div>""", endpoints));
        Assert.Single(ExploradorDeSondeos.Revisar("""<div hx-get="/no/existe" hx-trigger="load, every 5s"></div>""", endpoints));
        Assert.Single(ExploradorDeSondeos.Revisar("""<div hx-get="@Url.Action("Sondeo")" hx-trigger="every 20s"></div>""", endpoints));
        Assert.Single(ExploradorDeSondeos.Revisar("""<div hx-trigger="every 20s"></div>""", endpoints));
    }

    private static class ExploradorDeSondeos
    {
        private static readonly Regex Etiqueta = new(@"<[^<>]*hx-trigger\s*=\s*""[^""]*\bevery\b[^""]*""[^<>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex Get = new(@"hx-get\s*=\s*""(?<ruta>[^""]*)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static IReadOnlyList<string> Revisar(string vista, IReadOnlyList<RouteEndpoint> endpoints)
        {
            var fallos = new List<string>();
            foreach (Match etiqueta in Etiqueta.Matches(vista))
            {
                var get = Get.Match(etiqueta.Value);
                if (!get.Success || get.Groups["ruta"].Value.Contains('@', StringComparison.Ordinal) || !get.Groups["ruta"].Value.StartsWith('/'))
                {
                    fallos.Add($"un sondeo sin hx-get con una ruta literal que se pueda comprobar: {etiqueta.Value}");
                    continue;
                }

                var ruta = new PathString(get.Groups["ruta"].Value.Split('?')[0]);
                var destino = endpoints.FirstOrDefault(e => Coincide(e, ruta));
                if (destino is null)
                {
                    fallos.Add($"el sondeo {ruta} no corresponde a ningún endpoint");
                }
                else if (destino.Metadata.GetMetadata<ConsultaAutomaticaAttribute>() is null)
                {
                    fallos.Add($"el sondeo {ruta} no está declarado [ConsultaAutomatica]");
                }
            }

            return fallos;
        }

        private static bool Coincide(RouteEndpoint endpoint, PathString ruta) =>
            new TemplateMatcher(TemplateParser.Parse(endpoint.RoutePattern.RawText!), new RouteValueDictionary()).TryMatch(ruta, new RouteValueDictionary());
    }
}
