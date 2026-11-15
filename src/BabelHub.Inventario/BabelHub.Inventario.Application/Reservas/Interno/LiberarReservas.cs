using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

internal sealed class ValidadorDeLiberar : IValidador<LiberarReservasEntrada>
{
    public ResultadoDeValidacion Validar(LiberarReservasEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        if (entrada.PedidoId == Guid.Empty)
        {
            errores.Add(new ErrorDeCampo("pedidoId", ConstructorDeValidacion.Obligatorio));
        }

        if (!Enum.IsDefined(entrada.Motivo))
        {
            errores.Add(new ErrorDeCampo("motivo", ConstructorDeValidacion.FormatoInvalido));
        }

        if (entrada.LineaIds is { } ids)
        {
            if (ids.Count > LimitesDeReservas.MaximoDeLineas)
            {
                errores.Add(new ErrorDeCampo("lineaIds", ConstructorDeValidacion.DemasiadoLargo));
            }
            else if (ids.Any(i => i == Guid.Empty) || ids.Distinct().Count() != ids.Count)
            {
                errores.Add(new ErrorDeCampo("lineaIds", ConstructorDeValidacion.FormatoInvalido));
            }
        }

        return ReglasDeEntradaDeReservas.Resultado(errores);
    }
}

/// <summary>
/// Libera la reserva de un pedido o solo algunas de sus líneas (SEL-07, SEL-17, D15), sin token: solo la integración <c>expiracion-de-reservas</c>, bajo el bloqueo del pedido. <b>Primero se filtra por motivo y estado</b> con la
/// regla del Dominio (<see cref="ReglaDeLiberacion"/>): <c>checkout-cancelado</c> y <c>pago-vencido</c> solo liberan pendientes y rechazan todo pedido ya confirmado sin cancelar nada; los motivos que anulan el pedido
/// liberan pendientes y confirmadas, marcan anulada la venta de las líneas cerradas por traslado y, <b>antes de tocar líneas y stock y también en la repetición</b>, invocan a los receptores
/// (<see cref="IAlLiberarPedido"/>) con los traslados de las líneas afectadas. Luego bloquea las filas de stock en orden, libera las líneas por id con una sentencia condicional que devuelve solo las que cambiaron, devuelve
/// las unidades con <c>Aplicar(0, −c)</c> agregado y ordenado (el físico no se toca) y audita en la misma transacción. Repetir no cambia el stock (AC-SEL-07). Un volumen anómalo de liberaciones solo avisa.
/// </summary>
internal sealed class LiberarReservas(
    AutorizadorDeReservas autorizador,
    IValidador<LiberarReservasEntrada> validador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeReservas reservas,
    IMovimientosDeStock stock,
    DevolucionDeUnidades devolucion,
    IEnumerable<IAlLiberarPedido> receptores,
    AlertasDeVolumenDeReservas volumen,
    ConstructorDeLaReserva constructor,
    IReloj reloj) : ICasoDeUso<LiberarReservasEntrada, ReservaDelPedido>
{
    public async Task<Resultado<ReservaDelPedido>> Ejecutar(LiberarReservasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirIntegracionDeReservas(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ReservaDelPedido>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<ReservaDelPedido>(validacion);
        }

        try
        {
            return await Liberar(entrada, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeReservas.BloqueoOcupado<ReservaDelPedido>();
        }
    }

    private async Task<Resultado<ReservaDelPedido>> Liberar(LiberarReservasEntrada entrada, CancellationToken cancelacion)
    {
        await bloqueos.Pedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);

        var ahora = reloj.Ahora;
        var actual = await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (actual is null)
        {
            return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.NoEncontrado, ClavesDeReservas.ReservaNoEncontrada);
        }

        // Las líneas afectadas: todas las de la reserva vigente o solo las que acota lineaIds (si alguna no existe, no se encuentra).
        IReadOnlyCollection<LineaDeReserva> afectadas = actual.Lineas;
        if (entrada.LineaIds is { Count: > 0 } ids)
        {
            afectadas = [.. actual.Lineas.Where(l => ids.Contains(l.LineaId))];
            if (afectadas.Count != ids.Count)
            {
                return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.NoEncontrado, ClavesDeReservas.ReservaNoEncontrada);
            }
        }

        var motivo = ConversionesDeReservas.ADominio(entrada.Motivo);
        var decision = ReglaDeLiberacion.Decidir(motivo, actual.TipoDeEntrega, afectadas);
        if (decision.EstadoInvalido)
        {
            return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
        }

        // Los receptores de la cancelación se invocan ANTES de tocar líneas y stock, solo con los motivos que anulan el pedido y también en la repetición, acotados a los traslados de las líneas afectadas.
        if (decision.AnulaElPedido)
        {
            await AvisarALosReceptores(entrada, afectadas, cancelacion).ConfigureAwait(false);
        }

        if (decision.ALiberar.Count > 0)
        {
            var bloqueo = await stock.Bloquear([.. decision.ALiberar.Select(l => l.EdicionId).Distinct()], cancelacion).ConfigureAwait(false);
            if (!bloqueo.EstaBloqueado)
            {
                return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.ReglaDeNegocio, ClavesDeReservas.EdicionNoImpresa);
            }

            var liberadas = await reservas.LiberarLineasPorId([.. decision.ALiberar.Select(l => l.Id)], ahora, cancelacion).ConfigureAwait(false);
            if (liberadas.Count != decision.ALiberar.Count)
            {
                // Una línea cambió entre la lectura y la sentencia (por ejemplo, se cerró): no se libera a medias.
                return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaEstadoInvalido);
            }

            if (await devolucion.Devolver(liberadas, NombreDelMotivo(entrada.Motivo), cancelacion).ConfigureAwait(false) is { } falloAlDevolver)
            {
                return FallosDeReservas.Fallo<ReservaDelPedido>(falloAlDevolver);
            }

            await volumen.Registrar(VolumenDeOperacionesDeReservas.Liberaciones, VolumenDeOperacionesDeReservas.MotivoDeLiberaciones, liberadas.Count, cancelacion).ConfigureAwait(false);
        }

        if (decision.AAnular.Count > 0)
        {
            // La venta de una línea ya cerrada por traslado se anula sin error (TRA-19): no toca el stock, pero se audita.
            var anuladas = await reservas.AnularVentas([.. decision.AAnular.Select(l => l.Id)], ahora, cancelacion).ConfigureAwait(false);
            foreach (var linea in anuladas)
            {
                await devolucion.Auditar(linea, "venta-anulada", cancelacion).ConfigureAwait(false);
            }
        }

        var vigente = await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false) ?? actual;
        return Resultado<ReservaDelPedido>.Exito(await constructor.Construir(vigente, esRepeticion: decision.SinCambios, cancelacion).ConfigureAwait(false));
    }

    private async Task AvisarALosReceptores(LiberarReservasEntrada entrada, IReadOnlyCollection<LineaDeReserva> afectadas, CancellationToken cancelacion)
    {
        var trasladoIds = afectadas.Where(l => l.TrasladoId is not null).Select(l => l.TrasladoId!.Value).Distinct().ToList();
        var lista = receptores.ToList();
        if (lista.Count == 0 && trasladoIds.Count > 0)
        {
            // Sin receptores compuestos no se puede cancelar el traslado vinculado: se falla a la vista y no se anula el pedido a medias.
            throw new InvalidOperationException("Traslados no está compuesto: no se puede anular un pedido con traslados vinculados.");
        }

        foreach (var receptor in lista)
        {
            await receptor.AlLiberar(entrada.PedidoId, entrada.Motivo, trasladoIds, cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>El nombre estable del motivo, tal como figura en el detalle de la auditoría (el catálogo de <c>ReservaLiberada</c> guarda la causa).</summary>
    private static string NombreDelMotivo(MotivoDeLiberacionDeReserva motivo) => motivo switch
    {
        MotivoDeLiberacionDeReserva.CheckoutCancelado => "checkout-cancelado",
        MotivoDeLiberacionDeReserva.PagoVencido => "pago-vencido",
        MotivoDeLiberacionDeReserva.PedidoAnulado => "pedido-anulado",
        MotivoDeLiberacionDeReserva.CanceladoPorElCliente => "cancelado-por-el-cliente",
        MotivoDeLiberacionDeReserva.FallaPosteriorAlPago => "falla-posterior-al-pago",
        MotivoDeLiberacionDeReserva.NoRecogido => "no-recogido",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };
}
