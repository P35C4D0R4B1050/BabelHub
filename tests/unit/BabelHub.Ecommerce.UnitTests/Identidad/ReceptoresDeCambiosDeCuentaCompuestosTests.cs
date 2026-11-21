using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// El compuesto de los receptores de los cambios de cuenta (identidad-seguridad T58; AC-IDE-71, AC-IDE-72; IDE-99): los llama por módulo en el orden de la cancelación (los desconocidos al final, en el de registro),
/// se detiene en el primero que falla, falla cerrado si no hay ninguno y propaga la cancelación sin envolverla.
/// </summary>
public sealed class ReceptoresDeCambiosDeCuentaCompuestosTests
{
    private sealed class Receptor(string modulo, List<string> llamadas, Exception? falla = null) : IReceptorDeCambiosDeCuenta
    {
        public string Modulo { get; } = modulo;

        public Task AlCambiarElCorreo(Guid usuarioId, string correoAnteriorNormalizado, CancellationToken cancelacion = default)
        {
            llamadas.Add(Modulo);
            return falla is null ? Task.CompletedTask : Task.FromException(falla);
        }

        public Task AlReemplazarLaCuenta(Guid usuarioId, CancellationToken cancelacion = default)
        {
            llamadas.Add(Modulo);
            return falla is null ? Task.CompletedTask : Task.FromException(falla);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-71")]
    public async Task LosLlama_EnElOrdenDeLaCancelacion_ConLosDesconocidosAlFinalEnElDeRegistro()
    {
        var llamadas = new List<string>();
        var compuesto = new ReceptoresDeCambiosDeCuentaCompuestos([new Receptor("zeta", llamadas), new Receptor("reclamaciones", llamadas), new Receptor("alfa", llamadas), new Receptor("pedidos", llamadas)]);

        await compuesto.AlCambiarElCorreo(Guid.NewGuid(), "x@example.test");
        await compuesto.AlReemplazarLaCuenta(Guid.NewGuid());

        Assert.Equal(["pedidos", "reclamaciones", "zeta", "alfa", "pedidos", "reclamaciones", "zeta", "alfa"], llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-72")]
    public async Task SeDetieneEnElPrimeroQueFalla_ConUnaExcepcionSinMensajeNiCausaDelReceptor_YElNombreDelModulo()
    {
        var llamadas = new List<string>();
        var compuesto = new ReceptoresDeCambiosDeCuentaCompuestos([new Receptor("pedidos", llamadas, new InvalidOperationException("las hojas de persona@example.test")), new Receptor("reclamaciones", llamadas)]);

        var excepcion = await Assert.ThrowsAsync<CambiosDeCuentaNoAplicablesException>(() => compuesto.AlCambiarElCorreo(Guid.NewGuid(), "persona@example.test"));

        Assert.Equal(["pedidos"], llamadas);
        Assert.Equal("pedidos", excepcion.Modulo);
        Assert.DoesNotContain("persona@example.test", excepcion.Message, StringComparison.Ordinal);
        Assert.Null(excepcion.InnerException);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-72")]
    public async Task SinNingunReceptor_FallaCerrado()
    {
        var compuesto = new ReceptoresDeCambiosDeCuentaCompuestos([]);

        await Assert.ThrowsAsync<CambiosDeCuentaNoAplicablesException>(() => compuesto.AlCambiarElCorreo(Guid.NewGuid(), "x@example.test"));
        await Assert.ThrowsAsync<CambiosDeCuentaNoAplicablesException>(() => compuesto.AlReemplazarLaCuenta(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-72")]
    public async Task LaCancelacionDelReceptor_SePropagaSinEnvolverla()
    {
        var compuesto = new ReceptoresDeCambiosDeCuentaCompuestos([new Receptor("reclamaciones", [], new OperationCanceledException())]);

        await Assert.ThrowsAsync<OperationCanceledException>(() => compuesto.AlCambiarElCorreo(Guid.NewGuid(), "x@example.test"));
    }
}
