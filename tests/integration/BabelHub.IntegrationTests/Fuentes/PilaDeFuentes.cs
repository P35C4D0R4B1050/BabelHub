using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Inventario.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>Las alertas técnicas que se publicaron (la detección de «tocar el stock sin actor» no puede ser silenciosa).</summary>
public sealed class AlertasTecnicasEspia : IPublicadorDeAlertasTecnicas
{
    public List<AlertaTecnica> Alertas { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        lock (Alertas)
        {
            Alertas.Add(alerta);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Un <c>IReservasDeStock</c> de prueba (el módulo <c>Reservas</c> aún no existe): devuelve lo configurado.</summary>
public sealed class ReservasDeStockFijo : IReservasDeStock
{
    public List<UnidadesVencidas> Vencidas { get; } = [];

    public List<UnidadesLiberadas> Liberadas { get; } = [];

    public Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<UnidadesVencidas>>([.. Vencidas.Where(v => edicionIds.Contains(v.EdicionId))]);

    public Task<IReadOnlyList<UnidadesLiberadas>> LiberarVencidas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<UnidadesLiberadas>>([.. Liberadas.Where(l => edicionIds.Contains(l.EdicionId))]);

    public Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult(new PedidosQueApartanUnidades([], 0));
}

/// <summary>Un reloj fijo para las pruebas de integración.</summary>
public sealed class RelojDeIntegracion : IReloj
{
    public DateTimeOffset Ahora { get; set; } = new(2026, 11, 14, 15, 0, 0, TimeSpan.Zero);

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
}

/// <summary>
/// La pila real de Infraestructura de <c>Fuentes</c> (unidad de trabajo con el rol real de la aplicación, el acceso al esquema y los repositorios de PostgreSQL) para un actor dado, con las
/// piezas de otros módulos (<c>Reservas</c>) simuladas. Cada pila tiene su propia unidad de trabajo: se usa una por transacción concurrente.
/// </summary>
public sealed class PilaDeFuentes
{
    public PilaDeFuentes(PostgresDeFuentesFixture bd, ActorActual? actor, ReservasDeStockFijo? reservas = null)
    {
        Actor = new ActorFijo(actor);
        Unidad = new UnidadDeTrabajoPostgres(bd.Acceso, actor: Actor);
        Alertas = new AlertasTecnicasEspia();
        Reservas = reservas ?? new ReservasDeStockFijo();
        Acceso = new AccesoAFuentes(Unidad, Unidad, Unidad, Actor, Alertas, new RelojDeIntegracion());
        Existencias = new NpgsqlRepositorioDeExistencias(Acceso);
        Fichas = new NpgsqlRepositorioDeFichas(Acceso);
        Aplicador = new AplicadorDeMovimientos(Existencias);
        Movimientos = new MovimientosDeStock(Fichas, Existencias, Reservas, Aplicador);
    }

    public ActorFijo Actor { get; }

    public UnidadDeTrabajoPostgres Unidad { get; }

    public AlertasTecnicasEspia Alertas { get; }

    public ReservasDeStockFijo Reservas { get; }

    internal AccesoAFuentes Acceso { get; }

    internal NpgsqlRepositorioDeExistencias Existencias { get; }

    internal NpgsqlRepositorioDeFichas Fichas { get; }

    internal AplicadorDeMovimientos Aplicador { get; }

    internal MovimientosDeStock Movimientos { get; }
}
