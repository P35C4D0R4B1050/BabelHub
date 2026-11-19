using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
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

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>Un <c>ICarritoDelCliente</c> que anota cada llamada y responde lo que la prueba le pida: sirve para probar la Presentación sin la Aplicación.</summary>
public sealed class CarritoDelClienteFalso : ICarritoDelCliente
{
    private static readonly Resultado<ResultadoDeCarrito> Vacio = Resultado<ResultadoDeCarrito>.Exito(new ResultadoDeCarrito(new VistaDelCarrito([], 0m, [], false), null, false));

    public List<string> Llamadas { get; } = [];

    public List<object> Entradas { get; } = [];

    public Resultado<ResultadoDeCarrito> Respuesta { get; set; } = Vacio;

    public Resultado<ResultadoDeFusionDeCarrito>? RespuestaDeFusion { get; set; }

    public bool LaFusionLanza { get; set; }

    public Task<Resultado<ResultadoDeCarrito>> Obtener(SolicitudSobreElCarrito solicitud, CancellationToken cancelacion = default) => Anotar("obtener", solicitud);

    public Task<Resultado<ResultadoDeCarrito>> AgregarLinea(AgregarLineaEntrada entrada, CancellationToken cancelacion = default) => Anotar("agregar", entrada);

    public Task<Resultado<ResultadoDeCarrito>> ModificarLinea(ModificarLineaEntrada entrada, CancellationToken cancelacion = default) => Anotar("cambiar", entrada);

    public Task<Resultado<ResultadoDeCarrito>> EliminarLinea(EliminarLineaEntrada entrada, CancellationToken cancelacion = default) => Anotar("quitar", entrada);

    public Task<Resultado<ResultadoDeFusionDeCarrito>> FusionarCarritoDeVisitante(string cookieDelVisitante, CancellationToken cancelacion = default)
    {
        Llamadas.Add("fusionar");
        if (LaFusionLanza)
        {
            throw new InvalidOperationException("La fusión falló.");
        }

        return Task.FromResult(RespuestaDeFusion ?? Resultado<ResultadoDeFusionDeCarrito>.Exito(new ResultadoDeFusionDeCarrito(new VistaDelCarrito([], 0m, [], false), [], false, false)));
    }

    public Task<Resultado<ResultadoDeCarrito>> MarcarAvisosComoVistos(IReadOnlyCollection<Guid> avisoIds, CancellationToken cancelacion = default) => Anotar("avisos", avisoIds);

    private Task<Resultado<ResultadoDeCarrito>> Anotar(string llamada, object entrada)
    {
        Llamadas.Add(llamada);
        Entradas.Add(entrada);
        return Task.FromResult(Respuesta);
    }
}

/// <summary>
/// El carrito en proceso para probar la <b>Presentación</b> (T27): MVC con solo el <c>CarritoController</c>, el manejo de errores, el filtro global antifalsificación y un registrador de denegaciones espía, con un <c>ICarritoDelCliente</c> falso. El actor lo
/// fija un middleware de prueba con cabeceras. La Aplicación y PostgreSQL se prueban aparte (T30 a T32).
/// </summary>
public sealed class CarritoApiHost : IAsyncDisposable
{
    public static readonly Guid UsuarioPorOmision = Guid.Parse("c0000000-0000-0000-0000-000000000001");

    private WebApplication _app = null!;
    private (string Cookie, string Token)? _token;

    public HttpClient Cliente { get; private set; } = null!;

    public CarritoDelClienteFalso Carrito { get; } = new();

    public DenegacionesEspia Denegaciones { get; } = new();

    public static async Task<CarritoApiHost> Crear()
    {
        var host = new CarritoApiHost();
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
        builder.Services.AddSingleton<ICarritoDelCliente>(Carrito);
        builder.Services.AddSingleton<IRegistradorDeDenegacionesHttp>(Denegaciones);
        builder.Services.AddSingleton(new OpcionesDelHostWeb(HostWeb.Tienda, []));
        builder.Services.AgregarProteccionContraFalsificacion();
        builder.Services.AddSingleton(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton<ContextoDePagina>(sp => new ContextoDePagina(sp.GetRequiredService<IHttpContextAccessor>(), DispatchProxy.Create<IPreferenciaDeSucursal, DobleQueLanza>()));
        builder.Services
            .AddControllers()
            .ConfigureApplicationPartManager(gestor =>
            {
                gestor.ApplicationParts.Clear();
                gestor.ApplicationParts.Add(new PartesDeTipos(typeof(CarritoController)));
            });

        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.Use(async (contexto, siguiente) =>
        {
            if (Guid.TryParse(contexto.Request.Headers["X-Prueba-Usuario"], out var usuario))
            {
                actor.Establecer(ActorActual.DeUsuario(usuario, RolesDeUsuario.Cliente, null));
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

    /// <summary>
    /// Un <c>POST</c> con el token antifalsificación (salvo que se pida sin él), la sesión de un cliente si se indica y, si hay cookie de visitante, la cookie del carrito. El cuerpo es un formulario con los campos dados.
    /// </summary>
    public async Task<HttpResponseMessage> Enviar(
        string ruta,
        IEnumerable<KeyValuePair<string, string>>? campos = null,
        bool conSesion = false,
        string? cookieDelCarrito = null,
        bool conToken = true,
        bool htmx = false,
        HttpContent? cuerpo = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta);
        var cookies = new List<string>();
        if (conSesion)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", UsuarioPorOmision.ToString("D"));
        }

        if (conToken)
        {
            var (cookie, token) = await TokenAntifalsificacion();
            cookies.Add(cookie);
            solicitud.Headers.TryAddWithoutValidation(ProteccionContraFalsificacion.NombreDeLaCabecera, token);
        }

        if (cookieDelCarrito is not null)
        {
            cookies.Add($"{CookieDelCarritoNombre}={cookieDelCarrito}");
        }

        if (cookies.Count > 0)
        {
            solicitud.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", cookies));
        }

        if (htmx)
        {
            solicitud.Headers.TryAddWithoutValidation("HX-Request", "true");
        }

        solicitud.Content = cuerpo ?? new FormUrlEncodedContent(campos ?? []);
        return await Cliente.SendAsync(solicitud);
    }

    public const string CookieDelCarritoNombre = "__Host-bh-carrito";

    public static async Task<JsonElement> Leer(HttpResponseMessage respuesta) => (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    /// <summary>Un doble que lanza en toda llamada: la prueba de la Presentación no llega a pintar pantallas.</summary>
    public class DobleQueLanza : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos) => throw new NotSupportedException();
    }

    private sealed class PartesDeTipos(params Type[] tipos) : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "controladores-del-carrito";

        public IEnumerable<TypeInfo> Types => tipos.Select(t => t.GetTypeInfo());
    }
}

/// <summary>Anota las denegaciones que la Presentación registra (un acceso con un identificador que no se admite de afuera).</summary>
public sealed class DenegacionesEspia : IRegistradorDeDenegacionesHttp
{
    public List<(CategoriaDeError Categoria, string? Motivo)> Registradas { get; } = [];

    public Task Registrar(HttpContext contexto, CategoriaDeError categoria, string? motivo, string? codigoDeError)
    {
        Registradas.Add((categoria, motivo));
        return Task.CompletedTask;
    }
}
