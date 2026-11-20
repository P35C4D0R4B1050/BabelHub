using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Un verificador de la credencial que entrega el rol y la sucursal que fija la prueba (el personal ingresa por el host de gestión).</summary>
internal sealed class VerificadorConRol(RolWeb rol, Guid? sucursal) : IVerificadorDeCredencial
{
    public VerificacionDeCredencial Verificar(string credencial, DateTimeOffset ahora) =>
        credencial.StartsWith("jwt-ok", StringComparison.Ordinal)
            ? new VerificacionDeCredencial(EstadoDeVerificacion.Valida, new ActorVerificado(VerificadorPorPrefijo.Usuario, rol, sucursal))
            : new VerificacionDeCredencial(EstadoDeVerificacion.Invalida, null);
}

/// <summary>
/// T39: la política de textos pendientes con la sesión iniciada por <c>ISesionesWeb.Iniciar</c> (módulo real y PostgreSQL real, no por <c>/ingreso</c>, que es de WEB-2): la enumeración de todas las
/// rutas de ambos hosts, la caja negra, el retorno tras aceptar en la base, la falla cerrada y el evento propio (AC-WEB-28, AC-WEB-34 a AC-WEB-42).
/// </summary>
[Collection(ColeccionPostgresDeLaWeb.Nombre)]
public sealed class PoliticaConSesionRealTests(PostgresDeLaWebFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Mundo(
        AnfitrionWeb Anfitrion,
        EntornoDeSesiones Entorno,
        EstadoDeCuentaDePrueba Estado,
        AceptacionDePrueba Aceptacion,
        DenegacionesDePrueba Denegaciones,
        string Cookie) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Anfitrion.DisposeAsync();
    }

    private async Task<Mundo> Armar(HostWeb host = HostWeb.Tienda, RolWeb rol = RolWeb.Cliente, Guid? sucursal = null, LimitadorDeConsultasDeEstado? limitador = null)
    {
        var entorno = new EntornoDeSesiones(bd, verificador: new VerificadorConRol(rol, sucursal));
        var estado = new EstadoDeCuentaDePrueba();
        var aceptacion = new AceptacionDePrueba(estado);
        var denegaciones = new DenegacionesDePrueba();
        var anfitrion = await AnfitrionConSesion.IniciarCon(
            entorno.Web,
            new ActorDelFlujo(),
            host,
            servicios: s =>
            {
                s.AgregarPoliticaDeTextosPendientes();
                if (limitador is not null)
                {
                    s.AddSingleton(limitador);
                }

                s.AddSingleton<IEstadoDeCuentaDeLaWeb>(estado);
                s.AddSingleton<IAceptacionDeTextosEnIdentidad>(aceptacion);
                s.AddSingleton<BabelHub.Ecommerce.Application.Auditoria.IDenegacionDeAcceso>(denegaciones);
                s.AddSingleton<IAlmacenDeSesionesWeb>(entorno.Sesiones);
                s.AddSingleton<IRegistroDeSeguridadDeLaWeb>(entorno.Seguridad);
                s.AddSingleton<IPublicadorDeAlertasTecnicas>(entorno.Alertas);
                s.AddSingleton<IReloj>(entorno.Reloj);
                s.AddSingleton<IBorradoresDePantalla>(entorno.BorradoresDePantalla);
            },
            despuesDelHandler: app => app.UsarPoliticaDeTextosPendientes());
        var valor = await entorno.Abrir(host, rol);
        return new Mundo(anfitrion, entorno, estado, aceptacion, denegaciones, AnfitrionConSesion.Cookie(host, valor));
    }

    private static Task<HttpResponseMessage> Pedir(Mundo m, string ruta, string metodo = "GET", string? cookie = null)
    {
        var solicitud = new HttpRequestMessage(new HttpMethod(metodo), ruta);
        solicitud.Headers.Add("Cookie", cookie ?? m.Cookie);
        return m.Anfitrion.Cliente.SendAsync(solicitud);
    }

    private Task<string?> Retorno() => bd.Consultar<string>("SELECT retorno_tras_aceptar FROM web.sesiones_web");

    // ---- La enumeración de todas las rutas de ambos hosts ----

    private static readonly string[] AtribuidasComunes =
    [
        "GET /cuenta → Cuenta",
        "GET /cuenta/avisos → Cuenta",
        "GET /cuenta/cancelacion → Cuenta",
        "GET /cuenta/correo → Cuenta",
        "GET /cuenta/datos → Cuenta",
        "GET /cuenta/pedidos → Pedidos",
        "GET /cuenta/pedidos/{pedidoId:guid} → Pedidos",
        "GET /cuenta/segundo-factor → Cuenta",
        "GET /cuenta/solicitudes → Cuenta",
        "GET /legal/{texto} → TextosLegales",
        "GET /textos/aceptar → TextosLegales",
        "POST /sesion/cerrar → CerrarSesion",
        "POST /textos/aceptar → Aceptar",
    ];

    // El Libro de Reclamaciones solo existe en el host de la tienda (WEB-41): la lista cerrada de cada host es la común y, en la tienda, estas.
    private static readonly string[] AtribuidasDelLibro =
    [
        "GET /reclamaciones → LibroDeReclamaciones",
        "GET /reclamaciones/mis-hojas → LibroDeReclamaciones",
        "GET /reclamaciones/nueva → LibroDeReclamaciones",
        "GET /reclamaciones/{numero:regex(^[0-9]{{4}}-[0-9]{{6}}\\z)} → LibroDeReclamaciones",
        "POST /reclamaciones/nueva → EnviarHoja",
    ];

    private static IEnumerable<string> AtribuidasEsperadas(HostWeb host) =>
        (host == HostWeb.Tienda ? AtribuidasComunes.Concat(AtribuidasDelLibro) : AtribuidasComunes).Order(StringComparer.Ordinal);

    private static IEnumerable<RouteEndpoint> DeLaApi(AnfitrionWeb a) =>
        a.Servicios.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is { } accion && accion.ControllerTypeInfo.Assembly == BabelHub.Ecommerce.Api.AssemblyMarker.Assembly);

    [Theory]
    [Trait("Criterio", "AC-WEB-38")]
    [InlineData(HostWeb.Tienda, RolWeb.Cliente)]
    [InlineData(HostWeb.Gestion, RolWeb.AdministradorCentral)]
    public async Task LaListaDeRutasAtribuidas_CoincideConLaDelPlan_ConMetodosDentroDeSuClase_YLaDeSinConsultaEsCerrada(HostWeb host, RolWeb rol)
    {
        await using var m = await Armar(host, rol, rol == RolWeb.Cliente ? null : Guid.NewGuid());

        var endpoints = DeLaApi(m.Anfitrion).ToList();
        PoliticaDeTextosPendientes.Validar(endpoints);
        var atribuidas = endpoints
            .Where(e => e.Metadata.GetMetadata<PermitidaConTextosPendientesAttribute>() is not null)
            .SelectMany(e => e.Metadata.GetMetadata<IHttpMethodMetadata>()!.HttpMethods.Select(metodo => $"{metodo} /{e.RoutePattern.RawText!.TrimStart('/')} → {e.Metadata.GetMetadata<PermitidaConTextosPendientesAttribute>()!.Capacidad}"))
            .Order(StringComparer.Ordinal)
            .ToList();
        var sinConsulta = endpoints.Where(e => e.Metadata.GetMetadata<SinConsultaDeEstadoAttribute>() is not null).Select(e => "/" + e.RoutePattern.RawText!.TrimStart('/')).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(AtribuidasEsperadas(host), atribuidas);
        Assert.Equal(["/error", "/no-disponible", "/sin-permiso"], sinConsulta);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-38")]
    [InlineData(HostWeb.Tienda, RolWeb.Cliente)]
    [InlineData(HostWeb.Gestion, RolWeb.Encargado)]
    public async Task TodoEndpointYMetodoSinAtributo_SeNiegaConTextosPendientes_YNuncaSeEjecuta(HostWeb host, RolWeb rol)
    {
        await using var m = await Armar(host, rol, rol == RolWeb.Cliente ? null : Guid.NewGuid());
        var permitidas = new HashSet<string>(["/legal/terminos", "/textos/aceptar", "/sesion/cerrar", "/error", "/sin-permiso", "/no-disponible"]);
        var recorridas = 0;

        foreach (var endpoint in DeLaApi(m.Anfitrion))
        {
            var url = Regex.Replace(Regex.Replace("/" + endpoint.RoutePattern.RawText!.TrimStart('/'), @"\{[^}:]+:guid\}", Guid.NewGuid().ToString("D")), @"\{[^}]+\}", "terminos");
            foreach (var metodo in endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["GET"])
            {
                // Las rutas del Libro de Reclamaciones están atribuidas a propósito (WEB-41; la lista cerrada de la otra prueba las fija): con textos pendientes siguen abiertas y no se «niegan».
                var delLibro = endpoint.Metadata.GetMetadata<PermitidaConTextosPendientesAttribute>() is { Capacidad: CapacidadWeb.LibroDeReclamaciones or CapacidadWeb.EnviarHoja or CapacidadWeb.Cuenta or CapacidadWeb.Pedidos };
                if (delLibro || (permitidas.Contains(url) && (metodo == "GET" || url == "/sesion/cerrar" || url == "/textos/aceptar")))
                {
                    continue;
                }

                recorridas++;
                var respuesta = await Pedir(m, url, metodo);
                var cuerpo = await respuesta.Content.ReadAsStringAsync();
                // Una ruta de la administración que el rol no ve la niega primero el filtro de área (403 ECO-PROHIBIDO, antes de la acción): también es una negación, no una ejecución.
                var delAreaDeAdministracion = rol == RolWeb.Encargado && endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.AreaAttribute>()?.RouteValue == "Admin";
                // Una ruta con el valor de sucursal en la ruta ([ParametroDeSucursal]) compara primero con la sucursal del actor, antes de consultar a Identidad (D15): la sucursal «terminos» de la prueba no es la suya y se niega como fuera de alcance (403 ECO-PROHIBIDO), tampoco una ejecución.
                var delAlcanceDeSucursal = endpoint.Metadata.GetMetadata<ParametroDeSucursalAttribute>() is not null;
                var negada = (respuesta.StatusCode == HttpStatusCode.Found && respuesta.Headers.Location!.OriginalString is "/textos/aceptar" or "/sin-permiso")
                             || ((delAreaDeAdministracion || delAlcanceDeSucursal) && respuesta.StatusCode == HttpStatusCode.Forbidden && cuerpo.Contains("ECO-PROHIBIDO", StringComparison.Ordinal))
                             || (respuesta.StatusCode == HttpStatusCode.Forbidden && (cuerpo.Contains("ECO-TEXTOS-PENDIENTES", StringComparison.Ordinal) || cuerpo.Length == 0));
                Assert.True(negada, $"{host} {metodo} {url} → {(int)respuesta.StatusCode} {cuerpo}");
            }
        }

        Assert.True(recorridas >= 2, "debía recorrer las rutas reales de la Api");
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public async Task UnaRutaConValorDeSucursalSinParametroDeSucursal_SeDetecta()
    {
        await using var m = await Armar(HostWeb.Gestion, RolWeb.AdministradorCentral);
        bool ConSucursal(RouteEndpoint e) => e.RoutePattern.Parameters.Any(p => p.Name.Contains("sucursal", StringComparison.OrdinalIgnoreCase));

        var reales = DeLaApi(m.Anfitrion).Where(e => ConSucursal(e) && e.Metadata.GetMetadata<ParametroDeSucursalAttribute>() is null).ToList();
        var todas = m.Anfitrion.Servicios.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Where(e => ConSucursal(e) && e.Metadata.GetMetadata<ParametroDeSucursalAttribute>() is null).ToList();

        // La prueba de detección encuentra la ruta de prueba sin declarar y ninguna de la Api real la omite.
        Assert.Empty(reales);
        Assert.Single(todas);
    }

    // ---- Caja negra ----

    [Theory]
    [Trait("Criterio", "AC-WEB-38")]
    [InlineData("GET", "/ruta/que/no/existe", 302)]
    [InlineData("GET", "/lib/no-existe.js", 302)]
    [InlineData("HEAD", "/", 403)]
    [InlineData("OPTIONS", "/", 403)]
    [InlineData("TRACE", "/", 403)]
    [InlineData("POST", "/", 403)]
    [InlineData("POST", "/cuenta", 403)]
    [InlineData("POST", "/pedidos", 403)]
    [InlineData("PROPFIND", "/", 403)]
    [InlineData("get", "/", 403)]
    public async Task CajaNegra_ConTextosPendientes_TodoLoNoDeclaradoSeNiega(string metodo, string ruta, int estado)
    {
        await using var m = await Armar();

        var respuesta = await Pedir(m, ruta, metodo);

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.NotEmpty(m.Entorno.Seguridad.Bloqueos.Where(_ => estado == 403).DefaultIfEmpty(default!));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-38")]
    public async Task LosEstaticosYLaSalud_NoConsultanAIdentidad()
    {
        await using var m = await Armar(host: HostWeb.Tienda);
        m.Estado.Comportamiento = _ => throw new InvalidOperationException("no debe consultarse");

        var destinos = new[] { "/error", "/sin-permiso", "/no-disponible" };
        foreach (var destino in destinos)
        {
            Assert.NotEqual(HttpStatusCode.Found, (await Pedir(m, destino)).StatusCode);
        }

        Assert.Equal(0, m.Estado.Consultas);
    }

    // ---- Retorno tras aceptar, en la base ----

    [Fact]
    [Trait("Criterio", "AC-WEB-42")]
    public async Task ElRetorno_SeGuardaEnLaBase_SoloDesdeUnGetDePantallaCompleta_YSeEntregaUnaSolaVez()
    {
        await using var m = await Armar();
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/prueba/politica/catalogo/7");
        solicitud.Headers.Add("Cookie", m.Cookie);
        solicitud.Headers.Add("HX-Request", "true");
        await m.Anfitrion.Cliente.SendAsync(solicitud);
        Assert.Null(await Retorno());

        await Pedir(m, "/prueba/politica/catalogo/7");
        Assert.Equal("/prueba/politica/catalogo/7", await Retorno());

        m.Aceptacion.Vigente = await VersionVigente(m.Anfitrion);
        var envio = await AnfitrionConSesion.Post(m.Anfitrion, "/textos/aceptar", m.Cookie);
        envio.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["version"] = m.Aceptacion.Vigente });
        var respuesta = await m.Anfitrion.Cliente.SendAsync(envio);

        Assert.Equal("/prueba/politica/catalogo/7", respuesta.Headers.Location!.OriginalString);
        Assert.Null(await Retorno());
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-42")]
    [InlineData("/%2f%2fatacante.example/x")]
    [InlineData("/ruta%5cotra")]
    [InlineData("/prueba/politica/catalogo/7%0d%0aSet-Cookie:%20x=1")]
    [InlineData("/ruta%3Bparametro")]
    public async Task UnaRutaNoValidaComoRetorno_NoSeGuarda(string ruta)
    {
        await using var m = await Armar();

        await Pedir(m, ruta);

        Assert.Null(await Retorno());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-42")]
    public async Task UnaAccionBloqueadaNoSeGuardaNiSeReejecutaAlAceptar()
    {
        await using var m = await Armar();

        var bloqueada = await Pedir(m, "/prueba/politica/pago", "POST");

        Assert.Equal(HttpStatusCode.Forbidden, bloqueada.StatusCode);
        Assert.Null(await Retorno());
    }

    private static async Task<string> VersionVigente(AnfitrionWeb a)
    {
        using var ambito = a.Servicios.CreateScope();
        return (await ambito.ServiceProvider.GetRequiredService<IAceptacionDeTextos>().VersionVigente()).Valor.Identificador;
    }

    // ---- Cliente y personal, en ambos hosts ----

    [Theory]
    [Trait("Criterio", "AC-WEB-34")]
    [InlineData(HostWeb.Tienda, RolWeb.Cliente, "/prueba/politica/cuenta", 200)]
    [InlineData(HostWeb.Tienda, RolWeb.Cliente, "/prueba/politica/pedidos", 200)]
    [InlineData(HostWeb.Tienda, RolWeb.Cliente, "/prueba/politica/catalogo", 302)]
    [InlineData(HostWeb.Gestion, RolWeb.Encargado, "/prueba/politica/cuenta", 200)]
    [InlineData(HostWeb.Gestion, RolWeb.Encargado, "/prueba/politica/pedidos", 302)]
    [InlineData(HostWeb.Gestion, RolWeb.Logistica, "/prueba/politica/pedidos", 302)]
    [InlineData(HostWeb.Gestion, RolWeb.AdministradorCentral, "/prueba/politica/pedidos", 302)]
    public async Task ClienteYPersonal_AlAbrirPantallas_VenSoloLoQueLaDecisionBPermite(HostWeb host, RolWeb rol, string ruta, int estado)
    {
        await using var m = await Armar(host, rol, rol == RolWeb.Cliente ? null : Guid.NewGuid());

        var respuesta = await Pedir(m, ruta);

        Assert.Equal(estado, (int)respuesta.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-37")]
    public async Task UnEncargadoDeOtraSucursal_VeSinPermiso_YElCentralVeTodas_ConTextosPendientes()
    {
        var propia = Guid.NewGuid();
        await using var encargado = await Armar(HostWeb.Gestion, RolWeb.Encargado, propia);
        await using var central = await Armar(HostWeb.Gestion, RolWeb.AdministradorCentral);

        var ajena = await Pedir(encargado, $"/prueba/politica/sucursal/{Guid.NewGuid()}");
        var suya = await Pedir(encargado, $"/prueba/politica/sucursal/{propia}");
        var delCentral = await Pedir(central, $"/prueba/politica/sucursal/{Guid.NewGuid()}");

        Assert.Equal("/sin-permiso", ajena.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, suya.StatusCode);
        Assert.Equal(HttpStatusCode.OK, delCentral.StatusCode);
        Assert.Equal("fuera-de-sucursal", Assert.Single(encargado.Denegaciones.Registradas).Motivo);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-34")]
    [InlineData("/prueba/politica/pago")]
    [InlineData("/prueba/politica/datos")]
    public async Task LosPostDirectosDePagoYDeCambios_SeRechazanSiempreConElCodigoEstable(string ruta)
    {
        await using var m = await Armar();

        var respuesta = await Pedir(m, ruta, "POST");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains("ECO-TEXTOS-PENDIENTES", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-36")]
    public async Task LaHojaDeReclamacionYElLibro_SiguenDisponibles_ConTextosPendientes()
    {
        await using var m = await Armar();
        var hoja = await AnfitrionConSesion.Post(m.Anfitrion, "/prueba/politica/hoja", m.Cookie);

        Assert.Equal(HttpStatusCode.OK, (await Pedir(m, "/prueba/politica/libro")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await m.Anfitrion.Cliente.SendAsync(hoja)).StatusCode);
    }

    // ---- El paso del ingreso pesa más que los textos ----

    [Fact]
    [Trait("Criterio", "AC-WEB-28")]
    public async Task ElPersonalConUnPasoPendienteYTextosNuevos_VaPrimeroAlPasoDelIngreso()
    {
        var desafio = new DesafioDePrueba();
        var entorno = new EntornoDeSesiones(bd);
        await using var anfitrion = await AnfitrionConSesion.IniciarCon(
            entorno.Web,
            new ActorDelFlujo(),
            HostWeb.Gestion,
            servicios: s =>
            {
                s.AddSingleton<IDesafioDelIngreso>(desafio);
                s.AddSingleton<IEstadoDeCuentaDeLaWeb>(new EstadoDeCuentaDePrueba());
                s.AddSingleton<BabelHub.Ecommerce.Application.Auditoria.IDenegacionDeAcceso>(new DenegacionesDePrueba());
            });
        var solicitud = new HttpRequestMessage(HttpMethod.Get, "/prueba/area/sucursal");
        solicitud.Headers.Add("Cookie", $"{CookieDelDesafio.Nombre}=Qw3ErTy5UiOp7AsDf9GhJk1LzXc2VbNm4QwErTyUiOp");

        var respuesta = await anfitrion.Cliente.SendAsync(solicitud);

        Assert.Equal("/ingreso", respuesta.Headers.Location!.OriginalString);
        Assert.Single(desafio.Consultados);
    }

    // ---- Falla cerrada, con la sesión real ----

    [Fact]
    [Trait("Criterio", "AC-WEB-39")]
    public async Task ConIdentidadCaida_SeCierraLaSesionYLaSesionWebSeInvalida_AunSiLaRevocacionQuedaPendiente()
    {
        await using var m = await Armar();
        m.Estado.Comportamiento = _ => throw new InvalidOperationException("Identidad cayó");
        m.Entorno.Identidad.ConfirmaRevocaciones = false;
        var valor = m.Cookie[(m.Cookie.IndexOf('=') + 1)..];

        var pantalla = await Pedir(m, "/prueba/politica/cuenta");
        var cierre = await AnfitrionConSesion.Post(m.Anfitrion, "/sesion/cerrar", m.Cookie);
        var respuestaDeCierre = await m.Anfitrion.Cliente.SendAsync(cierre);

        Assert.Equal("/no-disponible", pantalla.Headers.Location!.OriginalString);
        Assert.Equal("/ingreso", respuestaDeCierre.Headers.Location!.OriginalString);
        Assert.Equal("voluntario", await bd.Consultar<string>($"SELECT motivo_de_cierre FROM web.sesiones_web WHERE id_hash = decode('{ValorDeCookieDeSesion.Hash(valor).Valor}', 'hex')"));
        Assert.True(await bd.Consultar<bool>("SELECT revocacion_pendiente FROM web.sesiones_web"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-39")]
    public async Task ConIdentidadCaidaAlRenovar_LaPantallaFallaCerrada_SinCerrarLaSesionNiPerderElCarrito()
    {
        await using var m = await Armar();
        m.Entorno.Reloj.Ahora = EntornoDeSesiones.Inicio.AddMinutes(14).AddSeconds(30);
        m.Estado.Estado = EstadoDeAceptacionDeTextos.Desconocido;

        var respuesta = await Pedir(m, "/prueba/politica/cuenta");

        Assert.Equal("/no-disponible", respuesta.Headers.Location!.OriginalString);
        Assert.Equal("abierta", await bd.Consultar<string>("SELECT coalesce(motivo_de_cierre, 'abierta') FROM web.sesiones_web"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-39")]
    public async Task ConElTopeDeConsultasLleno_LaSolicitudFallaCerradoDeInmediato()
    {
        await using var m = await Armar(limitador: new LimitadorDeConsultasDeEstado(1));
        var liberar = new TaskCompletionSource<EstadoDeAceptacionDeTextos>();
        m.Estado.Comportamiento = _ => liberar.Task;
        var primera = Pedir(m, "/prueba/politica/cuenta");
        while (m.Estado.Consultas == 0)
        {
            await Task.Delay(20);
        }

        var segunda = await Pedir(m, "/prueba/politica/cuenta");

        Assert.Equal("/no-disponible", segunda.Headers.Location!.OriginalString);
        Assert.Equal(1, m.Estado.Consultas);
        liberar.SetResult(EstadoDeAceptacionDeTextos.Pendientes);
        Assert.Equal(HttpStatusCode.OK, (await primera).StatusCode);
    }

    // ---- El evento propio ----

    [Fact]
    [Trait("Criterio", "AC-WEB-40")]
    public async Task OnceBloqueosEnCincoMinutos_SonOnceEventosPropios_YNingunoEntraEnElConteoDeAccesoDenegado()
    {
        await using var m = await Armar();

        for (var i = 0; i < 11; i++)
        {
            await Pedir(m, "/prueba/politica/pago", "POST");
        }

        Assert.Equal(11, m.Entorno.Seguridad.Bloqueos.Count);
        Assert.All(m.Entorno.Seguridad.Bloqueos, b => Assert.Equal("/prueba/politica/pago", b.PlantillaDeRuta));
        Assert.Empty(m.Denegaciones.Registradas);
        Assert.Empty(m.Entorno.Alertas.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-40")]
    public async Task UnaRutaConUnValor_NoDejaElValorEnElEvento_PorqueSeRegistraLaPlantillaOUnValorFijo()
    {
        await using var m = await Armar();

        await Pedir(m, "/prueba/politica/catalogo/987654", "POST");

        var clave = Assert.Single(m.Entorno.Seguridad.Bloqueos);
        // No hay endpoint para ese método: se registra el valor fijo, nunca la dirección con su valor.
        Assert.Equal(ClaveDeBloqueoPorTextos.RutaNoDeclarada, clave.PlantillaDeRuta);
        Assert.DoesNotContain("987654", clave.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-41")]
    public async Task FueraDeRolOFueraDeAlcance_SigueSiendoAcessoDenegado_NoUnBloqueoPorTextos()
    {
        await using var m = await Armar(HostWeb.Gestion, RolWeb.Encargado, Guid.NewGuid());

        await Pedir(m, "/prueba/area/admin");

        Assert.Equal("fuera-de-rol", Assert.Single(m.Denegaciones.Registradas).Motivo);
        Assert.Empty(m.Entorno.Seguridad.Bloqueos);
    }
}
