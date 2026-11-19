using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using PedidoDeDominio = BabelHub.Ecommerce.Domain.Pedidos.Pedido;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T27 (PED-15; AC-PED-13): la revisión de lo que no llega a un estado final y la purga de los estados de traslado.</summary>
public sealed class RevisarPedidosSinEstadoFinalTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static int _contador;

    private static string Numero() => "ABCD23456" + "ABCDEFGHJKLMNPQRSTUVWXYZ"[Interlocked.Increment(ref _contador) % 24];

    private static PedidoDeDominio Recojo(DateTimeOffset creadoEn)
    {
        var linea = new DatosDeLineaNueva(Guid.NewGuid(), Guid.NewGuid(), false, "Libro", null, 1, 50m, 50m, "LIM", false, null, null);
        return PedidoDeDominio.Crear(new DatosDePedidoNuevo(Guid.NewGuid(), Numero(), Guid.NewGuid(), TipoDeEntregaDelPedido.Recojo, "LIM", null, 0m, 50m, null, new("t-1", creadoEn), new("p-1", creadoEn), null, [linea], creadoEn)).Pedido!;
    }

    private static (RevisarPedidosSinEstadoFinal Caso, RepositorioDePedidosFalso Repositorio, AlertasTecnicasEspia Alertas) Armar(ActorDePrueba? actor = null)
    {
        var repositorio = new RepositorioDePedidosFalso();
        var alertas = new AlertasTecnicasEspia();
        return (new RevisarPedidosSinEstadoFinal(new AutorizadorDePedidos(actor ?? ActorDePrueba.DelSistema(), new DenegacionesDelCatalogoEspia(), new ResolucionDeSucursalFalsa()), repositorio, alertas, new RelojFijo(Ahora)), repositorio, alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-13")]
    public async Task Una_linea_no_recogida_de_mas_de_30_dias_o_una_impresa_de_mas_de_90_dias_sin_estado_final_publica_una_alerta_sin_modificar_nada()
    {
        var (caso, repositorio, alertas) = Armar();
        var noRecogida = Recojo(Ahora - TimeSpan.FromDays(60));
        noRecogida.PrepararLineas([noRecogida.Lineas[0].Id], Ahora - TimeSpan.FromDays(50));
        noRecogida.MarcarListas([noRecogida.Lineas[0].Id], Ahora - TimeSpan.FromDays(50));
        noRecogida.MarcarNoRecogidas(Ahora - TimeSpan.FromDays(31), new HashSet<Guid>());
        var estancada = Recojo(Ahora - TimeSpan.FromDays(91));
        repositorio.Poner(noRecogida);
        repositorio.Poner(estancada);

        var resultado = await caso.Revisar();

        Assert.Equal(new ResultadoDeLaRevisionSinEstadoFinal(1, 1, 0), resultado.Valor);
        Assert.Contains(alertas.Alertas, a => a.Motivo == RevisarPedidosSinEstadoFinal.AlertaOperacionPendiente);
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, noRecogida.Lineas[0].EstadoImpreso);
    }

    [Fact]
    public async Task Sin_lineas_vencidas_no_alerta_y_purga_los_estados_de_traslado_de_mas_de_90_dias()
    {
        var (caso, repositorio, alertas) = Armar();
        repositorio.Poner(Recojo(Ahora - TimeSpan.FromDays(10)));
        repositorio.EstadosDeTraslado["traslado:a:recibido"] = new EstadoDeTrasladoRegistrado("traslado:a:recibido", Guid.NewGuid(), Guid.NewGuid(), "recibido", "h", Ahora - TimeSpan.FromDays(91));
        repositorio.EstadosDeTraslado["traslado:b:recibido"] = new EstadoDeTrasladoRegistrado("traslado:b:recibido", Guid.NewGuid(), Guid.NewGuid(), "recibido", "h", Ahora - TimeSpan.FromDays(89));

        var resultado = await caso.Revisar();
        var repetida = await caso.Revisar();

        Assert.Equal(new ResultadoDeLaRevisionSinEstadoFinal(0, 0, 1), resultado.Valor);
        Assert.Equal(new ResultadoDeLaRevisionSinEstadoFinal(0, 0, 0), repetida.Valor);
        Assert.Empty(alertas.Alertas);
        Assert.Single(repositorio.EstadosDeTraslado);
    }

    [Fact]
    public async Task Solo_el_sistema_revisa()
    {
        var (caso, _, _) = Armar(ActorDePrueba.DeAdministradorCentral());

        var resultado = await caso.Revisar();

        Assert.Equal("prohibido", resultado.Error!.Clave);
    }
}
