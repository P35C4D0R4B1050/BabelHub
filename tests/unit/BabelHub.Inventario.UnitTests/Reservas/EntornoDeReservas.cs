using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.UnitTests.Plataforma;
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

    /// <summary>Los pedidos que se intentó bloquear sin esperar, en el orden en que se intentó.</summary>
    public List<Guid> Intentados { get; } = [];

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
        Intentados.Add(pedidoId);
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

    /// <summary>Si es verdadero, <c>Insertar</c> responde como la restricción única de la base ante una cabecera o una línea activa concurrentes.</summary>
    public bool InsertarChocaConUnaConcurrente { get; set; }

    public Task<ReservaDePedido?> UltimaDelPedido(Guid pedidoId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.UltimaDelPedido");
        return Task.FromResult(Guardadas.Where(r => r.PedidoId == pedidoId).OrderByDescending(r => r.Reemplazos).ThenByDescending(r => r.IniciadaEn).FirstOrDefault());
    }

    /// <summary>Los titulares dados de baja (SEL-35).</summary>
    public HashSet<Guid> Bajas { get; } = [];

    public Task<DateTimeOffset?> ExpiracionDeLaPendienteDelCliente(Guid clienteId, Guid exceptoPedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.PendienteDelCliente");
        var vence = Guardadas
            .Where(r => r.ClienteId == clienteId && r.PedidoId != exceptoPedidoId && r.TienePendienteVigente(ahora))
            .Select(r => r.FechaDeExpiracion)
            .Max();
        return Task.FromResult(vence);
    }

    public Task<ReservaPorClave?> PorClave(Guid clienteId, string clave, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.PorClave");
        var indice = Solicitudes.FindIndex(s => s.ClaveDeIdempotencia == clave && Guardadas[Solicitudes.IndexOf(s)].ClienteId == clienteId);
        return Task.FromResult(indice < 0 ? null : new ReservaPorClave(Guardadas[indice], Solicitudes[indice].HuellaDeLaCarga));
    }

    public Task<bool> ClienteDadoDeBaja(Guid clienteId, CancellationToken cancelacion = default) => Task.FromResult(Bajas.Contains(clienteId));

    public Task Insertar(ReservaDePedido reserva, DatosDeLaSolicitud solicitud, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.Insertar");
        if (InsertarChocaConUnaConcurrente)
        {
            throw new ReservaDuplicadaException();
        }

        Guardadas.Add(reserva);
        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }

    /// <summary>Las líneas pendientes de cabeceras ya vencidas, de esas ediciones (todas si es nulo).</summary>
    private IEnumerable<(ReservaDePedido Reserva, LineaDeReserva Linea)> Vencidas(IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora) =>
        Guardadas
            .Where(r => r.EstaVencida(ahora))
            .SelectMany(r => r.Lineas.Where(l => l.Estado == EstadoDeLinea.Pendiente && (edicionIds is null || edicionIds.Contains(l.EdicionId))).Select(l => (r, l)));

    public Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.VencidasPorEdicion");
        return Task.FromResult<IReadOnlyList<UnidadesVencidas>>(
            [.. Vencidas(edicionIds, ahora)
                .GroupBy(v => (v.Linea.FuenteId, v.Linea.EdicionId))
                .Select(g => new UnidadesVencidas(g.Key.FuenteId, g.Key.EdicionId, g.Sum(v => v.Linea.Cantidad)))]);
    }

    public Task<IReadOnlyList<Guid>> PedidosConVencidas(IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.PedidosConVencidas");
        return Task.FromResult<IReadOnlyList<Guid>>([.. Vencidas(edicionIds, ahora).Select(v => v.Reserva.PedidoId).Distinct().Order()]);
    }

    public Task<IReadOnlyList<LineaVencida>> VencidasDelPedido(Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.VencidasDelPedido");
        return Task.FromResult<IReadOnlyList<LineaVencida>>(
            [.. Vencidas(null, ahora).Where(v => v.Reserva.PedidoId == pedidoId).Select(v => new LineaVencida(pedidoId, v.Linea.Id, v.Linea.LineaId, v.Linea.EdicionId, v.Linea.FuenteId, v.Linea.Cantidad))]);
    }

    public Task<IReadOnlyList<LineaVencida>> ExpirarLineasVencidas(Guid pedidoId, IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.ExpirarLineasVencidas");
        ExpiracionesPedidas.Add(pedidoId);

        // La comprobación de vencimiento se repite con «ahora» al expirar (la sentencia condicional del repositorio real).
        var cambiadas = new List<LineaVencida>();
        foreach (var (reserva, linea) in Vencidas(edicionIds, ahora).Where(v => v.Reserva.PedidoId == pedidoId).ToList())
        {
            linea.Expirar(ahora);
            cambiadas.Add(new LineaVencida(pedidoId, linea.Id, linea.LineaId, linea.EdicionId, linea.FuenteId, linea.Cantidad));
        }

        return Task.FromResult<IReadOnlyList<LineaVencida>>(cambiadas);
    }

    public Task<IReadOnlyList<LineaLiberada>> LiberarLineas(Guid pedidoId, bool incluirConfirmadas, IReadOnlyCollection<Guid>? lineaIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.LiberarLineas");
        var liberadas = new List<LineaLiberada>();
        foreach (var reserva in Guardadas.Where(r => r.PedidoId == pedidoId))
        {
            foreach (var linea in reserva.Lineas.Where(l => (l.Estado == EstadoDeLinea.Pendiente || (incluirConfirmadas && l.Estado == EstadoDeLinea.Confirmada)) && (lineaIds is null || lineaIds.Contains(l.LineaId))))
            {
                var estabaConfirmada = linea.Estado == EstadoDeLinea.Confirmada;
                linea.Liberar(ahora);
                liberadas.Add(new LineaLiberada(pedidoId, linea.Id, linea.LineaId, linea.EdicionId, linea.FuenteId, linea.Cantidad, linea.TrasladoId, estabaConfirmada));
            }
        }

        return Task.FromResult<IReadOnlyList<LineaLiberada>>(liberadas);
    }

    public Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var pedidos = Guardadas
            .Where(r => r.Lineas.Any(l => l.FuenteId == fuenteId && l.EdicionId == edicionId && l.EstaActiva(r.FechaDeExpiracion, ahora)))
            .Select(r => r.PedidoId)
            .Distinct()
            .Order()
            .ToList();
        return Task.FromResult(new PedidosQueApartanUnidades([.. pedidos.Take(maximo)], pedidos.Count));
    }

    /// <summary>Los pedidos cuya expiración se pidió al repositorio, en orden.</summary>
    public List<Guid> ExpiracionesPedidas { get; } = [];
}

