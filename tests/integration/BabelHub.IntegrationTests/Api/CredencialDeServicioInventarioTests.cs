using System.Net;
using System.Security.Claims;
using System.Text.Json;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Api;

public sealed class CredencialDeServicioInventarioTests : IAsyncLifetime
{
    private const string CredencialDeDisponibilidad = "credencial-de-lectura-de-disponibilidad-0123456789";
    private const string CredencialDeUsuario = "credencial-de-operacion-de-usuario-abcdefghij";
    private const string CredencialDeExpiracion = "credencial-de-expiracion-de-reservas-qwertyuiop";
    private const string CredencialDeOrigen = "credencial-de-origen-reenviado-zxcvbnmasdf";
    private const string CredencialAnterior = "credencial-anterior-de-disponibilidad-mnbvcxzlkj";
    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

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

    private sealed class AlertasEspia : IPublicadorDeAlertasTecnicas
    {
        public List<AlertaTecnica> Alertas { get; } = [];

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            lock (Alertas)
            {
                Alertas.Add(alerta);
            }

            return Task.CompletedTask;
        }
    }

    private static readonly PoliticaDeLimite ReservasPorOrigen = new("reservas-por-origen-prueba", 2, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen);

    private readonly TiempoControlable _tiempo = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private readonly EventosEspia _eventos = new();
    private readonly AlertasEspia _alertas = new();
    private readonly CalculadorDeHuellaHmac _huellas = new(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella)));
    private int _ejecuciones;
    private WebApplication? _app;
    private HttpClient _cliente = null!;
    private MatrizDeAlcances _matriz = null!;

    public async Task InitializeAsync()
    {
        _matriz = new MatrizDeAlcances(
        [
            (new OperacionDeServicio("servicio.disponibilidad"), AlcanceDeServicio.LecturaDeDisponibilidad),
            (new OperacionDeServicio("servicio.reservas"), AlcanceDeServicio.OperacionDeUsuario),
            (new OperacionDeServicio("servicio.expirar"), AlcanceDeServicio.ExpiracionDeReservas),
        ]);

        var entrada = new OpcionesDeCredencialesDeEntrada(
        [
            new(AlcanceDeServicio.LecturaDeDisponibilidad, (CredencialDeDisponibilidad, CredencialAnterior)),
            new(AlcanceDeServicio.OperacionDeUsuario, (CredencialDeUsuario, null)),
            new(AlcanceDeServicio.ExpiracionDeReservas, (CredencialDeExpiracion, null)),
            new(AlcanceDeServicio.OrigenReenviado, (CredencialDeOrigen, null)),
        ]);

        var local = new MotorDeLimitesLocal(tiempo: _tiempo);
        var actor = new ActorDelFlujo();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV"));
        builder.Services.AgregarLimitesHttp(new OpcionesDeLimitesHttp());
        builder.Services.AgregarCredencialDeServicio(
            new OpcionesDeSuperficieDeServicio { TopeDeLaDireccionDirecta = PoliticasDeLimite.SolicitudesPorHuellaDirecta },
            entrada);
        builder.Services.AddSingleton<IMatrizDeAlcancesDeServicio>(_matriz);
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<ICalculadorDeHuella>(_huellas);
        builder.Services.AddSingleton<IReloj>(new RelojDePrueba(_tiempo));
        builder.Services.AddSingleton<IPublicadorDeEventosDeSeguridad>(_eventos);
        builder.Services.AddSingleton<IPublicadorDeAlertasTecnicas>(_alertas);
        builder.Services.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales).Append(ReservasPorOrigen)));
        builder.Services.AddScoped<IAutorizadorDeLlamadasDeServicio>(p => new AutorizarLlamadaDeServicio(
            _matriz, p.GetRequiredService<IActorActual>(), p.GetRequiredService<IOrigenDeSolicitud>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
        builder.Services.AddScoped<IResolutorDeOrigenDeServicio>(p => new ResolverOrigenDeLlamadaDeServicio(
            p.GetRequiredService<ICalculadorDeHuella>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
        builder.Services.AddScoped<IControlDeLimites>(p => new EvaluarLimite(
            p.GetRequiredService<CatalogoDePoliticas>(), local, local, p.GetRequiredService<IOrigenDeSolicitud>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
        _app = builder.Build();

        _app.UsarManejoDeErrores();
        _app.Use(async (contexto, siguiente) =>
        {
            contexto.Connection.RemoteIpAddress = IPAddress.Parse(contexto.Request.Headers["X-Prueba-Conexion"].FirstOrDefault() ?? "10.1.0.7");
            if (contexto.Request.Headers["X-Prueba-Http"].Count == 0)
            {
                contexto.Request.Scheme = "https";
            }

            await siguiente(contexto);
        });
        _app.UsarCredencialDeServicio();

        // Autenticación de la prueba: el usuario del token, no una cabecera de actor.
        _app.Use(async (contexto, siguiente) =>
        {
            var usuario = contexto.Request.Headers["X-Prueba-Token"].ToString();
            if (usuario.Length > 0)
            {
                contexto.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, usuario)], "prueba"));
                actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), "cliente"));
            }
            else
            {
                actor.Establecer(ActorActual.Anonimo());
            }

            await siguiente(contexto);
        });
        _app.UsarContextoDeUsuarioDeServicio();

        _app.MapGet("/servicio/disponibilidad", (HttpContext c, IOrigenDeSolicitud o) => Responder(c, o))
            .WithMetadata(new OperacionDeServicioAttribute("servicio.disponibilidad"));
        _app.MapGet("/servicio/reservas", (HttpContext c, IOrigenDeSolicitud o) => Responder(c, o))
            .WithMetadata(new OperacionDeServicioAttribute("servicio.reservas"));
        _app.MapGet("/servicio/expirar", (HttpContext c, IOrigenDeSolicitud o) => Responder(c, o))
            .WithMetadata(new OperacionDeServicioAttribute("servicio.expirar"));
        _app.MapGet("/servicio/sin-declarar", (HttpContext c, IOrigenDeSolicitud o) => Responder(c, o));
        _app.MapGet("/servicio/reservar-por-origen", async (IControlDeLimites control) =>
        {
            var resultado = await control.Reservar(new ClaveDePolitica("reservas-por-origen-prueba"));
            return Results.Json(new { permitido = resultado.EsPermitido, contada = resultado.Reserva is not null });
        }).WithMetadata(new OperacionDeServicioAttribute("servicio.disponibilidad"));
        _app.MapGet("/publico", () => Results.Ok(new { estado = "ok" }));

        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    private IResult Responder(HttpContext contexto, IOrigenDeSolicitud origenes)
    {
        Interlocked.Increment(ref _ejecuciones);
        return Results.Json(new
        {
            direccion = origenes.Actual.Direccion,
            aplicaLimitesPorOrigen = origenes.Actual.AplicaLimitesPorOrigen,
            reenviadoVisible = contexto.Request.Headers.ContainsKey("X-Origen-Reenviado"),
        });
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    private Task<HttpResponseMessage> Llamar(string ruta, params (string Nombre, string Valor)[] cabeceras)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        foreach (var (nombre, valor) in cabeceras)
        {
            solicitud.Headers.TryAddWithoutValidation(nombre, valor);
        }

        return _cliente.SendAsync(solicitud);
    }

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta) => JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;

    private EventoDeSeguridad[] Eventos()
    {
        lock (_eventos.Eventos)
        {
            return [.. _eventos.Eventos];
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task UnaLlamadaSinCredencial_SeRechazaConUn401UniformeYQuedaUnEventoSinEjecutarLaOperacion()
    {
        var respuesta = await Llamar("/servicio/disponibilidad");
        var cuerpo = await Cuerpo(respuesta);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("INV-NO-AUTENTICADO", cuerpo.GetProperty("code").GetString());
        Assert.Equal(0, _ejecuciones);
        var evento = Assert.Single(Eventos());
        Assert.Equal(TipoDeEventoDeSeguridad.LlamadaEntreServiciosSinCredencial, evento.Tipo);
        Assert.Equal("sin-credencial", evento.Motivo);
        Assert.Equal("10.1.0.7", evento.DireccionCompleta);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-14")]
    [InlineData("credencial-inventada-que-no-existe-en-ningun-alcance")]
    [InlineData("")]
    public async Task UnaCredencialDesconocida_SeRechazaIgualQueSiNoHubieraCredencial(string credencial)
    {
        var respuesta = await Llamar("/servicio/disponibilidad", ("X-Credencial-De-Servicio", credencial));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("sin-credencial", Assert.Single(Eventos()).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task UnaCredencialDeOtroAlcance_SeRechazaEnLaOperacionDeEsteAlcance()
    {
        var respuesta = await Llamar("/servicio/disponibilidad", ("X-Credencial-De-Servicio", CredencialDeExpiracion));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("otro-alcance", Assert.Single(Eventos()).Motivo);
        Assert.Equal(0, _ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task UnaCredencialRepetidaEnLaCabecera_SeTrataComoAusente()
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/servicio/disponibilidad");
        solicitud.Headers.TryAddWithoutValidation("X-Credencial-De-Servicio", CredencialDeDisponibilidad);
        solicitud.Headers.TryAddWithoutValidation("X-Credencial-De-Servicio", CredencialDeDisponibilidad);

        var respuesta = await _cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task UnaOperacionDeLaSuperficieSinDeclararSuAlcance_SeRechazaPorDefectoConCualquierCredencial()
    {
        var respuesta = await Llamar("/servicio/sin-declarar", ("X-Credencial-De-Servicio", CredencialDeDisponibilidad));

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Equal("operacion-no-declarada", Assert.Single(Eventos()).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task UnaLlamadaConLaCredencialDelAlcanceCorrecto_SeEjecuta()
    {
        var respuesta = await Llamar("/servicio/disponibilidad", ("X-Credencial-De-Servicio", CredencialDeDisponibilidad), ("X-Origen-Reenviado", "203.0.113.7"), ("X-Credencial-De-Origen", CredencialDeOrigen));

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(1, _ejecuciones);
        Assert.Empty(Eventos());
    }

    [Fact]
    public async Task UnEndpointFueraDeLaSuperficie_NoPideCredencial()
    {
        Assert.Equal(HttpStatusCode.OK, (await Llamar("/publico")).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task UnaLlamadaPorHttpEnVezDeHttps_SeRechaza()
    {
        var respuesta = await Llamar("/servicio/disponibilidad", ("X-Credencial-De-Servicio", CredencialDeDisponibilidad), ("X-Prueba-Http", "1"));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Equal(0, _ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task UnaOperacionDeUsuarioSinElContextoDeUnUsuario_SeRechazaDespuesDeAutenticarYUnaCabeceraDeActorNoLoReemplaza()
    {
        var sinUsuario = await Llamar("/servicio/reservas", ("X-Credencial-De-Servicio", CredencialDeUsuario), ("X-Actor", "administrador-central"), ("X-Origen-Reenviado", "203.0.113.7"), ("X-Credencial-De-Origen", CredencialDeOrigen));
        var conUsuario = await Llamar("/servicio/reservas", ("X-Credencial-De-Servicio", CredencialDeUsuario), ("X-Prueba-Token", "usuario-1"), ("X-Origen-Reenviado", "203.0.113.7"), ("X-Credencial-De-Origen", CredencialDeOrigen));

        Assert.Equal(HttpStatusCode.Unauthorized, sinUsuario.StatusCode);
        Assert.Equal("sin-contexto-de-usuario", Assert.Single(Eventos()).Motivo);
        Assert.Equal(HttpStatusCode.OK, conUsuario.StatusCode);
        Assert.Equal(1, _ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-14")]
    public async Task DuranteUnaRotacion_LaCredencialAnteriorSeAceptaYSuUsoQuedaAlertadoUnaVezPorMinuto()
    {
        var primera = await Llamar("/servicio/disponibilidad", ("X-Credencial-De-Servicio", CredencialAnterior));
        var segunda = await Llamar("/servicio/disponibilidad", ("X-Credencial-De-Servicio", CredencialAnterior));
        var actual = await Llamar("/servicio/disponibilidad", ("X-Credencial-De-Servicio", CredencialDeDisponibilidad));

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal(HttpStatusCode.OK, actual.StatusCode);
        lock (_alertas.Alertas)
        {
            Assert.Equal(CredencialDeServicioMiddleware.MotivoDeAlertaDeCredencialAnterior, Assert.Single(_alertas.Alertas).Motivo);
        }

        _tiempo.Ahora += TimeSpan.FromSeconds(61);
        await Llamar("/servicio/disponibilidad", ("X-Credencial-De-Servicio", CredencialAnterior));
        lock (_alertas.Alertas)
        {
            Assert.Equal(2, _alertas.Alertas.Count);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    [Trait("Criterio", "AC-SEG-20")]
    public async Task UnaLlamadaDeClienteSinDireccionReenviada_NoUsaLaDireccionDelEcommerceSeCuentaEnElOrigenDesconocidoYDejaElEvento()
    {
        var cuerpoDeLaLlamada = await Cuerpo(await Llamar(
            "/servicio/disponibilidad",
            ("X-Credencial-De-Servicio", CredencialDeDisponibilidad)));

        Assert.Equal(JsonValueKind.Null, cuerpoDeLaLlamada.GetProperty("direccion").ValueKind);
        Assert.True(cuerpoDeLaLlamada.GetProperty("aplicaLimitesPorOrigen").GetBoolean());
        var evento = Assert.Single(Eventos());
        Assert.Equal(TipoDeEventoDeSeguridad.OrigenIndeterminable, evento.Tipo);
        Assert.Equal("origen-no-determinable", evento.Motivo);
        Assert.Null(evento.DireccionCompleta);

        // Los límites por origen siguen aplicando, con un contador compartido del origen desconocido: la tercera llamada excede el máximo de 2 (SEG-23).
        var permitidas = new List<bool>();
        for (var i = 0; i < 4; i++)
        {
            var reserva = await Cuerpo(await Llamar("/servicio/reservar-por-origen", ("X-Credencial-De-Servicio", CredencialDeDisponibilidad), ("X-Prueba-Conexion", $"10.1.0.{20 + i}")));
            permitidas.Add(reserva.GetProperty("permitido").GetBoolean());
        }

        Assert.Equal([true, true, false, false], permitidas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task UnaLlamadaDeSistemaSinDireccionReenviada_SigueUsandoLaDireccionDeLaConexionSinEvento()
    {
        var cuerpoDeLaLlamada = await Cuerpo(await Llamar("/servicio/expirar", ("X-Credencial-De-Servicio", CredencialDeExpiracion)));

        Assert.Equal("10.1.0.7", cuerpoDeLaLlamada.GetProperty("direccion").GetString());
        Assert.Empty(Eventos());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-15")]
    public async Task ConLaDireccionReenviadaYLaCredencialDeOrigen_LaLlamadaSeCuentaConLaDireccionDelCliente()
    {
        var respuesta = await Llamar(
            "/servicio/disponibilidad",
            ("X-Credencial-De-Servicio", CredencialDeDisponibilidad),
            ("X-Origen-Reenviado", "203.0.113.7"),
            ("X-Credencial-De-Origen", CredencialDeOrigen));
        var cuerpo = await Cuerpo(respuesta);

        Assert.Equal("203.0.113.7", cuerpo.GetProperty("direccion").GetString());
        Assert.True(cuerpo.GetProperty("aplicaLimitesPorOrigen").GetBoolean());
        Assert.False(cuerpo.GetProperty("reenviadoVisible").GetBoolean());
        Assert.Empty(Eventos());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-29")]
    public async Task ConLaDireccionReenviadaSinLaCredencialDeOrigen_SeIgnoraSeRegistraYNoRigenLosLimitesPorOrigen()
    {
        var cuerpoDeLaLlamada = await Cuerpo(await Llamar(
            "/servicio/disponibilidad",
            ("X-Credencial-De-Servicio", CredencialDeDisponibilidad),
            ("X-Origen-Reenviado", "203.0.113.7")));

        Assert.Equal("10.1.0.7", cuerpoDeLaLlamada.GetProperty("direccion").GetString());
        Assert.False(cuerpoDeLaLlamada.GetProperty("aplicaLimitesPorOrigen").GetBoolean());
        var evento = Assert.Single(Eventos());
        Assert.Equal(TipoDeEventoDeSeguridad.OrigenReenviadoSinCredencial, evento.Tipo);
        Assert.Null(evento.DireccionCompleta);

        // La política por origen (el equivalente de SEL-19) no cuenta estas llamadas: siguen pasando aunque excedan su máximo de 2.
        for (var i = 0; i < 5; i++)
        {
            var reserva = await Cuerpo(await Llamar(
                "/servicio/reservar-por-origen",
                ("X-Credencial-De-Servicio", CredencialDeDisponibilidad),
                ("X-Origen-Reenviado", "203.0.113.7")));
            Assert.True(reserva.GetProperty("permitido").GetBoolean());
            Assert.False(reserva.GetProperty("contada").GetBoolean());
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-29")]
    public async Task ConLaDireccionReenviadaIgnorada_SeAplicaElTopeGeneralDeLaDireccionDirectaYOtroClienteConCredencialDeOrigenNoSeAfecta()
    {
        for (var i = 0; i < 120; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Llamar(
                "/servicio/disponibilidad",
                ("X-Credencial-De-Servicio", CredencialDeDisponibilidad),
                ("X-Origen-Reenviado", "203.0.113.7"))).StatusCode);
        }

        var rechazada = await Llamar(
            "/servicio/disponibilidad",
            ("X-Credencial-De-Servicio", CredencialDeDisponibilidad),
            ("X-Origen-Reenviado", "203.0.113.7"));
        var otroCliente = await Llamar(
            "/servicio/disponibilidad",
            ("X-Credencial-De-Servicio", CredencialDeDisponibilidad),
            ("X-Origen-Reenviado", "198.51.100.9"),
            ("X-Credencial-De-Origen", CredencialDeOrigen));

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("solicitudes-por-huella-directa", (await Cuerpo(rechazada)).GetProperty("motivo").GetString());
        Assert.Equal(HttpStatusCode.OK, otroCliente.StatusCode);
    }

    [Fact]
    public async Task LaVerificacionDeLaSuperficie_DetectaLosEndpointsSinOperacionDeclaradaYLasOperacionesFueraDeLaMatriz()
    {
        var fuente = _app!.Services.GetRequiredService<EndpointDataSource>();
        var opciones = _app.Services.GetRequiredService<OpcionesDeSuperficieDeServicio>();

        var problemas = SuperficieDeServicio.Verificar(fuente, _matriz, opciones);
        var sinOperacionExtra = SuperficieDeServicio.Verificar(fuente, new MatrizDeAlcances([]), opciones);

        Assert.Equal(["/servicio/sin-declarar: no declara su operación de servicio"], problemas);
        Assert.Contains(sinOperacionExtra, p => p.Contains("no está en la matriz", StringComparison.Ordinal));
    }
}
