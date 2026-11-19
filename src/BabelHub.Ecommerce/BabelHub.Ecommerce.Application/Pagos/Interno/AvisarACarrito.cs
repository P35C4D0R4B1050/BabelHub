using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>Lo que los orquestadores de Pagos usan para avisar a Carrito <b>después de confirmar</b> su transacción: nunca interrumpe ni es causa de compensación.</summary>
internal interface IAvisadorACarrito
{
    /// <summary>Envía a Carrito, en orden, los avisos listos de la intención. Una falla del envío queda anotada para el reintento del trabajo.</summary>
    Task Enviar(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary><c>reservas.extender</c> terminó: libera el <c>PagoPendiente</c> retenido del intento para que salga con los instantes definitivos (PAG-13).</summary>
    Task LiberarElPendienteRetenido(Guid pedidoId, Guid intentoId, CancellationToken cancelacion = default);
}

/// <summary>
/// Avisar a Carrito (T18, D13, PAG-28): los avisos se anotan en <c>avisos_a_carrito</c> <b>en la misma transacción de la transición</b> (clave <c>(intención, intento, transición)</c>) y salen después de confirmar, por
/// <see cref="IProgresoDelCheckout"/>, idempotentes y reintentados por un trabajo hasta que Carrito los acepta. Los instantes de un <c>PagoPendiente</c> se leen <b>al enviar</b> (Pagos es la fuente), y el de una compra con
/// impresos nace <c>retenido</c> hasta que termina <c>reservas.extender</c>. Los avisos de una misma intención salen en orden de <c>creado_en</c> y, si uno no se puede enviar, los siguientes esperan; un <c>PagoPendiente</c> de
/// un intento ya resuelto pasa a <c>omitido</c>. Una falla de Carrito nunca cambia el estado del pago.
/// </summary>
internal sealed class AvisarACarrito(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, IProgresoDelCheckout carrito, IPublicadorDeAlertasTecnicas alertas, IReloj reloj) : IAvisadorACarrito
{
    public const string MotivoDeAlertaAvisoACarritoFallido = "aviso-a-carrito-fallido";

