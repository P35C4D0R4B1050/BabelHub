using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// La expiración de las líneas pendientes vencidas, <b>compartida</b> por quien libera vencidas al reservar, ajustar o trasladar (<c>IReservasDeStock.LiberarVencidas</c>, que llama <c>Bloquear</c>) y por el trabajo de fondo
/// (<see cref="ExpirarReservasVencidas"/>): hay un solo camino (SEL-08, SEL-27). Por cada pedido candidato (los toma <b>sin bloquear</b> el repositorio y en un orden fijo): <c>IntentarPedido</c> (el que otra transacción
/// tiene se omite, error conservador); luego bloquea las líneas por id, <b>repite la comprobación de vencimiento con <c>ahora</c></b> y las pasa a expiradas con una sola sentencia condicional que devuelve solo las que de verdad
/// cambiaron; y audita <c>ReservaExpirada</c> con actor «sistema» en la transacción del llamador (decisión del 2026-11-12). No toca el stock: devuelve las unidades para que <c>Bloquear</c> las aplique con todas las filas
/// ya bloqueadas (D01, D04). Si no se puede auditar, la excepción revierte la transacción entera y las unidades no se liberan (AUD-04).
/// </summary>
internal sealed class ExpiradorDeReservas(
    IRepositorioDeReservas reservas,
    IBloqueosAsesores bloqueos,
    IAuditoria auditoria,
    IEjecutorComoSistema sistema,
    IReloj reloj)
{
    public async Task<IReadOnlyList<UnidadesLiberadas>> Expirar(IReadOnlyCollection<Guid>? edicionIds, CancellationToken cancelacion)
    {
        var ahora = reloj.Ahora;
        var pedidos = (await reservas.PedidosConVencidas(edicionIds, ahora, cancelacion).ConfigureAwait(false))
            .Distinct()
            .Order(OrdenDeBloqueo.DeIdentificadores);

        var expiradas = new List<LineaVencida>();
        foreach (var pedidoId in pedidos)
        {
            // Sin esperar: dos expiraciones o una reserva en curso del mismo pedido no se bloquean entre sí; lo que no se pudo tomar lo procesa quien tenga el bloqueo.
            if (!await bloqueos.IntentarPedido(pedidoId, cancelacion).ConfigureAwait(false))
            {
                continue;
            }

            var delPedido = await reservas.ExpirarLineasVencidas(pedidoId, edicionIds, ahora, cancelacion).ConfigureAwait(false);
            if (delPedido.Count == 0)
            {
                continue;
            }

            await Auditar(pedidoId, delPedido, cancelacion).ConfigureAwait(false);
            expiradas.AddRange(delPedido);
        }

        return [.. expiradas
            .GroupBy(l => (l.FuenteId, l.EdicionId))
            .Select(g => new UnidadesLiberadas(g.Key.FuenteId, g.Key.EdicionId, checked(g.Sum(l => l.Cantidad))))
            .OrderBy(u => u.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(u => u.FuenteId, OrdenDeBloqueo.DeIdentificadores)];
    }

    /// <summary>Una entrada de auditoría por línea expirada, con el actor «sistema» aunque la transacción sea de un cliente: la auditoría toma el actor del contexto y nunca de la solicitud.</summary>
    private Task Auditar(Guid pedidoId, IReadOnlyList<LineaVencida> lineas, CancellationToken cancelacion) =>
        sistema.Ejecutar(
            async token =>
            {
                foreach (var linea in lineas)
                {
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.ReservaExpirada)
                        {
                            ObjetoTipo = "pedido",
                            ObjetoId = pedidoId.ToString("D"),
                            SucursalOrigen = linea.FuenteId,
                            Detalle = new Dictionary<string, string?>
                            {
                                ["edicionId"] = linea.EdicionId.ToString("D"),
                                ["cantidad"] = linea.Cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            },
                        },
                        token).ConfigureAwait(false);
                }

                return 0;
            },
            cancelacion);
}

