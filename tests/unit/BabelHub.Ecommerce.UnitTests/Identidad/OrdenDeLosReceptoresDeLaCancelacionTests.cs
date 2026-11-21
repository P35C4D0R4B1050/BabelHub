using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>El orden de los receptores de la cancelación no depende del orden de la composición (decisión D04 de Devoluciones; AC-DEV-26, AC-IDE-43).</summary>
public sealed class OrdenDeLosReceptoresDeLaCancelacionTests
{
    private sealed class Receptor(string modulo) : IAplicadorDeCancelacion
    {
        public string Modulo { get; } = modulo;

        public Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public void Pedidos_va_antes_que_Devoluciones_y_esta_antes_que_Pagos_sea_cual_sea_el_orden_de_registro()
    {
        var registrados = new[] { "carrito", "pagos", "reclamaciones", "pedidos", "biblioteca", "devoluciones", "otro-modulo" }.Select(m => (IAplicadorDeCancelacion)new Receptor(m));

        var ordenados = EjecutorDeLaCancelacion.EnElOrdenDeLosReceptores(registrados).Select(r => r.Modulo).ToArray();

        Assert.Equal(["pedidos", "devoluciones", "pagos", "carrito", "biblioteca", "reclamaciones", "otro-modulo"], ordenados);
    }
}
