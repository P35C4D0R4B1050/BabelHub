using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Reservas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>Lo que llega al registro del servicio: el nivel, el texto y los campos estructurados. Seguro entre hilos.</summary>
internal sealed class RegistrosCapturados : ILoggerProvider, ILogger
{
    private readonly object _candado = new();

    public List<(LogLevel Nivel, string Texto, IReadOnlyDictionary<string, object?> Campos)> Entradas { get; } = [];

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var campos = state is IEnumerable<KeyValuePair<string, object?>> pares ? pares.Where(p => p.Key != "{OriginalFormat}").ToDictionary(p => p.Key, p => p.Value) : [];
        lock (_candado)
        {
            Entradas.Add((logLevel, formatter(state, exception), campos));
        }
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// T44: la alerta de concentración con el contenedor de producción y PostgreSQL (SEL-47): la alerta sale cuando la reserva ya quedó guardada, el máximo se configura y el aviso del registro lleva solo la edición, la
/// sucursal y las unidades, también a través de la redacción de registros.
/// </summary>
public sealed class ConcentracionDeReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly Dictionary<string, string?> MaximoDeDiez = new() { [ConfiguracionDeLaConcentracion.Clave] = "10" };

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ReservarEntrada PorSucursal(Guid libro, string sucursal, int cantidad) =>
        new(Guid.NewGuid(), TipoDeEntregaDeReserva.Domicilio, null, "lima", [new LineaSolicitada(Guid.NewGuid(), libro, cantidad, sucursal)], Guid.NewGuid().ToString("N"));

    [Fact]
    [Trait("Criterio", "AC-SEL-52")]
    public async Task ConElMaximoEnDiez_OnceUnidadesGeneranUnaAlertaYSuAvisoRedactado_ConLaReservaGuardada()
    {
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 20, 0);
        var registros = new RegistrosCapturados();
        using var pila = new PilaDeReservas(bd, ajustes: s => s.AddLogging(b => b.AddProvider(registros)).UsarRedaccionDeRegistros(), configuracion: MaximoDeDiez);

        var resultado = await pila.Reservar(Cliente(), PorSucursal(libro, "cusco", 11));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(["concentracion-de-reservas"], pila.AlertasPublicadas.Tecnicas);
        var aviso = Assert.Single(registros.Entradas, e => e.Campos.ContainsKey("CodigoDeSucursal"));
        Assert.Equal(LogLevel.Warning, aviso.Nivel);
        Assert.Equal(["CodigoDeSucursal", "EdicionId", "Unidades"], aviso.Campos.Keys.Order(StringComparer.Ordinal));
        Assert.Contains($"edición {libro}", aviso.Texto, StringComparison.Ordinal);
        Assert.Contains("sucursal cusco, 11 unidades", aviso.Texto, StringComparison.Ordinal);
        Assert.Equal((20, 11), await bd.StockDe(cusco, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-52")]
    public async Task SiPublicarLaAlertaFalla_LaReservaSeAceptaYSeConserva()
    {
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 20, 0);
        using var pila = new PilaDeReservas(bd, ajustes: s => s.AddSingleton<BabelHub.Inventario.Application.Auditoria.IAlertas, AlertasCaidas>(), configuracion: MaximoDeDiez);
        var resultado = await pila.Reservar(Cliente(), PorSucursal(libro, "cusco", 11));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((20, 11), await bd.StockDe(cusco, libro));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-51")]
    public async Task UnaReservaDeshechaPorFaltaDeStockNoCuenta()
    {
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 12, 0);
        using var pila = new PilaDeReservas(bd, configuracion: MaximoDeDiez);

        Assert.False((await pila.Reservar(Cliente(), PorSucursal(libro, "cusco", 13))).EsExito);
        Assert.True((await pila.Reservar(Cliente(), PorSucursal(libro, "cusco", 10))).EsExito);

        Assert.Empty(pila.AlertasPublicadas.Tecnicas);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-51")]
    [InlineData(null, 100)]
    [InlineData("", 100)]
    [InlineData("1", 1)]
    [InlineData("250", 250)]
    public void ElMaximoEsCienSiFaltaYElConfiguradoSiEsValido(string? valor, int esperado) =>
        Assert.Equal(esperado, ConfiguracionDeLaConcentracion.Maximo(Configuracion(valor)));

    [Theory]
    [Trait("Criterio", "AC-SEL-52")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("cien")]
    [InlineData(" 10")]
    [InlineData("99999999999")]
    public void UnMaximoInvalidoImpideArrancar_YElErrorNombraLaVariable(string valor)
    {
        var error = Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AgregarModuloReservas(Configuracion(valor)));

        Assert.Contains("BABELHUB__Inventario__MaximoApartadoParaAlertar", error.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuracion(string? valor) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [ConfiguracionDeLaConcentracion.Clave] = valor }).Build();

    private sealed class AlertasCaidas : BabelHub.Inventario.Application.Auditoria.IAlertas
    {
        public Task PublicarTecnica(BabelHub.Inventario.Application.Auditoria.CodigoDeAlerta motivo, CancellationToken cancelacion = default) =>
            throw new InvalidOperationException("Las alertas no responden.");

        public Task PublicarDeSeguridad(BabelHub.Inventario.Application.Auditoria.CodigoDeAlerta motivo, CancellationToken cancelacion = default) =>
            throw new InvalidOperationException("Las alertas no responden.");
    }
}