    public async Task Enviar(Guid pedidoId, CancellationToken cancelacion = default)
    {
        try
        {
            await EnviarLosListos(pedidoId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Carrito o la base no respondieron: los avisos siguen anotados y el trabajo los reintenta.
            await Alertar().ConfigureAwait(false);
        }
    }

    /// <summary>El trabajo: envía los avisos listos de hasta <paramref name="limite"/> intenciones. Devuelve cuántas intenciones atendió.</summary>
    public async Task<int> EnviarPendientes(int limite, CancellationToken cancelacion = default)
    {
        var intenciones = await unidad.Ejecutar(token => repositorio.ListarIntencionesConAvisosACarritoListos(reloj.Ahora, limite, token), cancelacion).ConfigureAwait(false);
        foreach (var pedidoId in intenciones)
        {
            await Enviar(pedidoId, cancelacion).ConfigureAwait(false);
        }

        return intenciones.Count;
    }

    public async Task LiberarElPendienteRetenido(Guid pedidoId, Guid intentoId, CancellationToken cancelacion = default)
    {
        try
        {
            await unidad.Ejecutar(
                async token =>
                {
                    foreach (var aviso in await repositorio.ListarAvisosACarritoDeLaIntencion(pedidoId, token).ConfigureAwait(false))
                    {
                        if (aviso.IntentoId == intentoId && aviso.Transicion == TransicionDeAviso.Pendiente && aviso.Liberar())
                        {
                            await repositorio.GuardarAvisoACarrito(aviso, token).ConfigureAwait(false);
                        }
                    }

                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Alertar().ConfigureAwait(false);
        }

        await Enviar(pedidoId, cancelacion).ConfigureAwait(false);
    }

    private async Task EnviarLosListos(Guid pedidoId, CancellationToken cancelacion)
    {
        var (intencion, avisos) = await unidad.Ejecutar(
            async token => (
                await repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, token).ConfigureAwait(false),
                await repositorio.ListarAvisosACarritoDeLaIntencion(pedidoId, token).ConfigureAwait(false)),
            cancelacion).ConfigureAwait(false);
        if (intencion is null)
        {
            return;
        }

        // En orden de creación: lo que pasó primero se avisa primero.
        foreach (var aviso in avisos.OrderBy(a => a.CreadoEn).ThenBy(a => a.Transicion))
        {
            if (aviso.Estado is EstadoDeAvisoACarrito.Enviado or EstadoDeAvisoACarrito.Omitido)
            {
                continue;
            }

            // Un «pendiente» de un intento que ya se resolvió no tiene sentido: lo reemplaza el aviso de lo que pasó.
            if (aviso.Transicion == TransicionDeAviso.Pendiente && await IntentoYaResuelto(aviso.IntentoId, cancelacion).ConfigureAwait(false))
            {
                aviso.Omitir();
                await unidad.Ejecutar(token => Guardar(aviso, token), cancelacion).ConfigureAwait(false);
                continue;
            }

            var ahora = reloj.Ahora;
            if (!aviso.EstaListo(ahora))
            {
                // Retenido o esperando tras una falla: los siguientes esperan para no salir desordenados.
                return;
            }

            if (!await Entregar(aviso, intencion, cancelacion).ConfigureAwait(false))
            {
                aviso.RegistrarFalla(ahora);
                await unidad.Ejecutar(token => Guardar(aviso, token), cancelacion).ConfigureAwait(false);
                return;
            }

            aviso.MarcarEnviado(ahora);
            await unidad.Ejecutar(token => Guardar(aviso, token), cancelacion).ConfigureAwait(false);
        }
    }

    private Task<bool> IntentoYaResuelto(Guid intentoId, CancellationToken cancelacion) =>
        unidad.Ejecutar(
            async token =>
            {
                var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
                return intento is null || intento.Estado is not (EstadoDeIntento.Iniciado or EstadoDeIntento.PendienteDeConfirmacion);
            },
            cancelacion);

    private async Task<bool> Guardar(AvisoACarrito aviso, CancellationToken token)
    {
        await repositorio.GuardarAvisoACarrito(aviso, token).ConfigureAwait(false);
        return true;
    }

    /// <summary>La llamada a Carrito, <b>fuera de toda transacción</b>: aceptado es aplicado, sin cambios o que ya no aplica; «aún no» o una excepción es un reintento.</summary>
    private async Task<bool> Entregar(AvisoACarrito aviso, IntencionDePago intencion, CancellationToken cancelacion)
    {
        try
        {
            var resultado = aviso.Transicion switch
            {
                // Los instantes son los de Pagos, leídos ahora (tras reservas.extender); una compra solo digital no tiene reserva y se informa el límite de espera.
                TransicionDeAviso.Pendiente => await carrito.PagoPendiente(intencion.Id, intencion.LimitePendienteEn, intencion.ReservaExpiraEn ?? intencion.LimitePendienteEn, cancelacion).ConfigureAwait(false),
                TransicionDeAviso.Aprobado => await carrito.PagoAprobado(intencion.Id, cancelacion).ConfigureAwait(false),
                TransicionDeAviso.Rechazado => await carrito.PagoResuelto(intencion.Id, MotivoDePagoResueltoDeCompra.Rechazado, cancelacion).ConfigureAwait(false),
                TransicionDeAviso.Vencido => await carrito.PagoResuelto(intencion.Id, MotivoDePagoResueltoDeCompra.Vencido, cancelacion).ConfigureAwait(false),
                _ => await carrito.PagoResuelto(intencion.Id, MotivoDePagoResueltoDeCompra.Compensado, cancelacion).ConfigureAwait(false),
            };
            return resultado != ResultadoDelAvisoDePago.AunNo;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task Alertar()
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(MotivoDeAlertaAvisoACarritoFallido, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe nada.
        }
    }
}
