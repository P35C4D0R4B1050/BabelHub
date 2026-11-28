using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Api.Notificaciones;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos.Ecommerce;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// Los endpoints de Notificaciones en la Api (transversal-notificaciones T19, T20, T21, T25; AC-NOT-01, AC-NOT-05, AC-NOT-08, AC-NOT-11, AC-NOT-13): la entrada firmada de estados de entrega (firma ausente, inválida, vieja, repetida,
/// de otro sentido, estado desconocido, cuerpo grande, y el mismo resultado al repetir), la solicitud del Inventario con su contrato (202, 400, 503 y la misma respuesta al repetir) y la administración (solo usuarios; el cuerpo de la
/// desactivación nunca vuelve ni se registra). La presentación solo traduce HTTP: la Aplicación decide.
/// </summary>
public sealed class EndpointsDeNotificacionesTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private static readonly byte[] ClaveEntrante = Encoding.UTF8.GetBytes("clave-del-sentido-entrante-0123456789-abcdef");

    private static readonly byte[] ClaveSaliente = Encoding.UTF8.GetBytes("clave-del-sentido-saliente-0123456789-abcdef");

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = EndpointsDeNotificacionesTests.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class ActorDePrueba : IActorActual, IEstablecedorDeActor
    {
        public ActorActual? Actual { get; set; }

        public ConcurrentBag<ActorActual> Establecidos { get; } = [];

        public void Establecer(ActorActual actor)
        {
            Actual = actor;
            Establecidos.Add(actor);
        }

        public void Limpiar() => Actual = null;
    }

    private sealed class AlmacenEnMemoria : IAlmacenDeEntradasProcesadas
    {
        private readonly ConcurrentDictionary<string, ResultadoMinimo?> _vistas = new();

        public Task<ReclamoDeEntrada> Reclamar(string proveedor, string idDeEntrada, CancellationToken cancelacion = default)
        {
            var clave = proveedor + "|" + idDeEntrada;
            if (_vistas.TryAdd(clave, null))
            {
                return Task.FromResult(new ReclamoDeEntrada(EstadoDeEntrada.Nueva));
            }

            var resultado = _vistas[clave];
            return Task.FromResult(resultado is null ? new ReclamoDeEntrada(EstadoDeEntrada.EnCurso) : new ReclamoDeEntrada(EstadoDeEntrada.Procesada, resultado));
        }

        public Task Completar(string proveedor, string idDeEntrada, ResultadoMinimo resultado, CancellationToken cancelacion = default)
        {
            _vistas[proveedor + "|" + idDeEntrada] = resultado;
            return Task.CompletedTask;
        }
    }

    private sealed class UnidadSimple : IUnidadDeTrabajo
    {
        public Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default) => trabajo(cancelacion);

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
    }

    private sealed class RegistroDePrueba : IRegistroDeEstadoDeEntrega
    {
        public ConcurrentBag<(Guid Aviso, EstadoDeEntrega Estado)> Registrados { get; } = [];

        public bool Falla { get; set; }

        public Task<Resultado<ConfirmacionDeNotificaciones>> Registrar(Guid avisoId, EstadoDeEntrega estado, DateTimeOffset marcaDeTiempo, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("La base no responde.");
            }

            Registrados.Add((avisoId, estado));
            return Task.FromResult(Resultado<ConfirmacionDeNotificaciones>.Exito(new ConfirmacionDeNotificaciones()));
        }
    }

    private sealed class EventosDePrueba : IPublicadorDeEventosDeSeguridad
    {
        public ConcurrentBag<EventoDeSeguridad> Publicados { get; } = [];

        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
        {
            Publicados.Add(evento);
            return Task.CompletedTask;
        }
    }

    /// <summary>Un antifalsificación de prueba: el token válido es la cabecera <c>X-Token</c> con el valor esperado.</summary>
    private sealed class AntifalsificacionDePrueba : IAntiforgery
    {
        public const string Token = "token-de-prueba";

        public AntiforgeryTokenSet GetAndStoreTokens(HttpContext httpContext) => new(Token, Token, "X-Token", "campo");

        public AntiforgeryTokenSet GetTokens(HttpContext httpContext) => GetAndStoreTokens(httpContext);

        public Task<bool> IsRequestValidAsync(HttpContext httpContext) => Task.FromResult(httpContext.Request.Headers["X-Token"] == Token);

        public Task ValidateRequestAsync(HttpContext httpContext) =>
            httpContext.Request.Headers["X-Token"] == Token ? Task.CompletedTask : throw new AntiforgeryValidationException("sin token");

        public void SetCookieTokenAndHeader(HttpContext httpContext)
        {
        }
    }

    private sealed class OrigenDePrueba : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    private sealed class RecepcionDePrueba : IRecepcionDeAvisos
    {
        public ResultadoDeLaSolicitudDeAviso Resultado { get; set; } = ResultadoDeLaSolicitudDeAviso.Creado;

        public List<SolicitudDeAvisoDelInventario> Recibidas { get; } = [];

        public Task<ResultadoDeLaSolicitudDeAviso> Recibir(SolicitudDeAvisoDelInventario solicitud, CancellationToken cancelacion = default)
        {
            Recibidas.Add(solicitud);
            return Task.FromResult(Resultado);
        }
    }

    private sealed class DesactivacionDePrueba : IDesactivacionDeNumeros
    {
        public Resultado<ConfirmacionDeNotificaciones> Respuesta { get; set; } = Resultado<ConfirmacionDeNotificaciones>.Exito(new ConfirmacionDeNotificaciones());

        public List<(string Numero, string? Motivo)> Llamadas { get; } = [];

        public Task<Resultado<ConfirmacionDeNotificaciones>> Desactivar(string numero, string motivo, CancellationToken cancelacion = default)
        {
            Llamadas.Add((numero, motivo));
            return Task.FromResult(Respuesta);
        }

        public Task<Resultado<ConfirmacionDeNotificaciones>> Reactivar(string numero, CancellationToken cancelacion = default)
        {
            Llamadas.Add((numero, null));
            return Task.FromResult(Respuesta);
        }
    }

    private sealed class ConsultaDePrueba : IConsultaDeAvisos
    {
        public List<(string? Estado, int Pagina, int Tamano)> Llamadas { get; } = [];

        public Task<Resultado<PaginaDeAvisos>> Listar(string? estado, int pagina, int tamano, CancellationToken cancelacion = default)
        {
            Llamadas.Add((estado, pagina, tamano));
            return Task.FromResult(Resultado<PaginaDeAvisos>.Exito(new PaginaDeAvisos([new AvisoConsultado(Guid.NewGuid(), "pedido:1:entregado", DestinatarioDelAviso.Cliente, "pedido-entregado", "fallido", 6, Ahora, Ahora, "reintentos-agotados")], pagina, tamano, 1)));
        }
    }

    private WebApplication _app = null!;
    private HttpClient _cliente = null!;
    private readonly RelojDePrueba _reloj = new();
    private readonly ActorDePrueba _actor = new();
    private readonly RegistroDePrueba _registro = new();
    private readonly EventosDePrueba _eventos = new();
    private readonly RecepcionDePrueba _recepcion = new();
    private readonly DesactivacionDePrueba _desactivacion = new();
    private readonly ConsultaDePrueba _consulta = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton<IReloj>(_reloj);
        builder.Services.AddSingleton<IActorActual>(_actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(_actor);
        builder.Services.AddSingleton<IUnidadDeTrabajo>(new UnidadSimple());
        builder.Services.AddSingleton<IOrigenDeSolicitud>(new OrigenDePrueba());
        builder.Services.AddSingleton<IPublicadorDeEventosDeSeguridad>(_eventos);
        builder.Services.AddSingleton<IAlmacenDeEntradasProcesadas>(new AlmacenEnMemoria());
        builder.Services.AddSingleton<IVerificadorDeFirma>(new VerificadorDeFirmaHmac(new OpcionesDeFirmaDeIntegraciones([new("n8n", [ClaveEntrante])])));
        builder.Services.AddSingleton(new OpcionesDeEntradasDeIntegracion(["n8n"]));
        builder.Services.AddSingleton<IRegistroDeEstadoDeEntrega>(_registro);
        builder.Services.AddSingleton<IProcesadorDeEntradasDeIntegracion, ProcesarEntradaDeIntegracion>();
        builder.Services.AddSingleton<IRecepcionDeEstadosDeEntrega, RecibirEstadoDeEntregaDeN8n>();
        builder.Services.AddSingleton<IRecepcionDeAvisos>(_recepcion);
        builder.Services.AddSingleton<IDesactivacionDeNumeros>(_desactivacion);
        builder.Services.AddSingleton<IConsultaDeAvisos>(_consulta);
        builder.Services.AddSingleton<IAntiforgery>(new AntifalsificacionDePrueba());
        builder.Services.AddSingleton(new OpcionesDelHostWeb(HostWeb.Tienda, ["tienda.example.org"]));
        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.MapearNotificaciones();
        _app.MapearEstadosDeEntregaDeN8n(habilitada: true);
        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _app.DisposeAsync();
    }

    // ---------------------------------------------------------------- los estados de entrega de n8n (T19)

    private static string Firmar(byte[] clave, string marca, string cuerpo) => "sha256=" + Convert.ToHexString(HMACSHA256.HashData(clave, Encoding.UTF8.GetBytes(marca + "." + cuerpo))).ToLowerInvariant();

    private string Marca(TimeSpan? desfase = null) => (_reloj.Ahora + (desfase ?? TimeSpan.Zero)).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

    private async Task<HttpResponseMessage> Estado(string cuerpo, string? marca, string? firma)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, EstadosDeEntregaDeN8nEnLaApi.Ruta) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };
        if (marca is not null)
        {
            peticion.Headers.TryAddWithoutValidation(EstadosDeEntregaDeN8nEnLaApi.CabeceraDeLaMarca, marca);
        }

        if (firma is not null)
        {
            peticion.Headers.TryAddWithoutValidation(EstadosDeEntregaDeN8nEnLaApi.CabeceraDeLaFirma, firma);
        }

        return await _cliente.SendAsync(peticion);
    }

    private async Task<HttpResponseMessage> EstadoFirmado(Guid aviso, string estado = "entregado", byte[]? clave = null, TimeSpan? desfase = null)
    {
        var cuerpo = $"{{\"avisoId\":\"{aviso:D}\",\"estado\":\"{estado}\"}}";
        var marca = Marca(desfase);
        return await Estado(cuerpo, marca, Firmar(clave ?? ClaveEntrante, marca, cuerpo));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task UnEstadoFirmado_SeRegistra_ConElActorDeLaIntegracion_YRepetirloResponde200SinRegistrarOtraVez()
    {
        var aviso = Guid.NewGuid();

        var primera = await EstadoFirmado(aviso);
        var repetida = await EstadoFirmado(aviso);

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, repetida.StatusCode);
        Assert.Equal((aviso, EstadoDeEntrega.Entregado), Assert.Single(_registro.Registrados));
        Assert.All(_actor.Establecidos, a => Assert.Equal(TipoDeActor.Integracion, a.Tipo));
        Assert.Null(_actor.Actual);
        Assert.Empty(_eventos.Publicados);
    }

    [Theory]
    [InlineData("sin-firma")]
    [InlineData("sin-marca")]
    [InlineData("firma-invalida")]
    [InlineData("firma-del-otro-sentido")]
    [InlineData("marca-vieja")]
    [InlineData("marca-del-futuro")]
    [InlineData("cuerpo-alterado")]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task LoNoVerificable_Responde401Uniforme_NoRegistraNada_YDejaElEventoDeSeguridad(string caso)
    {
        var aviso = Guid.NewGuid();
        var cuerpo = $"{{\"avisoId\":\"{aviso:D}\",\"estado\":\"entregado\"}}";
        var marca = Marca();

        var respuesta = caso switch
        {
            "sin-firma" => await Estado(cuerpo, marca, null),
            "sin-marca" => await Estado(cuerpo, null, Firmar(ClaveEntrante, marca, cuerpo)),
            "firma-invalida" => await Estado(cuerpo, marca, "sha256=" + new string('a', 64)),
            "firma-del-otro-sentido" => await Estado(cuerpo, marca, Firmar(ClaveSaliente, marca, cuerpo)),
            "marca-vieja" => await EstadoFirmado(aviso, desfase: TimeSpan.FromMinutes(-6)),
            "marca-del-futuro" => await EstadoFirmado(aviso, desfase: TimeSpan.FromMinutes(6)),
            _ => await Estado(cuerpo.Replace("entregado", "fallido", StringComparison.Ordinal), marca, Firmar(ClaveEntrante, marca, cuerpo)),
        };

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        var problema = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("ECO-NO-AUTENTICADO", problema, StringComparison.Ordinal);
        Assert.DoesNotContain(aviso.ToString("D"), problema, StringComparison.Ordinal);
        Assert.Empty(_registro.Registrados);
        if (caso is not ("sin-firma" or "sin-marca"))
        {
            var evento = Assert.Single(_eventos.Publicados);
            Assert.Equal(TipoDeEventoDeSeguridad.EntradaDeIntegracionRechazada, evento.Tipo);
            Assert.Equal("n8n", evento.Proveedor);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task UnEstadoDesconocidoOUnCuerpoIlegible_FirmadosSeDescartanSinEfecto_ConElMismoResultadoAlRepetir()
    {
        var aviso = Guid.NewGuid();

        var desconocido = await EstadoFirmado(aviso, estado: "leido");
        var repetido = await EstadoFirmado(aviso, estado: "leido");
        var cuerpo = "no es json";
        var marca = Marca();
        var ilegible = await Estado(cuerpo, marca, Firmar(ClaveEntrante, marca, cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, desconocido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, repetido.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, ilegible.StatusCode);
        Assert.Contains("ECO-VALIDACION-INVALIDA", await desconocido.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(_registro.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task UnCuerpoMasGrandeQueElTope_SeRechazaSinLeerloEntero()
    {
        var relleno = new string('x', EstadosDeEntregaDeN8nEnLaApi.TopeDelCuerpo + 1);
        var cuerpo = $"{{\"avisoId\":\"{Guid.NewGuid():D}\",\"estado\":\"entregado\",\"relleno\":\"{relleno}\"}}";
        var marca = Marca();

        var respuesta = await Estado(cuerpo, marca, Firmar(ClaveEntrante, marca, cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_registro.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task SiElRegistroFalla_Responde503_YElReclamoSeLibera_ParaQueN8nReintente()
    {
        var aviso = Guid.NewGuid();
        _registro.Falla = true;

        var fallida = await EstadoFirmado(aviso);

        // El almacén de pruebas no revierte (no hay transacción real): el reintento tras una falla propia se prueba con PostgreSQL en la entrada de la pasarela; aquí basta el 503 uniforme.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, fallida.StatusCode);
        Assert.Contains("ECO-SERVICIO-NO-DISPONIBLE", await fallida.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(_registro.Registrados);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task LaEntradaDeEstados_NoEstaDisponibleSiElEnvioNoEsPorN8n()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        await using var sinN8n = builder.Build();
        sinN8n.MapearEstadosDeEntregaDeN8n(habilitada: false);
        await sinN8n.StartAsync();
        using var cliente = sinN8n.GetTestClient();

        var respuesta = await cliente.PostAsync(EstadosDeEntregaDeN8nEnLaApi.Ruta, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
    }

    // ---------------------------------------------------------------- la solicitud del Inventario (T20, T25)

    private Task<HttpResponseMessage> SolicitudDelInventario(object cuerpo) =>
        _cliente.PostAsync(EndpointsDeNotificaciones.RutaDeLasSolicitudesDelInventario, System.Net.Http.Json.JsonContent.Create(cuerpo));

    private static SolicitudDeAvisoDeInventarioDto Aviso(string evento = "traslado:9:en-transito") =>
        new(evento, "traslado-cambio-de-estado", "AYACUCHO", new Dictionary<string, string> { ["numeroDeTraslado"] = "9", ["estado"] = "en-transito", ["rolDeLaSucursal"] = "destino", ["cantidad"] = "3" });

    [Theory]
    [InlineData(ResultadoDeLaSolicitudDeAviso.Creado)]
    [InlineData(ResultadoDeLaSolicitudDeAviso.YaExistia)]
    [InlineData(ResultadoDeLaSolicitudDeAviso.OmitidoSinConsentimiento)]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task LaSolicitudDelInventario_ConElContratoDeShared_Responde202_ElMismoResultadoAlRepetir(ResultadoDeLaSolicitudDeAviso resultado)
    {
        _recepcion.Resultado = resultado;

        var primera = await SolicitudDelInventario(Aviso());
        var repetida = await SolicitudDelInventario(Aviso());

        Assert.Equal(HttpStatusCode.Accepted, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, repetida.StatusCode);
        var recibida = _recepcion.Recibidas[0];
        Assert.Equal(("traslado:9:en-transito", "traslado-cambio-de-estado", "AYACUCHO"), (recibida.EventoId, recibida.Plantilla, recibida.CodigoDeSucursal));
        Assert.Equal("3", recibida.Campos["cantidad"]);
        Assert.All(_actor.Establecidos, a => Assert.Equal("inventario", a.Integracion));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task LaSolicitudRechazadaPorLaAplicacion_Es400ECOValidacion_YUnFalloPropio_Es503()
    {
        _recepcion.Resultado = ResultadoDeLaSolicitudDeAviso.Rechazado;
        var rechazada = await SolicitudDelInventario(Aviso());
        _recepcion.Resultado = ResultadoDeLaSolicitudDeAviso.NoSeGuardo;
        var noGuardada = await SolicitudDelInventario(Aviso());

        Assert.Equal(HttpStatusCode.BadRequest, rechazada.StatusCode);
        Assert.Contains("ECO-VALIDACION-INVALIDA", await rechazada.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noGuardada.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("no es json")]
    [InlineData("{\"eventoId\":\"traslado:1:aprobado\",\"plantilla\":\"x\",\"codigoDeSucursal\":\"AYACUCHO\",\"campos\":{},\"numero\":\"987654321\"}")]
    [Trait("Criterio", "AC-NOT-08")]
    public async Task LaSolicitudMalFormadaOConCamposDeMas_Es400_YNoLlegaALaAplicacion(string cuerpo)
    {
        var respuesta = await _cliente.PostAsync(EndpointsDeNotificaciones.RutaDeLasSolicitudesDelInventario, new StringContent(cuerpo, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_recepcion.Recibidas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-08")]
    public void LaOperacionDeLaSolicitud_EsLaDelContratoCompartido_ConElAlcanceSolicitarAviso()
    {
        var operacion = OperacionesDelEcommerce.Todas.Single(o => o.Nombre == EndpointsDeNotificaciones.OperacionDeLasSolicitudesDelInventario);

        Assert.Equal(EndpointsDeNotificaciones.RutaDeLasSolicitudesDelInventario, operacion.Ruta);
        Assert.Equal("POST", operacion.Metodo);
        Assert.Equal(202, operacion.CodigoDeExito);
        Assert.Equal(BabelHub.Shared.Contratos.AlcancesDeCredencial.SolicitarAviso, operacion.Alcance);
        var declarada = ((IEndpointRouteBuilder)_app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Single(e => e.RoutePattern.RawText == operacion.Ruta && e.Metadata.GetMetadata<OperacionDeServicioAttribute>() is not null);
        Assert.Equal(operacion.Nombre, declarada.Metadata.GetMetadata<OperacionDeServicioAttribute>()!.Operacion.ToString());
    }

    // ---------------------------------------------------------------- la administración (T21)

    private async Task<HttpResponseMessage> PostAdmin(string ruta, HttpContent contenido, bool conToken = true, string? origen = null, string? sitio = null)
    {
        using var peticion = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = contenido };
        if (conToken)
        {
            peticion.Headers.TryAddWithoutValidation("X-Token", AntifalsificacionDePrueba.Token);
        }

        if (origen is not null)
        {
            peticion.Headers.TryAddWithoutValidation("Origin", origen);
        }

        if (sitio is not null)
        {
            peticion.Headers.TryAddWithoutValidation("Sec-Fetch-Site", sitio);
        }

        return await _cliente.SendAsync(peticion);
    }

    [Theory]
    [InlineData("sin-token")]
    [InlineData("origen-ajeno")]
    [InlineData("otro-sitio")]
    [InlineData("text-plain")]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task LasOperacionesQueCambianAQuienSeAvisa_RechazanLaFalsificacionEntreSitios_ConElMismo403(string caso)
    {
        _actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central");
        var json = "{\"numero\":\"987654321\"}";

        foreach (var ruta in new[] { EndpointsDeNotificaciones.RutaDeLaReactivacion, EndpointsDeNotificaciones.RutaDeLosNumerosDesactivados })
        {
            var respuesta = caso switch
            {
                "sin-token" => await PostAdmin(ruta, new StringContent(json, Encoding.UTF8, "application/json"), conToken: false),
                "origen-ajeno" => await PostAdmin(ruta, new StringContent(json, Encoding.UTF8, "application/json"), origen: "https://sitio-hermano.example.org"),
                "otro-sitio" => await PostAdmin(ruta, new StringContent(json, Encoding.UTF8, "application/json"), sitio: "same-site"),
                _ => await PostAdmin(ruta, new StringContent(json, Encoding.UTF8, "text/plain")),
            };

            Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
            Assert.DoesNotContain("987654321", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        }

        Assert.Empty(_desactivacion.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task UnSegundoEstadoFallido_DeOtroReintento_SiSeRegistra_ElMismoMensajeRepetidoNo()
    {
        var aviso = Guid.NewGuid();
        var primero = await EstadoFirmado(aviso, estado: "fallido");
        var repetido = await EstadoFirmado(aviso, estado: "fallido");
        _reloj.Ahora = _reloj.Ahora.AddMinutes(1);
        var otroReintento = await EstadoFirmado(aviso, estado: "fallido");

        Assert.All(new[] { primero, repetido, otroReintento }, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(2, _registro.Registrados.Count(r => r.Aviso == aviso && r.Estado == EstadoDeEntrega.Fallido));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task LaDesactivacionYLaConsulta_SinUsuarioAutenticado_SonUn401_YNoLlegaALaAplicacion()
    {
        var desactivar = await PostAdmin(EndpointsDeNotificaciones.RutaDeLosNumerosDesactivados, System.Net.Http.Json.JsonContent.Create(new DesactivarNumeroDto("987654321", "a pedido del titular")));
        var reactivar = await PostAdmin(EndpointsDeNotificaciones.RutaDeLaReactivacion, System.Net.Http.Json.JsonContent.Create(new ReactivarNumeroDto("987654321")));
        var consultar = await _cliente.GetAsync(EndpointsDeNotificaciones.RutaDeLaConsulta);

        Assert.All(new[] { desactivar, reactivar, consultar }, r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));
        Assert.Empty(_desactivacion.Llamadas);
        Assert.Empty(_consulta.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task LaDesactivacion_ConUsuario_Responde204_SinDevolverElNumero_YElCuerpoNoSeRegistra()
    {
        _actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central");

        var respuesta = await PostAdmin(EndpointsDeNotificaciones.RutaDeLosNumerosDesactivados, System.Net.Http.Json.JsonContent.Create(new DesactivarNumeroDto("987 654 321", "a pedido del titular")));

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(("987 654 321", "a pedido del titular"), Assert.Single(_desactivacion.Llamadas));
        var texto = await respuesta.Content.ReadAsStringAsync();
        Assert.DoesNotContain("987", texto, StringComparison.Ordinal);
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnUsuarioQueNoEsAdministracionCentral_RecibeElProhibidoDeLaAplicacion_Sin403DeLaPresentacion()
    {
        _actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "cliente");
        _desactivacion.Respuesta = Resultado<ConfirmacionDeNotificaciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "acceso-denegado"));

        var respuesta = await PostAdmin(EndpointsDeNotificaciones.RutaDeLosNumerosDesactivados, System.Net.Http.Json.JsonContent.Create(new DesactivarNumeroDto("987654321", "motivo")));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Contains("ECO-PROHIBIDO", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"numero\":\"987654321\"}")]
    [InlineData("{\"numero\":\"987654321\",\"motivo\":\"x\",\"otro\":1}")]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task LaDesactivacionMalFormada_Es400_ConElCuerpoSinRepetirse(string cuerpo)
    {
        _actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central");

        var respuesta = await PostAdmin(EndpointsDeNotificaciones.RutaDeLosNumerosDesactivados, new StringContent(cuerpo, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.DoesNotContain("987654321", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(_desactivacion.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-05")]
    public async Task LaConsulta_PasaElFiltroYLaPagina_ListaLosFallidos_SinNumeroNiCampos()
    {
        _actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central");

        var respuesta = await _cliente.GetAsync(EndpointsDeNotificaciones.RutaDeLaConsulta + "?estado=fallido&pagina=2&tamano=10");
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(("fallido", 2, 10), Assert.Single(_consulta.Llamadas));
        Assert.Contains("\"estado\":\"fallido\"", cuerpo, StringComparison.Ordinal);
        Assert.Contains("\"motivoDeCierre\":\"reintentos-agotados\"", cuerpo, StringComparison.Ordinal);
        Assert.DoesNotContain("numero", cuerpo.Replace("numeroDe", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);
        Assert.True(respuesta.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("?pagina=abc")]
    [InlineData("?tamano=-1")]
    [InlineData("?pagina=1.5")]
    [Trait("Criterio", "AC-NOT-05")]
    public async Task LaConsulta_ConUnaPaginaQueNoEsUnEntero_Es400(string consulta)
    {
        _actor.Actual = ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central");

        var respuesta = await _cliente.GetAsync(EndpointsDeNotificaciones.RutaDeLaConsulta + consulta);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_consulta.Llamadas);
    }
}
