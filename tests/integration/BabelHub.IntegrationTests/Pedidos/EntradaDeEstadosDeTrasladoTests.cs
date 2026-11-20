using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Pedidos;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos.Ecommerce;
using BabelHub.Shared.Errores;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// La entrada HTTP de los estados de traslado del Inventario (<c>POST /api/ecommerce/v1/integraciones/inventario/traslados</c>; revisión de seguridad de ECO-6f, AM32, SEG-13; AC-PED-03): presentación pura sobre un receptor
/// doble. La credencial de servicio y su alcance los verifica el middleware de la superficie de servicio (sus pruebas son las de <c>Plataforma</c>); aquí: el tope de 4 KiB (por <c>Content-Length</c> y por trozos), el esquema
/// estricto (campo desconocido, mayúsculas, números como texto, <c>Content-Type</c>), la traducción de los errores del receptor, el <c>204</c> de un evento repetido y que el actor es siempre la integración del Inventario y se
/// limpia aunque falle, sin tomarse nunca de una cabecera.
/// </summary>
public sealed class EntradaDeEstadosDeTrasladoTests : IAsyncLifetime
{
    private sealed class ReceptorFalso : IReceptorDeEstadosDeTraslado
    {
        public List<EstadoDeTrasladoRecibido> Recibidos { get; } = [];

        public List<ActorActual?> Actores { get; } = [];

        public Resultado<bool> Respuesta { get; set; } = Resultado<bool>.Exito(true);

        public Func<ActorActual?>? ActorActual { get; set; }

        public Task<Resultado<bool>> Recibir(EstadoDeTrasladoRecibido estado, CancellationToken cancelacion = default)
        {
            Recibidos.Add(estado);
            Actores.Add(ActorActual?.Invoke());
            return Task.FromResult(Respuesta);
        }
    }

    private WebApplication _app = null!;
    private HttpClient _cliente = null!;
    private ActorDelFlujo _actor = null!;

    private readonly ReceptorFalso _receptor = new();

