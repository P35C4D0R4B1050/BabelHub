using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Web;

/// <summary>La descarga de un libro digital (T27, WEB-15): el navegador es redirigido al enlace firmado y la capa web nunca transmite el archivo.</summary>
public sealed partial class DescargaDeLibroTests
{
    private const string EnlaceFirmado = "https://almacen.example/biblioteca/archivo.epub?X-Signature=SECRETO-DE-LA-FIRMA&X-Expires=300";
    private static readonly Guid Acceso = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Token();

    [GeneratedRegex("name=\"clave\"[^>]*value=\"(?<clave>[^\"]*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Clave();

    private sealed class BibliotecaDePrueba(Func<Resultado<EnlaceDeDescarga>> respuesta) : IDescargasDeLaBiblioteca
    {
        public List<(Guid Acceso, string Clave)> Llamadas { get; } = [];

        public Task<Resultado<EnlaceDeDescarga>> SolicitarEnlace(Guid accesoId, string claveDeIdempotencia, CancellationToken cancelacion = default)
        {
            Llamadas.Add((accesoId, claveDeIdempotencia));
            return Task.FromResult(respuesta());
        }
    }

    private sealed class RegistroCapturado : ILoggerProvider
    {
        public List<string> Mensajes { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Capturador(this);

        public void Dispose()
        {
        }

        private sealed class Capturador(RegistroCapturado dueno) : ILogger
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

    private static Resultado<EnlaceDeDescarga> Enlace() => Resultado<EnlaceDeDescarga>.Exito(new EnlaceDeDescarga(new Uri(EnlaceFirmado), DateTimeOffset.UnixEpoch.AddMinutes(5)));

    private static async Task<AnfitrionWeb> Iniciar(BibliotecaDePrueba biblioteca, RegistroCapturado? registros = null) =>
        await AnfitrionWeb.Iniciar(
            s =>
            {
                s.AddSingleton<IDescargasDeLaBiblioteca>(biblioteca);
                if (registros is not null)
                {
                    s.AddLogging(l => l.AddProvider(registros));
                }
            },
            partes: [typeof(DescargaDeLibroTests).Assembly]);

    private static async Task<(string Token, string Clave, string Cookies)> DelFormulario(AnfitrionWeb anfitrion)
    {
        using var respuesta = await anfitrion.Cliente.GetAsync($"/prueba/boton-de-descarga/{Acceso}");
        var html = await respuesta.Content.ReadAsStringAsync();
        return (
            Token().Match(html).Groups["token"].Value,
            Clave().Match(html).Groups["clave"].Value,
            string.Join("; ", respuesta.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
    }

    private static async Task<HttpResponseMessage> Descargar(AnfitrionWeb anfitrion, string token, string clave, string cookies, string? id = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Post, $"/biblioteca/{id ?? Acceso.ToString()}/descargar")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["__RequestVerificationToken"] = token, ["clave"] = clave }),
        };
        solicitud.Headers.Add("Cookie", cookies);
        return await anfitrion.Cliente.SendAsync(solicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task Pulsar_descargar_redirige_al_enlace_firmado_sin_transmitir_el_archivo_y_sin_guardar_la_respuesta()
    {
        var biblioteca = new BibliotecaDePrueba(Enlace);
        await using var anfitrion = await Iniciar(biblioteca);
        var (token, clave, cookies) = await DelFormulario(anfitrion);

        using var respuesta = await Descargar(anfitrion, token, clave, cookies);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal(EnlaceFirmado, respuesta.Headers.Location?.OriginalString);
        Assert.Contains("no-store", respuesta.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.Empty(await respuesta.Content.ReadAsByteArrayAsync());
        Assert.Null(respuesta.Content.Headers.ContentType);
        Assert.Equal((Acceso, clave), Assert.Single(biblioteca.Llamadas));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task La_url_firmada_no_se_escribe_en_ningun_registro()
    {
        var registros = new RegistroCapturado();
        await using var anfitrion = await Iniciar(new BibliotecaDePrueba(Enlace), registros);
        var (token, clave, cookies) = await DelFormulario(anfitrion);

        using var respuesta = await Descargar(anfitrion, token, clave, cookies);

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        lock (registros.Mensajes)
        {
            Assert.DoesNotContain(registros.Mensajes, m => m.Contains("SECRETO-DE-LA-FIRMA", StringComparison.Ordinal) || m.Contains("X-Signature", StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task Dos_envios_con_la_misma_clave_llegan_a_la_biblioteca_con_la_misma_clave_para_que_cuenten_una_sola_descarga()
    {
        var biblioteca = new BibliotecaDePrueba(Enlace);
        await using var anfitrion = await Iniciar(biblioteca);
        var (token, clave, cookies) = await DelFormulario(anfitrion);

        using var primera = await Descargar(anfitrion, token, clave, cookies);
        using var segunda = await Descargar(anfitrion, token, clave, cookies);

        Assert.Equal(HttpStatusCode.Redirect, primera.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, segunda.StatusCode);
        Assert.Equal(2, biblioteca.Llamadas.Count);
        Assert.Single(biblioteca.Llamadas.Select(l => l.Clave).Distinct());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task Cada_formulario_genera_una_clave_nueva_de_un_solo_uso()
    {
        await using var anfitrion = await Iniciar(new BibliotecaDePrueba(Enlace));

        var primera = await DelFormulario(anfitrion);
        var segunda = await DelFormulario(anfitrion);

        Assert.Matches("^[0-9a-f]{32}$", primera.Clave);
        Assert.Matches("^[0-9a-f]{32}$", segunda.Clave);
        Assert.NotEqual(primera.Clave, segunda.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task La_descarga_no_es_un_GET_que_un_prefetch_pueda_gastar()
    {
        var biblioteca = new BibliotecaDePrueba(Enlace);
        await using var anfitrion = await Iniciar(biblioteca);

        using var respuesta = await anfitrion.Cliente.GetAsync($"/biblioteca/{Acceso}/descargar");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, respuesta.StatusCode);
        Assert.Empty(biblioteca.Llamadas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("corta")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("0123456789abcdef0123456789abcdef0123456789abcdef")]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task Una_clave_ausente_o_con_formato_invalido_se_rechaza_sin_llamar_a_la_biblioteca(string clave)
    {
        var biblioteca = new BibliotecaDePrueba(Enlace);
        await using var anfitrion = await Iniciar(biblioteca);
        var (token, _, cookies) = await DelFormulario(anfitrion);

        using var respuesta = await Descargar(anfitrion, token, clave, cookies);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(biblioteca.Llamadas);
        Assert.Null(respuesta.Headers.Location);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Sin_token_antifalsificacion_no_se_pide_la_descarga()
    {
        var biblioteca = new BibliotecaDePrueba(Enlace);
        await using var anfitrion = await Iniciar(biblioteca);
        var (_, clave, cookies) = await DelFormulario(anfitrion);

        using var respuesta = await Descargar(anfitrion, "token-falso", clave, cookies);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(biblioteca.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task Un_identificador_que_no_es_un_guid_no_existe()
    {
        var biblioteca = new BibliotecaDePrueba(Enlace);
        await using var anfitrion = await Iniciar(biblioteca);
        var (token, clave, cookies) = await DelFormulario(anfitrion);

        using var respuesta = await Descargar(anfitrion, token, clave, cookies, id: "no-es-un-guid");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Empty(biblioteca.Llamadas);
    }

    [Theory]
    [InlineData(CategoriaDeError.Prohibido, HttpStatusCode.Forbidden, "Tu cuenta no tiene acceso a esta sección.")]
    [InlineData(CategoriaDeError.NoDisponible, HttpStatusCode.ServiceUnavailable, "No podemos preparar tu descarga ahora.")]
    [InlineData(CategoriaDeError.ReglaDeNegocio, HttpStatusCode.UnprocessableEntity, "La operación no se puede completar.")]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task Un_rechazo_de_la_biblioteca_se_muestra_como_estado_y_nunca_redirige(CategoriaDeError categoria, HttpStatusCode esperado, string texto)
    {
        var biblioteca = new BibliotecaDePrueba(() => Resultado<EnlaceDeDescarga>.Fallo(new ErrorDeAplicacion(categoria, "motivo")));
        await using var anfitrion = await Iniciar(biblioteca);
        var (token, clave, cookies) = await DelFormulario(anfitrion);

        using var respuesta = await Descargar(anfitrion, token, clave, cookies);

        Assert.Equal(esperado, respuesta.StatusCode);
        Assert.Null(respuesta.Headers.Location);
        Assert.Contains(texto, WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://almacen.example/archivo.epub")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [Trait("Criterio", "AC-WEB-11")]
    public async Task Un_enlace_que_no_es_https_nunca_se_usa_como_destino(string enlace)
    {
        var biblioteca = new BibliotecaDePrueba(() => Resultado<EnlaceDeDescarga>.Exito(new EnlaceDeDescarga(new Uri(enlace), DateTimeOffset.UnixEpoch)));
        await using var anfitrion = await Iniciar(biblioteca);
        var (token, clave, cookies) = await DelFormulario(anfitrion);

        using var respuesta = await Descargar(anfitrion, token, clave, cookies);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Null(respuesta.Headers.Location);
    }
}
