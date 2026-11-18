using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Ecommerce;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using static BabelHub.IntegrationTests.Traslados.EscenarioDeTraslados;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// Un E-commerce simulado que solo conoce <c>OperacionesDelEcommerce</c> y los DTOs de ECO-0: exige la credencial <c>solicitar-aviso</c> en las dos rutas de integración, lee el cuerpo con esquema <b>estricto</b>, valida el
/// <c>eventoId</c>, la plantilla y los estados, repite sin efecto el mismo evento (idempotencia) y rechaza esa credencial en cualquier otra ruta.
/// </summary>
internal sealed partial class EcommerceDeContrato : IAsyncDisposable
{
    public const string Credencial = "credencial-de-solicitar-aviso-abcdefghijklmnop";

    private static readonly JsonSerializerOptions Estricto = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

    private readonly object _candado = new();
    private WebApplication _app = null!;

    public HttpStatusCode? Forzado { get; set; }

    public List<SolicitudDeAvisoDeInventarioDto> Avisos { get; } = [];

    public List<EstadoDeTrasladoDePedidoDto> Estados { get; } = [];

    public List<string> Rutas { get; } = [];

    public List<(string Ruta, bool ConCabeceraDeActor)> Llamadas { get; } = [];

    public HttpMessageHandler Manejador => _app.GetTestServer().CreateHandler();

