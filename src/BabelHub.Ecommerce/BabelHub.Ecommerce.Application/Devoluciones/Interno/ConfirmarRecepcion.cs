using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// <c>ConfirmarRecepcion</c> (T10; DEV-07, DEV-08, DEV-16, DEV-17, DEV-21; plan §1, D07): un <b>orquestador sin transacción</b> de pasos cortos. (1) Una transacción fija el marcador <c>reingreso_iniciado_en</c> bajo el bloqueo de la devolución y audita la recepción
/// iniciada; (2) <b>fuera de transacción</b>, el reingreso al Inventario (idempotente por devolución, carga inmutable desde el marcador); (2b) una transacción marca el reingreso confirmado y lo audita; (3) una transacción pasa las líneas a recibida y registra el
/// reembolso de cada una; (4) fuera de transacción, <c>Procesar</c> y reembolsada por línea al confirmar la pasarela. <b>Un resultado incierto del Inventario conserva el marcador</b> (el reingreso pudo aplicarse); solo un rechazo de negocio, sin intento incierto previo,
/// lo limpia; un conflicto de idempotencia no lo limpia y alerta. Si el Inventario falla, la línea no pasa a recibida y se repite sin reingresar dos veces. Un libro digital nunca llega aquí (regla 12).
/// </summary>
internal sealed class ConfirmarRecepcion(
    AutorizadorDeDevoluciones autorizador,
    LimitesDeDevoluciones limites,
    IInventarioDeDevoluciones inventario,
    IReembolsos reembolsos,
    IRepositorioDeDevoluciones repositorio,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    ProcesarReembolsoDeLinea procesador,
    IReloj reloj)
{
    public async Task<Resultado<DevolucionVista>> Ejecutar(Guid devolucionId, CancellationToken cancelacion = default)
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

            if (await autorizador.ExigirAlcanceDeGestion(inicial, cancelacion).ConfigureAwait(false) is { } fueraDeAlcance)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(fueraDeAlcance);
            }

            var responsable = ResponsableDelCambio.Personal(autorizador.Actor!.UsuarioId!.Value);

            // (1) El marcador, bajo el bloqueo de la devolución, con su auditoría (si no se puede auditar, no hay marcador ni llamada).
            var reingresoYaConfirmado = false;
            ReingresoPorDevolucion? carga = null;
            await unidad.Ejecutar(
                async token =>
                {
                    var devolucion = await Bloquear(devolucionId, token).ConfigureAwait(false);
                    switch (devolucion.IniciarReingreso(reloj.Ahora))
                    {
                        case ResultadoDeInicioDeReingreso.Iniciado:
                            await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                            await auditoria.Registrar(AuditoriaDeDevoluciones.Cambio(devolucion, EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Aprobada, "recepcion-iniciada"), token).ConfigureAwait(false);
                            break;
                        case ResultadoDeInicioDeReingreso.YaIniciado:
                            break;
                        default:
                            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeDevoluciones.EstadoInvalido));
                    }

                    reingresoYaConfirmado = devolucion.ReingresoConfirmadoEn is not null;
                    carga = new ReingresoPorDevolucion(devolucion.Id, devolucion.PedidoId, devolucion.CodigoDeSucursalReceptora!, [.. devolucion.Lineas.Select(l => new LineaDeReingreso(l.EdicionId, l.Cantidad))]);
                    return true;
                },
                cancelacion).ConfigureAwait(false);

            // (2) El Inventario, fuera de toda transacción; un reintento tras el reingreso confirmado salta al paso 3.
            if (!reingresoYaConfirmado)
            {
                var resultado = await Reingresar(carga!, cancelacion).ConfigureAwait(false);
                if (await ResolverElReingreso(devolucionId, resultado, cancelacion).ConfigureAwait(false) is { } error)
                {
                    return FallosDeDevoluciones.Fallo<DevolucionVista>(error);
                }
            }

            // (3) Recibida y el reembolso de cada línea; (4) la pasarela, fuera de transacción.
            var ids = new List<(Guid LineaDePedidoId, Guid ReembolsoId)>();
            await unidad.Ejecutar(
                async token =>
                {
                    var devolucion = await Bloquear(devolucionId, token).ConfigureAwait(false);
                    if (devolucion.Recibir(reloj.Ahora, responsable) != ResultadoDeTransicionDeDevolucion.Aplicada)
                    {
                        throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeDevoluciones.EstadoInvalido));
                    }

                    foreach (var linea in devolucion.Lineas)
                    {
                        var registrado = await reembolsos.Registrar(
                            new RegistrarReembolsoEntrada(devolucion.PedidoId, OrigenDelReembolso.Devolucion, $"dev:{devolucion.Id:D}:{linea.LineaDePedidoId:D}", [new LineaAReembolsar(linea.LineaDePedidoId, linea.Monto)], 0m),
                            token).ConfigureAwait(false);
                        if (!registrado.EsExito)
                        {
                            throw new RechazoQueRevierteException(registrado.Error!.Categoria == CategoriaDeError.NoDisponible
                                ? new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible)
                                : new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.LineaNoDevolvible));
                        }

                        devolucion.RegistrarReembolsoDeLinea(linea.LineaDePedidoId, reloj.Ahora);
                        ids.Add((linea.LineaDePedidoId, registrado.Valor.ReembolsoId));
                    }

                    await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                    await auditoria.Registrar(AuditoriaDeDevoluciones.Cambio(devolucion, EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Recibida), token).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);

            foreach (var (lineaDePedidoId, reembolsoId) in ids)
            {
                await procesador.Ejecutar(devolucionId, lineaDePedidoId, reembolsoId, cancelacion).ConfigureAwait(false);
            }

            var final = await repositorio.Obtener(devolucionId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            return Resultado<DevolucionVista>.Exito(MapeoDeDevoluciones.Vista(final!));
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

    private async Task<Devolucion> Bloquear(Guid devolucionId, CancellationToken token) =>
        await repositorio.Obtener(devolucionId, paraActualizar: true, token).ConfigureAwait(false)
        ?? throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDeDevoluciones.NoEncontrado));

    private async Task<ResultadoDelReingreso> Reingresar(ReingresoPorDevolucion carga, CancellationToken cancelacion)
    {
        try
        {
            return await inventario.Reingresar(carga, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una excepción del adaptador nunca propaga su texto: el reingreso pudo aplicarse, así que es incierto.
            return new ResultadoDelReingreso.Incierto();
        }
    }

    /// <summary>Traduce el resultado del Inventario a su marca y su error: <see langword="null"/> si el reingreso quedó confirmado y se sigue con el paso 3.</summary>
    private async Task<ErrorDeAplicacion?> ResolverElReingreso(Guid devolucionId, ResultadoDelReingreso resultado, CancellationToken cancelacion)
    {
        switch (resultado)
        {
            case ResultadoDelReingreso.Reingresado:
                await unidad.Ejecutar(
                    async token =>
                    {
                        var devolucion = await Bloquear(devolucionId, token).ConfigureAwait(false);
                        devolucion.ConfirmarReingreso(reloj.Ahora);
                        await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                        await auditoria.Registrar(
                            new SolicitudDeRegistro(AccionesAuditables.ReingresoPorDevolucion)
                            {
                                ObjetoTipo = "devolucion",
                                ObjetoId = devolucion.Id.ToString(),
                                Detalle = new Dictionary<string, string?> { ["devolucionId"] = devolucion.Id.ToString() },
                            },
                            token).ConfigureAwait(false);
                        return true;
                    },
                    cancelacion).ConfigureAwait(false);
                return null;

            case ResultadoDelReingreso.RechazoDeNegocio:
                // Garantiza que no se reingresó nada: limpia el marcador solo si ningún intento anterior quedó incierto, para que la revisión de plazos vuelva a aplicar. Sin bucle de reintentos.
                await unidad.Ejecutar(
                    async token =>
                    {
                        var devolucion = await Bloquear(devolucionId, token).ConfigureAwait(false);
                        if (devolucion.LimpiarMarcadorDeReingreso())
                        {
                            await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                        }

                        return true;
                    },
                    cancelacion).ConfigureAwait(false);
                await Alertar(cancelacion).ConfigureAwait(false);
                return new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.ReingresoRechazado);

            case ResultadoDelReingreso.ConflictoDeIdempotencia:
                // Implica un reingreso previo de esa devolución con otra carga (AJU-16): no limpia el marcador, alerta y se resuelve a mano.
                await Alertar(cancelacion).ConfigureAwait(false);
                return new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeDevoluciones.Conflicto);

            default:
                await unidad.Ejecutar(
                    async token =>
                    {
                        var devolucion = await Bloquear(devolucionId, token).ConfigureAwait(false);
                        devolucion.MarcarReingresoIncierto(reloj.Ahora);
                        await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                        return true;
                    },
                    cancelacion).ConfigureAwait(false);
                return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible);
        }
    }

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(ClavesDeDevoluciones.AlertaGenerica, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale nunca interrumpe la operación.
        }
    }

}
