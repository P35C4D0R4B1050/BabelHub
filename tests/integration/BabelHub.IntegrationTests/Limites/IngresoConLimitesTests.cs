using System.Net;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Limites;

/// <summary>
/// El límite de ingresos fallidos (SEG-06) de punta a punta con un host de prueba: el pipeline real de errores y límites, el caso de
/// uso real (<see cref="EvaluarLimite"/>) y el motor real contra Redis, y un endpoint de ingreso doble que hace lo que hará Identidad:
/// reservar el cupo <b>antes</b> de verificar la contraseña y devolverlo si el ingreso es correcto. La parte con <c>Auditoria</c> real
/// (un solo evento por ventana de 5 minutos) es de T27b.
/// </summary>
[Collection(RedisDeContadoresCollection.Nombre)]
public sealed class IngresoConLimitesTests : IAsyncLifetime
{
    private const string ClaveCorrecta = "clave-correcta-de-la-prueba-1";
    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

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

    private readonly RedisDeContadoresFixture _redis;
    private readonly EventosEspia _eventos = new();
    private readonly CalculadorDeHuellaHmac _huellas = new(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella)));
    private readonly HashSet<string> _cuentasExistentes = new(StringComparer.Ordinal);
    private int _verificaciones;
    private WebApplication? _app;
    private HttpClient _cliente = null!;

    public IngresoConLimitesTests(RedisDeContadoresFixture redis)
    {
        _redis = redis;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AgregarLimitesHttp(new OpcionesDeLimitesHttp());
        builder.Services.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas));
        builder.Services.AddSingleton<IReloj>(new RelojDePrueba());
        builder.Services.AddSingleton<IPublicadorDeEventosDeSeguridad>(_eventos);
        builder.Services.AddSingleton<ICalculadorDeHuella>(_huellas);
        builder.Services.AddScoped<IControlDeLimites>(p => new EvaluarLimite(
            p.GetRequiredService<CatalogoDePoliticas>(),
            _redis.Motor(),
            null,
            p.GetRequiredService<IOrigenDeSolicitud>(),
            p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(),
            p.GetRequiredService<IReloj>()));
        _app = builder.Build();

        _app.UsarManejoDeErrores();
        _app.Use(async (contexto, siguiente) =>
        {
            var direccion = contexto.Request.Headers["X-Prueba-Origen"].ToString();
            OrigenDeLaSolicitudHttp.Establecer(contexto, new OrigenDeSolicitud(_huellas.DeOrigen(direccion), direccion));
            await siguiente(contexto);
        });

        // El ingreso doble: reserva el cupo antes de verificar, devuelve el cupo si entra y deja que el fallo cuente.
        _app.MapPost("/ingreso", async (HttpContext contexto, IControlDeLimites control, string cuenta, string clave) =>
        {
            var huella = _huellas.DeSujeto(cuenta, "cuenta-de-ingreso");
            var reserva = await control.Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: huella);
            if (!reserva.EsPermitido)
            {
                await ProblemasHttp.EscribirLimite(contexto, contexto.RequestServices.GetRequiredService<OpcionesDeErrores>(), reserva);
                return;
            }

            Interlocked.Increment(ref _verificaciones);
            await Task.Delay(25); // la verificación real (el resumen de la contraseña) no es instantánea
            bool entro;
            lock (_cuentasExistentes)
            {
                entro = _cuentasExistentes.Contains(cuenta) && clave == ClaveCorrecta;
            }

            if (entro)
            {
                await control.Liberar(reserva.Reserva!);
                contexto.Response.StatusCode = StatusCodes.Status200OK;
                return;
            }

            contexto.Response.StatusCode = StatusCodes.Status401Unauthorized;
        });

        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private static string CuentaNueva() => $"lector-{Guid.NewGuid():N}@correo.pe";

    private static string OrigenNuevo() => $"198.51.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";

    private void Existe(string cuenta)
    {
        lock (_cuentasExistentes)
        {
            _cuentasExistentes.Add(cuenta);
        }
    }

    private Task<HttpResponseMessage> Ingresar(string cuenta, string clave, string origen)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, $"/ingreso?cuenta={Uri.EscapeDataString(cuenta)}&clave={Uri.EscapeDataString(clave)}");
        solicitud.Headers.Add("X-Prueba-Origen", origen);
        return _cliente.SendAsync(solicitud);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) =>
        JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement.Clone();

    private async Task FallarCinco(string cuenta, string origen)
    {
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(cuenta, "clave-equivocada", origen)).StatusCode);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-07")]
    public async Task ConCincoIngresosFallidos_UnaCuentaQueExisteYUnaQueNoRecibenLaMismaRespuestaDeLimite()
    {
        var existente = CuentaNueva();
        var inexistente = CuentaNueva();
        Existe(existente);
        var origen = OrigenNuevo();
        await FallarCinco(existente, origen);
        await FallarCinco(inexistente, origen);

        var deLaExistente = await Ingresar(existente, "clave-equivocada", origen);
        var deLaInexistente = await Ingresar(inexistente, "clave-equivocada", origen);
        var cuerpoA = await Cuerpo(deLaExistente);
        var cuerpoB = await Cuerpo(deLaInexistente);

        Assert.Equal(HttpStatusCode.TooManyRequests, deLaExistente.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, deLaInexistente.StatusCode);
        foreach (var propiedad in new[] { "code", "motivo", "detail", "title", "status", "type" })
        {
            Assert.Equal(cuerpoA.GetProperty(propiedad).ToString(), cuerpoB.GetProperty(propiedad).ToString());
        }

        Assert.Equal("ingresos-fallidos", cuerpoA.GetProperty("motivo").GetString());
        Assert.Equal(
            cuerpoA.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal),
            cuerpoB.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.InRange(Math.Abs(cuerpoA.GetProperty("retryAfterSeconds").GetInt32() - cuerpoB.GetProperty("retryAfterSeconds").GetInt32()), 0, 2);
        Assert.InRange(cuerpoA.GetProperty("retryAfterSeconds").GetInt32(), 890, 900);

        // El rechazo no dice nada de la cuenta: ni el correo ni su huella aparecen en el cuerpo.
        var texto = cuerpoA.GetRawText() + cuerpoB.GetRawText();
        Assert.DoesNotContain("correo.pe", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(_huellas.DeSujeto(existente, "cuenta-de-ingreso").Valor, texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-07")]
    public async Task UnaCombinacionBloqueada_RechazaConLasCredencialesCorrectasSinVerificarlas()
    {
        var cuenta = CuentaNueva();
        Existe(cuenta);
        var origen = OrigenNuevo();
        await FallarCinco(cuenta, origen);
        var verificadasAntes = Volatile.Read(ref _verificaciones);

        var conLaClaveCorrecta = await Ingresar(cuenta, ClaveCorrecta, origen);

        Assert.Equal(HttpStatusCode.TooManyRequests, conLaClaveCorrecta.StatusCode);
        Assert.Equal(verificadasAntes, Volatile.Read(ref _verificaciones));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public async Task ElBloqueoEsDeLaCombinacion_LaMismaCuentaDesdeOtroOrigenSigueAccesible()
    {
        var cuenta = CuentaNueva();
        Existe(cuenta);
        var origen = OrigenNuevo();
        var otroOrigen = OrigenNuevo();
        await FallarCinco(cuenta, origen);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await Ingresar(cuenta, ClaveCorrecta, origen)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Ingresar(cuenta, ClaveCorrecta, otroOrigen)).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public async Task ConTreintaIntentosFallidosEnParalelo_SoloSeVerificanCincoYElRestoSeRechazaAntesDeVerificar()
    {
        var cuenta = CuentaNueva();
        Existe(cuenta);
        var origen = OrigenNuevo();

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => Ingresar(cuenta, "clave-equivocada", origen)));

        Assert.Equal(5, respuestas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized));
        Assert.Equal(25, respuestas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests));
        Assert.Equal(5, Volatile.Read(ref _verificaciones));
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Ingresar(cuenta, ClaveCorrecta, origen)).StatusCode);
        Assert.Equal(5, Volatile.Read(ref _verificaciones));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-06")]
    public async Task ConCuatroFallosYUnIngresoCorrecto_ElExitoNoCuentaComoFalloYElTitularNoQuedaBloqueado()
    {
        var cuenta = CuentaNueva();
        Existe(cuenta);
        var origen = OrigenNuevo();
        for (var i = 0; i < 4; i++)
        {
            await Ingresar(cuenta, "clave-equivocada", origen);
        }

        Assert.Equal(HttpStatusCode.OK, (await Ingresar(cuenta, ClaveCorrecta, origen)).StatusCode);

        // El ingreso correcto devolvió su cupo y no cuenta como fallo: queda margen para el quinto fallo y solo el sexto intento se rechaza.
        Assert.Equal(HttpStatusCode.Unauthorized, (await Ingresar(cuenta, "clave-equivocada", origen)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Ingresar(cuenta, "clave-equivocada", origen)).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task CadaRechazoDelLimiteDeIngresosDejaSuEventoDeSeguridadSinLaCuentaNiElCorreo()
    {
        var cuenta = CuentaNueva();
        var origen = OrigenNuevo();
        await FallarCinco(cuenta, origen);
        lock (_eventos.Eventos)
        {
            _eventos.Eventos.Clear();
        }

        for (var i = 0; i < 3; i++)
        {
            await Ingresar(cuenta, "clave-equivocada", origen);
        }

        EventoDeSeguridad[] eventos;
        lock (_eventos.Eventos)
        {
            eventos = [.. _eventos.Eventos];
        }

        Assert.Equal(3, eventos.Length);
        Assert.All(eventos, e =>
        {
            Assert.Equal(TipoDeEventoDeSeguridad.LimiteExcedido, e.Tipo);
            Assert.Equal("ingresos-fallidos", e.Limite);
            Assert.Equal(origen, e.DireccionCompleta);
            Assert.Null(e.Usuario);
        });
    }
}
