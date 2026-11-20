using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>La composición real de Pedidos (sin base de datos): todo lo que el módulo expone y sus casos de uso se resuelven con solo los bordes de los demás módulos.</summary>
public sealed class ComposicionRealDePedidosTests
{
    [Fact]
    public async Task La_composicion_resuelve_las_fachadas_los_casos_de_uso_y_las_lecturas_para_otros_modulos()
    {
        await using var escenario = EscenarioDePedidos.Crear(EscenarioDePedidos.UnidadSinConexion());

        Assert.NotNull(escenario.Resolver<IPedidosDelCliente>());
        Assert.NotNull(escenario.Resolver<IGestionDeRecojo>());
        Assert.NotNull(escenario.Resolver<IGestionDeDespacho>());
        Assert.NotNull(escenario.Resolver<IAdministracionDePedidos>());
        Assert.NotNull(escenario.Resolver<IReceptorDeEstadosDeTraslado>());
        Assert.IsType<ConfirmarCompra>(escenario.Resolver<BabelHub.Ecommerce.Application.Pagos.IConfirmacionDeCompra>());
        Assert.NotNull(escenario.Resolver<IProgresoDeEnvios>());
        Assert.NotNull(escenario.Resolver<IPedidosParaEnvios>());
        Assert.NotNull(escenario.Resolver<IPedidosParaDevoluciones>());
        Assert.NotNull(escenario.Resolver<IContactoDeEntregaDelPedido>());
        Assert.NotNull(escenario.Resolver<CompletarOperacionesEnCurso>());
        Assert.NotNull(escenario.Resolver<MarcarLineasNoRecogidas>());
        Assert.NotNull(escenario.Resolver<AnonimizarContactosVencidos>());
        Assert.NotNull(escenario.Resolver<LiberarContactoDeIntenciones>());
        Assert.NotNull(escenario.Resolver<RevisarPedidosSinEstadoFinal>());
    }
}
