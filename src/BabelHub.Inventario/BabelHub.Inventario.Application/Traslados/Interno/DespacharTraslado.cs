using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>
/// Despacha un traslado aprobado (TRA-06, TRA-07): el origen o la administración central. Con el pedido (si es vinculado) y la fila del traslado bloqueados y el estado revalidado: <b>no vinculado</b>, con el stock del origen ya
/// bloqueado exige vendible suficiente y aplica <c>Aplicar(−c, 0)</c>; <b>vinculado</b>, la reserva confirmada de su pedido por esa cantidad la consume <see cref="IReservasParaTraslados"/> (el físico y lo reservado del origen bajan
/// y el vendible no cambia). Si falta stock, o la reserva ya no está confirmada, el despacho se rechaza con <c>stock-insuficiente</c> y el traslado sigue aprobado. «Denegado por alcance» de <c>Aplicar</c> o de
/// <see cref="IReservasParaTraslados"/> se traduce a <c>INV-PROHIBIDO</c> con un único evento y la transacción se revierte. Este caso de uso no pide la autorización a <c>Reservas</c>: la hace aquí, antes (<c>ReservasParaTraslados</c> confía
/// en quien lo llama).
/// </summary>
internal sealed class DespacharTraslado(
    EjecutorDeTransiciones ejecutor,
    IValidador<DespacharTrasladoEntrada> validador,
    IMovimientosDeStock stock,
    IReservasParaTraslados reservas) : ICasoDeUso<DespacharTrasladoEntrada, TrasladoRegistrado>
{
    public Task<Resultado<TrasladoRegistrado>> Ejecutar(DespacharTrasladoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return ejecutor.Ejecutar(OperacionDeTraslado.Despachar, entrada.TrasladoId, () => validador.Validar(entrada), (traslado, ahora) => Despachar(traslado, ahora, cancelacion), cancelacion);
    }

    private async Task<ErrorDeAplicacion?> Despachar(Traslado traslado, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        // La transición se revalida con el traslado ya bloqueado: un despacho que pierde frente a la anulación del pedido o a otro despacho recibe «estado inválido» antes de tocar el stock.
        traslado.Despachar(ahora);

        return traslado.PedidoId is { } pedidoId
            ? await DespacharVinculado(traslado, pedidoId, cancelacion).ConfigureAwait(false)
            : await DespacharDeReposicion(traslado, cancelacion).ConfigureAwait(false);
    }

    private async Task<ErrorDeAplicacion?> DespacharDeReposicion(Traslado traslado, CancellationToken cancelacion)
    {
        // Bloquear garantiza la fila, libera las vencidas y bloquea todas las fuentes de la edición: el vendible se decide con el stock ya bloqueado y actualizado (AC-TRA-23).
        var bloqueo = await stock.Bloquear([traslado.EdicionId], cancelacion).ConfigureAwait(false);
        if (!bloqueo.EstaBloqueado)
        {
            return new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.EdicionNoImpresa);
        }

        var fila = bloqueo.Fila(traslado.OrigenId, traslado.EdicionId);
        if (Math.Max(0, fila.Fisico - fila.Reservado) < traslado.Cantidad)
        {
            return new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente);
        }

        var resultado = await stock.Aplicar(new MovimientoDeStock(traslado.OrigenId, traslado.EdicionId, -traslado.Cantidad, 0), cancelacion).ConfigureAwait(false);
        return resultado.Resultado switch
        {
            ResultadoDeMovimiento.Aplicado => null,
            ResultadoDeMovimiento.DeniegadoPorAlcance => await ejecutor.Autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false),
            _ => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente),
        };
    }

    private async Task<ErrorDeAplicacion?> DespacharVinculado(Traslado traslado, Guid pedidoId, CancellationToken cancelacion)
    {
        // La reserva confirmada de su pedido por esa cantidad: sin ella (liberada, expirada, ya cerrada o de otra cantidad) el despacho se rechaza y el traslado sigue aprobado (AC-TRA-05).
        var reserva = await reservas.ConsultarConfirmada(pedidoId, traslado.OrigenId, traslado.EdicionId, cancelacion).ConfigureAwait(false);
        if (reserva is null || reserva.Cantidad != traslado.Cantidad)
        {
            return new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente);
        }

        var resultado = await reservas.ConsumirEnElOrigen(pedidoId, traslado.OrigenId, traslado.EdicionId, cancelacion).ConfigureAwait(false);
        return resultado switch
        {
            ResultadoDeMovimientoDeReserva.Aplicado => null,
            ResultadoDeMovimientoDeReserva.DeniegadoPorAlcance => await ejecutor.Autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false),
            _ => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeTraslados.StockInsuficiente),
        };
    }
}

