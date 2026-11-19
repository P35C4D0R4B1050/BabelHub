using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;

namespace BabelHub.Ecommerce.Application.Envios.Interno;

/// <summary>
/// Pasar un envío a en reparto (T07; ENV-11, ENV-12, ENV-19, AC-ENV-10, AC-ENV-11, AC-ENV-16): un <b>orquestador sin transacción</b>. (1) Una transacción corta confirma el <b>marcador de despacho iniciado</b> y su
/// arrendamiento (la simultánea recibe estado inválido; <c>Cancelar</c> ya ve «mantenido»). (2) <b>Fuera de toda transacción y sin retener ningún bloqueo</b>, una sola llamada de cierre al Inventario con todas las líneas
/// y <c>Idempotency-Key</c> = <c>envioId</c>. (3) Una transacción final pasa a en reparto y audita. (4) <b>Después de confirmar</b>, informa a Pedidos (idempotente; si falla, un trabajo lo reintenta). Un fallo del
/// Inventario deja el envío en guía generada sin descontar nada; el marcador se conserva ante todo resultado incierto y se limpia solo ante un rechazo de negocio sin intento incierto previo (D05).
/// </summary>
internal sealed class PasarAEnReparto(
    AutorizadorDeEnvios autorizador,
    LimitesDeEnvios limites,
    IRepositorioDeEnvios envios,
    IReservasDelEnvio reservas,
    InformadorDeProgreso informador,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    AvisosDeEnvios avisos,
    IReloj reloj) : ICasoDeUso<Guid, DetalleDeEnvio>
{
    public async Task<Resultado<DetalleDeEnvio>> Ejecutar(Guid envioId, CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirLogisticaOAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeEnvios.Fallo<DetalleDeEnvio>(rechazo);
        }

        if (await limites.Operacion(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeEnvios.Fallo<DetalleDeEnvio>(excedido);
        }

        try
        {
            // Sin lo que el aviso necesita no se cambia nada y se reintenta.
            var inicial = await envios.Obtener(envioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (inicial is null)
            {
                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.EnvioNoEncontrado);
            }

            if (inicial.Estado != EstadoDeEnvio.GuiaGenerada)
            {
                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.EstadoInvalido);
            }

            var datosDelAviso = await avisos.Preparar(inicial.PedidoId, cancelacion).ConfigureAwait(false);
            if (!datosDelAviso.EsExito)
            {
                return FallosDeEnvios.Fallo<DetalleDeEnvio>(datosDelAviso.Error!);
            }

            // (1) Transacción corta: el marcador de despacho y su arrendamiento.
            var inicio = await unidad.Ejecutar(
                async token =>
                {
                    var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                    if (envio is null)
                    {
                        return (Error: (Resultado<DetalleDeEnvio>?)FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.EnvioNoEncontrado), Lineas: (List<Guid>?)null, PedidoId: Guid.Empty);
                    }

                    if (envio.IniciarDespacho(reloj.Ahora) != ResultadoDeInicioDeDespacho.Iniciado)
                    {
                        return (Error: FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.EstadoInvalido), Lineas: null, PedidoId: Guid.Empty);
                    }

                    await envios.Guardar(envio, token).ConfigureAwait(false);
                    return (Error: null, Lineas: envio.Lineas.Select(l => l.LineaId).ToList(), PedidoId: envio.PedidoId);
                },
                cancelacion).ConfigureAwait(false);
            if (inicio.Error is not null)
            {
                return inicio.Error;
            }

            var lineas = inicio.Lineas!;
            var pedidoId = inicio.PedidoId;

            // (2) Fuera de toda transacción y sin ningún bloqueo: el Inventario descuenta todas las líneas o ninguna.
            ResultadoDeCierreDeReservas cierre;
            try
            {
                cierre = await reservas.Cerrar(pedidoId, envioId, lineas, cancelacion).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancelacion.IsCancellationRequested)
            {
                await TrasUnFallo(envioId, new ResultadoDeCierreDeReservas.Incierto(), CancellationToken.None).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Una excepción inesperada del adaptador es un resultado incierto: se anota, se libera el arrendamiento y el texto nunca se propaga (ENV-12, D05).
                cierre = new ResultadoDeCierreDeReservas.Incierto();
            }

            if (cierre is not ResultadoDeCierreDeReservas.Cerrado)
            {
                return await TrasUnFallo(envioId, cierre, CancellationToken.None).ConfigureAwait(false);
            }

            // (3) Transacción final: en reparto, auditoría. Sin poder auditar se revierte y el marcador sobrevive.
            Resultado<DetalleDeEnvio> terminado;
            try
            {
                terminado = await unidad.Ejecutar(
                    async token =>
                    {
                        var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                        if (envio is null)
                        {
                            return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.NoEncontrado, ClavesDeEnvios.EnvioNoEncontrado);
                        }

                        var anterior = envio.Estado;
                        switch (envio.PasarAEnReparto(reloj.Ahora))
                        {
                            case ResultadoDeTransicion.Hecha:
                                await auditoria.Registrar(
                                    AuditoriaDeEnvios.Cambio(envio, anterior, EstadoDeEnvio.EnReparto),
                                    token).ConfigureAwait(false);
                                await avisos.Solicitar(envio, datosDelAviso.Valor, AvisosDeEnvios.EstadoEnReparto, token).ConfigureAwait(false);
                                await envios.Guardar(envio, token).ConfigureAwait(false);
                                return Resultado<DetalleDeEnvio>.Exito(MapeoDeEnvios.Detalle(envio));

                            case ResultadoDeTransicion.YaEnReparto:
                                // Otra solicitud retomó un marcador vencido y terminó antes: éxito sin auditar ni avisar de nuevo.
                                return Resultado<DetalleDeEnvio>.Exito(MapeoDeEnvios.Detalle(envio));

                            default:
                                return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.EstadoInvalido);
                        }
                    },
                    cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await LiberarElArrendamiento(envioId).ConfigureAwait(false);
                return FallosDeEnvios.NoDisponible<DetalleDeEnvio>();
            }

            if (!terminado.EsExito)
            {
                return terminado;
            }

            // (4) Después de confirmar: Pedidos marca las líneas «despachadas». Si falla, queda pendiente y un trabajo lo reintenta (idempotente).
            await informador.Informar(terminado.Valor.Id, CancellationToken.None).ConfigureAwait(false);
            return terminado;
        }
        catch (EnviosNoDisponiblesException)
        {
            return FallosDeEnvios.NoDisponible<DetalleDeEnvio>();
        }
    }

    /// <summary>
    /// Un fallo del Inventario: el envío sigue en guía generada y no se descontó nada. El arrendamiento se libera siempre; el marcador <b>se conserva</b> ante todo resultado incierto (y se anota el primero) y solo
    /// se limpia ante un rechazo de negocio de la lista cerrada sin un intento incierto previo (D05).
    /// </summary>
    private async Task<Resultado<DetalleDeEnvio>> TrasUnFallo(Guid envioId, ResultadoDeCierreDeReservas cierre, CancellationToken cancelacion)
    {
        var rechazoDeNegocio = cierre is ResultadoDeCierreDeReservas.RechazoDeNegocio;
        var conservado = false;
        try
        {
            conservado = await unidad.Ejecutar(
                async token =>
                {
                    var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                    if (envio is not { Estado: EstadoDeEnvio.GuiaGenerada })
                    {
                        return false;
                    }

                    var queda = true;
                    if (rechazoDeNegocio)
                    {
                        queda = !envio.LimpiarMarcadorDeDespacho();
                    }
                    else
                    {
                        envio.MarcarDespachoIncierto(reloj.Ahora);
                    }

                    envio.LiberarArrendamientoDelDespacho();
                    await envios.Guardar(envio, token).ConfigureAwait(false);
                    return queda;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (EnviosNoDisponiblesException)
        {
            // El arrendamiento vence solo a los 90 s: el reintento espera, pero nada queda a medias.
        }

        if (rechazoDeNegocio)
        {
            if (conservado)
            {
                // Un intento anterior quedó incierto: el marcador no se puede limpiar y logística debe revisarlo.
                await PublicarAlerta().ConfigureAwait(false);
            }

            return FallosDeEnvios.De<DetalleDeEnvio>(CategoriaDeError.Conflicto, ClavesDeEnvios.InventarioRechazo);
        }

        return FallosDeEnvios.NoDisponible<DetalleDeEnvio>();
    }

    private async Task LiberarElArrendamiento(Guid envioId)
    {
        try
        {
            await unidad.Ejecutar(
                async token =>
                {
                    var envio = await envios.Obtener(envioId, paraActualizar: true, token).ConfigureAwait(false);
                    if (envio is not null)
                    {
                        envio.LiberarArrendamientoDelDespacho();
                        await envios.Guardar(envio, token).ConfigureAwait(false);
                    }

                    return true;
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (EnviosNoDisponiblesException)
        {
            // El arrendamiento vence solo a los 90 s.
        }
    }

    private async Task PublicarAlerta()
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(ClavesDeEnvios.MotivoDeAlertaDeDependencia, reloj.Ahora)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no se puede publicar nunca interrumpe la operación.
        }
    }
}
