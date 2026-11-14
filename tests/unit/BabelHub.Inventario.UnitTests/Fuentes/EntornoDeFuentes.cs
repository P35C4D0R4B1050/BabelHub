using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Domain.Fuentes;
using BabelHub.Inventario.UnitTests.Auditoria;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Fuentes;

/// <summary>Las denegaciones que registra el módulo, en el orden en que llegaron: así una prueba comprueba que cada rechazo dejó su evento.</summary>
internal sealed class DenegacionesEspia(BitacoraDeLlamadas? bitacora = null) : IDenegacionDeAcceso
{
    public List<DenegacionDeAcceso> Eventos { get; } = [];

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar($"denegacion.{denegacion.Motivo}");
        Eventos.Add(denegacion);
        return Task.CompletedTask;
    }
}

/// <summary>La auditoría que se pide registrar; puede fallar a pedido (<see cref="AuditoriaNoDisponibleException"/>), como una base de datos que no responde.</summary>
internal sealed class AuditoriaEspia(BitacoraDeLlamadas? bitacora = null) : IAuditoria
{
    public List<SolicitudDeRegistro> Solicitudes { get; } = [];

    public bool Falla { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        bitacora?.Anotar($"auditoria.{solicitud.Accion}");
        if (Falla)
        {
            return Task.FromException(new AuditoriaNoDisponibleException("La auditoría no responde."));
        }

        Solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }
}

internal sealed class BloqueosEspia(BitacoraDeLlamadas? bitacora = null) : IBloqueosAsesores
{
    public bool EsperaAgotada { get; set; }

    public int Recepciones { get; private set; }

    public Task Recepcion(CancellationToken cancelacion = default)
    {
        bitacora?.Anotar("bloqueo.Recepcion");
        Recepciones++;
        return EsperaAgotada ? Task.FromException(new EsperaDeBloqueoAgotadaException()) : Task.CompletedTask;
    }

    public Task Cliente(Guid clienteId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task Pedido(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<bool> IntentarPedido(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task Devolucion(Guid devolucionId, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

internal sealed class OrigenDeUnidadesVendidasDoble : IOrigenDeUnidadesVendidas
{
    public List<VentaAtribuida> Ventas { get; } = [];

    public List<(Guid? FuenteId, DateTimeOffset Desde, DateTimeOffset Hasta)> Consultas { get; } = [];

    Task<IReadOnlyList<VentaAtribuida>> IOrigenDeUnidadesVendidas.Ventas(Guid? fuenteId, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion)
    {
        Consultas.Add((fuenteId, desde, hasta));
        return Task.FromResult<IReadOnlyList<VentaAtribuida>>([.. Ventas.Where(v => fuenteId is null || v.FuenteId == fuenteId)]);
    }
}

internal sealed class ReingresosPorDevolucionDoble : IReingresosPorDevolucion
{
    public List<UnidadesReingresadas> Reingresos { get; } = [];

    public List<IReadOnlyCollection<Guid>> Consultas { get; } = [];

    public Task<IReadOnlyList<UnidadesReingresadas>> PorPedidos(IReadOnlyCollection<Guid> pedidoIds, CancellationToken cancelacion = default)
    {
        Consultas.Add(pedidoIds);
        return Task.FromResult<IReadOnlyList<UnidadesReingresadas>>([.. Reingresos.Where(r => pedidoIds.Contains(r.PedidoId))]);
    }
}

/// <summary>
/// Todo lo que los casos de uso del módulo necesitan, en memoria y con las tres fuentes de siempre (Lima, la de recepción, Cusco y Ayacucho): el actor de prueba, los
/// repositorios, las denegaciones y la auditoría que se piden, los bloqueos y el reloj. Cada prueba arma su caso de uso con lo que necesita.
/// </summary>
internal sealed class EntornoDeFuentes
{
    public static readonly DateTimeOffset Ahora = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    public EntornoDeFuentes(ActorDePrueba? actor = null)
    {
        Actor = actor ?? ActorDePrueba.DeAdministradorCentral();
        Fichas = new RepositorioDeFichasEnMemoria(Bitacora);
        Existencias = new RepositorioDeExistenciasEnMemoria(Fuentes, Bitacora);
        Reservas = new ReservasDeStockDoble(Bitacora);
        Denegaciones = new DenegacionesEspia(Bitacora);
        Auditoria = new AuditoriaEspia(Bitacora);
        Bloqueos = new BloqueosEspia(Bitacora);
        Lima = Fuentes.Agregar("lima", "lima", recepcion: true);
        Cusco = Fuentes.Agregar("cusco", "cusco");
        Ayacucho = Fuentes.Agregar("ayacucho", "ayacucho");
        Fuentes.Escritas.Clear();
    }

    public BitacoraDeLlamadas Bitacora { get; } = new();

    public ActorDePrueba Actor { get; }

    public RepositorioDeFuentesEnMemoria Fuentes { get; } = new();

    public RepositorioDeRutasEnMemoria Rutas { get; } = new();

    public RepositorioDeFichasEnMemoria Fichas { get; }

    public RepositorioDeExistenciasEnMemoria Existencias { get; }

    public ReservasDeStockDoble Reservas { get; }

    public DenegacionesEspia Denegaciones { get; }

    public AuditoriaEspia Auditoria { get; }

    public BloqueosEspia Bloqueos { get; }

    public LecturaDirecta Lectura { get; } = new();

    public RelojFijo Reloj { get; } = new(Ahora);

    public Fuente Lima { get; }

    public Fuente Cusco { get; }

    public Fuente Ayacucho { get; }

    public AutorizadorDeFuentes Autorizador => new(Actor, Denegaciones);

    public TotalesDeFuente Totales => new(Existencias, Reservas);

    public RegistrarFuente RegistrarFuente() => new(Autorizador, new ValidadorDeRegistrarFuente(), Bloqueos, Fuentes, Auditoria);

    public EditarFuente EditarFuente() => new(Autorizador, new ValidadorDeEditarFuente(), Fuentes, Totales, Auditoria);

    public Guid Impresa() => Fichas.Agregar().EdicionId;

    /// <summary>
    /// La auditoría real del Inventario (el catálogo completo, el constructor de registros y un almacén en memoria) con el actor y el reloj de este entorno: sirve para comprobar
    /// que lo que el caso de uso pide registrar lo acepta el catálogo (acción, objeto, claves del detalle) y qué queda guardado.
    /// </summary>
    public (IAuditoria Auditoria, AlmacenDeRegistrosEnMemoria Almacen) AuditoriaReal()
    {
        var almacen = new AlmacenDeRegistrosEnMemoria();
        var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Inventario, CatalogoDeAccionesDeAuditoria.Crear(), Reloj, Actor, new OrigenFijo(OrigenDeSolicitud.Desconocido));
        return (new AuditoriaDeAplicacion(constructor, almacen), almacen);
    }

    /// <summary>Las denegaciones que quedaron, como sus motivos.</summary>
    public IReadOnlyList<string> MotivosDeDenegacion => [.. Denegaciones.Eventos.Select(e => e.Motivo)];
}
