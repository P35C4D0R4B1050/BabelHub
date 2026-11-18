using System.Net;
using System.Text.Json;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Traslados.Interno;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos.Ecommerce;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// T19: el adaptador <c>HttpEnvioAlEcommerce</c> sin red: lo que sale (ruta y método del contrato ECO-0, cuerpo con los DTOs, plantilla de traslado, <c>Idempotency-Key</c> con la clave del evento, alcance <c>solicitar-aviso</c>) y cómo clasifica
/// lo que vuelve (solo el código de estado, nunca el cuerpo): nunca lanza.
/// </summary>
public sealed class HttpEnvioAlEcommerceTests
{
    private sealed class ManejadorFijo(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public List<(HttpRequestMessage Solicitud, string Cuerpo)> Recibidas { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Recibidas.Add((request, request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken)));
            return responder(request);
        }
    }

    private static HttpEnvioAlEcommerce Adaptador(ManejadorFijo manejador) => new(new HttpClient(manejador) { BaseAddress = new Uri("https://ecommerce.interno") });

    private static AvisoDeTrasladoParaEnviar Aviso() => new("traslado:6d0f6b34-5fb7-4a53-bf5d-3a1c9a64c1d1:aprobado", "ayacucho", 42, "aprobado", "destino", 3, new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));

    private static EstadoDeTrasladoDePedidoParaEnviar Estado() =>
        new("traslado:6d0f6b34-5fb7-4a53-bf5d-3a1c9a64c1d1:recibido", Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("6d0f6b34-5fb7-4a53-bf5d-3a1c9a64c1d1"), Guid.Parse("22222222-2222-2222-2222-222222222222"), 2, "recibido", new DateOnly(2026, 11, 20), false, new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElAvisoSaleALaRutaDelContrato_ConLaPlantillaDeTrasladoLosCamposTipadosYLaClaveDeIdempotencia()
    {
        var manejador = new ManejadorFijo(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        var resultado = await Adaptador(manejador).EnviarAviso(Aviso());

        Assert.Equal(ResultadoDeEnvio.Entregado, resultado);
        var (solicitud, cuerpo) = Assert.Single(manejador.Recibidas);
        Assert.Equal((HttpMethod.Post, "/api/ecommerce/v1/integraciones/inventario/avisos"), (solicitud.Method, solicitud.RequestUri!.AbsolutePath));
        Assert.Equal("traslado:6d0f6b34-5fb7-4a53-bf5d-3a1c9a64c1d1:aprobado", Assert.Single(solicitud.Headers.GetValues("Idempotency-Key")));
        Assert.Equal(AlcanceDeServicio.SolicitarAviso, solicitud.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.Alcance, out var alcance) ? alcance : default);
        Assert.True(solicitud.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.Idempotente, out var idempotente) && idempotente);
        var dto = JsonSerializer.Deserialize<SolicitudDeAvisoDeInventarioDto>(cuerpo, JsonSerializerOptions.Web)!;
        Assert.Equal(("traslado:6d0f6b34-5fb7-4a53-bf5d-3a1c9a64c1d1:aprobado", "traslado-cambio-de-estado", "ayacucho"), (dto.EventoId, dto.Plantilla, dto.CodigoDeSucursal));
        Assert.Equal(new Dictionary<string, string> { ["numeroDeTraslado"] = "42", ["estado"] = "aprobado", ["rolDeLaSucursal"] = "destino", ["cantidad"] = "3" }.OrderBy(k => k.Key), dto.Campos.OrderBy(k => k.Key));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-09")]
    public async Task ElEstadoDelPedidoSaleALaRutaDelContrato_ConElDtoDeEco0()
    {
        var manejador = new ManejadorFijo(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var resultado = await Adaptador(manejador).InformarEstadoDelPedido(Estado());

        Assert.Equal(ResultadoDeEnvio.Entregado, resultado);
        var (solicitud, cuerpo) = Assert.Single(manejador.Recibidas);
        Assert.Equal("/api/ecommerce/v1/integraciones/inventario/traslados", solicitud.RequestUri!.AbsolutePath);
        var dto = JsonSerializer.Deserialize<EstadoDeTrasladoDePedidoDto>(cuerpo, JsonSerializerOptions.Web)!;
        Assert.Equal(("traslado:6d0f6b34-5fb7-4a53-bf5d-3a1c9a64c1d1:recibido", 2, "recibido", new DateOnly(2026, 11, 20)), (dto.EventoId, dto.Cantidad, dto.Estado, dto.FechaEstimadaDeLlegada));
        Assert.Equal((Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222")), (dto.PedidoId, dto.EdicionId));
        Assert.Equal(AlcanceDeServicio.SolicitarAviso, solicitud.Options.TryGetValue(OpcionesDeLaSolicitudDeServicio.Alcance, out var alcance) ? alcance : default);
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-08")]
    [InlineData(200, "Entregado")]
    [InlineData(202, "Entregado")]
    [InlineData(204, "Entregado")]
    [InlineData(401, "CredencialRechazada")]
    [InlineData(403, "CredencialRechazada")]
    [InlineData(400, "Permanente")]
    [InlineData(404, "Permanente")]
    [InlineData(409, "Permanente")]
    [InlineData(422, "Permanente")]
    [InlineData(418, "Permanente")]
    [InlineData(302, "Permanente")]
    [InlineData(408, "Reintentable")]
    [InlineData(429, "Reintentable")]
    [InlineData(500, "Reintentable")]
    [InlineData(503, "Reintentable")]
    [InlineData(201, "Reintentable")]
    public void LaRespuestaSeClasificaSoloPorSuCodigoDeEstado(int codigo, string esperado) =>
        Assert.Equal(Enum.Parse<ResultadoDeEnvio>(esperado), HttpEnvioAlEcommerce.Clasificar((HttpStatusCode)codigo));

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElCuerpoDeLaRespuestaNuncaSeLee_UnaCargaHostilNoCambiaElResultado()
    {
        var manejador = new ManejadorFijo(_ => new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("{\"estado\":\"rechazado\"}; DROP TABLE traslados") });

        var resultado = await Adaptador(manejador).EnviarAviso(Aviso());

        Assert.Equal(ResultadoDeEnvio.Entregado, resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task UnaRedCaidaUnPlazoVencidoOUnCortacircuitosAbiertoSeReintentan_YNuncaLanzan()
    {
        var excepciones = new Exception[] { new HttpRequestException("sin red"), new TaskCanceledException("plazo"), new Polly.Timeout.TimeoutRejectedException(), new Polly.CircuitBreaker.BrokenCircuitException() };
        foreach (var excepcion in excepciones)
        {
            var manejador = new ManejadorFijo(_ => throw excepcion);

            Assert.Equal(ResultadoDeEnvio.Reintentable, await Adaptador(manejador).EnviarAviso(Aviso()));
            Assert.Equal(ResultadoDeEnvio.Reintentable, await Adaptador(manejador).InformarEstadoDelPedido(Estado()));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task LaCancelacionDeQuienLlamaSiSePropaga()
    {
        using var cancelacion = new CancellationTokenSource();
        await cancelacion.CancelAsync();
        var manejador = new ManejadorFijo(_ => throw new OperationCanceledException(cancelacion.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Adaptador(manejador).EnviarAviso(Aviso(), cancelacion.Token));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElAvisoNoLlevaTextoLibre_SoloCamposTipados()
    {
        var manejador = new ManejadorFijo(_ => new HttpResponseMessage(HttpStatusCode.Accepted));

        await Adaptador(manejador).EnviarAviso(Aviso());

        var cuerpo = manejador.Recibidas.Single().Cuerpo;
        Assert.DoesNotContain("motivo", cuerpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guia", cuerpo, StringComparison.OrdinalIgnoreCase);
    }
}
