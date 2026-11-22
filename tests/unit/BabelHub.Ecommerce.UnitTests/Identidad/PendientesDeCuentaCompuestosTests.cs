using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>El compuesto de pendientes de la cuenta separa lo de Pedidos y Devoluciones por sus contratos por tipo y deja el resto en «otros» (identidad-seguridad D5; IDE-27; AC-IDE-24); la suma no cambia y falla cerrado.</summary>
public sealed class PendientesDeCuentaCompuestosTests
{
    private sealed class DePedidos(PendientesDeCuentaPorTipo valor) : IPendientesDeCuentaPorTipo
    {
        public Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(valor.Total);

        public Task<PendientesDeCuentaPorTipo> ContarPorTipo(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(valor);
    }

    private sealed class DeDevoluciones(PendientesDeDevolucionesDeCuenta valor) : IPendientesDeDevolucionesDeCuenta
    {
        public Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(valor.Total);

        public Task<PendientesDeDevolucionesDeCuenta> ContarEnTres(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(valor);
    }

    private sealed class Simple(int cuenta) : IPendientesDeCuenta
    {
        public Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(cuenta);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task Desglosar_SeparaPedidosReembolsosDevolucionesYOtros_YLaSumaCoincide()
    {
        var compuesto = new PendientesDeCuentaCompuestos([
            new DePedidos(new PendientesDeCuentaPorTipo(PedidosSinCerrar: 2, ReembolsosPendientes: 1)),
            new DeDevoluciones(new PendientesDeDevolucionesDeCuenta(DevolucionesAbiertas: 1, ReportesSinDecidir: 1, ReembolsosPendientes: 2)),
            new Simple(3),
            new Simple(1),
        ]);

        var desglose = await compuesto.Desglosar(Guid.NewGuid());

        Assert.Equal(new DesgloseDePendientes(PedidosSinCerrar: 2, Reembolsos: 3, Devoluciones: 2, Otros: 4), desglose);
        Assert.Equal(11, await compuesto.Contar(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public async Task Desglosar_SinModulosOConUnConteoNegativo_Lanza_ENuncaAsumeCero()
    {
        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => new PendientesDeCuentaCompuestos([]).Desglosar(Guid.NewGuid()));
        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => new PendientesDeCuentaCompuestos([new DePedidos(new PendientesDeCuentaPorTipo(-1, 0))]).Desglosar(Guid.NewGuid()));
        await Assert.ThrowsAsync<PendientesNoDisponiblesException>(() => new PendientesDeCuentaCompuestos([new Simple(-1)]).Contar(Guid.NewGuid()));
    }
}
