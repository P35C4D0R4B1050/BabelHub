using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// <c>ProcesarReembolsoDeLinea</c> (T11; DEV-08, DEV-17, DEV-21): pide la pasarela <b>fuera de transacción</b> y pasa la línea a reembolsada <b>solo cuando la pasarela confirma</b>, con el responsable «sistema» y su auditoría; una falla o la falta de confirmación dejan la
/// línea como estaba (recibida de un impreso o aprobada de un digital) con el reembolso pendiente, reintentable sin repetir pasos. Nunca lanza.
/// </summary>
internal sealed class ProcesarReembolsoDeLinea(IReembolsos reembolsos, IRepositorioDeDevoluciones repositorio, IUnidadDeTrabajo unidad, IAuditoria auditoria, AvisosDeDevoluciones avisos, IReloj reloj)
{
    public async Task Ejecutar(Guid devolucionId, Guid lineaDePedidoId, Guid reembolsoId, CancellationToken cancelacion)
    {
        Resultado<EstadoDelReembolso> procesado;
        try
        {
            procesado = await reembolsos.Procesar(reembolsoId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        if (procesado.EsExito && procesado.Valor == EstadoDelReembolso.Reembolsado)
        {
            await PasarAReembolsada(devolucionId, lineaDePedidoId, cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>La transacción corta que pasa la línea a reembolsada: bajo el bloqueo de la devolución, idempotente (una línea ya reembolsada no cambia) y con la auditoría que falla cerrada. El aviso se pide en la misma transacción; la cuenta que lo recibe se lee de Pedidos antes de abrirla (si Pedidos no responde, no se completa el paso y la conciliación lo repite, sin perder el aviso).</summary>
    public async Task<bool> PasarAReembolsada(Guid devolucionId, Guid lineaDePedidoId, CancellationToken cancelacion)
    {
        try
        {
            var previa = await repositorio.Obtener(devolucionId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (previa is null || await avisos.Destinatario(previa.PedidoId, cancelacion).ConfigureAwait(false) is not { } cuentaId)
            {
                return false;
            }

            return await unidad.Ejecutar(
                async token =>
                {
                    var devolucion = await repositorio.Obtener(devolucionId, paraActualizar: true, token).ConfigureAwait(false);
                    var linea = devolucion?.Lineas.SingleOrDefault(l => l.LineaDePedidoId == lineaDePedidoId);
                    if (devolucion is null || linea is null)
                    {
                        return false;
                    }

                    var anterior = linea.Estado;
                    if (devolucion.Reembolsar(lineaDePedidoId, reloj.Ahora, ResponsableDelCambio.Sistema) != ResultadoDeTransicionDeDevolucion.Aplicada)
                    {
                        return false;
                    }

                    await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                    await auditoria.Registrar(AuditoriaDeDevoluciones.Cambio(devolucion, anterior, EstadoDeLineaDeDevolucion.Reembolsada, lineaDePedidoId: lineaDePedidoId), token).ConfigureAwait(false);
                    await avisos.Solicitar(devolucion, cuentaId, EstadoDeLineaDeDevolucion.Reembolsada, token).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La pasarela ya confirmó: la conciliación pasa la línea a reembolsada sin repetir el reembolso.
            return false;
        }
    }
}

/// <summary>
/// <c>ConciliarReembolsosDeDevoluciones</c> (T11; DEV-08, DEV-17, DEV-21, D08): el trabajo de cada 5 minutos, como «sistema», recorre las líneas pendientes (recibida de un impreso o aprobada de un digital): si no hay reembolso registrado lo <b>registra</b> (idempotente por la
/// clave natural) y lo procesa; si lo hay, <b>consulta <c>ConsultarPorOrigen</c></b> y, si ya está reembolsado (por el reintento de la administración en Pagos), pasa la línea a reembolsada; si sigue pendiente, lo vuelve a procesar. No completa la recepción (exige el
/// token de una persona para el Inventario). Un fallo de una línea no detiene a las demás.
/// </summary>
internal sealed class ConciliarReembolsosDeDevoluciones(
    AutorizadorDeDevoluciones autorizador,
    IRepositorioDeDevoluciones repositorio,
    IReembolsos reembolsos,
    IUnidadDeTrabajo unidad,
    ProcesarReembolsoDeLinea procesador,
    IReloj reloj)
{
    public async Task<Resultado<int>> Ejecutar(int maximo, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<int>(rechazo);
        }

        IReadOnlyList<(Guid DevolucionId, Guid LineaDePedidoId)> pendientes;
        try
        {
            pendientes = await repositorio.ObtenerConReembolsoPendiente(Math.Clamp(maximo, 1, 500), cancelacion).ConfigureAwait(false);
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<int>();
        }

        var conciliadas = 0;
        foreach (var (devolucionId, lineaDePedidoId) in pendientes)
        {
            try
            {
                if (await Conciliar(devolucionId, lineaDePedidoId, cancelacion).ConfigureAwait(false))
                {
                    conciliadas++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una línea que falla no detiene a las demás: se reintenta en la próxima corrida.
            }
        }

        return Resultado<int>.Exito(conciliadas);
    }

    private async Task<bool> Conciliar(Guid devolucionId, Guid lineaDePedidoId, CancellationToken cancelacion)
    {
        var devolucion = await repositorio.Obtener(devolucionId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        var linea = devolucion?.Lineas.SingleOrDefault(l => l.LineaDePedidoId == lineaDePedidoId);
        if (devolucion is null || linea is null || !MapeoDeDevoluciones.ReembolsoPendiente(devolucion, linea))
        {
            return false;
        }

        var clave = ClaveDeReembolso.De(devolucion.Id, linea.LineaDePedidoId);
        if (linea.ReembolsoRegistradoEn is null)
        {
            // Falta el reembolso: se registra (idempotente por la clave natural) en su propia transacción corta y se procesa.
            Guid? reembolsoId = null;
            await unidad.Ejecutar(
                async token =>
                {
                    var bloqueada = await repositorio.Obtener(devolucionId, paraActualizar: true, token).ConfigureAwait(false);
                    var actual = bloqueada?.Lineas.Single(l => l.LineaDePedidoId == lineaDePedidoId);
                    if (bloqueada is null || actual is null || actual.ReembolsoRegistradoEn is not null || !MapeoDeDevoluciones.ReembolsoPendiente(bloqueada, actual))
                    {
                        return true;
                    }

                    var registrado = await reembolsos.Registrar(new RegistrarReembolsoEntrada(bloqueada.PedidoId, OrigenDelReembolso.Devolucion, clave, [new LineaAReembolsar(actual.LineaDePedidoId, actual.Monto)], 0m), token).ConfigureAwait(false);
                    if (!registrado.EsExito)
                    {
                        throw new RechazoQueRevierteException(registrado.Error!);
                    }

                    bloqueada.RegistrarReembolsoDeLinea(actual.LineaDePedidoId, reloj.Ahora);
                    await repositorio.Guardar(bloqueada, token).ConfigureAwait(false);
                    reembolsoId = registrado.Valor.ReembolsoId;
                    return true;
                },
                cancelacion).ConfigureAwait(false);
            if (reembolsoId is { } id)
            {
                await procesador.Ejecutar(devolucionId, lineaDePedidoId, id, cancelacion).ConfigureAwait(false);
            }

            return true;
        }

        var consulta = await reembolsos.ConsultarPorOrigen(OrigenDelReembolso.Devolucion, clave, cancelacion).ConfigureAwait(false);
        if (!consulta.EsExito)
        {
            return false;
        }

        if (consulta.Valor is null)
        {
            // Registrado en la devolución pero sin rastro en Pagos: se deja para la administración (nunca se asume reembolsado).
            return false;
        }

        if (consulta.Valor.Estado == EstadoDelReembolso.Reembolsado)
        {
            return await procesador.PasarAReembolsada(devolucionId, lineaDePedidoId, cancelacion).ConfigureAwait(false);
        }

        await procesador.Ejecutar(devolucionId, lineaDePedidoId, consulta.Valor.Id, cancelacion).ConfigureAwait(false);
        return true;
    }
}
