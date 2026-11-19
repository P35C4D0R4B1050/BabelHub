using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// Los casos de uso de los trabajos de fondo de Pagos que no son la consulta ni la retoma (T23). Todos reclaman filas en <b>transacciones cortas</b> bajo el bloqueo de la intención (la marca de la cuenta primero, D24), hacen las
/// llamadas fuera de ellas, atienden un número acotado por corrida y no dejan que la falla de un elemento detenga a los demás (se alerta y la próxima corrida lo reintenta).
/// </summary>
internal static class AlertasDeTrabajos
{
    public static async Task Publicar(IPublicadorDeAlertasTecnicas alertas, string motivo, DateTimeOffset ahora)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe el trabajo.
        }
    }
}

/// <summary>
/// Cierra sin cobro las intenciones <c>abierta</c> cuya vigencia venció (T23, D23): <c>vence_en</c> solo cierra una intención <b>sin intento activo</b>; una con cobro se resuelve por su intento. Al cerrarse elimina el contacto
/// de entrega (PAG-27). Devuelve cuántas cerró.
/// </summary>
internal sealed class CerrarIntencionesVencidas(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, BloqueoDeIntenciones bloqueo, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public const string MotivoDeAlertaCierreFallido = "cierre-de-intenciones-fallido";

    public async Task<int> Ejecutar(int limite, CancellationToken cancelacion = default)
    {
        var ids = await unidad.Ejecutar(token => repositorio.ListarIntencionesAbiertasVencidas(reloj.Ahora, limite, token), cancelacion).ConfigureAwait(false);
        var cerradas = 0;
        foreach (var id in ids)
        {
            try
            {
                if (await unidad.Ejecutar(token => Cerrar(id, token), cancelacion).ConfigureAwait(false))
                {
                    cerradas++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await AlertasDeTrabajos.Publicar(alertas, MotivoDeAlertaCierreFallido, reloj.Ahora).ConfigureAwait(false);
            }
        }

        return cerradas;
    }

    private async Task<bool> Cerrar(Guid pedidoId, CancellationToken token)
    {
        var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
        if (intencion is null || !intencion.CerrarPorVencimiento(reloj.Ahora))
        {
            return false;
        }

        await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
        return true;
    }
}

/// <summary>Reintenta los reembolsos pendientes que ya se pueden pedir (T23, PAG-10): reclama cada uno en una transacción corta y llama a la pasarela fuera de ella (<see cref="ProcesarReembolso"/>). Devuelve cuántos intentó.</summary>
internal sealed class ReintentarReembolsosPendientes(IRepositorioDeReembolsos reembolsos, IUnidadDeTrabajo unidad, ProcesarReembolso procesar, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public const string MotivoDeAlertaReintentoFallido = "reintento-de-reembolsos-fallido";

    public async Task<int> Ejecutar(int limite, CancellationToken cancelacion = default)
    {
        var ids = await unidad.Ejecutar(token => reembolsos.ListarReintentables(reloj.Ahora, limite, token), cancelacion).ConfigureAwait(false);
        foreach (var id in ids)
        {
            try
            {
                await procesar.Ejecutar(id, cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await AlertasDeTrabajos.Publicar(alertas, MotivoDeAlertaReintentoFallido, reloj.Ahora).ConfigureAwait(false);
            }
        }

        return ids.Count;
    }
}

/// <summary>
/// Purga el contacto de entrega a los 30 días de creada la intención, haya o no pedido (T23, PAG-27). Una compra <b>aprobada sin pedido</b> que llega a los 30 días también lo pierde y <b>publica una alerta técnica</b> (algo
/// quedó trabado), y toda falla al purgar alerta y no detiene a las demás. Devuelve cuántos contactos eliminó.
/// </summary>
internal sealed class PurgarContactoDeIntenciones(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, BloqueoDeIntenciones bloqueo, IAuditoria auditoria, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public const string MotivoDeAlertaPurgaFallida = "purga-de-contacto-fallida";
    public const string MotivoDeAlertaAprobadaSinPedidoALos30Dias = "compra-aprobada-sin-pedido-a-los-30-dias";

    public async Task<int> Ejecutar(int limite, CancellationToken cancelacion = default)
    {
        var ids = await unidad.Ejecutar(token => repositorio.ListarIntencionesConContactoVencido(reloj.Ahora, limite, token), cancelacion).ConfigureAwait(false);
        var purgados = 0;
        foreach (var id in ids)
        {
            try
            {
                var (purgado, estabaAprobada) = await unidad.Ejecutar(token => Purgar(id, token), cancelacion).ConfigureAwait(false);
                if (purgado)
                {
                    purgados++;
                }

                if (estabaAprobada)
                {
                    await AlertasDeTrabajos.Publicar(alertas, MotivoDeAlertaAprobadaSinPedidoALos30Dias, reloj.Ahora).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await AlertasDeTrabajos.Publicar(alertas, MotivoDeAlertaPurgaFallida, reloj.Ahora).ConfigureAwait(false);
            }
        }

        return purgados;
    }

    private async Task<(bool Purgado, bool EstabaAprobada)> Purgar(Guid pedidoId, CancellationToken token)
    {
        var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
        if (intencion is null || !intencion.ContactoVencido(reloj.Ahora))
        {
            return (false, false);
        }

        var estabaAprobada = intencion.Estado == EstadoDeIntencion.Aprobada;
        intencion.LiberarContacto(reloj.Ahora);
        await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
        await AuditoriasDeContacto.Liberado(auditoria, "retencion", token).ConfigureAwait(false);
        return (true, estabaAprobada);
    }
}

/// <summary>
/// Desvincula de la cuenta las intenciones cerradas hace más de 2 años (T23, PAG-26, D17): anula la cuenta, los textos, la entrega, las líneas con edición, título e ISBN, el contacto y la huella, y conserva importes, fechas y
/// estados. Las lee con bloqueo, <b>audita su conteo sin datos personales</b> en la misma transacción y es idempotente. Devuelve cuántas desvinculó.
/// </summary>
internal sealed class DepurarIntencionesAntiguas(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, IAuditoria auditoria, IReloj reloj)
{
    /// <summary>Dos años desde el cierre.</summary>
    public static readonly TimeSpan Retencion = TimeSpan.FromDays(365 * 2);

    public Task<int> Ejecutar(int limite, CancellationToken cancelacion = default) =>
        unidad.Ejecutar(
            async token =>
            {
                var ahora = reloj.Ahora;
                var cantidad = 0;
                foreach (var intencion in await repositorio.ListarIntencionesCerradasParaDepurar(ahora - Retencion, limite, token).ConfigureAwait(false))
                {
                    if (intencion.Desvincular(ahora))
                    {
                        await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                        cantidad++;
                    }
                }

                if (cantidad > 0)
                {
                    await auditoria.Registrar(
                        new SolicitudDeRegistro(AccionesAuditables.IntencionesDePagoDesvinculadas)
                        {
                            Detalle = new Dictionary<string, string?> { ["cantidad"] = cantidad.ToString(System.Globalization.CultureInfo.InvariantCulture) },
                        },
                        token).ConfigureAwait(false);
                }

                return cantidad;
            },
            cancelacion);
}
