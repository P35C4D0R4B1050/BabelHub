using System.Collections.Concurrent;
using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ZXing;
using ZXing.Common;
using ZXing.QrCode;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El código QR del segundo factor (web-acceso ACC-13 a ACC-16): lo que se dibuja en la respuesta lo lee un lector <b>distinto</b> del que lo generó y dice exactamente la dirección del enlace; viaja dentro de la página, sin caché ni
/// recursos externos; y ni el código ni la clave llegan a un registro, tampoco cuando no se puede generar.
/// </summary>
public sealed class CodigoQrDelSegundoFactorTests
{
    private const string Clave = "JBSWY3DPEHPK3PXP";

    private const string DireccionEsperada = "otpauth://totp/BabelHub:encargada%40babelhub.example?secret=" + Clave + "&issuer=BabelHub";

    private static readonly string Desafio = new('d', 43);

    private static string CookieDelDesafio => $"{BabelHub.Ecommerce.Api.Web.CookieDelDesafio.Nombre}={Desafio}";

    private static readonly TimeSpan Plazo = TimeSpan.FromSeconds(2);

    // ---------- El lector independiente ----------

    /// <summary>Dibuja a píxeles el SVG de la respuesta (solo rectángulos de una fila, que es lo que escribe la vista) y lo lee con ZXing. Devuelve el texto leído y el margen claro mínimo, en módulos.</summary>
    internal static (string Texto, int Margen, int Lienzo) LeerElQr(string html)
    {
        var svg = Regex.Match(html, "<svg[^>]*data-bh-qr[^>]*>.*?</svg>", RegexOptions.Singleline, Plazo);
        Assert.True(svg.Success, "La página no trae el código QR.");
        var lienzo = int.Parse(Regex.Match(svg.Value, "viewBox=\"0 0 (\\d+) \\1\"", RegexOptions.None, Plazo).Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var trazo = Regex.Match(svg.Value, "<path class=\"bh-qr-tinta\" d=\"([^\"]*)\"", RegexOptions.None, Plazo).Groups[1].Value;
        const string Rectangulo = "M(\\d+) (\\d+)h(\\d+)v1h-\\3z";
        Assert.Matches($"^(?:{Rectangulo.Replace("\\3", "\\d+", StringComparison.Ordinal)})+$", trazo);

        const int Escala = 4;
        var lado = lienzo * Escala;
        var pixeles = new byte[lado * lado];
        Array.Fill(pixeles, byte.MaxValue);
        var margen = lienzo;
        foreach (Match tramo in Regex.Matches(trazo, Rectangulo, RegexOptions.None, Plazo))
        {
            var (x, y, ancho) = (Numero(tramo, 1), Numero(tramo, 2), Numero(tramo, 3));
            margen = new[] { margen, x, y, lienzo - (x + ancho), lienzo - (y + 1) }.Min();
            for (var fila = y * Escala; fila < (y + 1) * Escala; fila++)
            {
                Array.Fill(pixeles, (byte)0, (fila * lado) + (x * Escala), ancho * Escala);
            }
        }

        var imagen = new BinaryBitmap(new HybridBinarizer(new RGBLuminanceSource(pixeles, lado, lado, RGBLuminanceSource.BitmapFormat.Gray8)));
        var leido = new QRCodeReader().decode(imagen);
        Assert.NotNull(leido);
        return (leido.Text, margen, lienzo);
    }

    private static int Numero(Match tramo, int grupo) => int.Parse(tramo.Groups[grupo].Value, System.Globalization.CultureInfo.InvariantCulture);

    private static string Enlace(string html) => WebUtility.HtmlDecode(Regex.Match(html, "href=\"(otpauth:[^\"]*)\"", RegexOptions.None, Plazo).Groups[1].Value);

    // ---------- El adaptador ----------

    [Fact]
    [Trait("Criterio", "AC-ACC-12")]
    public void ElAdaptador_GeneraUnCodigoCuadrado_QueNoSeImprime()
    {
        var registro = new Registros();
        using var fabrica = LoggerFactory.Create(b => b.AddProvider(registro));
        var generador = new CodecreteGeneradorDeCodigoQr(fabrica.CreateLogger<CodecreteGeneradorDeCodigoQr>());

        var codigo = generador.Generar(new ValorSecreto(DireccionEsperada));

        Assert.NotNull(codigo);
        Assert.InRange(codigo.Lado, 21, 177);
        Assert.Equal("[código QR]", codigo.ToString());
        Assert.Empty(registro.Lineas);
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-15")]
    public void ElAdaptador_SiNoPuedeGenerarlo_DevuelveNulo_YElRegistroNoDiceElContenidoNiElDetalle()
    {
        var registro = new Registros();
        using var fabrica = LoggerFactory.Create(b => b.AddProvider(registro));
        var generador = new CodecreteGeneradorDeCodigoQr(fabrica.CreateLogger<CodecreteGeneradorDeCodigoQr>());
        var demasiadoLargo = Clave + new string('x', 5000);

        var codigo = generador.Generar(new ValorSecreto(demasiadoLargo));

        Assert.Null(codigo);
        var linea = Assert.Single(registro.Lineas);
        Assert.Equal("No se pudo generar el código QR del segundo factor; la pantalla muestra solo la clave.", linea);
    }

    // ---------- La pantalla de configuración del ingreso ----------

    private static async Task<(AccesoApiHost Host, Registros Registro)> Ingreso(IGeneradorDeCodigoQr? generador = null)
    {
        var registro = new Registros();
        var host = await AccesoApiHost.Crear(extra: servicios =>
        {
            servicios.AddSingleton<ILoggerProvider>(registro);
            if (generador is not null)
            {
                servicios.AddSingleton(generador);
            }
        });
        host.Ingreso.Paso = PasoPendienteDelIngreso.ConfigurarSegundoFactor;
        return (host, registro);
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-12")]
    [Trait("Criterio", "AC-ACC-14")]
    public async Task EnElIngreso_ElCodigoLeidoDiceExactamenteLaDireccionDelEnlace_YLaClaveSigueEnTexto()
    {
        var (host, _) = await Ingreso();
        await using var cerrar = host;

        using var respuesta = await host.Cliente.SendAsync(host.Pedir("/ingreso", CookieDelDesafio));

        var html = await respuesta.Content.ReadAsStringAsync();
        var (texto, margen, lienzo) = LeerElQr(html);
        Assert.Equal(DireccionEsperada, texto);
        Assert.Equal(texto, Enlace(html));
        Assert.True(margen >= 4, $"El margen claro es de {margen} módulos.");
        Assert.True(lienzo > 8);
        Assert.Contains($"data-bh-secreto>{Clave}</p>", html, StringComparison.Ordinal);
        var svg = Regex.Match(html, "<svg[^>]*data-bh-qr[^>]*>", RegexOptions.None, Plazo).Value;
        Assert.Contains("role=\"img\"", svg, StringComparison.Ordinal);
        Assert.Contains("aria-label=\"", svg, StringComparison.Ordinal);
        Assert.DoesNotContain(Clave, svg, StringComparison.Ordinal);
        Assert.DoesNotContain(" style=", svg, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-13")]
    public async Task EnElIngreso_ElCodigoVaDentroDeLaPagina_SinCacheSinRecursosExternos_YSinDejarRastroEnLosRegistros()
    {
        var (host, registro) = await Ingreso();
        await using var cerrar = host;

        using var respuesta = await host.Cliente.SendAsync(host.Pedir("/ingreso", CookieDelDesafio));

        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("data:image", html, StringComparison.OrdinalIgnoreCase);
        // Nada de la respuesta (cabeceras incluidas) lleva la clave fuera del cuerpo.
        Assert.DoesNotContain(Clave, respuesta.Headers.ToString() + respuesta.Content.Headers, StringComparison.Ordinal);
        Assert.All(registro.Lineas, l => Assert.False(l.Contains(Clave, StringComparison.Ordinal) || l.Contains("otpauth", StringComparison.Ordinal), "Un registro lleva el secreto."));
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-15")]
    public async Task SiElCodigoNoSePuedeGenerar_LaPantallaRespondeIgualConLaClaveYElEnlace()
    {
        var (host, registro) = await Ingreso(new GeneradorQueNoPuede());
        await using var cerrar = host;

        using var respuesta = await host.Cliente.SendAsync(host.Pedir("/ingreso", CookieDelDesafio));

        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain("data-bh-qr", html, StringComparison.Ordinal);
        Assert.DoesNotContain("escaneando", html, StringComparison.Ordinal);
        Assert.Contains($"data-bh-secreto>{Clave}</p>", html, StringComparison.Ordinal);
        Assert.Equal(DireccionEsperada, Enlace(html));
        Assert.Contains("action=\"/ingreso/configuracion\"", html, StringComparison.Ordinal);
        Assert.All(registro.Lineas, l => Assert.DoesNotContain(Clave, l, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-12")]
    public async Task ConIdentidadNoDisponible_NoHayNiClaveNiCodigo()
    {
        var (host, _) = await Ingreso();
        await using var cerrar = host;
        host.Ingreso.Secreto = Resultado<SecretoDelSegundoFactor>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));

        using var respuesta = await host.Cliente.SendAsync(host.Pedir("/ingreso", CookieDelDesafio));

        var html = await respuesta.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.DoesNotContain("data-bh-qr", html, StringComparison.Ordinal);
        Assert.DoesNotContain(Clave, html, StringComparison.Ordinal);
    }

    private sealed class GeneradorQueNoPuede : IGeneradorDeCodigoQr
    {
        public CodigoQr? Generar(ValorSecreto contenido) => null;
    }

    private sealed class Registros : ILoggerProvider, ILogger
    {
        private readonly ConcurrentQueue<string> _lineas = new();

        public IReadOnlyCollection<string> Lineas => _lineas;

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _lineas.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : " " + exception));

        public void Dispose()
        {
        }
    }
}
