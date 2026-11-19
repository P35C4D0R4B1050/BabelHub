using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Notificaciones;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using Microsoft.Extensions.Logging.Abstractions;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// Los adaptadores de envío (transversal-notificaciones T14, T15; AC-NOT-03, AC-NOT-09, AC-NOT-10, AC-NOT-11, AC-NOT-12): la llamada firmada a n8n con la clave del sentido saliente y sin texto del mensaje, el 200 «aceptado» frente al
/// «duplicado», lo que no es un envío entregado, y los adaptadores simulado y nulo que nunca envían a un número real.
/// </summary>
public sealed class AdaptadoresDeEnvioTests
{
    private static readonly Uri Webhook = new("http://n8n:5678/webhook/babelhub-avisos");

    private static readonly byte[] ClaveSaliente = Encoding.UTF8.GetBytes("clave-del-sentido-saliente-0123456789-abcdef");

    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = AdaptadoresDeEnvioTests.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class ServidorDeN8n(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Peticion, string Cuerpo)> Recibidas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recibidas.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await responder(request);
        }
    }

    private static HttpResponseMessage Json(string cuerpo, HttpStatusCode estado = HttpStatusCode.OK) => new(estado) { Content = new StringContent(cuerpo, Encoding.UTF8, "application/json") };

    private static (N8nEnvioDeWhatsApp Envio, ServidorDeN8n Servidor) Crear(Func<HttpRequestMessage, Task<HttpResponseMessage>> responder)
    {
        var servidor = new ServidorDeN8n(responder);
        return (new N8nEnvioDeWhatsApp(new HttpClient(servidor), new OpcionesDeEnvioPorN8n(Webhook, ClaveSaliente), new RelojDePrueba()), servidor);
    }

    private static SolicitudDeEnvio Solicitud()
    {
        Assert.True(NumeroDeDestino.TryNormalizar("987 654 321", out var numero));
        return new SolicitudDeEnvio(Guid.NewGuid(), "pedido-listo-para-recojo", 1, new Dictionary<string, string> { ["numeroDePedido"] = "BH-000123", ["nombre"] = "María" }, numero);
    }

    // ---------------------------------------------------------------- n8n (T14)

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task LaLlamada_VaFirmadaConLaClaveSalienteSobreMarcaMetodoRutaYCuerpo_ConElIdentificadorDelAviso()
    {
        var (envio, servidor) = Crear(_ => Task.FromResult(Json("{\"estado\":\"aceptado\",\"duplicado\":false}")));
        var solicitud = Solicitud();

        var resultado = await envio.Enviar(solicitud);

        Assert.Equal(ResultadoDelEnvio.Aceptado, resultado);
        var (peticion, cuerpo) = Assert.Single(servidor.Recibidas);
        Assert.Equal(HttpMethod.Post, peticion.Method);
        Assert.Equal(Webhook, peticion.RequestUri);
        var marca = Assert.Single(peticion.Headers.GetValues(N8nEnvioDeWhatsApp.CabeceraDeLaMarca));
        var firma = Assert.Single(peticion.Headers.GetValues(N8nEnvioDeWhatsApp.CabeceraDeLaFirma));
        Assert.Equal(Ahora.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture), marca);
        Assert.Equal(N8nEnvioDeWhatsApp.Firmar(ClaveSaliente, marca, "POST", "/webhook/babelhub-avisos", cuerpo), firma);

        // Otra clave (por ejemplo la del sentido entrante), otra ruta, otra marca o otro cuerpo no producen la misma firma.
        Assert.NotEqual(firma, N8nEnvioDeWhatsApp.Firmar(Encoding.UTF8.GetBytes("otra-clave-de-otro-sentido-0123456789-abc"), marca, "POST", "/webhook/babelhub-avisos", cuerpo));
        Assert.NotEqual(firma, N8nEnvioDeWhatsApp.Firmar(ClaveSaliente, marca, "POST", "/webhook/otro", cuerpo));
        Assert.NotEqual(firma, N8nEnvioDeWhatsApp.Firmar(ClaveSaliente, "1", "POST", "/webhook/babelhub-avisos", cuerpo));
        Assert.NotEqual(firma, N8nEnvioDeWhatsApp.Firmar(ClaveSaliente, marca, "POST", "/webhook/babelhub-avisos", cuerpo + " "));
        using var documento = JsonDocument.Parse(cuerpo);
        Assert.Equal(solicitud.AvisoId.ToString("D"), documento.RootElement.GetProperty("avisoId").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-09")]
    public async Task ElCuerpo_LlevaLaPlantillaLosCamposYElNumero_PeroNingunTextoDeMensaje_NiLaClave()
    {
        var (envio, servidor) = Crear(_ => Task.FromResult(Json("{\"estado\":\"aceptado\"}")));

        await envio.Enviar(Solicitud());

        var cuerpo = Assert.Single(servidor.Recibidas).Cuerpo;
        using var documento = JsonDocument.Parse(cuerpo);
        var raiz = documento.RootElement;
        Assert.Equal(["avisoId", "campos", "numero", "plantilla", "version"], raiz.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("pedido-listo-para-recojo", raiz.GetProperty("plantilla").GetString());
        Assert.Equal("BH-000123", raiz.GetProperty("campos").GetProperty("numeroDePedido").GetString());
        Assert.Equal("+51987654321", raiz.GetProperty("numero").GetString());
        Assert.DoesNotContain("clave-del-sentido-saliente", cuerpo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"estado\":\"aceptado\",\"duplicado\":false}", ResultadoDelEnvio.Aceptado)]
    [InlineData("{\"estado\":\"aceptado\",\"duplicado\":true}", ResultadoDelEnvio.Duplicado)]
    [InlineData("{\"estado\":\"aceptado\"}", ResultadoDelEnvio.Aceptado)]
    [InlineData("{\"estado\":\"entregado\"}", ResultadoDelEnvio.Fallido)]
    [InlineData("{\"duplicado\":true}", ResultadoDelEnvio.Fallido)]
    [InlineData("no es json", ResultadoDelEnvio.Fallido)]
    [InlineData("[]", ResultadoDelEnvio.Fallido)]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task ElCuerpoDeLaRespuesta_SoloSeAceptaConLaFormaDelContrato(string cuerpo, ResultadoDelEnvio esperado)
    {
        var (envio, _) = Crear(_ => Task.FromResult(Json(cuerpo)));

        Assert.Equal(esperado, await envio.Enviar(Solicitud()));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.Accepted)]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task UnaRespuestaQueNoEsUn200_EsUnEnvioFallido(HttpStatusCode estado)
    {
        var (envio, _) = Crear(_ => Task.FromResult(Json("{\"estado\":\"aceptado\"}", estado)));

        Assert.Equal(ResultadoDelEnvio.Fallido, await envio.Enviar(Solicitud()));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task LaRedCaidaOLaEsperaAgotada_SonUnEnvioFallido_NuncaUnaExcepcion()
    {
        var (caido, _) = Crear(_ => throw new HttpRequestException("n8n no responde"));
        var (lento, _) = Crear(_ => throw new TaskCanceledException("la espera de 10 segundos se agotó"));

        Assert.Equal(ResultadoDelEnvio.Fallido, await caido.Enviar(Solicitud()));
        Assert.Equal(ResultadoDelEnvio.Fallido, await lento.Enviar(Solicitud()));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-03")]
    public async Task LaCancelacionDelApagado_SePropaga_ComoCancelacion()
    {
        using var origen = new CancellationTokenSource();
        var (envio, _) = Crear(async _ =>
        {
            await origen.CancelAsync();
            origen.Token.ThrowIfCancellationRequested();
            return Json("{}");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => envio.Enviar(Solicitud(), origen.Token));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public async Task UnaRespuestaDemasiadoGrande_EsUnEnvioFallido()
    {
        var enorme = "{\"estado\":\"aceptado\",\"relleno\":\"" + new string('x', 10_000) + "\"}";
        var (envio, _) = Crear(_ => Task.FromResult(Json(enorme)));

        Assert.Equal(ResultadoDelEnvio.Fallido, await envio.Enviar(Solicitud()));
    }

    [Theory]
    [InlineData("https://n8n.example.org/webhook/x", true)]
    [InlineData("http://localhost:5678/webhook/x", true)]
    [InlineData("http://127.0.0.1:5678/webhook/x", true)]
    [InlineData("http://n8n:5678/webhook/x", true)]
    [InlineData("http://n8n.example.org/webhook/x", false)]
    [InlineData("http://8.8.8.8/webhook/x", false)]
    [InlineData("https://usuario:clave@n8n.example.org/webhook/x", false)]
    [InlineData("https://n8n.example.org/webhook/x?token=1", false)]
    [InlineData("ftp://n8n/webhook/x", false)]
    [Trait("Criterio", "AC-NOT-12")]
    public void ElWebhook_EsHttpsOHttpSoloInterno_SinCredencialesNiConsulta(string direccion, bool valida)
    {
        var construir = () => new OpcionesDeEnvioPorN8n(new Uri(direccion), ClaveSaliente);

        if (valida)
        {
            Assert.Equal("[envío por n8n]", construir().ToString());
        }
        else
        {
            Assert.Throws<ArgumentException>(construir);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public void LaClaveSaliente_DebeTenerAlMenos32Bytes_YNoSeImprime()
    {
        Assert.Throws<ArgumentException>(() => new OpcionesDeEnvioPorN8n(Webhook, Encoding.UTF8.GetBytes("corta")));
        Assert.DoesNotContain("clave-del-sentido", new OpcionesDeEnvioPorN8n(Webhook, ClaveSaliente).ToString(), StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------- el simulado y el nulo (T15)

    private sealed class RegistroDeEstados : IRegistroDeEstadoDeEntrega
    {
        public ConcurrentBag<(Guid Aviso, EstadoDeEntrega Estado)> Registrados { get; } = [];

        public Task<Resultado<ConfirmacionDeNotificaciones>> Registrar(Guid avisoId, EstadoDeEntrega estado, DateTimeOffset marcaDeTiempo, CancellationToken cancelacion = default)
        {
            Registrados.Add((avisoId, estado));
            return Task.FromResult(Resultado<ConfirmacionDeNotificaciones>.Exito(new ConfirmacionDeNotificaciones()));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-10")]
    public async Task ElEnvioSimulado_NoLlamaANadie_CompletaElCicloConElEstadoEntregado_YNoRegistraElNumero()
    {
        var estados = new RegistroDeEstados();
        var simulado = new SimuladoEnvioDeWhatsApp(estados, new RelojDePrueba(), NullLogger<SimuladoEnvioDeWhatsApp>.Instance);
        var solicitud = Solicitud();

        var resultado = await simulado.Enviar(solicitud);

        Assert.Equal(ResultadoDelEnvio.Aceptado, resultado);
        Assert.Equal((solicitud.AvisoId, EstadoDeEntrega.Entregado), Assert.Single(estados.Registrados));
        Assert.DoesNotContain("987654321", solicitud.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("987654321", solicitud.Numero.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-10")]
    public async Task ElEnvioNulo_NuncaEnvia_YResponde_Fallido()
    {
        Assert.Equal(ResultadoDelEnvio.Fallido, await new NuloEnvioDeWhatsApp().Enviar(Solicitud()));
    }
}
