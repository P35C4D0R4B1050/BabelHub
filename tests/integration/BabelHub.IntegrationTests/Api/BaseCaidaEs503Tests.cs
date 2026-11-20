using System.Net.Sockets;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// Hallazgo de K6-1 (resiliencia): con PostgreSQL caído o sin conexiones libres, las operaciones respondían 500 «error inesperado». Una base inalcanzable (fallo transitorio, plazo vencido o falta de
/// conexiones) es «servicio no disponible» (503 uniforme, el cliente reintenta); un error de base que no es transitorio (un defecto) sigue siendo inesperado y se registra.
/// </summary>
public sealed class BaseCaidaEs503Tests
{
    public static TheoryData<Exception> Transitorias => new()
    {
        new NpgsqlException("conexión perdida", new SocketException()),
        new NpgsqlException("plazo vencido", new TimeoutException()),
        new InvalidOperationException("envoltura", new NpgsqlException("sin red", new SocketException())),
    };

    private static async Task<(int Estado, JsonElement Cuerpo)> Inventario(Exception e)
    {
        var contexto = new DefaultHttpContext { TraceIdentifier = "t-1" };
        contexto.Response.Body = new MemoryStream();
        var manejador = new BabelHub.Inventario.Api.Plataforma.ManejadorDeExcepciones(new BabelHub.Inventario.Api.Plataforma.OpcionesDeErrores("INV"), NullLogger<BabelHub.Inventario.Api.Plataforma.ManejadorDeExcepciones>.Instance);
        Assert.True(await manejador.TryHandleAsync(contexto, e, CancellationToken.None));
        contexto.Response.Body.Position = 0;
        return (contexto.Response.StatusCode, (await JsonDocument.ParseAsync(contexto.Response.Body)).RootElement.Clone());
    }

    private static async Task<(int Estado, JsonElement Cuerpo)> Ecommerce(Exception e)
    {
        var contexto = new DefaultHttpContext { TraceIdentifier = "t-1" };
        contexto.Response.Body = new MemoryStream();
        var manejador = new BabelHub.Ecommerce.Api.Plataforma.ManejadorDeExcepciones(new BabelHub.Ecommerce.Api.Plataforma.OpcionesDeErrores("ECO"), NullLogger<BabelHub.Ecommerce.Api.Plataforma.ManejadorDeExcepciones>.Instance);
        Assert.True(await manejador.TryHandleAsync(contexto, e, CancellationToken.None));
        contexto.Response.Body.Position = 0;
        return (contexto.Response.StatusCode, (await JsonDocument.ParseAsync(contexto.Response.Body)).RootElement.Clone());
    }

    [Theory]
    [MemberData(nameof(Transitorias))]
    public async Task UnaBaseInalcanzable_EnElInventario_Es503Uniforme(Exception e)
    {
        var (estado, cuerpo) = await Inventario(e);

        Assert.Equal(503, estado);
        Assert.Equal("INV-SERVICIO-NO-DISPONIBLE", cuerpo.GetProperty("code").GetString());
        Assert.DoesNotContain("conexión", cuerpo.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [MemberData(nameof(Transitorias))]
    public async Task UnaBaseInalcanzable_EnElEcommerce_Es503Uniforme(Exception e)
    {
        var (estado, cuerpo) = await Ecommerce(e);

        Assert.Equal(503, estado);
        Assert.Equal("ECO-SERVICIO-NO-DISPONIBLE", cuerpo.GetProperty("code").GetString());
    }

    [Fact]
    public async Task UnTimeoutQueNoEsDeLaBase_SigueSiendoInesperado()
    {
        var otro = new TimeoutException("plazo de un cliente HTTP");

        Assert.Equal(500, (await Inventario(otro)).Estado);
        Assert.Equal(500, (await Ecommerce(otro)).Estado);
    }

    [Fact]
    public async Task UnErrorDeBaseNoTransitorio_SigueSiendoInesperado()
    {
        var defecto = new PostgresException("syntax error", "ERROR", "ERROR", "42601");

        Assert.Equal(500, (await Inventario(defecto)).Estado);
        Assert.Equal(500, (await Ecommerce(defecto)).Estado);
    }
}
