using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>El entorno de las pruebas de Envíos: los casos de uso reales con repositorios y contratos de otros módulos en memoria. Crece con cada tarea.</summary>
internal sealed class EntornoDeEnvios
{
    public static readonly DateTimeOffset AhoraInicial = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    public EntornoDeEnvios(ActorDePrueba? actor = null, DateTimeOffset? ahora = null)
    {
        Actor = actor ?? ActorDePrueba.DePersonalDeLogistica();
        Reloj = new RelojFijo(ahora ?? AhoraInicial);
        Envios = new RepositorioDeEnviosEnMemoria();
        Unidad = new UnidadDeTrabajoReversible(Envios);
        Pedidos = new PedidosParaEnviosDoble(Unidad);
        Auditoria = new AuditoriaDelCatalogoEspia();
        Denegaciones = new DenegacionesDelCatalogoEspia();

        Crear = new CrearEnvio(Pedidos, Envios, Reloj);
        Cancelar = new CancelarEnviosDelPedido(Envios, Auditoria, Reloj);
        Despacho = new DespachoDeEnviosDeLaAplicacion(Crear, Cancelar);
    }

    public ActorDePrueba Actor { get; }

    public RelojFijo Reloj { get; }

    public RepositorioDeEnviosEnMemoria Envios { get; }

    public UnidadDeTrabajoReversible Unidad { get; }

    public PedidosParaEnviosDoble Pedidos { get; }

    public AuditoriaDelCatalogoEspia Auditoria { get; }

    public DenegacionesDelCatalogoEspia Denegaciones { get; }

    public CrearEnvio Crear { get; }

    public CancelarEnviosDelPedido Cancelar { get; }

    public DespachoDeEnviosDeLaAplicacion Despacho { get; }

    /// <summary>Siembra un pedido a domicilio con las líneas dadas y lo devuelve.</summary>
    public PedidoParaEnvios SembrarPedido(bool aDomicilio = true, params LineaDePedidoParaEnvios[] lineas)
    {
        var pedido = new PedidoParaEnvios(Guid.NewGuid(), "PED-2026-0001", Guid.NewGuid(), aDomicilio, lineas);
        Pedidos.Sembrar(pedido);
        return pedido;
    }

    public static LineaDePedidoParaEnvios Impresa(string fuente = "lima-central", bool lista = true) => new(Guid.NewGuid(), false, fuente, lista);

    public static LineaDePedidoParaEnvios Digital() => new(Guid.NewGuid(), true, null, true);
}
