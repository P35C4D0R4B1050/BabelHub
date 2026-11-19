using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.UnitTests.Auditoria;
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
        Auditoria = new AuditoriaDeEnviosEspia();
        Unidad = new UnidadDeTrabajoReversible(Envios, Auditoria);
        Pedidos = new PedidosParaEnviosDoble(Unidad);
        Denegaciones = new DenegacionesDelCatalogoEspia();

        Contactos = new ContactoDeEntregaDoble(Unidad);
        Operador = new OperadorLogisticoDoble(Unidad);
        Alertas = new AlertasTecnicasEspia();
        var autorizador = new AutorizadorDeEnvios(Actor, Denegaciones);
        SolicitarGuia = new SolicitarGuia(autorizador, Envios, Contactos, Operador, Unidad, Auditoria, Alertas, Reloj);
        Reservas = new ReservasDelEnvioDoble(Unidad);
        Progreso = new ProgresoDeEnviosDoble(Unidad, Envios);
        Informador = new InformadorDeProgreso(Envios, Progreso, Unidad, Reloj);
        PasarAEnReparto = new PasarAEnReparto(autorizador, Envios, Reservas, Informador, Unidad, Auditoria, Alertas, Reloj);
        MarcarEntregado = new MarcarEntregado(autorizador, Envios, Unidad, Auditoria, Informador, Reloj);
        Listar = new ListarEnvios(autorizador, Envios);
        Obtener = new ObtenerEnvio(autorizador, Envios);
        EnviosDelPedido = new ObtenerEnviosDeUnPedido(Actor, Denegaciones, Pedidos, Envios);
        InformarProgreso = new InformarProgresoAPedidos(ActorDePrueba.DelSistema(), Envios, Informador, Alertas, Reloj);
        Crear = new CrearEnvio(Pedidos, Envios, Reloj);
        Cancelar = new CancelarEnviosDelPedido(Envios, Auditoria, Reloj);
        Despacho = new DespachoDeEnviosDeLaAplicacion(Crear, Cancelar);
    }

    public ActorDePrueba Actor { get; }

    public RelojFijo Reloj { get; }

    public RepositorioDeEnviosEnMemoria Envios { get; }

    public UnidadDeTrabajoReversible Unidad { get; }

    public PedidosParaEnviosDoble Pedidos { get; }

    public AuditoriaDeEnviosEspia Auditoria { get; }

    public DenegacionesDelCatalogoEspia Denegaciones { get; }

    public ContactoDeEntregaDoble Contactos { get; }

    public OperadorLogisticoDoble Operador { get; }

    public AlertasTecnicasEspia Alertas { get; }

    public SolicitarGuia SolicitarGuia { get; }

    public ReservasDelEnvioDoble Reservas { get; }

    public ProgresoDeEnviosDoble Progreso { get; }

    public ListarEnvios Listar { get; }

    public ObtenerEnvio Obtener { get; }

    public ObtenerEnviosDeUnPedido EnviosDelPedido { get; }

    public InformadorDeProgreso Informador { get; }

    public PasarAEnReparto PasarAEnReparto { get; }

    public MarcarEntregado MarcarEntregado { get; }

    public InformarProgresoAPedidos InformarProgreso { get; }

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
