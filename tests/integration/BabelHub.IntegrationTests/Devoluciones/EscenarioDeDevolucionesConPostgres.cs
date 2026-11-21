using System.Collections.Concurrent;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Devoluciones;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;
using BabelHub.Shared.Contratos;

namespace BabelHub.IntegrationTests.Devoluciones;

internal sealed class RelojDeDevoluciones(DateTimeOffset ahora) : IReloj
{
    public DateTimeOffset Ahora { get; set; } = ahora;

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
}

internal sealed class ActorFijo(ActorActual actor) : IActorActual
{
    public ActorActual? Actual { get; } = actor;
}

internal sealed class DenegacionesDeDevolucionesEspia : IDenegacionDeAcceso
{
    public ConcurrentBag<DenegacionDeAcceso> Registradas { get; } = [];

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        Registradas.Add(denegacion);
        return Task.CompletedTask;
    }
}

internal sealed class AuditoriaDeDevolucionesNula : IAuditoria
{
    public ConcurrentBag<SolicitudDeRegistro> Solicitudes { get; } = [];

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }
}

internal sealed class NotificadorDeDevolucionesNulo : INotificador
{
    public Task<ResultadoDeLaSolicitudDeAviso> Solicitar(SolicitudDeAviso solicitud, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaSolicitudDeAviso.Creado);
}

internal sealed class AlertasDeDevolucionesNulas : IPublicadorDeAlertasTecnicas
{
    public ConcurrentBag<string> Motivos { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        Motivos.Add(alerta.Motivo);
        return Task.CompletedTask;
    }
}

internal sealed class LimitesPermisivos : IControlDeLimites
{
    public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, BabelHub.Ecommerce.Application.Plataforma.ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDeLimite.PermitidoSinContar());

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}

internal sealed class CuentasAlDia : IEstadoDeCuenta
{
    public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult<EstadoDeCuenta?>(new EstadoDeCuenta(usuarioId, true, EstadoDeAceptacionDeLosTextos.AlDia, true, false, false));

    public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

internal sealed class MarcaLibre : IMarcaDeCancelacionDeCuenta
{
    public Task<EstadoDeLaMarca> LeerConBloqueo(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(EstadoDeLaMarca.Libre);
}

/// <summary>Pedidos para Devoluciones sembrado a mano y seguro entre hilos.</summary>
internal sealed class PedidosSembrados : IPedidosParaDevoluciones
{
    private readonly ConcurrentDictionary<Guid, PedidoParaDevoluciones> _pedidos = new();

    public void Sembrar(PedidoParaDevoluciones pedido) => _pedidos[pedido.PedidoId] = pedido;

    public Task<Resultado<PedidoParaDevoluciones>> Obtener(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult(_pedidos.TryGetValue(pedidoId, out var pedido)
            ? Resultado<PedidoParaDevoluciones>.Exito(pedido)
            : Resultado<PedidoParaDevoluciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "no-encontrado")));

    public Task<Resultado<PaginaDePedidosDeUnaCuenta>> ListarDeUnaCuenta(Guid cuentaId, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var ids = _pedidos.Values.Where(p => p.CuentaId == cuentaId).Select(p => p.PedidoId).ToList();
        return Task.FromResult(Resultado<PaginaDePedidosDeUnaCuenta>.Exito(new PaginaDePedidosDeUnaCuenta(ids, pagina, tamano, ids.Count)));
    }
}

/// <summary>El Inventario simulado fiel al contrato v1: el reingreso es idempotente por devolución (<c>yaAplicado</c> es un hecho) y se puede dejar caído o con un corte.</summary>
internal sealed class InventarioSimulado : IInventarioDeDevoluciones
{
    private readonly ConcurrentDictionary<Guid, int> _aplicados = new();

    public HashSet<string> Sucursales { get; } = ["lima-central", "arequipa"];

    /// <summary>Verdadero: no responde (el reingreso es incierto y las sucursales no están disponibles).</summary>
    public volatile bool Caido;

    /// <summary>Verdadero: aplica el reingreso pero la respuesta se pierde (incierto).</summary>
    public volatile bool PierdeLaRespuesta;

    public int Llamadas;

    public int Aplicaciones(Guid devolucionId) => _aplicados.GetValueOrDefault(devolucionId);

    public Task<ResultadoDeSucursalesActivas> ListarSucursalesActivas(CancellationToken cancelacion = default) =>
        Task.FromResult<ResultadoDeSucursalesActivas>(Caido ? new ResultadoDeSucursalesActivas.NoDisponible() : new ResultadoDeSucursalesActivas.Lista([.. Sucursales]));

