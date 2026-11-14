using System.Text;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Registros;

/// <summary>
/// SEG-10 y SEG-11 de punta a punta en el registro: se siembran en la solicitud las cabeceras sensibles y, en una excepción y en
/// mensajes, contraseñas, credenciales, identificadores de sesión, códigos, tarjetas y enlaces firmados, con el registro HTTP del
/// marco configurado al máximo (la peor configuración posible), y se revisa todo lo que llega al registro real y todo lo que se
/// responde. Una prueba de control sin la redacción comprueba que la revisión sí detecta una fuga.
/// </summary>
public sealed class RegistrosSinSecretosEcommerceTests
{
    private static readonly (string Cabecera, string Valor)[] CabecerasSembradas =
    [
        ("Authorization", "Bearer SEMILLA-bearer-4f8a1c9d2b7e"),
        ("Cookie", "babelhub.sesion=SEMILLA-cookie-9a8b7c6d5e4f"),
        ("X-Credencial-De-Servicio", "SEMILLA-credencial-servicio-1a2b3c4d5e6f"),
        ("X-Credencial-De-Origen", "SEMILLA-credencial-origen-6f5e4d3c2b1a"),
        ("X-Verificacion-De-Persona", "SEMILLA-verificacion-0z9y8x7w6v5u"),
        ("X-Origen-Reenviado", "SEMILLA-origen-203.0.113.250"),
        ("X-Firma-Del-Webhook", "SEMILLA-firma-webhook-aabbccddeeff0011"),
    ];

    private static readonly string[] ValoresSembradosEnTexto =
    [
        "SEMILLA-contrasena-Zq8Lw2Xv5N",
        "SEMILLA-codigo-482913",
        "SEMILLA-sesion-id-77aa66bb55cc",
        "SEMILLA-enlace-firmado-9f8e7d6c5b4a",
        "SEMILLA-secreto-2fa-JBSWY3DPEHPK3PXP",
        "SEMILLA-clave-de-firma-0f1e2d3c4b5a",
        "4111 1111 1111 1111",
    ];

    private sealed class ProveedorCapturador : ILoggerProvider
    {
        private readonly List<string> _entradas = [];

        public string Todo
        {
            get
            {
                lock (_entradas)
                {
                    return string.Join("\n", _entradas);
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Capturador(this);

        public void Dispose()
        {
        }

        private sealed class Capturador(ProveedorCapturador dueno) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                dueno.Agregar(Aplanar(state));
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                dueno.Agregar(formatter(state, exception) + " " + Aplanar(state) + " " + exception);
            }

            private static string Aplanar<TState>(TState state)
            {
                if (state is IEnumerable<KeyValuePair<string, object?>> campos)
                {
                    return string.Join(' ', campos.Select(c => $"{c.Key}={c.Value}"));
                }

                return state?.ToString() ?? string.Empty;
            }
        }

        private void Agregar(string texto)
        {
            lock (_entradas)
            {
                _entradas.Add(texto);
            }
        }
    }

    private static string TextoConDatosProhibidos() => string.Join(
        ' ',
        $"password={ValoresSembradosEnTexto[0]}",
        $"codigo={ValoresSembradosEnTexto[1]}",
        $"sid={ValoresSembradosEnTexto[2]}",
        $"https://almacen.babelhub.pe/libro.epub?signature={ValoresSembradosEnTexto[3]}",
        $"secreto={ValoresSembradosEnTexto[4]}",
        $"signing-key={ValoresSembradosEnTexto[5]}",
        $"tarjeta {ValoresSembradosEnTexto[6]}");

