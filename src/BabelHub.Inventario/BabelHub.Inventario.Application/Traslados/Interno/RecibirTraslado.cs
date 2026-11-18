using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>
/// Recibe un traslado en tránsito (TRA-08, TRA-14, TRA-17): el destino o la administración central. Con la fila bloqueada y el estado revalidado (una recepción repetida o simultánea no suma dos veces: la segunda recibe «estado
/// inválido»): <b>vinculado a un pedido activo</b>, <see cref="IReservasParaTraslados.RecrearEnElDestino"/> suma el físico y lo reservado del destino (<c>Aplicar(+c, +c)</c>: el vendible no cambia, así que otro cliente no puede tomar
/// las unidades); <b>sin pedido o con el pedido anulado</b>, <c>Aplicar(+c, 0)</c> y las unidades quedan libres. Si <c>RecrearEnElDestino</c> informa que la reserva ya no existe, la recepción falla y se revierte. «Denegado por
/// alcance» se traduce a <c>INV-PROHIBIDO</c> con un único evento y no se confunde con falta de stock.
/// </summary>
internal sealed class RecibirTraslado(
    EjecutorDeTransiciones ejecutor,
    IValidador<RecibirTrasladoEntrada> validador,
    IMovimientosDeStock stock,
    IReservasParaTraslados reservas) : ICasoDeUso<RecibirTrasladoEntrada, TrasladoRegistrado>
{
    public Task<Resultado<TrasladoRegistrado>> Ejecutar(RecibirTrasladoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return ejecutor.Ejecutar(OperacionDeTraslado.Recibir, entrada.TrasladoId, () => validador.Validar(entrada), (traslado, ahora) => Recibir(traslado, ahora, cancelacion), cancelacion);
    }

    private async Task<ErrorDeAplicacion?> Recibir(Traslado traslado, DateTimeOffset ahora, CancellationToken cancelacion)
    {
        traslado.Recibir(ahora);

        if (traslado.PedidoId is { } pedidoId && !traslado.PedidoAnulado)
        {
            var recreada = await reservas.RecrearEnElDestino(pedidoId, traslado.DestinoId, traslado.EdicionId, traslado.Cantidad, traslado.Id, cancelacion).ConfigureAwait(false);
            return recreada switch
            {
                ResultadoDeMovimientoDeReserva.Aplicado => null,
                ResultadoDeMovimientoDeReserva.DeniegadoPorAlcance => await ejecutor.Autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false),

                // La reserva ya no se puede recrear (el pedido se anuló sin marcar el traslado, o ya se recreó): la recepción falla y se revierte; no es falta de stock.
                _ => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeTraslados.TrasladoEstadoInvalido),
            };
        }

        // Sin pedido, o con el pedido anulado: las unidades suman al físico del destino y quedan libres (TRA-17). Bloquear garantiza la fila y libera las vencidas.
        var bloqueo = await stock.Bloquear([traslado.EdicionId], cancelacion).ConfigureAwait(false);
        if (!bloqueo.EstaBloqueado)
        {
            return new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeTraslados.EdicionNoImpresa);
        }

        var resultado = await stock.Aplicar(new MovimientoDeStock(traslado.DestinoId, traslado.EdicionId, traslado.Cantidad, 0), cancelacion).ConfigureAwait(false);
        return resultado.Resultado switch
        {
            ResultadoDeMovimiento.Aplicado => null,
            ResultadoDeMovimiento.DeniegadoPorAlcance => await ejecutor.Autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false),

            // Sumar nunca debería fallar con la fila bloqueada: un estado imposible aborta y la transacción se revierte.
            _ => throw new InvalidOperationException("No se pudo sumar al stock del destino la mercadería de un traslado en tránsito."),
        };
    }
}

