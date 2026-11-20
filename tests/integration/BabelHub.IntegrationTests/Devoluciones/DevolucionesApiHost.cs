using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Areas.Admin.Controllers;
using BabelHub.Ecommerce.Api.Areas.Sucursal.Controllers;
using BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Carrito;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>Los contratos de Devoluciones que anotan cada llamada y responden lo que la prueba pida: sirven para probar la Presentación sin la Aplicación.</summary>
public sealed class DevolucionesFalsas : IDevolucionesDelCliente, IGestionDeDevoluciones
{
    public static readonly Guid DevolucionId = Guid.Parse("d1000000-0000-0000-0000-000000000001");

    public static DevolucionVista Vista(TipoDeLaDevolucion tipo = TipoDeLaDevolucion.Impreso, OrigenDeLaDevolucion origen = OrigenDeLaDevolucion.Solicitud) =>
        new(DevolucionId, Guid.NewGuid(), tipo, origen, tipo == TipoDeLaDevolucion.Impreso ? "lima-central" : null, new TextoLibre("motivo"), null, false, DateTimeOffset.UtcNow, [new LineaDeDevolucionVista(Guid.NewGuid(), Guid.NewGuid(), 1, 10m, EstadoDeLaLineaDeDevolucion.Solicitada, DateTimeOffset.UtcNow, false)]);

    public List<string> Llamadas { get; } = [];

    public SolicitarDevolucionDeImpresoEntrada? UltimoImpreso { get; private set; }

    public PedirReembolsoDeDigitalEntrada? UltimoDigital { get; private set; }

    public ReportarArchivoDefectuosoEntrada? UltimoReporte { get; private set; }

    public Resultado<DevolucionVista> Respuesta { get; set; } = Resultado<DevolucionVista>.Exito(Vista());

    public Task<Resultado<DevolucionVista>> SolicitarImpreso(SolicitarDevolucionDeImpresoEntrada entrada, CancellationToken cancelacion = default)
    {
        UltimoImpreso = entrada;
        return Anotar("impreso");
    }

    public Task<Resultado<DevolucionVista>> PedirReembolsoDeDigital(PedirReembolsoDeDigitalEntrada entrada, CancellationToken cancelacion = default)
    {
        UltimoDigital = entrada;
        return Anotar("digital");
    }

    public Task<Resultado<DevolucionVista>> ReportarArchivoDefectuoso(ReportarArchivoDefectuosoEntrada entrada, CancellationToken cancelacion = default)
    {
        UltimoReporte = entrada;
        return Anotar("reporte");
    }