    private static async Task<(WebApplication App, HttpClient Cliente, ProveedorCapturador Registros)> Iniciar(bool conRedaccion)
    {
        var registros = new ProveedorCapturador();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(registros);
        if (conRedaccion)
        {
            builder.Services.UsarRedaccionDeRegistros();
        }

        // La peor configuración: el registro HTTP del marco con todas las cabeceras sembradas permitidas y el cuerpo.
        builder.Services.AddHttpLogging(opciones =>
        {
            opciones.LoggingFields = HttpLoggingFields.All;
            foreach (var (cabecera, _) in CabecerasSembradas)
            {
                opciones.RequestHeaders.Add(cabecera);
                opciones.ResponseHeaders.Add(cabecera);
            }
        });
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        var app = builder.Build();
        app.UseHttpLogging();
        app.UsarManejoDeErrores();

        app.MapPost("/eco", async (HttpContext c) =>
        {
            // Una Api que devuelve cabeceras o cuerpo recibidos sería una fuga aparte; aquí responde sin eco.
            using var lector = new StreamReader(c.Request.Body);
            _ = await lector.ReadToEndAsync();
            return Results.Ok(new { estado = "ok" });
        });
        app.MapGet("/inesperado", () => { throw new InvalidOperationException("falló: " + TextoConDatosProhibidos()); });
        app.MapGet("/mensaje", (ILoggerFactory fabrica) =>
        {
            var registro = fabrica.CreateLogger("Pruebas.Mensaje");
            registro.LogInformation("Descarga de un cliente: " + TextoConDatosProhibidos());
            registro.LogWarning("Contexto {Contrasena} {Token} {Sesion}", ValoresSembradosEnTexto[0], ValoresSembradosEnTexto[3], ValoresSembradosEnTexto[2]);
            using (registro.BeginScope("alcance " + TextoConDatosProhibidos()))
            {
                registro.LogError(new InvalidOperationException(TextoConDatosProhibidos()), "Falla con excepción");
            }

            return Results.Ok(new { estado = "ok" });
        });

        await app.StartAsync();
        return (app, app.GetTestClient(), registros);
    }

    private static HttpRequestMessage Solicitud(HttpMethod metodo, string ruta)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta);
        foreach (var (cabecera, valor) in CabecerasSembradas)
        {
            solicitud.Headers.TryAddWithoutValidation(cabecera, valor);
        }

        if (metodo == HttpMethod.Post)
        {
            solicitud.Content = new StringContent($"{{\"contrasena\":\"{ValoresSembradosEnTexto[0]}\",\"codigo\":\"{ValoresSembradosEnTexto[1]}\"}}", Encoding.UTF8, "application/json");
        }

        return solicitud;
    }

    private static IEnumerable<string> TodosLosSembrados() => CabecerasSembradas.Select(c => c.Valor).Concat(ValoresSembradosEnTexto);

    private static async Task<string> Texto(HttpResponseMessage respuesta)
    {
        var cabeceras = string.Join('\n', respuesta.Headers.Concat(respuesta.Content.Headers).Select(c => $"{c.Key}: {string.Join(",", c.Value)}"));
        return cabeceras + "\n" + await respuesta.Content.ReadAsStringAsync();
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-10")]
    [Trait("Criterio", "AC-SEG-25")]
    [InlineData("POST", "/eco")]
    [InlineData("GET", "/inesperado")]
    [InlineData("GET", "/mensaje")]
    [InlineData("GET", "/no-existe")]
    public async Task ConLasCabecerasYLosDatosProhibidosSembrados_NiElRegistroNiLaRespuestaLosContienen(string metodo, string ruta)
    {
        var (app, cliente, registros) = await Iniciar(conRedaccion: true);
        await using var _ = app;
        using var __ = cliente;

        using var solicitud = Solicitud(new HttpMethod(metodo), ruta);
        using var respuesta = await cliente.SendAsync(solicitud);
        var registrado = registros.Todo;
        var respondido = await Texto(respuesta);

        // Control positivo: el registro y la respuesta existen y llevan lo esperado (la prueba no pasa por estar vacía).
        Assert.Contains($"Path: {ruta}", registrado, StringComparison.Ordinal);
        foreach (var sembrado in TodosLosSembrados())
        {
            Assert.DoesNotContain(sembrado, registrado, StringComparison.Ordinal);
            Assert.DoesNotContain(sembrado, respondido, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-10")]
    public async Task ElRegistroHttpDelMarco_MuestraLosNombresDeLasCabecerasPeroConSuValorRedactado()
    {
        var (app, cliente, registros) = await Iniciar(conRedaccion: true);
        await using var _ = app;
        using var __ = cliente;

        using var solicitud = Solicitud(HttpMethod.Post, "/eco");
        using var respuesta = await cliente.SendAsync(solicitud);
        var registrado = registros.Todo;

        Assert.Equal(System.Net.HttpStatusCode.OK, respuesta.StatusCode);
        foreach (var (cabecera, _) in CabecerasSembradas)
        {
            Assert.Contains(cabecera, registrado, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("[redactado]", registrado, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SinLaRedaccion_LaMismaRevisionDetectaLaFuga()
    {
        var (app, cliente, registros) = await Iniciar(conRedaccion: false);
        await using var _ = app;
        using var __ = cliente;

        using var solicitud = Solicitud(HttpMethod.Get, "/mensaje");
        using var respuesta = await cliente.SendAsync(solicitud);
        var registrado = registros.Todo;

        Assert.Contains(TodosLosSembrados(), s => registrado.Contains(s, StringComparison.Ordinal));
    }
}
