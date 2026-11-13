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

public sealed class LimitesDeUsoInventarioTests : IAsyncLifetime
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

    private sealed class EventosNulos : IPublicadorDeEventosDeSeguridad
    {
        public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private readonly TiempoControlable _tiempo = new(new DateTimeOffset(2026, 11, 13, 12, 0, 0, TimeSpan.Zero));
    private WebApplication? _app;
    private HttpClient _cliente = null!;

    public async Task InitializeAsync()
    {
        var local = new MotorDeLimitesLocal(tiempo: _tiempo);
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV"));
        builder.Services.AgregarLimitesHttp(new OpcionesDeLimitesHttp { PorUsuario = PoliticasDeLimite.SolicitudesPorUsuario });
        builder.Services.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales)));
        builder.Services.AddScoped<IControlDeLimites>(proveedor => new EvaluarLimite(
            proveedor.GetRequiredService<CatalogoDePoliticas>(),
            local,
            local,
            proveedor.GetRequiredService<IOrigenDeSolicitud>(),
            new EventosNulos(),
            new RelojDePrueba(_tiempo)));
        _app = builder.Build();

        _app.UsarManejoDeErrores();
        _app.UsarTopePrevio();
        _app.Use(async (contexto, siguiente) =>
        {
            var usuario = contexto.Request.Headers["X-Prueba-Usuario"].ToString();
            if (usuario.Length > 0)
            {
                contexto.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, usuario)], "prueba"));
            }

            await siguiente(contexto);
        });
        _app.UsarLimitesGenerales();
        _app.MapGet("/disponibilidad", () => Results.Ok(new { estado = "ok" }));
        _app.MapGet("/health/ready", () => Results.Ok(new { estado = "listo" }));

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

    private async Task<HttpResponseMessage> Pedir(string usuario, string ruta = "/disponibilidad")
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        solicitud.Headers.Add("X-Prueba-Usuario", usuario);
        return await _cliente.SendAsync(solicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-05")]
    public async Task UnUsuarioConCienSolicitudesEnElUltimoMinuto_RecibeLaRespuestaDeLimiteDelInventarioYOtroNoSeAfecta()
    {
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir("u-1")).StatusCode);
        }

        var rechazada = await Pedir("u-1");
        var cuerpo = JsonDocument.Parse(await rechazada.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.TooManyRequests, rechazada.StatusCode);
        Assert.Equal("INV-LIMITE-EXCEDIDO", cuerpo.GetProperty("code").GetString());
        Assert.Equal("solicitudes-por-usuario", cuerpo.GetProperty("motivo").GetString());
        Assert.NotEmpty(rechazada.Headers.GetValues("Retry-After").Single());
        Assert.Equal(HttpStatusCode.OK, (await Pedir("u-2")).StatusCode);

        _tiempo.Ahora += TimeSpan.FromSeconds(cuerpo.GetProperty("retryAfterSeconds").GetInt32());
        Assert.Equal(HttpStatusCode.OK, (await Pedir("u-1")).StatusCode);
    }

    [Fact]
    public async Task LaSalud_NoSeLimita()
    {
        for (var i = 0; i < 300; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await Pedir("u-1", "/health/ready")).StatusCode);
        }
    }
}
