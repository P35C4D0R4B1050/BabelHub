using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;
/// <summary>
/// <see cref="IAlLiberarPedido"/> (TRA-15, TRA-19): <c>Reservas</c> lo invoca <b>antes de tocar líneas y stock</b>, en la misma transacción, también en la repetición, solo con los motivos que anulan el pedido y con los
/// <c>trasladoId</c> de las líneas afectadas (<c>Traslados</c> no conoce el <c>linea_id</c>, regla 13). Con el pedido ya bloqueado por quien llama, toma esos traslados <c>FOR UPDATE</c> y: los <c>solicitado</c> o <c>aprobado</c>
/// pasan a <c>cancelado</c> con el motivo «pedido anulado» (con auditoría y avisos a las dos sucursales y el estado del pedido); el <c>en-transito</c> queda marcado de pedido anulado, de modo que al recibirse sus unidades quedan
/// libres (la marca no cambia el estado: no es un cambio auditable ni deja avisos); uno ya cerrado o ya marcado no cambia. <b>Nunca falla</b> por el estado del traslado: anular un pedido tiene reembolso y no puede depender de la
/// logística. Solo actúa sobre los traslados recibidos y que sean de ese pedido, y es idempotente. No depende de <c>IMovimientosDeStock</c> ni de <c>IReservasParaTraslados</c>, y ningún endpoint ni caso de uso expuesto lo alcanza.
/// </summary>
internal sealed class CancelarTrasladosDePedido(
    IRepositorioDeTraslados traslados,
    IDirectorioDeFuentes fuentes,
    IBuzonDeSalida buzon,
    IAuditoria auditoria,
    IReloj reloj) : IAlLiberarPedido
{
    public async Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(trasladoIds);

        // Solo los motivos que anulan el pedido (TRA-15, TRA-19): un checkout cancelado o un pago vencido no tocan un pedido ya pagado.
        if (!Anula(motivo) || trasladoIds.Count == 0)
        {
            return;
        }

        var ahora = reloj.Ahora;
        var pedidos = trasladoIds.Distinct().ToList();
        var bloqueados = await traslados.PorIdsBloqueados(pedidos, cancelacion).ConfigureAwait(false);

        // Los traslados nunca se borran: un identificador que no vuelve es uno que la política de filas ocultó porque el actor no tiene alcance sobre él. Anular un pedido con reembolso dejando un traslado activo
        // sería peor que fallar: la operación falla cerrada (lo hace un actor de servicio con alcance «red», que nunca llega aquí).
        if (bloqueados.Count < pedidos.Count)
        {
            throw new AlcanceDeDatosDenegadoException();
        }

        foreach (var traslado in bloqueados)
        {
            if (traslado.PedidoId != pedidoId)
            {
                continue;
            }

            var anterior = traslado.Estado;
            var version = traslado.Version;
            var efecto = traslado.AnularPorPedido(ahora);
            if (efecto == EfectoDeLaAnulacionDelPedido.SinCambios)
            {
                continue;
            }

            if (!await traslados.Guardar(traslado, version, cancelacion).ConfigureAwait(false))
            {
                // Con la fila bloqueada nadie pudo cambiarla: un estado imposible aborta y la transacción se revierte.
                throw new InvalidOperationException("No se pudo guardar un traslado ya bloqueado.");
            }

            if (efecto != EfectoDeLaAnulacionDelPedido.Cancelado)
            {
                continue;
            }

            var origen = await fuentes.PorId(traslado.OrigenId, cancelacion).ConfigureAwait(false);
            var destino = await fuentes.PorId(traslado.DestinoId, cancelacion).ConfigureAwait(false);
            if (origen is null || destino is null)
            {
                throw new InvalidOperationException("Un traslado apunta a una fuente que no existe.");
            }

            await auditoria.Registrar(ConversionDeTraslados.Auditar(traslado, anterior), cancelacion).ConfigureAwait(false);
            await buzon.Insertar(ConversionDeTraslados.Mensajes(traslado, origen.Codigo, destino.Codigo, ahora), cancelacion).ConfigureAwait(false);
        }
    }

    private static bool Anula(MotivoDeLiberacionDeReserva motivo) =>
        motivo is MotivoDeLiberacionDeReserva.PedidoAnulado or MotivoDeLiberacionDeReserva.CanceladoPorElCliente or MotivoDeLiberacionDeReserva.FallaPosteriorAlPago or MotivoDeLiberacionDeReserva.NoRecogido;
}
