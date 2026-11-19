using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// <c>AprobarDevolucion</c> y <c>RechazarDevolucion</c> de un impreso (T09; DEV-10, DEV-11, DEV-12, DEV-13, DEV-17): solo el encargado de la sucursal receptora o la administración central, sobre <b>la devolución completa</b>. Rechazar devuelve el cupo de cada
/// línea (DEV-18). Repetir la operación es estado inválido sin efectos. La auditoría y el aviso de cada cambio van en la misma transacción (la auditoría falla cerrada; el Notificador nunca interrumpe).
/// </summary>
internal sealed class AprobarYRechazarDevolucion(
    AutorizadorDeDevoluciones autorizador,
    LimitesDeDevoluciones limites,
    IRepositorioDeDevoluciones repositorio,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    AvisosDeDevoluciones avisos,
    IReloj reloj)
{
    public Task<Resultado<DevolucionVista>> Aprobar(Guid devolucionId, CancellationToken cancelacion = default) => Decidir(devolucionId, aprobar: true, cancelacion);

    public Task<Resultado<DevolucionVista>> Rechazar(Guid devolucionId, CancellationToken cancelacion = default) => Decidir(devolucionId, aprobar: false, cancelacion);

    private async Task<Resultado<DevolucionVista>> Decidir(Guid devolucionId, bool aprobar, CancellationToken cancelacion)
    {
        if (await autorizador.ExigirPersonal(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(rechazo);
        }

        if (await limites.Operacion(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(excedido);
        }

        try
        {
            var inicial = await repositorio.Obtener(devolucionId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (inicial is null)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(await autorizador.NoEncontrado(MotivosDeDenegacionDeDevoluciones.RecursoAjeno, cancelacion).ConfigureAwait(false));
            }

            // El alcance (sucursal receptora, nunca un reporte ni un digital para un encargado) antes del estado.
            if (await autorizador.ExigirAlcanceDeGestion(inicial, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(fueraDeAlcance);
            }

            if (inicial.Tipo != TipoDeDevolucion.Impreso || inicial.Origen != OrigenDeDevolucion.Solicitud)
            {
                // Un reporte o un digital lo decide DecidirReporte, no esta operación.
                return FallosDeDevoluciones.De<DevolucionVista>(CategoriaDeError.Conflicto, ClavesDeDevoluciones.EstadoInvalido);
            }

            // La cuenta del aviso sale de Pedidos antes de abrir la transacción: si no responde, no se cambia nada y se reintenta.
            if (await avisos.Destinatario(inicial.PedidoId, cancelacion).ConfigureAwait(false) is not { } cuentaId)
            {
                return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
            }

            var responsable = ResponsableDelCambio.Personal(autorizador.Actor!.UsuarioId!.Value);
            var ahora = reloj.Ahora;
            Devolucion? decidida = null;
            await unidad.Ejecutar(
                async token =>
                {
                    var devolucion = await repositorio.Obtener(devolucionId, paraActualizar: true, token).ConfigureAwait(false)
                        ?? throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDeDevoluciones.NoEncontrado));
                    var resultado = aprobar ? devolucion.Aprobar(ahora, responsable) : devolucion.Rechazar(ahora, responsable);
                    if (resultado != ResultadoDeTransicionDeDevolucion.Aplicada)
                    {
                        throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeDevoluciones.EstadoInvalido));
                    }

                    if (!aprobar)
                    {
                        await repositorio.DevolverCupos([.. devolucion.Lineas.Select(l => (l.LineaDePedidoId, l.Cantidad))], token).ConfigureAwait(false);
                    }

                    await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                    await auditoria.Registrar(
                        AuditoriaDeDevoluciones.Cambio(devolucion, EstadoDeLineaDeDevolucion.Solicitada, aprobar ? EstadoDeLineaDeDevolucion.Aprobada : EstadoDeLineaDeDevolucion.Rechazada),
                        token).ConfigureAwait(false);
                    await avisos.Solicitar(devolucion, cuentaId, aprobar ? EstadoDeLineaDeDevolucion.Aprobada : EstadoDeLineaDeDevolucion.Rechazada, token).ConfigureAwait(false);
                    decidida = devolucion;
                    return true;
                },
                cancelacion).ConfigureAwait(false);
            return Resultado<DevolucionVista>.Exito(MapeoDeDevoluciones.Vista(decidida!));
        }
        catch (RechazoQueRevierteException revertido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(revertido.Error);
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
        }
    }
}
