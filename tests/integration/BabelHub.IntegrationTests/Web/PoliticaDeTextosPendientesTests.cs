using System.Net;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>T37: el middleware de la política entre el enrutado y los endpoints, su traducción a HTTP y la validación de arranque de los atributos (WEB-41 a WEB-49).</summary>
public sealed class PoliticaDeTextosPendientesTests
{
    private static Task<HttpResponseMessage> Pedir(AnfitrionWeb a, string ruta, string? cookie = null, string metodo = "GET", bool htmx = false)
    {
        var solicitud = new HttpRequestMessage(new HttpMethod(metodo), ruta);
        if (cookie is not null)
        {
            solicitud.Headers.Add("Cookie", cookie);
        }

        if (htmx)
        {
            solicitud.Headers.Add("HX-Request", "true");
        }

        return a.Cliente.SendAsync(solicitud);
    }

    private static string Cookie(HostWeb host = HostWeb.Tienda) => AnfitrionConSesion.Cookie(host);

    // ---- Traducción de la decisión a HTTP ----

    [Theory]
    [Trait("Criterio", "AC-WEB-34")]
    [InlineData("/prueba/politica/catalogo")]
    [InlineData("/prueba/politica/catalogo/7")]
    [InlineData("/no/existe")]
    [InlineData("/")]
    public async Task UnaPantallaNoDeclarada_ConTextosPendientes_RedirigeAAceptar_ConNoStore(string ruta)
    {
        var (a, _, estado, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;

        var respuesta = await Pedir(a, ruta, Cookie());

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/textos/aceptar", respuesta.Headers.Location!.OriginalString);
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
        Assert.Equal(1, estado.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task UnaSolicitudDeHtmx_RecibeHxRedirect_EnLugarDeUn302()
    {
        var (a, _, _, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;

        var respuesta = await Pedir(a, "/prueba/politica/catalogo", Cookie(), htmx: true);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("/textos/aceptar", respuesta.Headers.GetValues("HX-Redirect").Single());
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-38")]
    [InlineData("POST", "/prueba/politica/pago")]
    [InlineData("POST", "/prueba/politica/datos")]
    [InlineData("PUT", "/prueba/politica/catalogo")]
    [InlineData("DELETE", "/prueba/politica/catalogo")]
    [InlineData("OPTIONS", "/prueba/politica/catalogo")]
    [InlineData("TRACE", "/prueba/politica/catalogo")]
    [InlineData("HEAD", "/prueba/politica/catalogo")]
    [InlineData("POST", "/prueba/politica/cuenta")]
    [InlineData("POST", "/no/existe")]
    public async Task UnaAccionDirectaOUnMetodoNoDeclarado_Recibe403ConElCodigoEstable_SinEjecutarNada(string metodo, string ruta)
    {
        var (a, _, _, _, _, _, seguridad) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;

        var respuesta = await Pedir(a, ruta, Cookie(), metodo);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        if (metodo != "HEAD")
        {
            var cuerpo = await respuesta.Content.ReadAsStringAsync();
            Assert.Contains("\"code\":\"ECO-TEXTOS-PENDIENTES\"", cuerpo, StringComparison.Ordinal);
            Assert.DoesNotContain("textosPendientes", cuerpo, StringComparison.Ordinal);
        }

        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType?.MediaType ?? "application/problem+json");
        Assert.Single(seguridad.Bloqueos);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-36")]
    [InlineData("/prueba/politica/cuenta", RolWeb.Cliente)]
    [InlineData("/prueba/politica/pedidos", RolWeb.Cliente)]
    [InlineData("/prueba/politica/libro", RolWeb.Cliente)]
    [InlineData("/prueba/politica/cuenta", RolWeb.Encargado)]
    [InlineData("/legal/terminos", RolWeb.Cliente)]
    public async Task LasRutasDeclaradasPermitidas_SeAtienden_ConElIndicadorDeTextosPendientes(string ruta, RolWeb rol)
    {
        var sesiones = new SesionesWebDePrueba().ConSesionValida(rol, rol == RolWeb.Encargado ? Guid.NewGuid() : null);
        var (a, _, _, _, _, _, _) = await AnfitrionConPolitica.Iniciar(rol == RolWeb.Cliente ? HostWeb.Tienda : HostWeb.Gestion, sesiones);
        await using var _ = a;

        var respuesta = await Pedir(a, ruta, Cookie(rol == RolWeb.Cliente ? HostWeb.Tienda : HostWeb.Gestion));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        if (ruta.StartsWith("/prueba", StringComparison.Ordinal))
        {
            Assert.Contains("\"textosPendientes\":true", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-34")]
    public async Task ConLosTextosAceptados_TodoSigueSuCamino_SinIndicador()
    {
        var (a, _, estado, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        estado.Estado = EstadoDeAceptacionDeTextos.Aceptados;

        var catalogo = await Pedir(a, "/prueba/politica/catalogo", Cookie());
        var cuenta = await Pedir(a, "/prueba/politica/cuenta", Cookie());

        Assert.Equal(HttpStatusCode.OK, catalogo.StatusCode);
        Assert.Contains("\"textosPendientes\":false", await cuenta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // ---- Orden: sesión, rol y alcance antes de consultar ----

    [Fact]
    [Trait("Criterio", "AC-WEB-37")]
    public async Task UnEncargadoDeOtraSucursal_VeSinPermiso_SinConsultarAIdentidad_YQuedaRegistradoComoFueraDeSucursal()
    {
        var propia = Guid.NewGuid();
        var sesiones = new SesionesWebDePrueba().ConSesionValida(RolWeb.Encargado, propia);
        var (a, _, estado, _, denegaciones, _, _) = await AnfitrionConPolitica.Iniciar(HostWeb.Gestion, sesiones);
        await using var _ = a;

        var ajena = await Pedir(a, $"/prueba/politica/sucursal/{Guid.NewGuid()}", Cookie(HostWeb.Gestion));
        var invalida = await Pedir(a, "/prueba/politica/sucursal/no-es-una-sucursal", Cookie(HostWeb.Gestion));
        var suya = await Pedir(a, $"/prueba/politica/sucursal/{propia}", Cookie(HostWeb.Gestion));

        Assert.Equal("/sin-permiso", ajena.Headers.Location!.OriginalString);
        Assert.Equal("/sin-permiso", invalida.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, suya.StatusCode);
        Assert.Equal(1, estado.Consultas);
        Assert.Equal(["fuera-de-sucursal", "fuera-de-sucursal"], denegaciones.Registradas.Select(d => d.Motivo).ToArray());
    }

    // ---- Falla cerrada ----

    [Fact]
    [Trait("Criterio", "AC-WEB-39")]
    public async Task ConIdentidadCaida_UnaPantallaIrAServicioNoDisponible_UnaAccionRecibe503_YLaSesionSeConserva()
    {
        var (a, sesiones, estado, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        estado.Comportamiento = _ => throw new InvalidOperationException("Identidad cayó");

        var pantalla = await Pedir(a, "/prueba/politica/cuenta", Cookie());
        var accion = await Pedir(a, "/prueba/politica/pago", Cookie(), "POST");

        Assert.Equal("/no-disponible", pantalla.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, accion.StatusCode);
        Assert.Contains("ECO-SERVICIO-NO-DISPONIBLE", await accion.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(sesiones.Cierres);
        Assert.False(pantalla.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-39")]
    public async Task ConIdentidadCaida_SoloSePermiteCerrarSesion_LosTextosYElLibro()
    {
        var (a, _, estado, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        estado.Estado = EstadoDeAceptacionDeTextos.Desconocido;

        var libro = await Pedir(a, "/prueba/politica/libro", Cookie());
        var textos = await Pedir(a, "/legal/terminos", Cookie());
        var cierre = await a.Cliente.SendAsync(await AnfitrionConSesion.Post(a, "/sesion/cerrar", Cookie()));
        var cuenta = await Pedir(a, "/prueba/politica/cuenta", Cookie());

        Assert.Equal(HttpStatusCode.OK, libro.StatusCode);
        Assert.Equal(HttpStatusCode.OK, textos.StatusCode);
        Assert.Equal("/ingreso", cierre.Headers.Location!.OriginalString);
        Assert.Equal("/no-disponible", cuenta.Headers.Location!.OriginalString);
    }

    // ---- La página de contacto y la redirección de «Cambios y devoluciones» (web-informacion-del-comercio) ----

    [Fact]
    [Trait("Criterio", "AC-COM-06")]
    public async Task LaPaginaDeContacto_ConTextosPendientes_RecibeElDesvioComunDeUnaPantallaNoDeclarada()
    {
        var (a, _, _, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;

        var respuesta = await Pedir(a, "/contacto", Cookie());

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal("/textos/aceptar", respuesta.Headers.Location!.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-06")]
    public async Task LaPaginaDeContacto_ConIdentidadCaida_VaAServicioNoDisponible_YUnVisitanteLaVe()
    {
        var (a, _, estado, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        estado.Comportamiento = _ => throw new InvalidOperationException("Identidad cayó");

        var conSesion = await Pedir(a, "/contacto", Cookie());
        var visitante = await Pedir(a, "/contacto");

        Assert.Equal("/no-disponible", conSesion.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, visitante.StatusCode);
    }

    [Theory]
    [Trait("Criterio", "AC-COM-09")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaDireccionAnteriorDelReembolso_RedirigeDeFormaPermanente_ConTextosPendientesYConIdentidadCaida(bool identidadCaida)
    {
        var (a, _, estado, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        if (identidadCaida)
        {
            estado.Comportamiento = _ => throw new InvalidOperationException("Identidad cayó");
        }

        var respuesta = await Pedir(a, "/legal/reembolso?destino=//otro.example", Cookie());

        Assert.Equal(HttpStatusCode.MovedPermanently, respuesta.StatusCode);
        Assert.Equal("/legal/cambios-y-devoluciones", respuesta.Headers.Location!.OriginalString);
    }

    [Theory]
    [Trait("Criterio", "AC-COM-09")]
    [InlineData("POST")]
    [InlineData("PUT")]
    public async Task LaDireccionAnteriorDelReembolso_SoloSeAtiendeConGet_ConTextosPendientes(string metodo)
    {
        var (a, _, _, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;

        var respuesta = await Pedir(a, "/legal/reembolso", Cookie(), metodo);

        Assert.NotEqual(HttpStatusCode.MovedPermanently, respuesta.StatusCode);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-38")]
    [InlineData("/error")]
    [InlineData("/sin-permiso")]
    [InlineData("/no-disponible")]
    public async Task LosDestinosDeLasRedirecciones_SeSirvenSinConsultarAIdentidad_AunConIdentidadCaida(string ruta)
    {
        var (a, _, estado, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        estado.Comportamiento = _ => throw new InvalidOperationException("Identidad cayó");

        var respuesta = await Pedir(a, ruta, Cookie());

        Assert.NotEqual(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Equal(0, estado.Consultas);
    }

    // ---- Retorno ----

    [Fact]
    [Trait("Criterio", "AC-WEB-42")]
    public async Task UnaPantallaCompleta_GuardaSuRuta_PeroUnFragmentoUnSondeoOUnaAccionNo()
    {
        var (a, _, _, _, _, almacen, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;

        await Pedir(a, "/prueba/politica/catalogo/7?x=1", Cookie(), htmx: true);
        Assert.Empty(almacen.Retornos);
        await Pedir(a, "/prueba/politica/sin-retorno", Cookie());
        Assert.Empty(almacen.Retornos);
        await Pedir(a, "/prueba/politica/pago", Cookie(), "POST");
        Assert.Empty(almacen.Retornos);

        await Pedir(a, "/prueba/politica/catalogo/7?x=1", Cookie());
        Assert.Equal("/prueba/politica/catalogo/7", Assert.Single(almacen.Retornos).Value);
    }

    // ---- Validación de arranque ----

    private static RouteEndpoint Endpoint(string patron, string[]? metodos, params object[] metadatos)
    {
        var lista = new List<object>(metadatos);
        if (metodos is not null)
        {
            lista.Add(new HttpMethodMetadata(metodos));
        }

        return new RouteEndpoint(_ => Task.CompletedTask, RoutePatternFactory.Parse(patron), 0, new EndpointMetadataCollection(lista), patron);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public async Task LasRutasRealesDeLaAplicacion_PasanLaValidacionDeArranque()
    {
        var (a, _, _, _, _, _, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;

        PoliticaDeTextosPendientes.Validar(a.Servicios.GetRequiredService<EndpointDataSource>().Endpoints);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-38")]
    [InlineData(CapacidadWeb.Cuenta, "GET", true)]
    [InlineData(CapacidadWeb.Cuenta, "POST", false)]
    [InlineData(CapacidadWeb.Cuenta, "HEAD", false)]
    [InlineData(CapacidadWeb.Aceptar, "POST", true)]
    [InlineData(CapacidadWeb.Aceptar, "GET", false)]
    [InlineData(CapacidadWeb.CerrarSesion, "PUT", false)]
    [InlineData(CapacidadWeb.TextosLegales, "DELETE", false)]
    public void UnMetodoFueraDeLaClaseDeLaCapacidad_HaceFallarElArranque(CapacidadWeb capacidad, string metodo, bool valido)
    {
        var endpoint = Endpoint("/x", [metodo], new PermitidaConTextosPendientesAttribute(capacidad));

        if (valido)
        {
            PoliticaDeTextosPendientes.Validar([endpoint]);
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([endpoint]));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public void UnEndpointAtribuidoSinMetodosDeclarados_HaceFallarElArranque_PorqueAdmiteTodos()
    {
        var endpoint = Endpoint("/x", null, new PermitidaConTextosPendientesAttribute(CapacidadWeb.Cuenta));

        Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([endpoint]));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public void SinConsultaDeEstado_JuntoAUnaCapacidad_OFueraDeLaListaCerrada_HaceFallarElArranque()
    {
        var junto = Endpoint("/error", ["GET"], new SinConsultaDeEstadoAttribute(), new PermitidaConTextosPendientesAttribute(CapacidadWeb.Cuenta));
        var fuera = Endpoint("/cuenta", ["GET"], new SinConsultaDeEstadoAttribute());
        var enAccion = Endpoint("/pago", ["POST"], new SinConsultaDeEstadoAttribute());
        var valido = Endpoint("/no-disponible", ["GET"], new SinConsultaDeEstadoAttribute());

        Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([junto]));
        Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([fuera]));
        Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([enAccion]));
        PoliticaDeTextosPendientes.Validar([valido]);
        Assert.Equal(["/error", "/sin-permiso", "/no-disponible"], PoliticaDeTextosPendientes.RutasSinConsultaDeEstado);
    }

    private sealed class ShortCircuitMetadata;

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public void UnEndpointConShortCircuit_OUnDestinoSinConsultaQueAdmiteUnaAccion_HaceFallarElArranque()
    {
        var corto = Endpoint("/x", ["GET"], new ShortCircuitMetadata());
        var accion = Endpoint("/error", ["POST"], new SinConsultaDeEstadoAttribute());
        var cualquiera = Endpoint("/error", null, new SinConsultaDeEstadoAttribute());

        Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([corto]));
        Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([accion]));
        Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([cualquiera]));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public async Task MontarLaPolitica_SinRegistrarSuServicio_HaceFallarElArranqueYNoCadaSolicitud()
    {
        var sesiones = new SesionesWebDePrueba();
        var flujo = new BabelHub.Ecommerce.Infrastructure.Plataforma.ActorDelFlujo();

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await using var _ = await AnfitrionConSesion.IniciarCon(sesiones, flujo, despuesDelHandler: app => app.UsarPoliticaDeTextosPendientes());
        });
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-42")]
    [InlineData("Sec-Fetch-Mode", "no-cors")]
    [InlineData("Sec-Fetch-Mode", "cors")]
    [InlineData("Sec-Fetch-Dest", "image")]
    [InlineData("Sec-Fetch-Dest", "empty")]
    public async Task UnRecursoQueElNavegadorPideSolo_NoSeGuardaComoRetorno(string cabecera, string valor)
    {
        var (a, _, _, _, _, almacen, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/prueba/politica/catalogo");
        solicitud.Headers.Add("Cookie", Cookie());
        solicitud.Headers.Add(cabecera, valor);

        await a.Cliente.SendAsync(solicitud);

        Assert.Empty(almacen.Retornos);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-42")]
    public async Task UnaNavegacionRealOSinCabecerasFetch_SiSeGuarda()
    {
        var (a, _, _, _, _, almacen, _) = await AnfitrionConPolitica.Iniciar();
        await using var _ = a;
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/prueba/politica/catalogo");
        solicitud.Headers.Add("Cookie", Cookie());
        solicitud.Headers.Add("Sec-Fetch-Mode", "navigate");
        solicitud.Headers.Add("Sec-Fetch-Dest", "document");

        await a.Cliente.SendAsync(solicitud);

        Assert.Single(almacen.Retornos);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public void UnParametroDeSucursalQueLaRutaNoTiene_HaceFallarElArranque()
    {
        var mal = Endpoint("/sucursal/{codigo}", ["GET"], new ParametroDeSucursalAttribute("sucursalId"));
        var bien = Endpoint("/sucursal/{sucursalId}", ["GET"], new ParametroDeSucursalAttribute("sucursalId"));

        Assert.Throws<InvalidOperationException>(() => PoliticaDeTextosPendientes.Validar([mal]));
        PoliticaDeTextosPendientes.Validar([bien]);
    }
}
