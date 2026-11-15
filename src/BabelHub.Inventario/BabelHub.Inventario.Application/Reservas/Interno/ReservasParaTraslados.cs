using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// <see cref="IReservasParaTraslados"/>: lo que <c>Traslados</c> necesita de las reservas sin leer sus tablas (regla 13; SEL-11, SEL-28, SEL-29). Cada operación toma el <b>bloqueo del pedido</b> (reentrante: si quien llama ya lo
/// tiene, no espera) y sigue la jerarquía de D04: pedido → líneas por id → filas de stock. <b>Quien llama autoriza antes</b> (el encargado de la sucursal de origen despacha, el del destino recibe); aquí solo se propaga el
/// resultado tipado «denegado por alcance» de <c>Aplicar</c>. Una reserva que no está confirmada (liberada, expirada o ya cerrada por otra operación) es <see cref="ResultadoDeMovimientoDeReserva.EstadoInvalido"/> sin cambios.
/// </summary>
public sealed class ReservasParaTraslados(
    IBloqueosAsesores bloqueos,
    IRepositorioDeReservas reservas,
    IMovimientosDeStock stock,
    IReloj reloj) : IReservasParaTraslados
{
    public async Task<ReservaConfirmadaDeLaFuente?> ConsultarConfirmada(Guid pedidoId, Guid fuenteId, Guid edicionId, CancellationToken cancelacion = default)
    {
        await bloqueos.Pedido(pedidoId, cancelacion).ConfigureAwait(false);

        var reserva = await reservas.UltimaDelPedido(pedidoId, cancelacion).ConfigureAwait(false);
        var linea = reserva?.Lineas.FirstOrDefault(l => l.Estado == EstadoDeLinea.Confirmada && l.FuenteId == fuenteId && l.EdicionId == edicionId);
        return linea is null ? null : new ReservaConfirmadaDeLaFuente(pedidoId, fuenteId, edicionId, linea.Cantidad, linea.TrasladoId);
    }

    /// <summary>
    /// Consume la reserva en el origen: el físico y lo reservado bajan, la línea pasa a cerrada por traslado y queda sin recrear en el destino. Lo hace el despacho del traslado (SEL-28, SEL-29).
    /// </summary>
    public async Task<ResultadoDeMovimientoDeReserva> ConsumirEnElOrigen(Guid pedidoId, Guid fuenteDeOrigenId, Guid edicionId, CancellationToken cancelacion = default)
    {
        await bloqueos.Pedido(pedidoId, cancelacion).ConfigureAwait(false);

        var reserva = await reservas.UltimaDelPedido(pedidoId, cancelacion).ConfigureAwait(false);
        var delOrigen = reserva?.Lineas.Where(l => l.FuenteId == fuenteDeOrigenId && l.EdicionId == edicionId && l.CreadaPorTraslado == false).ToList();
        if (reserva is null || delOrigen is null || delOrigen.Count == 0)
        {
            return ResultadoDeMovimientoDeReserva.NoEncontrada;
        }

        var linea = delOrigen.FirstOrDefault(l => l.Estado == EstadoDeLinea.Confirmada);
        if (linea is null)
        {
            // Liberada, expirada, pendiente o ya cerrada (por entrega, reparto o por este mismo traslado): no hay nada que consumir (SEL-28, SEL-29).
            return ResultadoDeMovimientoDeReserva.EstadoInvalido;
        }

        // Jerarquía D04: las líneas por id antes que las filas de stock.
        await reservas.BloquearLineas([linea.Id], cancelacion).ConfigureAwait(false);
        var bloqueo = await stock.Bloquear([edicionId], cancelacion).ConfigureAwait(false);
        if (!bloqueo.EstaBloqueado)
        {
            return ResultadoDeMovimientoDeReserva.NoEncontrada;
        }

        var cerradas = await reservas.CerrarLineas([linea.Id], CausaDeCierre.Traslado, null, reloj.Ahora, cancelacion).ConfigureAwait(false);
        if (cerradas.Count != 1)
        {
            // La primera operación que llega gana: otra liberó o cerró la línea entre la lectura y la sentencia.
            return ResultadoDeMovimientoDeReserva.EstadoInvalido;
        }

        return await Aplicar(new MovimientoDeStock(fuenteDeOrigenId, edicionId, -linea.Cantidad, -linea.Cantidad), cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Recrea la reserva en el destino al recibir la mercadería: el físico y lo reservado suben, la línea de origen queda marcada como recreada y nace una línea confirmada creada por traslado, que no cuenta como venta
    /// (FUE-22, TRA-14). Si la venta ya se anuló, o la línea ya se recreó, es estado inválido.
    /// </summary>
    public async Task<ResultadoDeMovimientoDeReserva> RecrearEnElDestino(Guid pedidoId, Guid fuenteDeDestinoId, Guid edicionId, int cantidad, Guid trasladoId, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cantidad);
        await bloqueos.Pedido(pedidoId, cancelacion).ConfigureAwait(false);

        var reserva = await reservas.UltimaDelPedido(pedidoId, cancelacion).ConfigureAwait(false);
        var origen = reserva?.Lineas.FirstOrDefault(l => l.EdicionId == edicionId && l.TrasladoId == trasladoId && l.Estado == EstadoDeLinea.Cerrada && l.CerradaPor == CausaDeCierre.Traslado);
        if (reserva is null || origen is null)
        {
            return ResultadoDeMovimientoDeReserva.NoEncontrada;
        }

        if (origen.RecreadaEnDestino || origen.AnuladaEn is not null || origen.Cantidad != cantidad)
        {
            return ResultadoDeMovimientoDeReserva.EstadoInvalido;
        }

        await reservas.BloquearLineas([origen.Id], cancelacion).ConfigureAwait(false);
        var bloqueo = await stock.Bloquear([edicionId], cancelacion).ConfigureAwait(false);
        if (!bloqueo.EstaBloqueado)
        {
            return ResultadoDeMovimientoDeReserva.NoEncontrada;
        }

        if (!await reservas.MarcarRecreadaEnDestino(origen.Id, cancelacion).ConfigureAwait(false))
        {
            // Otra operación la anuló o ya la recreó entre la lectura y la sentencia: la primera gana.
            return ResultadoDeMovimientoDeReserva.EstadoInvalido;
        }

        var ahora = reloj.Ahora;
        var resultado = await Aplicar(new MovimientoDeStock(fuenteDeDestinoId, edicionId, cantidad, cantidad), cancelacion).ConfigureAwait(false);
        if (resultado != ResultadoDeMovimientoDeReserva.Aplicado)
        {
            return resultado;
        }

        await reservas.InsertarLineaDeTraslado(reserva.Id, pedidoId, LineaDeReserva.ConfirmadaPorTraslado(Guid.NewGuid(), origen.LineaId, edicionId, fuenteDeDestinoId, cantidad, trasladoId, ahora), cancelacion).ConfigureAwait(false);
        return ResultadoDeMovimientoDeReserva.Aplicado;
    }

    private async Task<ResultadoDeMovimientoDeReserva> Aplicar(MovimientoDeStock movimiento, CancellationToken cancelacion)
    {
        var resultado = await stock.Aplicar(movimiento, cancelacion).ConfigureAwait(false);
        return resultado.Resultado switch
        {
            ResultadoDeMovimiento.Aplicado => ResultadoDeMovimientoDeReserva.Aplicado,

            // El llamador lo traduce a «prohibido» con su evento: la política de filas rechazó la escritura por alcance.
            ResultadoDeMovimiento.DeniegadoPorAlcance => ResultadoDeMovimientoDeReserva.DeniegadoPorAlcance,

            // Lo que la reserva ya apartaba nunca debería fallar al descontar o sumar: un estado imposible aborta y la transacción se revierte.
            _ => throw new InvalidOperationException("No se pudo mover el stock de una reserva confirmada por un traslado."),
        };
    }
}