/// <summary>
/// El ejecutor «sistema» de prueba: mientras dura el trabajo el actor del contexto es el sistema y al terminar se restaura el anterior (también si falla), como el de la Infraestructura.
/// </summary>
internal sealed class EjecutorComoSistemaDoble(ActorDePrueba actor) : IEjecutorComoSistema
{
    public int Llamadas { get; private set; }

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        Llamadas++;
        var anterior = actor.Actual;
        actor.Establecer(ActorActual.DelSistema());
        try
        {
            return await trabajo(cancelacion);
        }
        finally
        {
            actor.Establecer(anterior);
        }
    }
}

/// <summary>La auditoría que anota, junto a cada solicitud, el tipo del actor del contexto en ese instante: así se comprueba que lo expirado se audita con actor «sistema» aunque la transacción sea de un cliente.</summary>
internal sealed class AuditoriaConActor(ActorDePrueba actor, BitacoraDeLlamadas bitacora) : IAuditoria
{
    public List<(SolicitudDeRegistro Solicitud, TipoDeActor? Actor)> Registros { get; } = [];

    public bool Falla { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        bitacora.Anotar($"auditoria.{solicitud.Accion}");
        if (Falla)
        {
            return Task.FromException(new AuditoriaNoDisponibleException("La auditoría no responde."));
        }

        Registros.Add((solicitud, actor.Actual?.Tipo));
        return Task.CompletedTask;
    }
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
        Auditoria = new AuditoriaConActor(Base.Actor, Base.Bitacora);
        Sistema = new EjecutorComoSistemaDoble(Base.Actor);
    }

    public EntornoDeFuentes Base { get; }

    public RepositorioDeReservasEnMemoria Reservas { get; }

    public BloqueosDeReservasEspia Bloqueos { get; }

    public AuditoriaConActor Auditoria { get; }

    public EjecutorComoSistemaDoble Sistema { get; }

    /// <summary>Un origen conocido (la huella de una dirección): los límites por origen rigen.</summary>
    public OrigenFijo Origen { get; } = new(new OrigenDeSolicitud(HuellaDeOrigen.Crear("k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE", "v1"), "203.0.113.7"));

    public PublicadorDeEventosEspia Eventos { get; } = new();

    public Guid ClienteId => Base.Actor.Actual!.UsuarioId!.Value;

    public AutorizadorDeReservas Autorizador => new(Base.Actor, Base.Denegaciones);

    public DirectorioDeFuentes Directorio => new(Base.Lectura, Base.Fuentes, Base.Rutas, Base.Fichas);

    public IAplicadorDeMovimientos Aplicador => new AplicadorDeMovimientos(Base.Existencias);

    public IMovimientosDeStock Stock => new MovimientosDeStock(Base.Fichas, Base.Existencias, ReservasDeStock, Aplicador);

    public ReservasDeStock ReservasDeStock => new(Reservas, Bloqueos, Auditoria, Sistema, Base.Reloj);

    public LimitesDeReservar Limites => _limites ??= new(
        new EvaluarLimite(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas), new MotorDeMemoria(Base.Reloj), null, Origen, Eventos, Base.Reloj),
        Eventos,
        Reservas,
        Origen,
        Base.Reloj);

    private LimitesDeReservar? _limites;

    public ExpirarReservasVencidas ExpirarPedido() => new(Autorizador, Bloqueos, Reservas, Stock, Base.Reloj);

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
            Auditoria,
            Limites,
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
