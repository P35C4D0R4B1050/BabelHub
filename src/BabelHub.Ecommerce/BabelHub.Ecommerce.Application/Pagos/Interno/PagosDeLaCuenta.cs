using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// Lo que Pagos aporta al conteo de pendientes de una cuenta (T22, IDE-27, PAG-24): <b>los pagos en curso</b> (un cobro sin resolver o una compra aprobada sin pedido) y los <b>reembolsos de compensación</b> que faltan. Los
/// reembolsos de anulación, cancelación y devolución los cuentan Pedidos y Devoluciones por <see cref="IReembolsos"/>. Corre dentro de la transacción de Identidad y, si no puede contar, lanza: nunca se asume cero.
/// </summary>
internal sealed class ContarPendientesDeCuenta(IRepositorioDePagos repositorio, IRepositorioDeReembolsos reembolsos) : IPendientesDeCuenta
{
    public async Task<int> Contar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        var enCurso = await repositorio.ContarIntencionesEnCursoDeLaCuenta(usuarioId, cancelacion).ConfigureAwait(false);
        var reembolsosDeCompensacion = await reembolsos.ContarPendientesDeLaCuenta(usuarioId, OrigenDeReembolso.Compensacion, cancelacion).ConfigureAwait(false);
        return enCurso + reembolsosDeCompensacion;
    }
}

/// <summary>
/// La parte de Pagos de la cancelación de una cuenta (T22, IDE-70, PAG-26): <b>desvincula</b> todas las intenciones del titular —anula la cuenta, los textos aceptados, la entrega, la edición, el título y el ISBN de las líneas, el
/// contacto y la huella— y conserva los importes, las fechas y los estados. Las lee con bloqueo (el orden global: la marca de cancelación es de Identidad), corre <b>dentro de la transacción de Identidad</b> (si falla, todo
/// se revierte) y es idempotente y reaplicable: tras restaurar un respaldo, una intención que reapareció con su cuenta se vuelve a desvincular.
/// </summary>
internal sealed class DesvincularPagosDeUnaCuenta(IRepositorioDePagos repositorio, IReloj reloj) : IAplicadorDeCancelacion
{
    public string Modulo => "pagos";

    public async Task Aplicar(Guid usuarioId, Guid identificadorOpaco, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La cuenta necesita su identificador.", nameof(usuarioId));
        }

        var ahora = reloj.Ahora;
        foreach (var intencion in await repositorio.ListarIntencionesDeLaCuenta(usuarioId, paraActualizar: true, cancelacion).ConfigureAwait(false))
        {
            if (intencion.Desvincular(ahora))
            {
                await repositorio.GuardarIntencion(intencion, cancelacion).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Que Pedidos pida eliminar el contacto de la intención tras guardar su copia (T22, PED-35, PAG-27): <b>idempotente</b>. Solo libera el de una compra <c>completada</c> (con pedido); en cualquier otro estado el contacto aún lo
/// necesita la compra, o ya se eliminó al resolverse sin pedido, y no hace nada. Una intención desconocida no falla.
/// </summary>
internal sealed class LiberarContactoDeLaIntencion(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, BloqueoDeIntenciones bloqueo, IReloj reloj) : ILiberacionDeContactoDePago
{
    public Task Liberar(Guid pedidoId, CancellationToken cancelacion = default)
    {
        if (pedidoId == Guid.Empty)
        {
            return Task.CompletedTask;
        }

        return unidad.Ejecutar(
            async token =>
            {
                var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
                if (intencion is { Estado: EstadoDeIntencion.Completada } && intencion.LiberarContacto(reloj.Ahora))
                {
                    await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion);
    }
}