    public Task<ResultadoDelReingreso> Reingresar(ReingresoPorDevolucion reingreso, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref Llamadas);
        if (Caido)
        {
            return Task.FromResult<ResultadoDelReingreso>(new ResultadoDelReingreso.Incierto());
        }

        _aplicados.TryAdd(reingreso.DevolucionId, 0);
        _aplicados[reingreso.DevolucionId] = 1;
        return Task.FromResult<ResultadoDelReingreso>(PierdeLaRespuesta ? new ResultadoDelReingreso.Incierto() : new ResultadoDelReingreso.Reingresado());
    }
}

/// <summary>Pagos simulado: el registro es idempotente por clave natural y el proceso confirma o deja pendiente según se pida.</summary>
internal sealed class ReembolsosSimulados : IReembolsos
{
    private readonly ConcurrentDictionary<string, Guid> _porClave = new();
    private readonly ConcurrentDictionary<Guid, EstadoDelReembolso> _estado = new();

    public volatile bool PasarelaCaida;

    public int Registrados => _porClave.Count;

    public int Procesados;

    public Task<Resultado<ReembolsoRegistrado>> Registrar(RegistrarReembolsoEntrada entrada, CancellationToken cancelacion = default)
    {
        var existia = _porClave.ContainsKey(entrada.ClaveNatural);
        var id = _porClave.GetOrAdd(entrada.ClaveNatural, _ => Guid.NewGuid());
        var estado = _estado.GetOrAdd(id, EstadoDelReembolso.Pendiente);
        return Task.FromResult(Resultado<ReembolsoRegistrado>.Exito(new ReembolsoRegistrado(id, entrada.Lineas.Sum(l => l.Monto), estado, existia)));
    }

    public Task<Resultado<EstadoDelReembolso>> Procesar(Guid reembolsoId, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref Procesados);
        if (PasarelaCaida)
        {
            return Task.FromResult(Resultado<EstadoDelReembolso>.Exito(EstadoDelReembolso.Pendiente));
        }

        _estado[reembolsoId] = EstadoDelReembolso.Reembolsado;
        return Task.FromResult(Resultado<EstadoDelReembolso>.Exito(EstadoDelReembolso.Reembolsado));
    }

    public Task<int> ContarPendientes(Guid cuentaId, OrigenDelReembolso origen, CancellationToken cancelacion = default) => Task.FromResult(_estado.Values.Count(e => e != EstadoDelReembolso.Reembolsado));

    public Task<Resultado<ReembolsoPendienteVista?>> ConsultarPorOrigen(OrigenDelReembolso origen, string claveNatural, CancellationToken cancelacion = default)
    {
        if (!_porClave.TryGetValue(claveNatural, out var id))
        {
            return Task.FromResult(Resultado<ReembolsoPendienteVista?>.Exito(null));
        }

        var estado = _estado[id];
        return Task.FromResult(Resultado<ReembolsoPendienteVista?>.Exito(estado == EstadoDelReembolso.Reembolsado ? null : new ReembolsoPendienteVista(id, origen, 0m, estado, null, DateTimeOffset.UtcNow, 0)));
    }

    public Task<Resultado<PaginaDeReembolsos>> ListarPendientes(int pagina, int tamano, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<EstadoDelReembolso>> Reintentar(Guid reembolsoId, CancellationToken cancelacion = default) => Procesar(reembolsoId, cancelacion);
}

/// <summary>
/// La Aplicación real de Devoluciones (autorizador, límites, casos de uso y avisos) sobre el repositorio y la unidad de trabajo reales de PostgreSQL, con los bordes simulados (Pedidos, Inventario, Pagos, avisos y auditoría). Cada llamada arma sus casos de uso con su propio
/// actor, así que varias tareas pueden operar a la vez sin pisarse.
/// </summary>
internal sealed class EscenarioDeDevolucionesConPostgres(PostgresDeDevolucionesFixture fixture)
{
    public static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    public static readonly Guid EncargadoDeLima = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public RelojDeDevoluciones Reloj { get; } = new(Ahora);

    public PedidosSembrados Pedidos { get; } = new();

    public InventarioSimulado Inventario { get; } = new();

    public ReembolsosSimulados Reembolsos { get; } = new();

    public AuditoriaDeDevolucionesNula Auditoria { get; } = new();

    public DenegacionesDeDevolucionesEspia Denegaciones { get; } = new();

    public AlertasDeDevolucionesNulas Alertas { get; } = new();

