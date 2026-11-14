namespace BabelHub.Inventario.Application.Fuentes;

/// <summary>
/// Directorio de fuentes y rutas, de solo lectura, para los otros módulos (Reservas, Ajustes, Traslados). No aplica alcance por sucursal:
/// cada módulo que lo usa autoriza al actor en su propia Aplicación antes de pedir datos de otra fuente.
/// </summary>
public interface IDirectorioDeFuentes
{
    Task<FuenteDeStock?> PorId(Guid fuenteId, CancellationToken cancelacion = default);

    Task<FuenteDeStock?> PorCodigo(string codigo, CancellationToken cancelacion = default);

    /// <summary>Todas las fuentes, ordenadas por código.</summary>
    Task<IReadOnlyList<FuenteDeStock>> Listar(CancellationToken cancelacion = default);

    /// <summary>La única fuente de recepción (FUE-19); <see langword="null"/> solo si aún no hay fuentes.</summary>
    Task<FuenteDeStock?> FuenteDeRecepcion(CancellationToken cancelacion = default);

    /// <summary>Las fuentes de una región (puede haber varias, SEL-24), ordenadas por código.</summary>
    Task<IReadOnlyList<FuenteDeStock>> DeLaRegion(string region, CancellationToken cancelacion = default);

    /// <summary>Los días de la ruta de origen a destino, o <see langword="null"/> si la ruta no está definida (FUE-13). No impide vender.</summary>
    Task<int?> PlazoDeRuta(Guid origenId, Guid destinoId, CancellationToken cancelacion = default);

    /// <summary>Las rutas definidas hacia un destino (para ordenar los orígenes posibles de un traslado).</summary>
    Task<IReadOnlyList<RutaDeTraslado>> RutasHaciaDestino(Guid destinoId, CancellationToken cancelacion = default);
}

/// <summary>Las fichas de edición que el Inventario conserva: solo su formato (D03).</summary>
public interface IFichasDeEdiciones
{
    /// <summary>La ficha de la edición, o <see langword="null"/> si no existe (FUE-15).</summary>
    Task<FichaDeEdicionRegistrada?> PorId(Guid edicionId, CancellationToken cancelacion = default);
}

/// <summary>
/// Lectura del vendible, siempre en una sola instantánea (<c>REPEATABLE READ</c>, D06) y con las vencidas ya devueltas (FUE-03). El llamador
/// comprueba antes la ficha de la edición y su alcance: para una edición que no es impresa solo hay ceros.
/// </summary>
public interface IExistencias
{
    /// <summary>La disponibilidad de varias ediciones: el vendible de la red y el desglose por fuente (las fuentes sin fila, en cero).</summary>
    Task<IReadOnlyList<DisponibilidadDeEdicion>> Disponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>Las existencias de una fuente con filtros y paginación (FUE-09); solo las parejas que tienen fila.</summary>
    Task<PaginaDeExistencias> DeLaFuente(ConsultaDeExistenciasDeFuente consulta, CancellationToken cancelacion = default);

}

/// <summary>
/// Solo <see cref="Aplicar"/>: el cambio atómico de una fila de stock <b>sin liberar antes las reservas vencidas</b>. Existe para que
/// <c>Reservas</c> pueda expirar reservas sin un ciclo de inyección con <see cref="IMovimientosDeStock"/>, que llama a <c>IReservasDeStock</c>.
/// </summary>
public interface IAplicadorDeMovimientos
{
    /// <summary>
    /// Aplica el movimiento con una sola sentencia condicional (<c>0 ≤ reservado ≤ físico</c>) y devuelve las cantidades anterior y posterior de
    /// la fila ya bloqueada. Corre con el alcance del actor: una fila fuera de su alcance da <see cref="ResultadoDeMovimiento.DeniegadoPorAlcance"/>
    /// y no «falta de stock». Exige una transacción activa y haber bloqueado antes la fila.
    /// </summary>
    Task<ResultadoDeAplicacion> Aplicar(MovimientoDeStock movimiento, CancellationToken cancelacion = default);
}

