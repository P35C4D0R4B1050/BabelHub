using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// <c>RechazarDevolucionesVencidas</c> (T12; DEV-12, DEV-14, DEV-16; plan §1 y §5): la revisión horaria, como «sistema». Cada devolución se revisa <b>en su propia transacción, bajo su bloqueo con <c>SKIP LOCKED</c> y revalidando</b>: pasa a rechazada, sin reembolso y con el cupo devuelto, la que está
/// aprobada hace <b>más de 10 días</b> (10 exactos no) <b>sin el marcador de reingreso</b> (si la confirmación lo fijó, se respeta: DEV-16); la auditoría va en la misma transacción y falla cerrada. Una devolución bloqueada por otra transacción, ya cambiada o que falla no detiene a las demás: se reintenta
/// en la próxima hora. El aviso del cambio llega con T14.
/// </summary>
internal sealed class RechazarDevolucionesVencidas(
    AutorizadorDeDevoluciones autorizador,
    IRepositorioDeDevoluciones repositorio,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj)
{
    public async Task<Resultado<int>> Ejecutar(int maximo, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<int>(rechazo);
        }

        var ahora = reloj.Ahora;
        IReadOnlyList<Guid> candidatas;
        try
        {
            // Más de 10 días desde la aprobación: la aprobada antes de este instante (el borde exacto no vence).
            candidatas = await repositorio.ObtenerConRecepcionVencida(ahora - PoliticaDeDevolucion.PlazoParaRecibirUnImpreso, Math.Clamp(maximo, 1, 500), cancelacion).ConfigureAwait(false);
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<int>();
        }

        var rechazadas = 0;
        foreach (var devolucionId in candidatas)
        {
            try
            {
                if (await Revisar(devolucionId, ahora, cancelacion).ConfigureAwait(false))
                {
                    rechazadas++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una devolución que falla (la auditoría caída, el almacén o un bloqueo agotado) no detiene a las demás: se reintenta en la próxima revisión.
            }
        }

        return Resultado<int>.Exito(rechazadas);
    }

    private Task<bool> Revisar(Guid devolucionId, DateTimeOffset ahora, CancellationToken cancelacion) =>
        unidad.Ejecutar(
            async token =>
            {
                // SKIP LOCKED: una recepción en curso tiene la fila y se respeta; null también si ya no existe.
                var devolucion = await repositorio.ObtenerParaActualizarSinEsperar(devolucionId, token).ConfigureAwait(false);
                if (devolucion is null || devolucion.RechazarPorPlazoDeRecepcion(ahora) != ResultadoDeTransicionDeDevolucion.Aplicada)
                {
                    // Ya cambió, ya tiene el marcador, no venció o no es un impreso aprobado: la revalidación bajo el bloqueo manda.
                    return false;
                }

                await repositorio.DevolverCupos([.. devolucion.Lineas.Select(l => (l.LineaDePedidoId, l.Cantidad))], token).ConfigureAwait(false);
                await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                await auditoria.Registrar(AuditoriaDeDevoluciones.Cambio(devolucion, EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Rechazada), token).ConfigureAwait(false);
                return true;
            },
            cancelacion);
}

/// <summary>
/// <c>RevisarDevolucionesEstancadas</c> (T12; DEV-12, D07): publica la alerta técnica si hay una devolución con el reingreso iniciado hace <b>más de 24 horas</b> sin completarse (todas sus líneas siguen aprobadas). Una sola alerta por corrida, con el motivo del catálogo cerrado y sin el
/// identificador de la devolución ni dato alguno; una alerta que no sale nunca interrumpe la revisión. Devuelve cuántas encontró. Solo el «sistema».
/// </summary>
internal sealed class RevisarDevolucionesEstancadas(AutorizadorDeDevoluciones autorizador, IRepositorioDeDevoluciones repositorio, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public static readonly TimeSpan PlazoDeEstancamiento = TimeSpan.FromHours(24);

    public async Task<Resultado<int>> Ejecutar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<int>(rechazo);
        }

        IReadOnlyList<Guid> estancadas;
        try
        {
            estancadas = await repositorio.ObtenerConReingresoEstancado(reloj.Ahora - PlazoDeEstancamiento, 500, cancelacion).ConfigureAwait(false);
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<int>();
        }

        if (estancadas.Count > 0)
        {
            try
            {
                await alertas.Publicar(new AlertaTecnica(ClavesDeDevoluciones.AlertaGenerica, reloj.Ahora), cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una alerta que no sale nunca interrumpe la revisión: la próxima hora vuelve a intentarlo.
            }
        }

        return Resultado<int>.Exito(estancadas.Count);
    }
}