    private readonly NotificadorDeDevolucionesNulo _notificador = new();
    private readonly LimitesPermisivos _limites = new();
    private readonly CuentasAlDia _cuentas = new();
    private readonly MarcaLibre _marca = new();

    /// <summary>Un cliente con un pedido de impresos entregado hace tres días.</summary>
    public (Guid Cliente, PedidoParaDevoluciones Pedido, LineaDePedidoParaDevoluciones Linea) SembrarPedidoDeImpresos(int cantidad = 1)
    {
        var cliente = Guid.NewGuid();
        var linea = new LineaDePedidoParaDevoluciones(Guid.NewGuid(), Guid.NewGuid(), false, cantidad, 50m, cantidad * 50m, EstadoVisibleDeLaLinea.Entregado, Reloj.Ahora.AddDays(-3));
        var pedido = new PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0001", cliente, Reloj.Ahora.AddDays(-10), [linea]);
        Pedidos.Sembrar(pedido);
        return (cliente, pedido, linea);
    }

    private AutorizadorDeDevoluciones Autorizador(ActorActual actor) =>
        new(new ActorFijo(actor), Denegaciones, _cuentas, new ResolucionDeSucursalDeDevoluciones(Inventario));

    private AvisosDeDevoluciones Avisos() => new(_notificador, Pedidos, Alertas, Reloj);

    private ProcesarReembolsoDeLinea Procesador() => new(Reembolsos, fixture.Devoluciones, fixture.Unidad, Auditoria, Avisos(), Reloj);

    public Task<Resultado<DevolucionVista>> Solicitar(Guid cliente, Guid pedidoId, Guid lineaId, int cantidad, string sucursal = "lima-central")
    {
        var actor = ActorActual.DeUsuario(cliente, RolesDeUsuario.Cliente);
        var caso = new SolicitarDevolucionDeImpreso(Autorizador(actor), new LimitesDeDevoluciones(_limites, new ActorFijo(actor)), Pedidos, Inventario, _marca, fixture.Devoluciones, fixture.Unidad, Auditoria, Reloj);
        return caso.Ejecutar(new SolicitarDevolucionDeImpresoEntrada(pedidoId, [new LineaPedidaParaDevolver(lineaId, cantidad)], new TextoLibre("No era lo que esperaba"), sucursal));
    }

    private (AutorizadorDeDevoluciones Autorizador, LimitesDeDevoluciones Limites) ComoEncargado()
    {
        var actor = ActorActual.DeUsuario(EncargadoDeLima, RolesDeUsuario.EncargadoDeSucursal, IdentificadorDeSucursal.DesdeCodigo("lima-central"));
        return (Autorizador(actor), new LimitesDeDevoluciones(_limites, new ActorFijo(actor)));
    }

    public Task<Resultado<DevolucionVista>> Aprobar(Guid devolucionId)
    {
        var (autorizador, limites) = ComoEncargado();
        return new AprobarYRechazarDevolucion(autorizador, limites, fixture.Devoluciones, fixture.Unidad, Auditoria, Avisos(), Reloj).Aprobar(devolucionId);
    }

    public Task<Resultado<DevolucionVista>> AprobarComo(ActorActual actor, Guid devolucionId) =>
        new AprobarYRechazarDevolucion(Autorizador(actor), new LimitesDeDevoluciones(_limites, new ActorFijo(actor)), fixture.Devoluciones, fixture.Unidad, Auditoria, Avisos(), Reloj).Aprobar(devolucionId);

    public Task<Resultado<DevolucionVista>> Recibir(Guid devolucionId)
    {
        var (autorizador, limites) = ComoEncargado();
        return new ConfirmarRecepcion(autorizador, limites, Inventario, Reembolsos, fixture.Devoluciones, fixture.Unidad, Auditoria, Avisos(), Alertas, Procesador(), Reloj).Ejecutar(devolucionId);
    }

    public Task<Resultado<DevolucionVista>> RecibirComo(ActorActual actor, Guid devolucionId) =>
        new ConfirmarRecepcion(Autorizador(actor), new LimitesDeDevoluciones(_limites, new ActorFijo(actor)), Inventario, Reembolsos, fixture.Devoluciones, fixture.Unidad, Auditoria, Avisos(), Alertas, Procesador(), Reloj).Ejecutar(devolucionId);

    public Task<Resultado<int>> Conciliar()
    {
        var actor = ActorActual.DelSistema();
        return new ConciliarReembolsosDeDevoluciones(Autorizador(actor), fixture.Devoluciones, Reembolsos, fixture.Unidad, Procesador(), Reloj).Ejecutar(100);
    }
}
