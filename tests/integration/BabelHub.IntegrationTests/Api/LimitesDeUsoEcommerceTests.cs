using System.Net;
using System.Security.Claims;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Api;

public sealed class LimitesDeUsoEcommerceTests : IAsyncLifetime
{
    private sealed class TiempoControlable(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private sealed class RelojDePrueba(TiempoControlable tiempo) : IReloj
    {
        public DateTimeOffset Ahora => tiempo.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class EventosEspia : IPublicadorDeEventosDeSeguridad
    {
        public List<EventoDeSeguridad> Eventos { get; } = [];

        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default)
        {
            lock (Eventos)
            {
                Eventos.Add(evento);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class MotorCaido : IMotorDeLimites
    {
        public Task<ResultadoDeLimite> Reservar(PoliticaDeLimite politica, string contador, string? claveDelLlamador, ClaveDeIdempotencia? idempotencia, CancellationToken cancelacion) =>
            throw new MotorDeLimitesNoDisponibleException();

        public Task Liberar(PoliticaDeLimite politica, ReservaDeLimite reserva, CancellationToken cancelacion) => throw new MotorDeLimitesNoDisponibleException();

        public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion) => throw new MotorDeLimitesNoDisponibleException();

        public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion) => throw new MotorDeLimitesNoDisponibleException();

        public Task<int> RegistrarRechazoDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion) => throw new MotorDeLimitesNoDisponibleException();

        public Task<int> ContarRechazosDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion) => throw new MotorDeLimitesNoDisponibleException();
    }

    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private readonly TiempoControlable _tiempo = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly EventosEspia _eventos = new();
    private readonly CalculadorDeHuellaHmac _huellas = new(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella)));
    private int _autenticaciones;
    private WebApplication? _app;
    private HttpClient _cliente = null!;

    public Task InitializeAsync() => Iniciar(conAlmacenCaido: false);

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private async Task Iniciar(bool conAlmacenCaido)
    {
        if (_app is not null)
        {
            _cliente.Dispose();
            await _app.DisposeAsync();
        }

        var local = new MotorDeLimitesLocal(tiempo: _tiempo);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AgregarLimitesHttp(new OpcionesDeLimitesHttp
        {
            TopePrevio = PoliticasDeLimite.SolicitudesPreviasPorOrigen,
            TopePrevioDelOrigenDesconocido = PoliticasDeLimite.SolicitudesDelOrigenDesconocido,
            SinSesionPorOrigen = PoliticasDeLimite.SolicitudesSinSesionPorOrigen,
            PorUsuario = PoliticasDeLimite.SolicitudesPorUsuario,
        });
        builder.Services.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales)));
        builder.Services.AddSingleton<IReloj>(new RelojDePrueba(_tiempo));
        builder.Services.AddSingleton<IPublicadorDeEventosDeSeguridad>(_eventos);
        builder.Services.AddScoped<IControlDeLimites>(proveedor => new EvaluarLimite(
            proveedor.GetRequiredService<CatalogoDePoliticas>(),
            conAlmacenCaido ? new MotorCaido() : local,
            local,
            proveedor.GetRequiredService<IOrigenDeSolicitud>(),
            proveedor.GetRequiredService<IPublicadorDeEventosDeSeguridad>(),
            proveedor.GetRequiredService<IReloj>()));
        _app = builder.Build();

        _app.UsarManejoDeErrores();

        // Origen de la prueba: lo que luego fija OrigenDelBorde (T24).
        _app.Use(async (contexto, siguiente) =>
        {
            var direccion = contexto.Request.Headers["X-Prueba-Origen"].ToString();
            if (direccion.Length > 0)
            {
                OrigenDeLaSolicitudHttp.Establecer(contexto, new OrigenDeSolicitud(_huellas.DeOrigen(direccion), direccion));
            }

            await siguiente(contexto);
        });
        _app.UsarTopePrevio();

        // Autenticación de la prueba: cuenta cuántas veces se ejecuta (cada ejecución sería una búsqueda de sesión).
        _app.Use(async (contexto, siguiente) =>
        {
            Interlocked.Increment(ref _autenticaciones);
            var usuario = contexto.Request.Headers["X-Prueba-Usuario"].ToString();
            if (usuario.Length > 0)
            {
                contexto.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, usuario)], "prueba"));
            }

            await siguiente(contexto);
        });
        _app.UsarLimitesGenerales();

        _app.MapGet("/catalogo", () => Results.Ok(new { estado = "ok" }));
        _app.MapGet("/health/live", () => Results.Ok(new { estado = "vivo" }));

        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    private async Task<HttpResponseMessage> Pedir(string ruta = "/catalogo", string? origen = null, string? usuario = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (origen is not null)
        {
            solicitud.Headers.Add("X-Prueba-Origen", origen);
        }

        if (usuario is not null)
        {
            solicitud.Headers.Add("X-Prueba-Usuario", usuario);
        }

        return await _cliente.SendAsync(solicitud);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) => JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public async Task UnUsuarioAutenticadoCon100SolicitudesEnElUltimoMinuto_RecibeLaRespuestaDeLimiteYAlPasarElTiempoSugeridoVuelveAFuncionar()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir(usuario: "u-1")).StatusCode);
            _tiempo.Ahora += TimeSpan.FromMilliseconds(500);
        }

        var rechazada = await Pedir(usuario: "u-1");
        var cuerpo = await Cuerpo(rechazada);

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("ECO-LIMITE-EXCEDIDO", cuerpo.GetProperty("code").GetString());
        Assert.Equal("solicitudes-por-usuario", cuerpo.GetProperty("motivo").GetString());
        var espera = cuerpo.GetProperty("retryAfterSeconds").GetInt32();
        Assert.Equal(espera.ToString(System.Globalization.CultureInfo.InvariantCulture), rechazada.Headers.GetValues("Retry-After").Single());

        _tiempo.Ahora += TimeSpan.FromSeconds(espera);
        Assert.Equal(HttpStatusCode.OK, (await Pedir(usuario: "u-1")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Pedir(usuario: "u-2")).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-16")]
    public async Task UnVisitanteCon60SolicitudesEnElUltimoMinuto_RecibeLaRespuestaDeLimiteYOtroOrigenNoSeAfecta()
    {
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir(origen: "203.0.113.7")).StatusCode);
        }

        var rechazada = await Pedir(origen: "203.0.113.7");

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("solicitudes-sin-sesion-por-origen", (await Cuerpo(rechazada)).GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.OK, (await Pedir(origen: "198.51.100.9")).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-16")]
    public async Task LosUsuariosDeUnaRedCompartida_NoSePerjudicanEntreSiNiAUnVisitanteDeEsaRed()
    {
        // 50 personas con sesión detrás de la misma dirección (una red universitaria), 10 solicitudes cada una.
        for (var usuario = 0; usuario < 50; usuario++)
        {
            for (var i = 0; i < 10; i++)
            {
                Assert.Equal(HttpStatusCode.OK, (await Pedir(origen: "203.0.113.7", usuario: $"u-{usuario}")).StatusCode);
            }
        }

        // Sus solicitudes con sesión no cuentan en el límite del origen sin sesión: un visitante de la misma red sigue pudiendo navegar.
        Assert.Equal(HttpStatusCode.OK, (await Pedir(origen: "203.0.113.7")).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public async Task ElTopePrevioPorHuella_RechazaLaSolicitud601DelOrigenAntesDeAutenticar()
    {
        for (var i = 0; i < 600; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir(origen: "203.0.113.7", usuario: $"u-{i % 300}")).StatusCode);
        }

        var autenticacionesAntes = Volatile.Read(ref _autenticaciones);
        var rechazada = await Pedir(origen: "203.0.113.7", usuario: "u-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("solicitudes-previas-por-origen", (await Cuerpo(rechazada)).GetProperty("motivo").GetString());
        Assert.Equal(autenticacionesAntes, Volatile.Read(ref _autenticaciones));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-20")]
    public async Task ElOrigenDesconocido_TieneUnTopePreviaPropioMasAltoYComparteElContadorDeSinSesion()
    {
        // Sin origen determinable: todos caen en el mismo bucket, con un tope previo más alto que el de un origen conocido.
        for (var i = 0; i < 700; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir(usuario: $"u-{i % 400}")).StatusCode);
        }

        // Y las solicitudes sin sesión de todos ellos suman en el mismo contador de «origen desconocido».
        for (var i = 0; i < 60; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir()).StatusCode);
        }

        var rechazada = await Pedir();

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("solicitudes-sin-sesion-por-origen", (await Cuerpo(rechazada)).GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task LaSalud_NoSeLimitaAunqueSeaMuyFrecuente()
    {
        for (var i = 0; i < 800; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir("/health/live", origen: "203.0.113.7")).StatusCode);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task UnLimiteExcedido_DejaUnEventoDeSeguridadConElOrigenYLaPolitica()
    {
        for (var i = 0; i < 61; i++)
        {
            await Pedir(origen: "203.0.113.7");
        }

        EventoDeSeguridad evento;
        lock (_eventos.Eventos)
        {
            evento = Assert.Single(_eventos.Eventos);
        }

        Assert.Equal(TipoDeEventoDeSeguridad.LimiteExcedido, evento.Tipo);
        Assert.Equal("solicitudes-sin-sesion-por-origen", evento.Limite);
        Assert.Equal("203.0.113.7", evento.DireccionCompleta);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task ConElAlmacenDeContadoresCaido_LosLimitesGeneralesSiguenAplicandoseConElLimiteLocal()
    {
        await Iniciar(conAlmacenCaido: true);

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir(usuario: "u-1")).StatusCode);
        }

        var rechazada = await Pedir(usuario: "u-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("solicitudes-por-usuario", (await Cuerpo(rechazada)).GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task UnaSolicitudDentroDeLosLimites_PasaSinCambios()
    {
        var respuesta = await Pedir(origen: "203.0.113.7", usuario: "u-1");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("ok", (await Cuerpo(respuesta)).GetProperty("estado").GetString());
        Assert.True(respuesta.Headers.Contains("X-Trace-Id"));
        Assert.False(respuesta.Headers.Contains("Retry-After"));
    }
}
