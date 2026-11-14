using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class EjecutorDePasosDeRestauracionTests
{
    private static readonly DateTimeOffset Respaldo = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Un valor sembrado distinto en cada ejecución: nunca un secreto fijo en el repositorio.</summary>
    private static readonly string ValorSembrado = "Sup3r" + Guid.NewGuid().ToString("N");

    private sealed class PasoDePrueba(int orden, string nombre, List<string> bitacora, ResultadoDePaso resultado = ResultadoDePaso.Correcto, bool lanza = false) : IPasoDeRestauracion
    {
        public int Orden { get; } = orden;

        public string Nombre { get; } = nombre;

        public Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default)
        {
            bitacora.Add(Nombre);
            return lanza ? throw new InvalidOperationException("falló con password=" + ValorSembrado + "") : Task.FromResult(resultado);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-43")]
    public async Task LosPasosSeEjecutanPorModuloPlataformaIdentidadYAuditoriaSinImportarElOrdenDeRegistro()
    {
        var bitacora = new List<string>();
        var ejecutor = new EjecutorDePasosDeRestauracion(
        [
            new PasoDePrueba(OrdenDePasoDeRestauracion.Auditoria, "depurar", bitacora),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Identidad + 5, "reenviar-bajas", bitacora),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Plataforma + 10, "reaplicar", bitacora),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Identidad, "rotar-claves-e-invalidar-sesiones", bitacora),
            new PasoDePrueba(OrdenDePasoDeRestauracion.Plataforma, "verificar-registro", bitacora),
        ]);

        var resultado = await ejecutor.Ejecutar();

        Assert.True(resultado.PermiteAbrir);
        Assert.Equal(["verificar-registro", "reaplicar", "rotar-claves-e-invalidar-sesiones", "reenviar-bajas", "depurar"], bitacora);
        Assert.Equal(bitacora, resultado.PasosEjecutados);
        Assert.Null(resultado.PasoFallido);
    }

    [Fact]
    public async Task ConElMismoOrden_SeDesempataPorNombreDeFormaDeterminista()
    {
        var bitacora = new List<string>();
        var ejecutor = new EjecutorDePasosDeRestauracion(
        [
            new PasoDePrueba(100, "b", bitacora),
            new PasoDePrueba(100, "a", bitacora),
            new PasoDePrueba(100, "c", bitacora),
        ]);

        await ejecutor.Ejecutar();

        Assert.Equal(["a", "b", "c"], bitacora);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task ElPrimerPasoQueFalla_DetieneLaRestauracionYElServicioNoSeAbre()
    {
        var bitacora = new List<string>();
        var ejecutor = new EjecutorDePasosDeRestauracion(
        [
            new PasoDePrueba(100, "verificar-registro", bitacora),
            new PasoDePrueba(110, "reaplicar", bitacora, ResultadoDePaso.Fallido),
            new PasoDePrueba(200, "rotar-claves", bitacora),
        ]);

        var resultado = await ejecutor.Ejecutar();

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal("reaplicar", resultado.PasoFallido);
        Assert.Equal(["verificar-registro", "reaplicar"], bitacora);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task UnPasoQueLanzaUnaExcepcion_CuentaComoFallidoYSuMensajeNoSale()
    {
        var bitacora = new List<string>();
        var ejecutor = new EjecutorDePasosDeRestauracion([new PasoDePrueba(100, "verificar-registro", bitacora, lanza: true)]);

        var resultado = await ejecutor.Ejecutar();

        Assert.False(resultado.PermiteAbrir);
        Assert.Equal("verificar-registro", resultado.PasoFallido);
        Assert.DoesNotContain(ValorSembrado, string.Join(' ', resultado.PasosEjecutados) + resultado.PasoFallido, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SinPasos_LaRestauracionPuedeAbrir()
    {
        var resultado = await new EjecutorDePasosDeRestauracion([]).Ejecutar();

        Assert.True(resultado.PermiteAbrir);
        Assert.Empty(resultado.PasosEjecutados);
    }

    [Fact]
    public async Task LaCancelacionDeQuienEjecuta_SePropagaYNoCuentaComoUnPasoFallido()
    {
        using var origen = new CancellationTokenSource();
        await origen.CancelAsync();
        var ejecutor = new EjecutorDePasosDeRestauracion([new PasoQueCancela(origen.Token)]);

        await Assert.ThrowsAsync<OperationCanceledException>(() => ejecutor.Ejecutar(origen.Token));
    }

    private sealed class PasoQueCancela(CancellationToken token) : IPasoDeRestauracion
    {
        public int Orden => 1;

        public string Nombre => "cancelable";

        public Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(ResultadoDePaso.Correcto);
        }
    }
}