    public async Task InitializeAsync()
    {
        _actor = new ActorDelFlujo();
        _receptor.ActorActual = () => _actor.Actual;
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton<IActorActual>(_actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(_actor);
        builder.Services.AddSingleton(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton<IReceptorDeEstadosDeTraslado>(_receptor);
        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.MapearPedidos();
        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _cliente.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static readonly Guid Traslado = Guid.Parse("c1000000-0000-0000-0000-000000000001");

    private static EstadoDeTrasladoDePedidoDto Cuerpo(string estado = "en-transito") =>
        new($"traslado:{Traslado:D}:{estado}", Traslado, Guid.Parse("a1000000-0000-0000-0000-000000000001"), Guid.Parse("e1000000-0000-0000-0000-000000000001"), 2, estado, new DateOnly(2026, 11, 25), DateTimeOffset.UtcNow);

    private static StringContent Json(string texto, string tipo = "application/json") => new(texto, Encoding.UTF8, tipo);

    private Task<HttpResponseMessage> Enviar(HttpContent contenido, Action<HttpRequestMessage>? ajustar = null)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Post, EndpointsDePedidos.RutaDeLosEstadosDeTraslado) { Content = contenido };
        ajustar?.Invoke(solicitud);
        return _cliente.SendAsync(solicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task Un_estado_valido_responde_204_lo_pasa_al_receptor_con_la_integracion_del_Inventario_y_limpia_el_actor()
    {
        using var respuesta = await Enviar(JsonContent.Create(Cuerpo()));

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal("no-store", respuesta.Headers.CacheControl!.ToString());
        var recibido = Assert.Single(_receptor.Recibidos);
        Assert.Equal((Traslado, 2, EstadoDelTrasladoInformado.EnTransito, new DateOnly(2026, 11, 25)), (recibido.TrasladoId, recibido.Cantidad, recibido.Estado, recibido.FechaEstimadaDeLlegada));
        Assert.Equal((TipoDeActor.Integracion, IntegracionesDeServicio.Inventario), (_receptor.Actores.Single()!.Tipo, _receptor.Actores.Single()!.Integracion));
        Assert.Null(_actor.Actual);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task El_actor_nunca_sale_de_una_cabecera()
    {
        using var respuesta = await Enviar(JsonContent.Create(Cuerpo()), s =>
        {
            s.Headers.TryAddWithoutValidation("X-Actor", "administrador-central");
            s.Headers.TryAddWithoutValidation("X-Prueba-Usuario", Guid.NewGuid().ToString("D"));
        });

        Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        Assert.Equal(IntegracionesDeServicio.Inventario, _receptor.Actores.Single()!.Integracion);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task El_mismo_evento_repetido_responde_204_cada_vez()
    {
        for (var i = 0; i < 3; i++)
        {
            using var respuesta = await Enviar(JsonContent.Create(Cuerpo("recibido")));
            Assert.Equal(HttpStatusCode.NoContent, respuesta.StatusCode);
        }

        Assert.Equal(3, _receptor.Recibidos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task Un_cuerpo_de_mas_de_4_KiB_se_rechaza_con_400_sin_llegar_al_receptor_con_o_sin_Content_Length_honesto()
    {
        var grande = JsonSerializer.Serialize(Cuerpo() with { EventoId = new string('x', 5000) });

        using var conLongitud = await Enviar(Json(grande));
        using var porTrozos = await Enviar(new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(grande))) { Headers = { { "Content-Type", "application/json" } } }, s => s.Headers.TransferEncodingChunked = true);

        Assert.Equal(HttpStatusCode.BadRequest, conLongitud.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, porTrozos.StatusCode);
        Assert.Empty(_receptor.Recibidos);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-03")]
    [InlineData("{\"eventoId\":\"traslado:x:en-transito\",\"trasladoId\":\"c1000000-0000-0000-0000-000000000001\",\"pedidoId\":\"a1000000-0000-0000-0000-000000000001\",\"edicionId\":\"e1000000-0000-0000-0000-000000000001\",\"cantidad\":2,\"estado\":\"en-transito\",\"ocurridoEn\":\"2026-11-20T10:00:00Z\",\"extra\":1}")]
    [InlineData("{\"EventoId\":\"traslado:x:en-transito\",\"TrasladoId\":\"c1000000-0000-0000-0000-000000000001\",\"PedidoId\":\"a1000000-0000-0000-0000-000000000001\",\"EdicionId\":\"e1000000-0000-0000-0000-000000000001\",\"Cantidad\":2,\"Estado\":\"en-transito\",\"OcurridoEn\":\"2026-11-20T10:00:00Z\"}")]
    [InlineData("{\"eventoId\":\"traslado:x:en-transito\",\"trasladoId\":\"c1000000-0000-0000-0000-000000000001\",\"pedidoId\":\"a1000000-0000-0000-0000-000000000001\",\"edicionId\":\"e1000000-0000-0000-0000-000000000001\",\"cantidad\":\"2\",\"estado\":\"en-transito\",\"ocurridoEn\":\"2026-11-20T10:00:00Z\"}")]
    [InlineData("{\"eventoId\":\"traslado:x:otro\",\"trasladoId\":\"c1000000-0000-0000-0000-000000000001\",\"pedidoId\":\"a1000000-0000-0000-0000-000000000001\",\"edicionId\":\"e1000000-0000-0000-0000-000000000001\",\"cantidad\":2,\"estado\":\"otro\",\"ocurridoEn\":\"2026-11-20T10:00:00Z\"}")]
    [InlineData("no es json")]
    [InlineData("")]
    public async Task Un_cuerpo_que_no_cumple_el_esquema_estricto_es_400_y_no_llega_al_receptor(string cuerpo)
    {
        using var respuesta = await Enviar(Json(cuerpo));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Equal("ECO-VALIDACION-INVALIDA", (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Empty(_receptor.Recibidos);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public async Task Un_Content_Type_que_no_es_JSON_es_400_y_no_llega_al_receptor()
    {
        using var respuesta = await Enviar(Json(JsonSerializer.Serialize(Cuerpo()), "text/plain"));

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(_receptor.Recibidos);
    }

    [Theory]
    [Trait("Criterio", "AC-PED-03")]
    [InlineData(CategoriaDeError.NoEncontrado, "no-encontrado", 404, "ECO-NO-ENCONTRADO")]
    [InlineData(CategoriaDeError.Validacion, "validacion-fallida", 400, "ECO-VALIDACION-INVALIDA")]
    [InlineData(CategoriaDeError.Conflicto, "conflicto", 409, "ECO-CONFLICTO")]
    [InlineData(CategoriaDeError.NoDisponible, "servicio-no-disponible", 503, "ECO-SERVICIO-NO-DISPONIBLE")]
    [InlineData(CategoriaDeError.Prohibido, "prohibido", 403, "ECO-PROHIBIDO")]
    public async Task Los_errores_del_receptor_salen_como_problem_json_con_su_codigo_y_el_actor_se_limpia(CategoriaDeError categoria, string clave, int estado, string codigo)
    {
        _receptor.Respuesta = Resultado<bool>.Fallo(new ErrorDeAplicacion(categoria, clave));

        using var respuesta = await Enviar(JsonContent.Create(Cuerpo()));

        Assert.Equal(estado, (int)respuesta.StatusCode);
        Assert.Equal(codigo, (await respuesta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Null(_actor.Actual);
    }
}