/// <summary>
/// <see cref="IReservasDeStock"/>: lo que <c>Fuentes</c> necesita saber de las reservas sin leer sus tablas (regla 13, D02). <c>LiberarVencidas</c> usa el expirador compartido; <c>VencidasPorEdicion</c> y
/// <c>PedidosQueApartan</c> solo leen, sin bloquear, y usan la hora del reloj y no la de la base (D12).
/// </summary>
public sealed class ReservasDeStock(IRepositorioDeReservas reservas, IBloqueosAsesores bloqueos, IAuditoria auditoria, IEjecutorComoSistema sistema, IReloj reloj) : IReservasDeStock
{
    private readonly ExpiradorDeReservas _expirador = new(reservas, bloqueos, auditoria, sistema, reloj);

    public Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        return reservas.VencidasPorEdicion(edicionIds, reloj.Ahora, cancelacion);
    }

    public Task<IReadOnlyList<UnidadesLiberadas>> LiberarVencidas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        return _expirador.Expirar(edicionIds, cancelacion);
    }

    public Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, CancellationToken cancelacion = default) =>
        reservas.PedidosQueApartan(fuenteId, edicionId, maximo, reloj.Ahora, cancelacion);
}

/// <summary>Expira las líneas vencidas de un pedido (la cola del trabajo de fondo, T22): el pedido y, de ser necesario, otras líneas de sus mismas ediciones.</summary>
internal sealed record ExpirarReservasEntrada(Guid PedidoId);

/// <summary>Lo que hizo la expiración de un pedido: si lo pudo procesar (si otra transacción lo tenía, no) y cuántas líneas pasaron a expiradas.</summary>
internal sealed record ExpiracionDelPedido(Guid PedidoId, bool Procesado, int LineasExpiradas);

/// <summary>
/// Expira las líneas pendientes vencidas de un pedido (SEL-08, SEL-27, AC-SEL-08): lo corre el trabajo de fondo con el actor «sistema» explícito; sin actor es «no autenticado» y no toca nada. Toma el bloqueo del pedido
/// sin esperar (<c>IntentarPedido</c>: si otra transacción lo tiene, se omite y el próximo ciclo lo reintenta), lee las ediciones de sus vencidas y pide <c>Bloquear</c> las filas de stock de esas ediciones, que es quien
/// expira y devuelve las unidades con todas las filas ya bloqueadas, en el orden de la jerarquía (pedido → líneas → filas de stock). El trabajo nunca bloquea la cabecera antes que el pedido.
/// </summary>
internal sealed class ExpirarReservasVencidas(
    AutorizadorDeReservas autorizador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeReservas reservas,
    IMovimientosDeStock stock,
    IReloj reloj) : ICasoDeUso<ExpirarReservasEntrada, ExpiracionDelPedido>
{
    public async Task<Resultado<ExpiracionDelPedido>> Ejecutar(ExpirarReservasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ExpiracionDelPedido>(rechazo);
        }

        if (entrada.PedidoId == Guid.Empty)
        {
            return FallosDeReservas.Validacion<ExpiracionDelPedido>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("pedidoId", ConstructorDeValidacion.Obligatorio)]));
        }

        if (!await bloqueos.IntentarPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false))
        {
            return Resultado<ExpiracionDelPedido>.Exito(new ExpiracionDelPedido(entrada.PedidoId, Procesado: false, 0));
        }

        var ahora = reloj.Ahora;
        var vencidas = await reservas.VencidasDelPedido(entrada.PedidoId, ahora, cancelacion).ConfigureAwait(false);
        if (vencidas.Count == 0)
        {
            return Resultado<ExpiracionDelPedido>.Exito(new ExpiracionDelPedido(entrada.PedidoId, Procesado: true, 0));
        }

        var bloqueo = await stock.Bloquear([.. vencidas.Select(l => l.EdicionId).Distinct()], cancelacion).ConfigureAwait(false);
        if (!bloqueo.EstaBloqueado)
        {
            return FallosDeReservas.De<ExpiracionDelPedido>(CategoriaDeError.ReglaDeNegocio, ClavesDeReservas.EdicionNoImpresa);
        }

        // Con el bloqueo del pedido tomado, Bloquear expiró todas las suyas: lo que queda sin expirar sería un error de orden o de reloj.
        var pendientes = await reservas.VencidasDelPedido(entrada.PedidoId, ahora, cancelacion).ConfigureAwait(false);
        return Resultado<ExpiracionDelPedido>.Exito(new ExpiracionDelPedido(entrada.PedidoId, Procesado: true, vencidas.Count - pendientes.Count));
    }
}