    [GeneratedRegex(@"^traslado:[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}:[a-z-]{4,16}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormaDelEvento();

    public static async Task<EcommerceDeContrato> Iniciar()
    {
        var simulado = new EcommerceDeContrato();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        simulado._app = builder.Build();
        simulado._app.Run(simulado.Atender);
        await simulado._app.StartAsync();
        return simulado;
    }

    private async Task Atender(HttpContext c)
    {
        var ruta = c.Request.Path.Value ?? string.Empty;
        lock (_candado)
        {
            Rutas.Add(ruta);
            Llamadas.Add((ruta, c.Request.Headers.ContainsKey("X-Actor") || c.Request.Headers.ContainsKey("Authorization")));
        }

        if (c.Request.Headers["X-Credencial-De-Servicio"] != Credencial)
        {
            c.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var esAvisos = ruta == RutasDeServicio.Ecommerce + "/integraciones/inventario/avisos";
        var esEstados = ruta == RutasDeServicio.Ecommerce + "/integraciones/inventario/traslados";
        if (c.Request.Method != "POST" || !(esAvisos || esEstados))
        {
            // La credencial solicitar-aviso solo vale en las dos rutas de integración: en cualquier otra, prohibido.
            c.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        if (Forzado is { } forzado)
        {
            c.Response.StatusCode = (int)forzado;
            return;
        }

        try
        {
            using var lector = new StreamReader(c.Request.Body);
            var cuerpo = await lector.ReadToEndAsync();
            if (esAvisos)
            {
                var dto = JsonSerializer.Deserialize<SolicitudDeAvisoDeInventarioDto>(cuerpo, Estricto)!;
                if (!FormaDelEvento().IsMatch(dto.EventoId) || dto.Plantilla != "traslado-cambio-de-estado" || dto.Campos.Count != 4 || !dto.Campos.ContainsKey("numeroDeTraslado"))
                {
                    c.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                lock (_candado)
                {
                    if (!Avisos.Any(a => a.EventoId == dto.EventoId && a.CodigoDeSucursal == dto.CodigoDeSucursal))
                    {
                        Avisos.Add(dto);
                    }
                }

                c.Response.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            var estado = JsonSerializer.Deserialize<EstadoDeTrasladoDePedidoDto>(cuerpo, Estricto)!;
            if (!FormaDelEvento().IsMatch(estado.EventoId) || !EstadosDeTrasladoDePedido.Todos.Contains(estado.Estado) || estado.Cantidad <= 0)
            {
                c.Response.StatusCode = StatusCodes.Status422UnprocessableEntity;
                return;
            }

            lock (_candado)
            {
                if (!Estados.Any(e => e.EventoId == estado.EventoId))
                {
                    Estados.Add(estado);
                }
            }

            c.Response.StatusCode = StatusCodes.Status204NoContent;
        }
        catch (JsonException)
        {
            c.Response.StatusCode = StatusCodes.Status400BadRequest;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }
}

/// <summary>
/// T24 (parte del contrato REST de la salida): los avisos y el estado de un traslado de un pedido contra un E-commerce simulado que solo conoce los DTOs de ECO-0: credencial <c>solicitar-aviso</c> en las dos llamadas, esquema
/// estricto, idempotencia del evento, errores del E-commerce clasificados, ninguna cabecera de actor y ninguna otra ruta; y la entrega extremo a extremo del buzón por el adaptador real.
/// </summary>
public sealed class ContratoRestDeLaSalidaDeTrasladosTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ServiceProvider Cliente(EcommerceDeContrato ecommerce, string credencial = EcommerceDeContrato.Credencial)
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton(new OpcionesDeCredencialesDeSalida([new(AlcanceDeServicio.SolicitarAviso, new ValorSecreto(credencial))]));
        servicios.AddSingleton<IOrigenDeSolicitud>(new OrigenPorFlujo());
        servicios.AgregarSalidaAlEcommerce(new Uri("https://ecommerce.interno"), new OpcionesDelClienteDeServicio { ReintentosMaximos = 0 });
        servicios.AddHttpClient(HttpEnvioAlEcommerce.NombreDelCliente).ConfigurePrimaryHttpMessageHandler(() => ecommerce.Manejador);
        return servicios.BuildServiceProvider();
    }

    private static AvisoDeTrasladoParaEnviar Aviso(Guid? traslado = null) =>
        new($"traslado:{traslado ?? Guid.NewGuid():D}:aprobado", "ayacucho", 42, "aprobado", "destino", 3, new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));

    private static EstadoDeTrasladoDePedidoParaEnviar Estado(string estado = "recibido") =>
        new($"traslado:{Guid.NewGuid():D}:{estado}", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, estado, new DateOnly(2026, 11, 20), false, new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElAvisoLlegaConLaCredencialSolicitarAvisoYElEsquemaEstricto_Y202EsEntregado()
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        using var sp = Cliente(ecommerce);

        var resultado = await sp.GetRequiredService<IEnvioAlEcommerce>().EnviarAviso(Aviso());

        Assert.Equal(ResultadoDeEnvio.Entregado, resultado);
        var aviso = Assert.Single(ecommerce.Avisos);
        Assert.Equal(("traslado-cambio-de-estado", "ayacucho"), (aviso.Plantilla, aviso.CodigoDeSucursal));
        Assert.Equal(["/api/ecommerce/v1/integraciones/inventario/avisos"], ecommerce.Rutas.ToArray());
        Assert.All(ecommerce.Llamadas, l => Assert.False(l.ConCabeceraDeActor));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task ElEstadoDelTrasladoDeUnPedidoLlegaAlEndpointDeEco0_Y204EsEntregado()
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        using var sp = Cliente(ecommerce);

        var resultado = await sp.GetRequiredService<IEnvioAlEcommerce>().InformarEstadoDelPedido(Estado());

        Assert.Equal(ResultadoDeEnvio.Entregado, resultado);
        var estado = Assert.Single(ecommerce.Estados);
        Assert.Equal(("recibido", 2, new DateOnly(2026, 11, 20)), (estado.Estado, estado.Cantidad, estado.FechaEstimadaDeLlegada));
        Assert.Equal(["/api/ecommerce/v1/integraciones/inventario/traslados"], ecommerce.Rutas.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task RepetirElMismoEventoNoCambiaNadaEnElEcommerce()
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        using var sp = Cliente(ecommerce);
        var envio = sp.GetRequiredService<IEnvioAlEcommerce>();
        var aviso = Aviso();
        var estado = Estado();

        var resultados = new[] { await envio.EnviarAviso(aviso), await envio.EnviarAviso(aviso), await envio.InformarEstadoDelPedido(estado), await envio.InformarEstadoDelPedido(estado) };

        Assert.All(resultados, r => Assert.Equal(ResultadoDeEnvio.Entregado, r));
        Assert.Equal((1, 1), (ecommerce.Avisos.Count, ecommerce.Estados.Count));
        Assert.Equal(4, ecommerce.Rutas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnaCredencialMalRotadaEsUnaCredencialRechazada_NoUnReintentoCadaHora()
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        using var sp = Cliente(ecommerce, "otra-credencial-que-el-ecommerce-no-conoce-xyz");

        var resultado = await sp.GetRequiredService<IEnvioAlEcommerce>().EnviarAviso(Aviso());

        Assert.Equal(ResultadoDeEnvio.CredencialRechazada, resultado);
        Assert.Empty(ecommerce.Avisos);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-08")]
    [InlineData(404, "Permanente")]
    [InlineData(422, "Permanente")]
    [InlineData(400, "Permanente")]
    [InlineData(409, "Permanente")]
    [InlineData(503, "Reintentable")]
    [InlineData(500, "Reintentable")]
    [InlineData(429, "Reintentable")]
    public async Task LosErroresDelEcommerceSeClasificanPorSuCodigo(int codigo, string esperado)
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        ecommerce.Forzado = (HttpStatusCode)codigo;
        using var sp = Cliente(ecommerce);
        var envio = sp.GetRequiredService<IEnvioAlEcommerce>();

        var aviso = await envio.EnviarAviso(Aviso());
        var estado = await envio.InformarEstadoDelPedido(Estado());

        Assert.Equal((Enum.Parse<ResultadoDeEnvio>(esperado), Enum.Parse<ResultadoDeEnvio>(esperado)), (aviso, estado));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnEstadoFueraDeLaListaDeEco0SeRechazaPorElEcommerce_ComoErrorPermanente()
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        using var sp = Cliente(ecommerce);

        var resultado = await sp.GetRequiredService<IEnvioAlEcommerce>().InformarEstadoDelPedido(Estado("aprobado"));

        Assert.Equal(ResultadoDeEnvio.Permanente, resultado);
        Assert.Empty(ecommerce.Estados);
    }

    [Fact]
    public async Task LaCredencialSolicitarAvisoSoloValeEnLasDosRutasDeIntegracion_EnOtraSeRechaza()
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        using var sp = Cliente(ecommerce);
        using var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpEnvioAlEcommerce.NombreDelCliente);
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/api/ecommerce/v1/auditoria");
        solicitud.Options.Set(OpcionesDeLaSolicitudDeServicio.Alcance, AlcanceDeServicio.SolicitarAviso);

        var respuesta = await http.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
    }

    [Fact]
    public async Task UnaLlamadaSinAlcanceDeclaradoNoSale()
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        using var sp = Cliente(ecommerce);
        using var http = sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpEnvioAlEcommerce.NombreDelCliente);

        await Assert.ThrowsAsync<InvalidOperationException>(() => http.PostAsync("/api/ecommerce/v1/integraciones/inventario/avisos", new StringContent("{}")));

        Assert.Empty(ecommerce.Rutas);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task LaEntregaDelBuzonPorElAdaptadorRealLlegaAlEcommerceSimulado_AvisosYEstadoDelPedido()
    {
        await using var ecommerce = await EcommerceDeContrato.Iniciar();
        using var escenario = new EscenarioDeTraslados(bd);
        await escenario.Preparar(
            stockDeCusco: 1,
            pila: s =>
            {
                s.AddSingleton(new OpcionesDeCredencialesDeSalida([new(AlcanceDeServicio.SolicitarAviso, new ValorSecreto(EcommerceDeContrato.Credencial))]));
                s.AgregarSalidaAlEcommerce(new Uri("https://ecommerce.interno"), new OpcionesDelClienteDeServicio { ReintentosMaximos = 0 });
                s.AddHttpClient(HttpEnvioAlEcommerce.NombreDelCliente).ConfigurePrimaryHttpMessageHandler(() => ecommerce.Manejador);
            });
        var (pedido, traslado) = await escenario.VinculadoAprobado();
        await escenario.Ejecutar(Administracion(), new DespacharTrasladoEntrada(traslado));
        await escenario.Ejecutar(Encargado(escenario.Lima), new RecibirTrasladoEntrada(traslado));

        for (var i = 0; i < 3; i++)
        {
            var entrega = await escenario.Pila.Ejecutar<EntregarMensajesSalientesEntrada, ResultadoDeLaEntrega>(ActorActual.DelSistema(), new EntregarMensajesSalientesEntrada());
            Assert.True(entrega.EsExito, entrega.Error?.Clave);
        }

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes WHERE estado <> 'enviado'"));
        Assert.Equal(["en-transito", "recibido"], ecommerce.Estados.Select(e => e.Estado).Order(StringComparer.Ordinal).ToArray());
        Assert.All(ecommerce.Estados, e => Assert.Equal((pedido, traslado), (e.PedidoId, e.TrasladoId)));
        Assert.Contains(ecommerce.Avisos, a => a.CodigoDeSucursal == "lima" && a.EventoId == $"traslado:{traslado:D}:recibido");
        Assert.Equal(10, ecommerce.Rutas.Count);
    }
}
