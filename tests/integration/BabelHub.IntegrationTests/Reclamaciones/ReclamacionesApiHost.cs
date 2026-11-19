using System.Net.Http.Json;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Carrito;
using BabelHub.IntegrationTests.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Reclamaciones;

/// <summary>Un <c>IReclamaciones</c> que anota la entrada y responde lo que la prueba le pida.</summary>
public sealed class ReclamacionesFalso : IReclamaciones
{
    public List<RegistrarHojaEntrada> Entradas { get; } = [];

    public Resultado<ConstanciaDeHoja> Respuesta { get; set; } = Resultado<ConstanciaDeHoja>.Exito(new ConstanciaDeHoja(
        "2026-000001",
        new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero),
        "reclamo",
        "Cien años de soledad",
        59.90m,
        "Llegó dañado",
        "PED-1",
        "Ana Pérez",
        "12345678",
        "Jr. Lima 123",
        "ana@example.com",
        15,
        new DateOnly(2026, 12, 4)));

    public Task<Resultado<ConstanciaDeHoja>> Registrar(RegistrarHojaEntrada entrada, CancellationToken cancelacion = default)
    {
        Entradas.Add(entrada);
        return Task.FromResult(Respuesta);
    }
}

/// <summary>Un <c>IHojasDelConsumidor</c> que anota la llamada y responde lo que la prueba le pida.</summary>
public sealed class HojasDelConsumidorFalso : IHojasDelConsumidor
{
    public List<string> Llamadas { get; } = [];

    public Resultado<PaginaDeMisHojas> Pagina { get; set; } = Resultado<PaginaDeMisHojas>.Exito(new PaginaDeMisHojas([new MiHoja("2026-000001", new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero), "reclamo", EstadoDeLaHoja.Pendiente, null, null)], 1, 20, 1));

    public Resultado<MiHoja> Hoja { get; set; } = Resultado<MiHoja>.Exito(new MiHoja("2026-000001", new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero), "queja", EstadoDeLaHoja.Respondido, "Gracias por avisarnos.", new DateTimeOffset(2026, 11, 20, 15, 0, 0, TimeSpan.Zero)));

    public Task<Resultado<PaginaDeMisHojas>> Listar(ListarMisHojasEntrada entrada, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"listar:{entrada.Pagina}:{entrada.Tamano}");
        return Task.FromResult(Pagina);
    }

    public Task<Resultado<MiHoja>> Obtener(string? numero, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"obtener:{numero}");
        return Task.FromResult(Hoja);
    }
}

/// <summary>La Presentación del Libro de Reclamaciones del consumidor en proceso (T15), con las vistas reales y los contratos del módulo doblados.</summary>
internal sealed class ReclamacionesApiHost : IAsyncDisposable
{
    private AnfitrionWeb _anfitrion = null!;
    private (string Cookie, string Token)? _token;

    public HttpClient Cliente { get; private set; } = null!;

    public ReclamacionesFalso Reclamaciones { get; } = new();

    public HojasDelConsumidorFalso Consumidor { get; } = new();

    public AdministracionDeReclamacionesFalso Administracion { get; } = new();

    public static async Task<ReclamacionesApiHost> Crear()
    {
        var host = new ReclamacionesApiHost();
        await host.Iniciar();
        return host;
    }

