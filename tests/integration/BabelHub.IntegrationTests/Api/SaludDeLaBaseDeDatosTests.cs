using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Ecommerce = BabelHub.Ecommerce;
using Inventario = BabelHub.Inventario;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// <c>/health/ready</c> detecta la caída de la base de datos (RNF03; lo halló la prueba de carga con k6: sin ninguna comprobación registrada el servicio decía «listo» con la base caída). No necesita Docker: la «base caída» es un
/// puerto cerrado con un plazo corto, y la comprobación real es la misma de los dos servicios (<c>SELECT 1</c> con la cuenta de acceso).
/// </summary>
public sealed class SaludDeLaBaseDeDatosTests
{
    /// <summary>Una cadena a un puerto en el que nadie escucha: abrir la conexión falla de inmediato. La contraseña se genera al vuelo (nunca un secreto fijo).</summary>
    private static string BaseCaida()
    {
        var cadena = new NpgsqlConnectionStringBuilder { Host = "127.0.0.1", Port = 1, Database = "ninguna", Username = "acceso", Timeout = 1, CommandTimeout = 1, Pooling = false };
        cadena.Password = Guid.NewGuid().ToString("N");
        return cadena.ConnectionString;
    }

    private static async Task<(HttpClient Cliente, WebApplication App)> Servidor(Action<IServiceCollection> componentes, Action<WebApplication> mapear)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        componentes(builder.Services);
        var app = builder.Build();
        app.Use(async (contexto, siguiente) =>
        {
            contexto.Connection.RemoteIpAddress = IPAddress.Parse("10.0.0.5");
            await siguiente(contexto);
        });
        mapear(app);
        await app.StartAsync();
        return (app.GetTestClient(), app);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task Inventario_ConLaBaseCaida_ReadyDice503_NombrandoLaBaseDeDatos()
    {
        var (cliente, app) = await Servidor(
            s =>
            {
                s.AddSingleton(new Inventario.Infrastructure.Plataforma.ComprobacionDeLaBaseDeDatos(NpgsqlDataSource.Create(BaseCaida())));
                s.AddSingleton<Inventario.Api.Plataforma.IComprobacionDeSalud, Inventario.Api.Plataforma.ComprobacionDeSaludDeLaBase>();
            },
            a => Inventario.Api.Plataforma.SaludHttp.MapearSalud(a, new Inventario.Api.Plataforma.OpcionesDeSalud { PlazoPorComponente = TimeSpan.FromSeconds(3) }));
        await using var _ = app;

        var respuesta = await cliente.GetAsync("/health/ready");
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Equal("no-disponible", cuerpo.GetProperty("estado").GetString());
        var componente = Assert.Single(cuerpo.GetProperty("componentes").EnumerateArray());
        Assert.Equal("base-de-datos", componente.GetProperty("nombre").GetString());
        Assert.Equal("falla", componente.GetProperty("estado").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task Ecommerce_ConLaBaseCaida_ReadyDice503_NombrandoLaBaseDeDatos()
    {
        var (cliente, app) = await Servidor(
            s =>
            {
                s.AddSingleton(new Ecommerce.Infrastructure.Plataforma.ComprobacionDeLaBaseDeDatos(NpgsqlDataSource.Create(BaseCaida())));
                s.AddSingleton<Ecommerce.Api.Plataforma.IComprobacionDeSalud, Ecommerce.Api.Plataforma.ComprobacionDeSaludDeLaBase>();
            },
            a => Ecommerce.Api.Plataforma.SaludHttp.MapearSalud(a, new Ecommerce.Api.Plataforma.OpcionesDeSalud { PlazoPorComponente = TimeSpan.FromSeconds(3) }));
        await using var _ = app;

        var respuesta = await cliente.GetAsync("/health/ready");
        var cuerpo = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        var componente = Assert.Single(cuerpo.GetProperty("componentes").EnumerateArray());
        Assert.Equal("base-de-datos", componente.GetProperty("nombre").GetString());
        Assert.Equal("falla", componente.GetProperty("estado").GetString());
    }
}
