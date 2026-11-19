using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Areas.Sucursal.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Envios;
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

namespace BabelHub.IntegrationTests.Envios;

/// <summary>Un <c>IEnviosDeLogistica</c> que anota cada llamada y responde lo que la prueba le pida: sirve para probar la Presentación sin la Aplicación.</summary>
public sealed class EnviosDeLogisticaFalso : IEnviosDeLogistica
{
    public static readonly DetalleDeEnvio Detalle = new(Guid.NewGuid(), Guid.NewGuid(), "lima-central", EstadoDelEnvio.GuiaGenerada, "GUIA-000123", "operador-a", [Guid.NewGuid()], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, null);

    public List<string> Llamadas { get; } = [];

    public Resultado<DetalleDeEnvio> RespuestaDeOperacion { get; set; } = Resultado<DetalleDeEnvio>.Exito(Detalle);

    public Resultado<PaginaDeEnvios> RespuestaDeLista { get; set; } = Resultado<PaginaDeEnvios>.Exito(new PaginaDeEnvios([], 1, 20, 0));

    public ListarEnviosEntrada? UltimaConsulta { get; private set; }

    public Task<Resultado<PaginaDeEnvios>> Listar(ListarEnviosEntrada entrada, CancellationToken cancelacion = default)
    {
        Llamadas.Add("listar");
        UltimaConsulta = entrada;
        return Task.FromResult(RespuestaDeLista);
    }

    public Task<Resultado<DetalleDeEnvio>> Obtener(Guid envioId, CancellationToken cancelacion = default) => Anotar("obtener", envioId);

    public Task<Resultado<DetalleDeEnvio>> SolicitarGuia(Guid envioId, CancellationToken cancelacion = default) => Anotar("guia", envioId);

    public Task<Resultado<DetalleDeEnvio>> PasarAEnReparto(Guid envioId, CancellationToken cancelacion = default) => Anotar("reparto", envioId);

    public Task<Resultado<DetalleDeEnvio>> MarcarEntregado(Guid envioId, CancellationToken cancelacion = default) => Anotar("entregado", envioId);

    private Task<Resultado<DetalleDeEnvio>> Anotar(string llamada, Guid envioId)
    {
        Llamadas.Add($"{llamada}:{envioId:D}");
        return Task.FromResult(RespuestaDeOperacion);
    }
}

/// <summary>
/// La Presentación de logística en proceso (T16): MVC con solo el <c>LogisticaController</c>, el manejo de errores, el filtro global antifalsificación y un registrador de denegaciones espía. Un middleware de prueba fija
/// el actor con una cabecera. La Aplicación y PostgreSQL se prueban aparte.
/// </summary>
public sealed class LogisticaApiHost : IAsyncDisposable
{
    private WebApplication _app = null!;
    private (string Cookie, string Token)? _token;

    public HttpClient Cliente { get; private set; } = null!;

    public EnviosDeLogisticaFalso Envios { get; } = new();

    public DenegacionesEspia Denegaciones { get; } = new();

    public static async Task<LogisticaApiHost> Crear()
    {
        var host = new LogisticaApiHost();
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
        builder.Services.AddSingleton<IEnviosDeLogistica>(Envios);
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
                gestor.ApplicationParts.Add(new PartesDeTipos(typeof(LogisticaController)));
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

    /// <summary>Un <c>POST</c> con el token antifalsificación (salvo que se pida sin él) y la sesión de logística si se indica. El cuerpo es un formulario con los campos dados.</summary>
    public async Task<HttpResponseMessage> Enviar(string ruta, IEnumerable<KeyValuePair<string, string>>? campos = null, bool conSesion = true, bool conToken = true, bool htmx = false, bool html = false)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta);
        if (conSesion)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", Guid.NewGuid().ToString("D"));
        }

        if (conToken)
        {
            var (cookie, token) = await TokenAntifalsificacion();
            solicitud.Headers.TryAddWithoutValidation("Cookie", cookie);
            solicitud.Headers.TryAddWithoutValidation(ProteccionContraFalsificacion.NombreDeLaCabecera, token);
        }

        if (htmx)
        {
            solicitud.Headers.TryAddWithoutValidation("HX-Request", "true");
        }

        if (html)
        {
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        }

        solicitud.Content = new FormUrlEncodedContent(campos ?? []);
        return await Cliente.SendAsync(solicitud);
    }

    public async Task<HttpResponseMessage> Consultar(string ruta, bool conSesion = true)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (conSesion)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", Guid.NewGuid().ToString("D"));
        }

        return await Cliente.SendAsync(solicitud);
    }

    public static async Task<JsonElement> Leer(HttpResponseMessage respuesta) => (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private sealed class PartesDeTipos(params Type[] tipos) : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "controladores-de-logistica";

        public IEnumerable<TypeInfo> Types => tipos.Select(t => t.GetTypeInfo());
    }
}
