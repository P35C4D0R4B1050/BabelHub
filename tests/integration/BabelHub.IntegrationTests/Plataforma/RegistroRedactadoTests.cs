using System.Net;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class RegistroRedactadoTests
{
    private const string Contrasena = "Sup3rSecreta-2026";
    private const string Tarjeta = "4111 1111 1111 1111";
    private const string Firma = "a1b2c3d4e5f60718";
    private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjMifQ.firma123";
    private const string Credencial = "credencial-de-servicio-0123456789-abcdef";
    private const string Codigo = "482913";

    private static readonly string[] Prohibidos = [Contrasena, "4111 1111 1111 1111", Firma, "eyJhbGci", Credencial, Codigo, "abcdefghijklmnop12345"];

    private sealed record Entrada(LogLevel Nivel, EventId Evento, string Texto, string Campos, string? Excepcion);

    private sealed class ProveedorCapturador : ILoggerProvider
    {
        public List<Entrada> Entradas { get; } = [];

        public List<string> Alcances { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturador(this);

        public void Dispose()
        {
        }

        private sealed class Capturador(ProveedorCapturador dueno) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                dueno.Alcances.Add(state is IEnumerable<KeyValuePair<string, object?>> pares
                    ? state + "|" + string.Join(",", pares.Select(p => $"{p.Key}={p.Value}"))
                    : state.ToString() ?? string.Empty);
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var campos = state is IEnumerable<KeyValuePair<string, object?>> pares ? string.Join(",", pares.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
                dueno.Entradas.Add(new Entrada(logLevel, eventId, formatter(state, exception), campos, exception?.ToString()));
            }
        }
    }

    private static (ILogger<RegistroRedactadoTests> Registro, ProveedorCapturador Capturador, ServiceProvider Proveedor) Armar(bool redactar = true)
    {
        var capturador = new ProveedorCapturador();
        var servicios = new ServiceCollection();
        servicios.AddLogging(b => b.AddProvider(capturador).SetMinimumLevel(LogLevel.Trace));
        if (redactar)
        {
            servicios.UsarRedaccionDeRegistros();
        }

        var proveedor = servicios.BuildServiceProvider();
        return (proveedor.GetRequiredService<ILogger<RegistroRedactadoTests>>(), capturador, proveedor);
    }

    private static void SinProhibidos(string? texto)
    {
        foreach (var prohibido in Prohibidos)
        {
            Assert.DoesNotContain(prohibido, texto ?? string.Empty, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public void SinLaRedaccion_UnMensajeConDatosProhibidosSiLosDejaPasar()
    {
        var (registro, capturador, _) = Armar(redactar: false);

        registro.LogInformation("clave password={Clave}", Contrasena);

        Assert.Contains(Contrasena, Assert.Single(capturador.Entradas).Texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public void Registrar_ConDatosProhibidosEnElMensaje_NoLosEntregaAlRegistroReal()
    {
        var (registro, capturador, _) = Armar();

        registro.LogWarning("Falló el ingreso con password={Clave} y tarjeta {Tarjeta}", Contrasena, Tarjeta);
        registro.LogError("Enlace https://tienda.pe/descargas/7?firma={Firma}&expira=1763035200", Firma);
        registro.LogInformation("Authorization: Bearer abcdefghijklmnop12345 y {Jwt}", Jwt);
        registro.LogInformation("Llamada con X-Credencial-De-Servicio: {Credencial}", Credencial);
        registro.LogInformation("código de uso único codigo={Codigo}", Codigo);

        Assert.Equal(5, capturador.Entradas.Count);
        Assert.All(capturador.Entradas, e =>
        {
            SinProhibidos(e.Texto);
            SinProhibidos(e.Campos);
            Assert.Contains("[redactado]", e.Texto, StringComparison.Ordinal);
        });
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public void Registrar_ConCamposEstructuradosDeNombreSensible_OcultaSuValorYLosDemasPasan()
    {
        var (registro, capturador, _) = Armar();

        registro.LogInformation("Solicitud {Ruta} con {Authorization} y {Cookie} para {PedidoId}", "/reservas", "Bearer token-secreto-xyz", "sid=abc123", 42);

        var entrada = Assert.Single(capturador.Entradas);
        Assert.Contains("Authorization=[redactado]", entrada.Campos, StringComparison.Ordinal);
        Assert.Contains("Cookie=[redactado]", entrada.Campos, StringComparison.Ordinal);
        Assert.Contains("Ruta=/reservas", entrada.Campos, StringComparison.Ordinal);
        Assert.Contains("PedidoId=42", entrada.Campos, StringComparison.Ordinal);
        Assert.DoesNotContain("token-secreto-xyz", entrada.Texto + entrada.Campos, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public void Registrar_ConUnaExcepcionQueLlevaDatosProhibidos_LaEntregaConSuMensajeRedactado()
    {
        var (registro, capturador, _) = Armar();
        var excepcion = new InvalidOperationException($"No se pudo conectar con password={Contrasena} a la base");

        registro.LogError(excepcion, "Falló la operación");

        var entrada = Assert.Single(capturador.Entradas);
        SinProhibidos(entrada.Excepcion);
        Assert.Contains("InvalidOperationException", entrada.Excepcion, StringComparison.Ordinal);
        Assert.Contains("No se pudo conectar", entrada.Excepcion, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public void BeginScope_ConDatosProhibidos_LosRedacta()
    {
        var (registro, capturador, _) = Armar();

        using (registro.BeginScope("sesión sid={Sid}", "4f9a2c1d8e7b6a5f"))
        using (registro.BeginScope(new Dictionary<string, object?> { ["Authorization"] = "Bearer abcdefghijklmnop12345", ["Pedido"] = 7 }))
        {
            registro.LogInformation("dentro del alcance");
        }

        Assert.Equal(2, capturador.Alcances.Count);
        Assert.All(capturador.Alcances, a =>
        {
            Assert.DoesNotContain("4f9a2c1d8e7b6a5f", a, StringComparison.Ordinal);
            Assert.DoesNotContain("abcdefghijklmnop12345", a, StringComparison.Ordinal);
        });
        Assert.Contains("Pedido=7", capturador.Alcances[1], StringComparison.Ordinal);
    }

    [Fact]
    public void Registrar_ConUnMensajeCorriente_LoDejaIgualYConservaNivelEventoYHabilitacion()
    {
        var (registro, capturador, _) = Armar();

        registro.Log(LogLevel.Information, new EventId(42, "reserva"), "Reserva {Id} creada para el cliente {Cliente}", 9, "c-7");

        var entrada = Assert.Single(capturador.Entradas);
        Assert.Equal("Reserva 9 creada para el cliente c-7", entrada.Texto);
        Assert.Equal(LogLevel.Information, entrada.Nivel);
        Assert.Equal(42, entrada.Evento.Id);
        Assert.True(registro.IsEnabled(LogLevel.Debug));
    }

    [Fact]
    public void Registrar_ConUnNivelDeshabilitado_NoLlamaAlRegistroReal()
    {
        var capturador = new ProveedorCapturador();
        var servicios = new ServiceCollection();
        servicios.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        servicios.UsarRedaccionDeRegistros();
        var registro = servicios.BuildServiceProvider().GetRequiredService<ILogger<RegistroRedactadoTests>>();

        registro.LogInformation("no debe salir");

        Assert.False(registro.IsEnabled(LogLevel.Information));
        Assert.Empty(capturador.Entradas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public async Task ElRegistroDeUnClienteHttp_ConUnSecretoEnLaUrl_NoLoDejaPasar()
    {
        var capturador = new ProveedorCapturador();
        var servicios = new ServiceCollection();
        servicios.AddLogging(b => b.AddProvider(capturador).SetMinimumLevel(LogLevel.Trace));
        servicios.UsarRedaccionDeRegistros();
        servicios.AddHttpClient("externo").ConfigurePrimaryHttpMessageHandler(() => new RespuestaOk());
        var cliente = servicios.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient("externo");

        await cliente.GetAsync($"https://proveedor.pe/consulta?token={Credencial}&firma={Firma}&modo=lectura");

        Assert.NotEmpty(capturador.Entradas);
        Assert.All(capturador.Entradas, e =>
        {
            SinProhibidos(e.Texto);
            SinProhibidos(e.Campos);
        });
    }

    [Fact]
    public void ExcepcionRedactada_ConservaElTipoYLaPilaSinElValor()
    {
        var original = new HttpRequestException($"fallo en https://x.pe/?token={Credencial}");

        var redactada = new ExcepcionRedactada(original);

        Assert.Equal(typeof(HttpRequestException).FullName, redactada.TipoOriginal);
        Assert.DoesNotContain(Credencial, redactada.ToString(), StringComparison.Ordinal);
        Assert.Contains("fallo en", redactada.Message, StringComparison.Ordinal);
    }

    private sealed class RespuestaOk : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
