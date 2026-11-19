using System.Net;
using BabelHub.Ecommerce.Api.Pagos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// La ráfaga de la entrada de avisos de la pasarela (PAG-19, SEG-05, SEG-18, SEG-26; AC-PAG-16): la política <c>avisos-de-la-pasarela-por-origen</c> se reserva <b>antes</b> de que el aviso llegue a la verificación de la firma, un exceso responde el 429
/// uniforme, solo cuenta el POST a esa ruta y la ruta está exenta del tope previo y del límite sin sesión (su único límite es la ráfaga).
/// </summary>
public sealed class LimiteDeAvisosDeLaPasarelaTests : IAsyncLifetime
{
    private sealed class ControlRegistrador(Func<int, ResultadoDeLimite> decidir) : IControlDeLimites
    {
        public List<ClaveDePolitica> Reservas { get; } = [];

        public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huella = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
        {
            Reservas.Add(politica);
            return Task.FromResult(decidir(Reservas.Count));
        }

        public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private WebApplication _app = null!;
    private HttpClient _cliente = null!;
    private ControlRegistrador _control = null!;
    private readonly List<string> _orden = [];

    public async Task InitializeAsync()
    {
        _control = new ControlRegistrador(n => n <= 2 ? ResultadoDeLimite.PermitidoSinContar() : ResultadoDeLimite.Excedido("avisos-de-la-pasarela-por-origen", 30, DateTimeOffset.UtcNow.AddSeconds(30)));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton<IControlDeLimites>(_control);
        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.UsarLimiteDeAvisosDeLaPasarela();
        _app.MapPost(AvisosDeLaPasarelaEnLaApi.Ruta, () =>
        {
            _orden.Add("firma");
            return Results.Ok();
        });
        _app.MapGet(AvisosDeLaPasarelaEnLaApi.Ruta, () => Results.Ok());
        _app.MapPost(AvisosDeLaPasarelaEnLaApi.Ruta + "/{**resto}", () => Results.Ok());
        _app.MapPost("/otra", () => Results.Ok());
        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task LaRafaga_SeReservaAntesDeLaFirma_YUnExcesoResponde429SinLlegarAlEndpoint()
    {
        var primera = await _cliente.PostAsync(AvisosDeLaPasarelaEnLaApi.Ruta, new StringContent("{}"));
        var segunda = await _cliente.PostAsync(AvisosDeLaPasarelaEnLaApi.Ruta, new StringContent("{}"));
        var excedida = await _cliente.PostAsync(AvisosDeLaPasarelaEnLaApi.Ruta, new StringContent("{}"));

        Assert.Equal(HttpStatusCode.OK, primera.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, excedida.StatusCode);
        Assert.True(excedida.Headers.Contains("Retry-After"));
        Assert.Equal(["firma", "firma"], _orden);
        Assert.All(_control.Reservas, r => Assert.Equal(PoliticasDeLimite.AvisosDeLaPasarelaPorOrigen, r));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task CuentaTodoLoQueLaExencionDejaPasar_ConElMismoCriterioDeRuta_YNadaMas()
    {
        // Lo exento nunca queda sin límite: la barra final, las mayúsculas, las subrutas y los demás métodos cuentan igual que el POST exacto.
        await _cliente.PostAsync(AvisosDeLaPasarelaEnLaApi.Ruta + "/", new StringContent("{}"));
        await _cliente.PostAsync(AvisosDeLaPasarelaEnLaApi.Ruta.ToUpperInvariant(), new StringContent("{}"));
        await _cliente.PostAsync(AvisosDeLaPasarelaEnLaApi.Ruta + "/otra-cosa", new StringContent("{}"));
        await _cliente.GetAsync(AvisosDeLaPasarelaEnLaApi.Ruta);
        using var put = new HttpRequestMessage(HttpMethod.Put, AvisosDeLaPasarelaEnLaApi.Ruta) { Content = new StringContent("{}") };
        await _cliente.SendAsync(put);
        Assert.Equal(5, _control.Reservas.Count);

        // Otras rutas, aunque se parezcan, no cuentan ni están exentas.
        await _cliente.PostAsync("/otra", new StringContent("{}"));
        await _cliente.PostAsync(AvisosDeLaPasarelaEnLaApi.Ruta + "x", new StringContent("{}"));
        Assert.Equal(5, _control.Reservas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-16")]
    public void LaRutaDeLosAvisos_EstaExentaDelTopePrevioYDelLimiteSinSesion_ConSuPropiaRafaga()
    {
        // La política es por origen y su tope es mayor que el límite sin sesión de 60 por minuto: la composición exime la ruta y la ráfaga es su único límite.
        Assert.Equal(TipoDeSujetoDeLimite.Origen, PoliticasDeSeguridad.AvisosDeLaPasarelaPorOrigen.Tipo);
        Assert.True(PoliticasDeSeguridad.AvisosDeLaPasarelaPorOrigen.Maximo > PoliticasDeSeguridad.SolicitudesSinSesionPorOrigen.Maximo);
        var exentas = new OpcionesDeLimitesHttp { RutasExentas = ["/health", AvisosDeLaPasarelaEnLaApi.Ruta] };
        Assert.True(exentas.EsExenta(AvisosDeLaPasarelaEnLaApi.Ruta));
        Assert.False(exentas.EsExenta("/api/ecommerce/v1/integraciones/otra"));
    }
}
