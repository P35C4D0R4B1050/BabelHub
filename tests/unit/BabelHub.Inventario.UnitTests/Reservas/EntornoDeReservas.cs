using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.Domain.Reservas;
using BabelHub.Inventario.UnitTests.Fuentes;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>Los bloqueos asesores que pidió el caso de uso, en orden y en la bitácora común: así una prueba comprueba la jerarquía de D04.</summary>
internal sealed class BloqueosDeReservasEspia(BitacoraDeLlamadas bitacora) : IBloqueosAsesores
{
    public bool EsperaAgotada { get; set; }

    /// <summary>Pedidos que otra transacción tiene bloqueados: <c>IntentarPedido</c> responde falso.</summary>
    public HashSet<Guid> PedidosOcupados { get; } = [];

    public List<Guid> Clientes { get; } = [];

    public List<Guid> Pedidos { get; } = [];

    public Task Cliente(Guid clienteId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("bloqueo.Cliente");
        Clientes.Add(clienteId);
        return EsperaAgotada ? Task.FromException(new EsperaDeBloqueoAgotadaException()) : Task.CompletedTask;
    }

    public Task Pedido(Guid pedidoId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("bloqueo.Pedido");
        Pedidos.Add(pedidoId);
        return EsperaAgotada ? Task.FromException(new EsperaDeBloqueoAgotadaException()) : Task.CompletedTask;
    }

    public Task<bool> IntentarPedido(Guid pedidoId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("bloqueo.IntentarPedido");
        return Task.FromResult(!PedidosOcupados.Contains(pedidoId));
    }

    public Task Recepcion(CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task Devolucion(Guid devolucionId, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

/// <summary>El repositorio de reservas en memoria: guarda lo que se inserta y responde por pedido. Las operaciones de expiración las agregan las pruebas de T19.</summary>
internal sealed class RepositorioDeReservasEnMemoria(BitacoraDeLlamadas bitacora) : IRepositorioDeReservas
{
    public List<ReservaDePedido> Guardadas { get; } = [];

    public List<DatosDeLaSolicitud> Solicitudes { get; } = [];

    public Task<ReservaDePedido?> UltimaDelPedido(Guid pedidoId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.UltimaDelPedido");
        return Task.FromResult(Guardadas.Where(r => r.PedidoId == pedidoId).OrderByDescending(r => r.IniciadaEn).FirstOrDefault());
    }

    public Task Insertar(ReservaDePedido reserva, DatosDeLaSolicitud solicitud, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.Insertar");
        Guardadas.Add(reserva);
        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<Guid>> PedidosConVencidas(IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<LineaExpirada>> ExpirarLineasVencidas(Guid pedidoId, IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        throw new NotSupportedException();

    public Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        throw new NotSupportedException();
}

/// <summary>
/// Todo lo que los casos de uso de <c>Reservas</c> necesitan, en memoria y sobre el entorno de <c>Fuentes</c> (tres fuentes: Lima, la de recepción, Cusco y Ayacucho, cada una en su región): el cliente de prueba como actor,
/// el stock real en memoria con sus reglas, el directorio de fuentes, los bloqueos y el reloj.
/// </summary>
internal sealed class EntornoDeReservas
{
    public EntornoDeReservas(ActorDePrueba? actor = null)
    {
        Base = new EntornoDeFuentes(actor ?? ActorDePrueba.DeCliente());
        Reservas = new RepositorioDeReservasEnMemoria(Base.Bitacora);
        Bloqueos = new BloqueosDeReservasEspia(Base.Bitacora);
    }

    public EntornoDeFuentes Base { get; }

    public RepositorioDeReservasEnMemoria Reservas { get; }

    public BloqueosDeReservasEspia Bloqueos { get; }

    public Guid ClienteId => Base.Actor.Actual!.UsuarioId!.Value;

    public AutorizadorDeReservas Autorizador => new(Base.Actor, Base.Denegaciones);

    public DirectorioDeFuentes Directorio => new(Base.Lectura, Base.Fuentes, Base.Rutas, Base.Fichas);

    public IAplicadorDeMovimientos Aplicador => new AplicadorDeMovimientos(Base.Existencias);

    public IMovimientosDeStock Stock => new MovimientosDeStock(Base.Fichas, Base.Existencias, Base.Reservas, Aplicador);

    public Guid Impresa() => Base.Impresa();

    public Guid Digital() => Base.Fichas.Agregar(FormatoDeEdicion.Digital).EdicionId;

    public ReservarUnidades Reservar(IEstrategiaDeSeleccion? estrategia = null) =>
        new(
            Autorizador,
            new ValidadorDeReservar(),
            Bloqueos,
            Reservas,
            Directorio,
            Stock,
            new SelectorDeFuentes(Directorio, estrategia ?? new SeleccionPorCercania()),
            new ConstructorDeLaReserva(Directorio),
            Base.Reloj);

    public static LineaSolicitada Linea(Guid edicionId, int cantidad = 1, Guid? lineaId = null) => new(lineaId ?? Guid.NewGuid(), edicionId, cantidad);

    public static ReservarEntrada ARecojo(string sucursal, IReadOnlyList<LineaSolicitada> lineas, Guid? pedidoId = null, string? clave = null) =>
        new(pedidoId ?? Guid.NewGuid(), TipoDeEntregaDeReserva.Recojo, sucursal, null, lineas, clave ?? ClaveNueva());

    public static ReservarEntrada ADomicilio(string region, IReadOnlyList<LineaSolicitada> lineas, Guid? pedidoId = null, string? clave = null) =>
        new(pedidoId ?? Guid.NewGuid(), TipoDeEntregaDeReserva.Domicilio, null, region, lineas, clave ?? ClaveNueva());

    public static string ClaveNueva() => Guid.NewGuid().ToString("N");

    /// <summary>El físico y lo reservado de una pareja, tal como está en la tabla en memoria.</summary>
    public (int Fisico, int Reservado) FilaDe(Fuente fuente, Guid edicionId) => Base.Existencias.Filas[(fuente.Id, edicionId)];
}
