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

    /// <summary>Una copia con el estado de ahora, como la que la Infraestructura rehidrata de la base: lo que el caso de uso cambie en la copia no cambia lo guardado hasta que se escribe.</summary>
    private static ReservaDePedido Copiar(ReservaDePedido r) =>
        ReservaDePedido.Rehidratar(
            r.Id, r.PedidoId, r.ClienteId, r.TipoDeEntrega, r.SucursalDeRecojoId, r.RegionDeEntrega, r.IniciadaEn, r.FechaDeExpiracion, r.Extendida, r.Reemplazos,
            r.Lineas.Select(l => LineaDeReserva.Rehidratar(l.Id, l.LineaId, l.EdicionId, l.FuenteId, l.Cantidad, l.Estado, l.CerradaPor, l.TrasladoId, l.CreadaPorTraslado, l.RecreadaEnDestino, l.ConfirmadaEn, l.AnuladaEn, l.EstadoCambiadoEn)));

    public Task<ReservaDePedido?> UltimaDelPedido(Guid pedidoId, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.UltimaDelPedido");
        var ultima = Guardadas.Where(r => r.PedidoId == pedidoId).OrderByDescending(r => r.Reemplazos).ThenByDescending(r => r.IniciadaEn).FirstOrDefault();
        return Task.FromResult(ultima is null ? null : Copiar(ultima));
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
        return Task.FromResult(indice < 0 ? null : new ReservaPorClave(Copiar(Guardadas[indice]), Solicitudes[indice].HuellaDeLaCarga));
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

    public Task<IReadOnlyList<LineaConfirmada>> ConfirmarLineas(Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.ConfirmarLineas");
        var confirmadas = new List<LineaConfirmada>();
        foreach (var reserva in Guardadas.Where(r => r.PedidoId == pedidoId && !r.EstaVencida(ahora)))
        {
            var pendientes = reserva.Lineas.Where(l => l.Estado == EstadoDeLinea.Pendiente).ToList();
            if (pendientes.Count == 0)
            {
                continue;
            }

            foreach (var linea in pendientes)
            {
                confirmadas.Add(new LineaConfirmada(pedidoId, linea.Id, linea.LineaId, linea.EdicionId, linea.FuenteId, linea.Cantidad));
            }

            reserva.Confirmar(ahora);
        }

        return Task.FromResult<IReadOnlyList<LineaConfirmada>>(confirmadas);
    }

    public Task VincularTraslados(IReadOnlyCollection<(Guid LineaDeReservaId, Guid TrasladoId)> vinculos, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.VincularTraslados");
        foreach (var (lineaId, trasladoId) in vinculos)
        {
            Guardadas.SelectMany(r => r.Lineas).Single(l => l.Id == lineaId).VincularTraslado(trasladoId);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExtenderExpiracion(Guid reservaId, DateTimeOffset nuevaExpiracion, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.ExtenderExpiracion");
        var reserva = Guardadas.Single(r => r.Id == reservaId);
        return Task.FromResult(!reserva.Extendida && reserva.FechaDeExpiracion is not null && !reserva.EstaVencida(ahora) && reserva.Extender(ahora));
    }

    public Task<IReadOnlyList<LineaLiberada>> LiberarLineasPorId(IReadOnlyCollection<Guid> lineaDeReservaIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.LiberarLineasPorId");
        var liberadas = new List<LineaLiberada>();
        foreach (var linea in Guardadas.SelectMany(r => r.Lineas).Where(l => lineaDeReservaIds.Contains(l.Id) && l.Estado is EstadoDeLinea.Pendiente or EstadoDeLinea.Confirmada))
        {
            var estabaConfirmada = linea.Estado == EstadoDeLinea.Confirmada;
            var pedido = Guardadas.Single(r => r.Lineas.Contains(linea)).PedidoId;
            linea.Liberar(ahora);
            liberadas.Add(new LineaLiberada(pedido, linea.Id, linea.LineaId, linea.EdicionId, linea.FuenteId, linea.Cantidad, linea.TrasladoId, estabaConfirmada));
        }

        return Task.FromResult<IReadOnlyList<LineaLiberada>>(liberadas);
    }

    public Task<IReadOnlyList<LineaLiberada>> AnularVentas(IReadOnlyCollection<Guid> lineaDeReservaIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.AnularVentas");
        var anuladas = new List<LineaLiberada>();
        foreach (var linea in Guardadas.SelectMany(r => r.Lineas).Where(l => lineaDeReservaIds.Contains(l.Id) && l.Estado == EstadoDeLinea.Cerrada && l.CerradaPor == CausaDeCierre.Traslado && l.AnuladaEn is null))
        {
            var pedido = Guardadas.Single(r => r.Lineas.Contains(linea)).PedidoId;
            linea.Anular(ahora);
            anuladas.Add(new LineaLiberada(pedido, linea.Id, linea.LineaId, linea.EdicionId, linea.FuenteId, linea.Cantidad, linea.TrasladoId, false));
        }

        return Task.FromResult<IReadOnlyList<LineaLiberada>>(anuladas);
    }

    public Dictionary<(Guid Pedido, string Tipo, string Clave), OperacionGuardada> Operaciones { get; } = [];

    public Task<OperacionGuardada?> ObtenerOperacion(Guid pedidoId, string tipo, string clave, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.ObtenerOperacion");
        return Task.FromResult(Operaciones.GetValueOrDefault((pedidoId, tipo, clave)));
    }

    public Task GuardarOperacion(Guid pedidoId, string tipo, string clave, string huellaDeLaCarga, string resultado, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.GuardarOperacion");
        Operaciones[(pedidoId, tipo, clave)] = new OperacionGuardada(huellaDeLaCarga, resultado);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<LineaCerrada>> CerrarLineas(IReadOnlyCollection<Guid> lineaDeReservaIds, CausaDeCierre causa, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("reservas.CerrarLineas");
        var cerradas = new List<LineaCerrada>();
        foreach (var reserva in Guardadas)
        {
            foreach (var linea in reserva.Lineas.Where(l => lineaDeReservaIds.Contains(l.Id) && l.Estado == EstadoDeLinea.Confirmada))
            {
                linea.Cerrar(causa, ahora);
                cerradas.Add(new LineaCerrada(reserva.PedidoId, linea.Id, linea.LineaId, linea.EdicionId, linea.FuenteId, linea.Cantidad));
            }
        }

        return Task.FromResult<IReadOnlyList<LineaCerrada>>(cerradas);
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


/// <summary>Un <c>Traslados</c> de prueba: crea un traslado por pareja (fuente, edición) del pedido y, como el real, devuelve el mismo al repetir (idempotente por pedido, fuente y edición).</summary>
internal sealed class GeneradorDeTrasladosDoble(RepositorioDeFuentesEnMemoria fuentes, BitacoraDeLlamadas bitacora) : IGeneradorDeTrasladosDePedido
{
    private readonly Dictionary<(Guid Pedido, Guid Fuente, Guid Edicion), Guid> _creados = [];

    public int Llamadas { get; private set; }

    public List<IReadOnlyCollection<SolicitudDeTrasladoDePedido>> Solicitudes { get; } = [];

    public Task<IReadOnlyList<TrasladoVinculadoAlPedido>> Generar(Guid pedidoId, Guid sucursalDeRecojoId, IReadOnlyCollection<SolicitudDeTrasladoDePedido> solicitudes, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("traslados.Generar");
        Llamadas++;
        Solicitudes.Add(solicitudes);
        var destino = fuentes.Fuentes.Single(f => f.Id == sucursalDeRecojoId);
        var resultado = new List<TrasladoVinculadoAlPedido>();
        foreach (var s in solicitudes)
        {
            var clave = (pedidoId, s.FuenteDeOrigenId, s.EdicionId);
            if (!_creados.TryGetValue(clave, out var id))
            {
                id = Guid.NewGuid();
                _creados[clave] = id;
            }

            var origen = fuentes.Fuentes.Single(f => f.Id == s.FuenteDeOrigenId);
            resultado.Add(new TrasladoVinculadoAlPedido(id, s.EdicionId, s.Cantidad, origen.Codigo, destino.Codigo, "solicitado", null));
        }

        return Task.FromResult<IReadOnlyList<TrasladoVinculadoAlPedido>>(resultado);
    }
}

/// <summary>Las alertas que se publicaron. Como el catálogo real, rechaza con <see cref="ArgumentException"/> un motivo que no conoce.</summary>
internal sealed class AlertasEspia : IAlertas
{
    public HashSet<string> Catalogados { get; } = ["motivo-de-alerta-no-catalogado"];

    public List<string> Tecnicas { get; } = [];

    public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
    {
        if (!Catalogados.Contains(motivo.Valor))
        {
            throw new ArgumentException("El motivo no consta en el catálogo.", nameof(motivo));
        }

        Tecnicas.Add(motivo.Valor);
        return Task.CompletedTask;
    }

    public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => throw new NotSupportedException();
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

    public DevolucionDeUnidades Devolucion => new(Stock, Auditoria, Autorizador);

    public LimitesDeReservar Limites => _limites ??= new(
        new EvaluarLimite(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas), new MotorDeMemoria(Base.Reloj), null, Origen, Eventos, Base.Reloj),
        Eventos,
        Reservas,
        Origen,
        Base.Reloj);

    private LimitesDeReservar? _limites;

    public GeneradorDeTrasladosDoble Traslados => _traslados ??= new(Base.Fuentes, Base.Bitacora);

    private GeneradorDeTrasladosDoble? _traslados;

    public AlertasEspia Alertas { get; } = new();

    public int UmbralDeVolumen { get; set; } = 200;

    private VolumenDeOperaciones? _volumen;

    public AlertasDeVolumenDeReservas Volumen => new(_volumen ??= new VolumenDeOperaciones(Base.Reloj, UmbralDeVolumen), Alertas);

    public ConfirmarReservas Confirmar() =>
        new(Autorizador, new ValidadorDeConfirmar(), Bloqueos, Reservas, Directorio, Traslados, Auditoria, Volumen, new ConstructorDeLaReserva(Directorio), Base.Reloj);

    /// <summary>Una reserva pendiente ya guardada (sin pasar por reservar): una línea por edición y fuente, iniciada hace <paramref name="haceMinutos"/>.</summary>
    public ReservaDePedido Pendiente(Guid pedidoId, IEnumerable<(Guid Edicion, Fuente Fuente, int Cantidad)> lineas, bool recojoEnLima = true, int haceMinutos = 1, Guid? clienteId = null)
    {
        var iniciada = Base.Reloj.Ahora.AddMinutes(-haceMinutos);
        var reserva = ReservaDePedido.Iniciar(
            Guid.NewGuid(), pedidoId, clienteId ?? Guid.NewGuid(), recojoEnLima ? TipoDeEntrega.Recojo : TipoDeEntrega.Domicilio, recojoEnLima ? Base.Lima.Id : null, recojoEnLima ? null : "lima", iniciada,
            lineas.Select(l => LineaDeReserva.Pendiente(Guid.NewGuid(), Guid.NewGuid(), l.Edicion, l.Fuente.Id, l.Cantidad, iniciada)));
        Reservas.Guardadas.Add(reserva);
        return reserva;
    }

    public List<(Guid Pedido, MotivoDeLiberacionDeReserva Motivo, IReadOnlyCollection<Guid> Traslados)> Cancelaciones { get; } = [];

    public bool ConReceptores { get; set; } = true;

    public LiberarReservas Liberar() =>
        new(Autorizador, new ValidadorDeLiberar(), Bloqueos, Reservas, Stock, Devolucion, ConReceptores ? [new ReceptorDeCancelacionDoble(Cancelaciones, Base.Bitacora)] : [], Volumen, new ConstructorDeLaReserva(Directorio), Base.Reloj);

    public ConsultarSeleccion ConsultarSeleccion() => new(Autorizador, new ValidadorDeConsultarSeleccion(), Directorio, new ConsultaDeExistencias(Base.Lectura, Base.Fuentes, Base.Existencias, ReservasDeStock), new SelectorDeFuentes(Directorio, new SeleccionPorCercania()), Base.Reloj);

    public ConsultarReserva ConsultarReserva() => new(Autorizador, new ValidadorDeConsultarReserva(), Reservas, new ConstructorDeLaReserva(Directorio), Base.Reloj);

    public CerrarReservas Cerrar() => new(Autorizador, new ValidadorDeCerrar(), Bloqueos, Reservas, Stock, Auditoria, Base.Reloj);

    public ExtenderReservas Extender() => new(Autorizador, new ValidadorDeExtender(), Bloqueos, Reservas, new ConstructorDeLaReserva(Directorio), Base.Reloj);

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
            Devolucion,
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

/// <summary>Un receptor de la cancelación de prueba (<c>Traslados</c>): anota lo que recibe y cuándo, para comprobar que se invoca antes de tocar líneas y stock.</summary>
internal sealed class ReceptorDeCancelacionDoble(List<(Guid Pedido, MotivoDeLiberacionDeReserva Motivo, IReadOnlyCollection<Guid> Traslados)> cancelaciones, BitacoraDeLlamadas bitacora) : IAlLiberarPedido
{
    public Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default)
    {
        bitacora.Anotar("traslados.AlLiberar");
        cancelaciones.Add((pedidoId, motivo, trasladoIds));
        return Task.CompletedTask;
    }
}