    private sealed class ListaVacia : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([], true));
    }

    private async Task Iniciar()
    {
        var actor = new ActorDelFlujo();
        _anfitrion = await AnfitrionWeb.Iniciar(
            servicios =>
            {
                servicios.AddSingleton<IActorActual>(actor);
                servicios.AddSingleton<IEstablecedorDeActor>(actor);
                servicios.AddSingleton<IReclamaciones>(Reclamaciones);
                servicios.AddSingleton<IHojasDelConsumidor>(Consumidor);
                servicios.AddSingleton<IAdministracionDeReclamaciones>(Administracion);
                servicios.AddSingleton<IRegistradorDeDenegacionesHttp>(new DenegacionesEspia());
                servicios.AddSingleton<IListaDeSucursales>(new ListaVacia());
                servicios.AddSingleton(new OpcionesDeVerificacionWeb("clave-de-sitio-de-prueba"));
                servicios.AddSingleton(new OpcionesDelHostWeb(HostWeb.Tienda, []));
                servicios.AgregarProteccionContraFalsificacion();
            },
            despuesDelEnrutado: app => app.MapGet("/prueba/token", (HttpContext contexto, IAntiforgery antiforgery) => Results.Text(antiforgery.GetAndStoreTokens(contexto).RequestToken)));
        Cliente = _anfitrion.Cliente;
    }

    public async ValueTask DisposeAsync()
    {
        if (_anfitrion is not null)
        {
            await _anfitrion.DisposeAsync();
        }
    }

    public HttpRequestMessage Pedir(string ruta, bool html = true)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (html)
        {
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        }

        return solicitud;
    }

    public async Task<HttpResponseMessage> Enviar(IEnumerable<KeyValuePair<string, string>> campos, bool html = true, bool conToken = true, string ruta = "/reclamaciones/nueva")
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta);
        if (conToken)
        {
            if (_token is null)
            {
                using var respuestaDelToken = await Cliente.GetAsync("/prueba/token");
                var cookie = respuestaDelToken.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal)).Split(';')[0];
                _token = (cookie, await respuestaDelToken.Content.ReadAsStringAsync());
            }

            solicitud.Headers.TryAddWithoutValidation("Cookie", _token.Value.Cookie);
            solicitud.Headers.TryAddWithoutValidation(ProteccionContraFalsificacion.NombreDeLaCabecera, _token.Value.Token);
        }

        if (html)
        {
            solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        }

        solicitud.Content = new FormUrlEncodedContent(campos);
        return await Cliente.SendAsync(solicitud);
    }

    public static async Task<JsonElement> Leer(HttpResponseMessage respuesta) => (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).Clone();
}

/// <summary>Un <c>IAdministracionDeReclamaciones</c> que anota la llamada y responde lo que la prueba le pida.</summary>
public sealed class AdministracionDeReclamacionesFalso : IAdministracionDeReclamaciones
{
    public List<string> Llamadas { get; } = [];

    public List<ResponderHojaEntrada> Respuestas { get; } = [];

    public Resultado<PaginaDeHojas> Pagina { get; set; } = Resultado<PaginaDeHojas>.Exito(new PaginaDeHojas([new ResumenDeHoja(Guid.Parse("a0000000-0000-0000-0000-000000000001"), "2026-000001", new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero), "reclamo", EstadoDeLaHoja.Pendiente, null)], 1, 20, 1));

    public Resultado<DetalleDeHoja> Detalle { get; set; } = Resultado<DetalleDeHoja>.Exito(new DetalleDeHoja(
        Guid.Parse("a0000000-0000-0000-0000-000000000001"),
        "2026-000001",
        new DateTimeOffset(2026, 11, 19, 15, 0, 0, TimeSpan.Zero),
        "reclamo",
        EstadoDeLaHoja.Pendiente,
        "Ana Pérez",
        "12345678",
        "Jr. Lima 123",
        "ana@example.com",
        "Libro",
        59.90m,
        "<script>alert('x')</script> Llegó dañado",
        null,
        null,
        null,
        null,
        false,
        false,
        new DateOnly(2026, 12, 4)));

    public Resultado<ResultadoDeLaHoja> Operacion { get; set; } = Resultado<ResultadoDeLaHoja>.Exito(new ResultadoDeLaHoja("2026-000001", EstadoDeLaHoja.Respondido, AvisoDeLaRespuesta.Pendiente));

    public Task<Resultado<PaginaDeHojas>> Listar(ListarHojasEntrada entrada, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"listar:{entrada.Estado}:{entrada.Pagina}:{entrada.Tamano}");
        return Task.FromResult(Pagina);
    }

    public Task<Resultado<DetalleDeHoja>> Obtener(Guid hojaId, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"obtener:{hojaId:D}");
        return Task.FromResult(Detalle);
    }

    public Task<Resultado<ResultadoDeLaHoja>> Responder(ResponderHojaEntrada entrada, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"responder:{entrada.HojaId:D}");
        Respuestas.Add(entrada);
        return Task.FromResult(Operacion);
    }

    public Task<Resultado<ResultadoDeLaHoja>> Descartar(Guid hojaId, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"descartar:{hojaId:D}");
        return Task.FromResult(Operacion);
    }

    public Task<Resultado<ResultadoDeLaHoja>> ReintentarAviso(Guid hojaId, CancellationToken cancelacion = default)
    {
        Llamadas.Add($"reintentar:{hojaId:D}");
        return Task.FromResult(Operacion);
    }
}