/// <summary>
/// Único camino para cambiar el stock (D01): <c>Fuentes</c> es el dueño de la tabla. Toda operación que cambia el stock toma primero
/// <see cref="Bloquear"/> con las ediciones involucradas y luego aplica sus movimientos, dentro de su propia transacción y con la jerarquía de
/// bloqueos de D04 (las filas de stock son lo último que se bloquea).
/// </summary>
public interface IMovimientosDeStock : IAplicadorDeMovimientos
{
    /// <summary>
    /// Para las ediciones dadas: (1) garantiza la fila de cada fuente con ceros (solo ediciones impresas), (2) libera por <see cref="IReservasDeStock"/> las
    /// reservas pendientes vencidas, (3) bloquea las filas de <b>todas las fuentes</b> de esas ediciones en orden (edición, fuente) y (4) aplica los deltas
    /// agregados de las liberaciones, ordenados, con elevación a toda la red (son una acción del sistema ya auditada). Devuelve las cantidades vigentes.
    /// </summary>
    Task<BloqueoDeStock> Bloquear(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);
}

/// <summary>
/// Puerto que <b>declara</b> <c>Fuentes</c> y <b>implementa</b> <c>Reservas</c>: lo que Fuentes necesita saber de las reservas sin leer sus tablas
/// (regla 13). Su implementación audita <c>ReservaExpirada</c> con actor «sistema» en la transacción del llamador (decisión del 2026-11-12).
/// </summary>
public interface IReservasDeStock
{
    /// <summary>Unidades de reservas pendientes ya vencidas y sin procesar, por pareja, de las ediciones dadas. Solo lectura.</summary>
    Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>
    /// Bloquea las líneas vencidas por id, las pasa a expiradas y devuelve las unidades de las que de verdad cambiaron, agregadas por pareja y
    /// ordenadas por (edición, fuente). No toca el stock: quien llama (<see cref="IMovimientosDeStock.Bloquear"/>) aplica los deltas con todas las filas bloqueadas.
    /// </summary>
    Task<IReadOnlyList<UnidadesLiberadas>> LiberarVencidas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);

    /// <summary>Hasta <paramref name="maximo"/> pedidos que apartan unidades de la pareja (solo identificadores) y el total de pedidos, leídos sin bloquear líneas (AJU-06).</summary>
    Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, CancellationToken cancelacion = default);
}

/// <summary>Puerto que <b>declara</b> <c>Fuentes</c> y <b>implementa</b> <c>Reservas</c>: las ventas atribuidas de una ventana de tiempo (FUE-21, FUE-22).</summary>
public interface IOrigenDeUnidadesVendidas
{
    /// <summary>
    /// Las unidades de impresos de pedidos pagados entre <paramref name="desde"/> y <paramref name="hasta"/>, sin contar pedidos anulados, atribuidas a una sola
    /// fuente (la que despacha el pedido a domicilio o la sucursal que lo entrega en recojo) y una vez por pedido y edición; un traslado de reposición
    /// no es una venta. Con <paramref name="fuenteId"/> nulo, las de toda la red.
    /// </summary>
    Task<IReadOnlyList<VentaAtribuida>> Ventas(Guid? fuenteId, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default);
}

/// <summary>Puerto que <b>declara</b> <c>Fuentes</c> y <b>implementa</b> <c>Ajustes</c>: lo que volvió al stock por devoluciones (FUE-21).</summary>
public interface IReingresosPorDevolucion
{
    /// <summary>Las unidades reingresadas por devolución de esos pedidos, por pedido y edición, sumadas en todas las sucursales.</summary>
    Task<IReadOnlyList<UnidadesReingresadas>> PorPedidos(IReadOnlyCollection<Guid> pedidoIds, CancellationToken cancelacion = default);
}
