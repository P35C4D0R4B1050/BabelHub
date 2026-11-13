using System.Net;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Api;

public sealed class ManejoDeErroresEcommerceTests : IAsyncLifetime
{
    private const string Prefijo = "ECO";
    private const string SecretoSembrado = "Sup3rSecreta-2026";

    private sealed class ProveedorCapturador : ILoggerProvider
    {
        public List<string> Mensajes { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturador(this);

        public void Dispose()
        {
        }

        private sealed class Capturador(ProveedorCapturador dueno) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                lock (dueno.Mensajes)
                {
                    dueno.Mensajes.Add(formatter(state, exception) + " " + exception);
                }
            }
        }
    }

    private readonly ProveedorCapturador _registros = new();
    private WebApplication? _app;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(_registros);
        builder.Services.UsarRedaccionDeRegistros();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores(
            Prefijo,
            new Dictionary<string, MensajeEspecifico>
            {
                ["stock-insuficiente"] = new("ECO-STOCK-INSUFICIENTE", "No hay stock para completar el pedido."),
            }));
        _app = builder.Build();
        _app.UsarManejoDeErrores();

        _app.MapGet("/validacion", (HttpContext c) => ProblemasHttp.Escribir(
            c,
            ProblemasHttp.Crear(
                c,
                c.RequestServices.GetRequiredService<OpcionesDeErrores>(),
                new ErrorDeAplicacion(CategoriaDeError.Validacion, "validacion-fallida", [new ErrorDeCampo("cantidad", "fuera-de-rango")]))));
        _app.MapGet("/regla", () => { throw new ExcepcionDeNegocio("stock-insuficiente"); });
        _app.MapGet("/regla-sin-catalogo", () => { throw new ExcepcionDeNegocio("otra-regla"); });
        _app.MapGet("/inesperado", () => { throw new InvalidOperationException($"falló con password={SecretoSembrado} en C:\\Servidor\\Datos"); });
        _app.MapGet("/sin-actor", () => { throw new ContextoDeActorAusenteException(); });
        _app.MapGet("/almacen-caido", () => { throw new MotorDeLimitesNoDisponibleException(); });
        _app.MapGet("/no-autenticado", () => Results.Unauthorized());
        _app.MapGet("/prohibido", () => Results.StatusCode(403));
        _app.MapGet("/conflicto", () => Results.StatusCode(409));
        _app.MapGet("/ok", () => Results.Ok(new { estado = "ok" }));
        _app.MapGet("/limite/{motivo}", (HttpContext c, string motivo, int espera) => ProblemasHttp.EscribirLimite(
            c,
            c.RequestServices.GetRequiredService<OpcionesDeErrores>(),
            ResultadoDeLimite.Excedido(motivo, espera, DateTimeOffset.UtcNow.AddSeconds(espera))));
        _app.MapGet("/limite-sin-almacen", (HttpContext c) => ProblemasHttp.EscribirLimite(
            c, c.RequestServices.GetRequiredService<OpcionesDeErrores>(), ResultadoDeLimite.NoDisponible()));

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

    private static async Task<JsonElement> Cuerpo(HttpResponseMessage respuesta)
    {
        var texto = await respuesta.Content.ReadAsStringAsync();
        return JsonDocument.Parse(texto).RootElement;
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task UnaSolicitudInvalida_TraeCodigoEstableIdentificadorYLosCamposSinTrazas()
    {
        var respuesta = await _cliente.GetAsync("/validacion");
        var cuerpo = await Cuerpo(respuesta);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType!.MediaType);
        Assert.Equal("ECO-VALIDACION-INVALIDA", cuerpo.GetProperty("code").GetString());
        Assert.Equal(respuesta.Headers.GetValues("X-Trace-Id").Single(), cuerpo.GetProperty("traceId").GetString());
        Assert.Equal("cantidad", cuerpo.GetProperty("errores")[0].GetProperty("campo").GetString());
        Assert.Equal("fuera-de-rango", cuerpo.GetProperty("errores")[0].GetProperty("clave").GetString());
        Assert.Equal(400, cuerpo.GetProperty("status").GetInt32());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task UnaReglaDeNegocioIncumplida_IndicaElMotivoConSuCodigoEspecifico()
    {
        var respuesta = await _cliente.GetAsync("/regla");
        var cuerpo = await Cuerpo(respuesta);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal("ECO-STOCK-INSUFICIENTE", cuerpo.GetProperty("code").GetString());
        Assert.Equal("No hay stock para completar el pedido.", cuerpo.GetProperty("detail").GetString());
        Assert.False(string.IsNullOrEmpty(cuerpo.GetProperty("traceId").GetString()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task UnaReglaDeNegocioSinCodigoEspecifico_UsaElCodigoGenericoDeReglaDeNegocio()
    {
        var cuerpo = await Cuerpo(await _cliente.GetAsync("/regla-sin-catalogo"));

        Assert.Equal("ECO-REGLA-DE-NEGOCIO-INCUMPLIDA", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task UnErrorInesperado_ResponderUnMensajeGenericoSinTrazasNiDatosInternos()
    {
        var respuesta = await _cliente.GetAsync("/inesperado");
        var texto = await respuesta.Content.ReadAsStringAsync();
        var cuerpo = JsonDocument.Parse(texto).RootElement;

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.Equal("ECO-ERROR-INESPERADO", cuerpo.GetProperty("code").GetString());
        Assert.Equal("Ocurrió un error inesperado. Inténtalo de nuevo más tarde.", cuerpo.GetProperty("detail").GetString());
        Assert.DoesNotContain(SecretoSembrado, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("InvalidOperationException", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Servidor", texto, StringComparison.Ordinal);
        Assert.DoesNotContain(" at ", texto, StringComparison.Ordinal);
        Assert.Equal(0, cuerpo.GetProperty("errores").GetArrayLength());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public async Task UnErrorInesperado_SeRegistraConSuExcepcionPeroSinElDatoProhibido()
    {
        await _cliente.GetAsync("/inesperado");

        string mensajes;
        lock (_registros.Mensajes)
        {
            mensajes = string.Join("\n", _registros.Mensajes);
        }

        Assert.Contains("Error inesperado en GET /inesperado", mensajes, StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", mensajes, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretoSembrado, mensajes, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-03")]
    [InlineData("/sin-actor", HttpStatusCode.Unauthorized, "ECO-NO-AUTENTICADO")]
    [InlineData("/almacen-caido", HttpStatusCode.ServiceUnavailable, "ECO-SERVICIO-NO-DISPONIBLE")]
    [InlineData("/no-autenticado", HttpStatusCode.Unauthorized, "ECO-NO-AUTENTICADO")]
    [InlineData("/prohibido", HttpStatusCode.Forbidden, "ECO-PROHIBIDO")]
    [InlineData("/conflicto", HttpStatusCode.Conflict, "ECO-CONFLICTO")]
    [InlineData("/no-existe", HttpStatusCode.NotFound, "ECO-NO-ENCONTRADO")]
    public async Task LosDemasErroresYLosEstadosDelMarco_TambienSalenComoElErrorUniforme(string ruta, HttpStatusCode estado, string codigo)
    {
        var respuesta = await _cliente.GetAsync(ruta);
        var cuerpo = await Cuerpo(respuesta);

        Assert.Equal(estado, respuesta.StatusCode);
        Assert.Equal("application/problem+json", respuesta.Content.Headers.ContentType!.MediaType);
        Assert.Equal(codigo, cuerpo.GetProperty("code").GetString());
        Assert.False(string.IsNullOrEmpty(cuerpo.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task UnaSolicitudSinErrores_NoSeAlteraYTraeElIdentificadorDeSeguimiento()
    {
        var respuesta = await _cliente.GetAsync("/ok");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal("ok", (await Cuerpo(respuesta)).GetProperty("estado").GetString());
        Assert.NotEmpty(respuesta.Headers.GetValues("X-Trace-Id").Single());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task ElIdentificadorDeCorrelacionDelCliente_NoReemplazaAlDeSeguimientoYCienSolicitudesSonCienDistintas()
    {
        var ids = new HashSet<string>();
        for (var i = 0; i < 100; i++)
        {
            using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/inesperado");
            solicitud.Headers.Add("X-Correlacion-Id", "mismo-id-del-cliente");
            var respuesta = await _cliente.SendAsync(solicitud);
            ids.Add((await Cuerpo(respuesta)).GetProperty("traceId").GetString()!);
        }

        Assert.Equal(100, ids.Count);
        Assert.DoesNotContain("mismo-id-del-cliente", ids);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-08")]
    [InlineData("descargas-por-usuario", 30)]
    [InlineData("asistente-por-usuario", 12)]
    [InlineData("reservas-por-cliente", 3600)]
    [InlineData("ingresos-fallidos", 900)]
    public async Task UnLimiteExcedido_TieneSiempreElMismoFormatoConRetryAfterYMotivoSinDatosDeCuentas(string motivo, int espera)
    {
        var respuesta = await _cliente.GetAsync($"/limite/{motivo}?espera={espera}");
        var texto = await respuesta.Content.ReadAsStringAsync();
        var cuerpo = JsonDocument.Parse(texto).RootElement;

        Assert.Equal(HttpStatusCode.TooManyRequests, respuesta.StatusCode);
        Assert.Equal(espera.ToString(System.Globalization.CultureInfo.InvariantCulture), respuesta.Headers.GetValues("Retry-After").Single());
        Assert.Equal("ECO-LIMITE-EXCEDIDO", cuerpo.GetProperty("code").GetString());
        Assert.Equal(motivo, cuerpo.GetProperty("motivo").GetString());
        Assert.Equal(espera, cuerpo.GetProperty("retryAfterSeconds").GetInt32());
        Assert.True(cuerpo.GetProperty("disponibleDesde").GetDateTimeOffset() > DateTimeOffset.UtcNow);
        Assert.Equal(
            ["code", "detail", "disponibleDesde", "errores", "motivo", "retryAfterSeconds", "status", "title", "traceId", "type"],
            cuerpo.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-07")]
    public async Task ElLimiteDeIngresos_EsIdenticoExistaONoLaCuenta()
    {
        var existente = await Cuerpo(await _cliente.GetAsync("/limite/ingresos-fallidos?espera=900"));
        var inexistente = await Cuerpo(await _cliente.GetAsync("/limite/ingresos-fallidos?espera=900"));

        Assert.Equal(existente.GetProperty("code").GetString(), inexistente.GetProperty("code").GetString());
        Assert.Equal(existente.GetProperty("motivo").GetString(), inexistente.GetProperty("motivo").GetString());
        Assert.Equal(existente.GetProperty("retryAfterSeconds").GetInt32(), inexistente.GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal(existente.GetProperty("detail").GetString(), inexistente.GetProperty("detail").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-24")]
    public async Task ConElAlmacenDeContadoresCaido_ElLimiteResponderServicioNoDisponible()
    {
        var respuesta = await _cliente.GetAsync("/limite-sin-almacen");
        var cuerpo = await Cuerpo(respuesta);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("ECO-SERVICIO-NO-DISPONIBLE", cuerpo.GetProperty("code").GetString());
        Assert.False(respuesta.Headers.Contains("Retry-After"));
    }

    [Theory]
    [InlineData(CategoriaDeError.Validacion, 400, "VALIDACION-INVALIDA")]
    [InlineData(CategoriaDeError.NoAutenticado, 401, "NO-AUTENTICADO")]
    [InlineData(CategoriaDeError.Prohibido, 403, "PROHIBIDO")]
    [InlineData(CategoriaDeError.NoEncontrado, 404, "NO-ENCONTRADO")]
    [InlineData(CategoriaDeError.Conflicto, 409, "CONFLICTO")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, 422, "REGLA-DE-NEGOCIO-INCUMPLIDA")]
    [InlineData(CategoriaDeError.Limite, 429, "LIMITE-EXCEDIDO")]
    [InlineData(CategoriaDeError.Inesperado, 500, "ERROR-INESPERADO")]
    [InlineData(CategoriaDeError.NoDisponible, 503, "SERVICIO-NO-DISPONIBLE")]
    public void ElMapeoDeCategorias_DaElEstadoYElSufijoDeLaTabla(CategoriaDeError categoria, int estado, string sufijo)
    {
        Assert.Equal(estado, ProblemasHttp.EstadoDe(categoria));
        Assert.Equal(sufijo, ProblemasHttp.SufijoDe(categoria));
        Assert.False(string.IsNullOrWhiteSpace(ProblemasHttp.TituloDe(categoria)));
    }

    [Fact]
    public void ElMapeoDeCategorias_CubreTodasLasCategorias()
    {
        foreach (var categoria in Enum.GetValues<CategoriaDeError>())
        {
            Assert.InRange(ProblemasHttp.EstadoDe(categoria), 400, 599);
            Assert.NotEmpty(ProblemasHttp.SufijoDe(categoria));
        }

    }
}