    public Task<Resultado<PaginaDeDevoluciones>> ListarMias(ListarDevolucionesEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<DevolucionVista>> ObtenerMia(Guid devolucionId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<PaginaDeDevoluciones>> ListarDeLaSucursal(string codigoDeSucursal, ListarDevolucionesEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<PaginaDeDevoluciones>> ListarDeLaAdministracion(ListarDevolucionesDeLaAdministracionEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<DevolucionVista>> Obtener(Guid devolucionId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<DevolucionVista>> Aprobar(Guid devolucionId, CancellationToken cancelacion = default) => Anotar($"aprobar:{devolucionId:D}");

    public Task<Resultado<DevolucionVista>> Rechazar(Guid devolucionId, CancellationToken cancelacion = default) => Anotar($"rechazar:{devolucionId:D}");

    public Task<Resultado<DevolucionVista>> ConfirmarRecepcion(Guid devolucionId, CancellationToken cancelacion = default) => Anotar($"recibir:{devolucionId:D}");

    public Task<Resultado<DevolucionVista>> AprobarReporte(Guid devolucionId, CancellationToken cancelacion = default) => Anotar($"aprobar-reporte:{devolucionId:D}");

    public Task<Resultado<DevolucionVista>> RechazarReporte(Guid devolucionId, CancellationToken cancelacion = default) => Anotar($"rechazar-reporte:{devolucionId:D}");

    private Task<Resultado<DevolucionVista>> Anotar(string llamada)
    {
        Llamadas.Add(llamada);
        return Task.FromResult(Respuesta);
    }
}

/// <summary>
/// La Presentación de Devoluciones en proceso (T23, T24): MVC con solo los tres controladores, el manejo de errores, el filtro global antifalsificación y un registrador de denegaciones espía. Un middleware de prueba fija el actor con una cabecera.
/// La Aplicación y PostgreSQL se prueban aparte.
/// </summary>
public sealed class DevolucionesApiHost : IAsyncDisposable
{
    private WebApplication _app = null!;
    private (string Cookie, string Token)? _token;

    public HttpClient Cliente { get; private set; } = null!;

    public DevolucionesFalsas Devoluciones { get; } = new();

    public DenegacionesEspia Denegaciones { get; } = new();

    public static async Task<DevolucionesApiHost> Crear()
    {
        var host = new DevolucionesApiHost();
        await host.Iniciar();
        return host;
    }

    private async Task Iniciar()
    {
        var actor = new ActorDelFlujo();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<IDevolucionesDelCliente>(Devoluciones);
        builder.Services.AddSingleton<IGestionDeDevoluciones>(Devoluciones);
        builder.Services.AddSingleton<IRegistradorDeDenegacionesHttp>(Denegaciones);
        builder.Services.AddSingleton(new OpcionesDelHostWeb(HostWeb.Gestion, []));
        builder.Services.AgregarProteccionContraFalsificacion();
        builder.Services.AddSingleton(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton<ContextoDePagina>(sp => new ContextoDePagina(sp.GetRequiredService<IHttpContextAccessor>(), DispatchProxy.Create<IPreferenciaDeSucursal, CarritoApiHost.DobleQueLanza>()));
        builder.Services
            .AddControllers()
            .ConfigureApplicationPartManager(gestor =>
            {
                gestor.ApplicationParts.Clear();
                gestor.ApplicationParts.Add(new PartesDeTipos(typeof(DevolucionesController), typeof(DevolucionesDeSucursalController), typeof(AdministracionDeDevolucionesController)));
            });

        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.Use(async (contexto, siguiente) =>
        {
            if (Guid.TryParse(contexto.Request.Headers["X-Prueba-Usuario"], out var usuario))
            {
                actor.Establecer(ActorActual.DeUsuario(usuario, RolesDeUsuario.PersonalDeLogistica, null));
            }

            await siguiente();
        });
        _app.UseRouting();
        _app.MapGet("/prueba/token", (HttpContext contexto, IAntiforgery antiforgery) => Results.Text(antiforgery.GetAndStoreTokens(contexto).RequestToken));
        _app.MapControllers();
        await _app.StartAsync();
        Cliente = _app.GetTestClient();
        Cliente.BaseAddress = new Uri("https://localhost/");
    }

    public async ValueTask DisposeAsync()
    {
        Cliente?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    private async Task<(string Cookie, string Token)> TokenAntifalsificacion()
    {
        if (_token is { } guardado)
        {
            return guardado;
        }

        using var respuesta = await Cliente.GetAsync("/prueba/token");
        var cookie = respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal)).Split(';')[0];
        _token = (cookie, await respuesta.Content.ReadAsStringAsync());
        return _token.Value;
    }

    /// <summary>Un <c>POST</c> con el token antifalsificación y la sesión indicada. El cuerpo es un formulario con los campos dados o, si se pasa, el contenido tal cual (p. ej. un <c>multipart</c>).</summary>
    public async Task<HttpResponseMessage> Enviar(string ruta, IEnumerable<KeyValuePair<string, string>>? campos = null, HttpContent? contenido = null, bool html = false)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta);
        solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", Guid.NewGuid().ToString("D"));
        var (cookie, token) = await TokenAntifalsificacion();
        solicitud.Headers.TryAddWithoutValidation("Cookie", cookie);
        solicitud.Headers.TryAddWithoutValidation(ProteccionContraFalsificacion.NombreDeLaCabecera, token);
        if (html)
        {
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        }

        solicitud.Content = contenido ?? new FormUrlEncodedContent(campos ?? []);
        return await Cliente.SendAsync(solicitud);
    }

    public static async Task<JsonElement> Leer(HttpResponseMessage respuesta) => (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private sealed class PartesDeTipos(params Type[] tipos) : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "controladores-de-devoluciones";

        public IEnumerable<TypeInfo> Types => tipos.Select(t => t.GetTypeInfo());
    }
}
