using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// Hallazgo de K6-1 (perfilado): sin filtros, cada solicitud escribía varias líneas de información del servidor web (<c>Executed endpoint</c>, el resultado, el tiempo) y de <c>HttpClient</c>, con costo de CPU y de salida
/// por solicitud en una VM de 1 OCPU, y con la dirección completa y su consulta (AUD-06, SEG-10). Solo avisos y errores del servidor web y del cliente HTTP; los registros propios de BabelHub conservan su nivel.
/// </summary>
public sealed class RegistroPorSolicitudTests
{
    /// <summary>Un proveedor mínimo: sin ninguno, ningún nivel está activo y el filtro no se puede observar.</summary>
    private sealed class ProveedorAbierto : ILoggerProvider
    {
        private sealed class Abierto : ILogger
        {
            public IDisposable? BeginScope<TState>(TState estado) where TState : notnull => null;

            public bool IsEnabled(LogLevel nivel) => true;

            public void Log<TState>(LogLevel nivel, EventId id, TState estado, Exception? excepcion, Func<TState, Exception?, string> formateador)
            {
            }
        }

        public ILogger CreateLogger(string categoria) => new Abierto();

        public void Dispose()
        {
        }
    }

    private static ILoggerFactory Fabrica(Action<IServiceCollection> componer)
    {
        var servicios = new ServiceCollection();
        componer(servicios);
        servicios.AddLogging(l => l.AddProvider(new ProveedorAbierto()));
        return servicios.BuildServiceProvider().GetRequiredService<ILoggerFactory>();
    }

    [Theory]
    [InlineData("Microsoft.AspNetCore.Routing.EndpointMiddleware")]
    [InlineData("Microsoft.AspNetCore.Http.Result.OkObjectResult")]
    [InlineData("Microsoft.AspNetCore.Hosting.Diagnostics")]
    [InlineData("System.Net.Http.HttpClient.IObtenedorDeClavesPublicadas.LogicalHandler")]
    public void InventarioYEcommerce_NoRegistranInformacionPorSolicitudDelServidorWebNiDelClienteHttp(string categoria)
    {
        var inventario = Fabrica(s => BabelHub.Inventario.Api.Plataforma.DenegacionesHttpExtensiones.AgregarRegistroDeDenegaciones(s));
        var ecommerce = Fabrica(s => BabelHub.Ecommerce.Api.Plataforma.DenegacionesHttpExtensiones.AgregarRegistroDeDenegaciones(s));

        foreach (var fabrica in new[] { inventario, ecommerce })
        {
            var registro = fabrica.CreateLogger(categoria);
            Assert.False(registro.IsEnabled(LogLevel.Information), categoria);
            Assert.True(registro.IsEnabled(LogLevel.Warning), categoria);
        }
    }

    [Fact]
    public void LosRegistrosPropiosDeBabelHub_ConservanSuNivel()
    {
        var fabrica = Fabrica(s => BabelHub.Inventario.Api.Plataforma.DenegacionesHttpExtensiones.AgregarRegistroDeDenegaciones(s));

        Assert.True(fabrica.CreateLogger("BabelHub.Inventario.Api.Plataforma.ManejadorDeExcepciones").IsEnabled(LogLevel.Information));
    }
}
