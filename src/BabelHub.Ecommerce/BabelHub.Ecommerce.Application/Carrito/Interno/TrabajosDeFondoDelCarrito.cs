using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

public sealed record TrabajoDelCarritoEntrada;

/// <summary>Cuántos registros tocó una corrida de un trabajo de fondo: solo un conteo, sin datos personales (D09, AUD-31).</summary>
public sealed record ResumenDeTrabajoDelCarrito(int Tocados);

/// <summary>Lo común de los trabajos de fondo de Carrito (T23): solo corren con el actor «sistema» (el ejecutor de Infraestructura los hospeda como tal) y procesan a lo más un lote por corrida; lo que falte lo retoma la siguiente.</summary>
internal static class TrabajosDelCarrito
{
    public const int MaximoPorCorrida = 500;

    public static TimeSpan Retencion { get; } = TimeSpan.FromDays(30);

    public static TimeSpan AvisosVistos { get; } = TimeSpan.FromDays(30);

    public static TimeSpan AvisosSinVer { get; } = TimeSpan.FromDays(90);

    public static TimeSpan PagoPendienteAlertable { get; } = TimeSpan.FromMinutes(45);

    public static TimeSpan ReservandoAtascado { get; } = Checkout.DuracionMaximaDeReservando;
}

/// <summary><c>PurgarCarritosDeVisitante</c> (CAR-29; T23; AC-CAR-25): elimina los carritos de visitante que vencieron a los 30 días de su última acción de la persona y las constancias de fusión de más de 30 días. Sin datos personales.</summary>
internal sealed class PurgarCarritosDeVisitante(ServiciosDelCarrito servicios) : ICasoDeUso<TrabajoDelCarritoEntrada, ResumenDeTrabajoDelCarrito>
{
    public async Task<Resultado<ResumenDeTrabajoDelCarrito>> Ejecutar(TrabajoDelCarritoEntrada entrada, CancellationToken cancelacion = default)
    {
        if (await servicios.Autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResumenDeTrabajoDelCarrito>.Fallo(rechazo);
        }

        var ahora = servicios.Reloj.Ahora;
        var tocados = await servicios.Unidad.Ejecutar(
            async token =>
            {
                var carritos = await servicios.Carritos.EliminarDeVisitantesVencidos(ahora, TrabajosDelCarrito.MaximoPorCorrida, token).ConfigureAwait(false);
                var fusiones = await servicios.Carritos.EliminarFusionesAnterioresA(ahora - TrabajosDelCarrito.Retencion, TrabajosDelCarrito.MaximoPorCorrida, token).ConfigureAwait(false);
                await servicios.Auditoria.PurgaRealizada("carritos-de-visitante", carritos + fusiones, token).ConfigureAwait(false);
                return carritos + fusiones;
            },
            cancelacion).ConfigureAwait(false);
        return Resultado<ResumenDeTrabajoDelCarrito>.Exito(new ResumenDeTrabajoDelCarrito(tocados));
    }
}

/// <summary><c>PurgarAvisosVistos</c> (D15): elimina los avisos 30 días después de vistos o a los 90 días de creados.</summary>
internal sealed class PurgarAvisosVistos(ServiciosDelCarrito servicios) : ICasoDeUso<TrabajoDelCarritoEntrada, ResumenDeTrabajoDelCarrito>
{
    public async Task<Resultado<ResumenDeTrabajoDelCarrito>> Ejecutar(TrabajoDelCarritoEntrada entrada, CancellationToken cancelacion = default)
    {
        if (await servicios.Autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResumenDeTrabajoDelCarrito>.Fallo(rechazo);
        }

        var ahora = servicios.Reloj.Ahora;
        var tocados = await servicios.Unidad.Ejecutar(
            async token =>
            {
                var eliminados = await servicios.Carritos.EliminarAvisosVencidos(ahora - TrabajosDelCarrito.AvisosVistos, ahora - TrabajosDelCarrito.AvisosSinVer, TrabajosDelCarrito.MaximoPorCorrida, token).ConfigureAwait(false);
                await servicios.Auditoria.PurgaRealizada("avisos", eliminados, token).ConfigureAwait(false);
                return eliminados;
            },
            cancelacion).ConfigureAwait(false);
        return Resultado<ResumenDeTrabajoDelCarrito>.Exito(new ResumenDeTrabajoDelCarrito(tocados));
    }
}

/// <summary>
/// <c>PurgarContactoYCheckoutsVencidos</c> (CAR-34, D08, D09; T23; AC-CAR-30): a los 30 días de la <b>última modificación de la persona</b> elimina el contacto (con el checkout en <c>en_edicion</c> o <c>pago_pendiente</c>) o cancela el checkout abandonado
/// (<c>reservando</c> o <c>en_pago</c>, cancelando antes su intención y anotando la reserva), elimina los checkouts <b>terminados</b> de más de 30 días y <b>todo lo de las cuentas que ya no están activas</b>; registra en la auditoría el conteo, sin datos personales.
/// Cada checkout se trata en su propia transacción, bloqueando el checkout y después el carrito (D10), y de forma condicional al estado leído.
/// </summary>
internal sealed class PurgarContactoYCheckoutsVencidos(ServiciosDelCheckout servicios) : ICasoDeUso<TrabajoDelCarritoEntrada, ResumenDeTrabajoDelCarrito>
{
    public async Task<Resultado<ResumenDeTrabajoDelCarrito>> Ejecutar(TrabajoDelCarritoEntrada entrada, CancellationToken cancelacion = default)
    {
        var carrito = servicios.Carrito;
        if (await carrito.Autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResumenDeTrabajoDelCarrito>.Fallo(rechazo);
        }

        var ahora = carrito.Reloj.Ahora;
        var antes = ahora - TrabajosDelCarrito.Retencion;
        var tocados = 0;

        // 1. Los terminados de 30 días o más (solo guardan cuenta, pedido y fechas; el repositorio no borra los que aún tienen una reserva por liberar). Va primero para que un checkout que esta misma corrida cancela por vencimiento se borre en la siguiente, no en esta.
        tocados += await carrito.Unidad.Ejecutar(token => carrito.Checkouts.EliminarTerminadosAnterioresA(antes, TrabajosDelCarrito.MaximoPorCorrida, token), cancelacion).ConfigureAwait(false);

        // 2. Los checkouts vivos que la persona no toca desde hace 30 días.
        foreach (var cuenta in await carrito.Checkouts.CuentasConCheckoutVivoSinMovimientoDesde(antes, TrabajosDelCarrito.MaximoPorCorrida, cancelacion).ConfigureAwait(false))
        {
            tocados += await VencerUno(cuenta, cancelacion).ConfigureAwait(false);
        }

        // 3. Todo lo de las cuentas que ya no están activas.
        tocados += await PurgarCuentasInactivas(antes, cancelacion).ConfigureAwait(false);

        await carrito.Unidad.Ejecutar(
            async token =>
            {
                await carrito.Auditoria.PurgaRealizada("contacto-y-checkouts", tocados, token).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
        return Resultado<ResumenDeTrabajoDelCarrito>.Exito(new ResumenDeTrabajoDelCarrito(tocados));
    }

    /// <summary>Un checkout vivo vencido: abandonado en <c>reservando</c> o <c>en_pago</c> se cancela (con su intención cancelada antes y la liberación anotada); en <c>en_edicion</c> o <c>pago_pendiente</c> se le elimina el contacto.</summary>
    private async Task<int> VencerUno(Guid cuenta, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var leido = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (leido is null || !(leido.ContactoVencido(carrito.Reloj.Ahora) || leido.Estado is EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago))
        {
            return 0;
        }

        var abandonado = leido.Estado is EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago;
        if (abandonado && leido.PedidoId is { } pedido)
        {
            // La intención se cancela primero y fuera de transacción; si Pagos no responde o hay un cobro vivo, esta corrida no lo toca.
            try
            {
                if (await carrito.Edicion.Pagos.Cancelar(cuenta, pedido, cancelacion).ConfigureAwait(false) == ResultadoDeCancelarIntencion.NoCancelable)
                {
                    return 0;
                }
            }
            catch (Exception excepcion) when (excepcion is not OperationCanceledException)
            {
                return 0;
            }
        }

        var cambiado = await carrito.Unidad.Ejecutar(
            async token =>
            {
                var ahora = carrito.Reloj.Ahora;
                var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: true, token).ConfigureAwait(false);
                var delCarrito = await carrito.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, token).ConfigureAwait(false);
                if (checkout is null || delCarrito is null || checkout.Id != leido.Id || checkout.Estado != leido.Estado)
                {
                    return (Guid?)null;
                }

                var efecto = abandonado ? checkout.CancelarPorVencimiento(delCarrito, ahora) : checkout.EliminarContacto(MotivoDeEliminacionDeContacto.Vencido, ahora);
                if (efecto.Efecto != EfectoDeTransicion.Aplicada)
                {
                    return null;
                }

                await carrito.Checkouts.Guardar(checkout, token).ConfigureAwait(false);
                if (efecto.Retiradas.Count > 0)
                {
                    await carrito.Carritos.Guardar(delCarrito, token).ConfigureAwait(false);
                    await carrito.Carritos.AgregarAvisos(cuenta, carrito.AvisosDeRetiro(efecto.Retiradas, new Dictionary<Guid, EdicionConsultada>(), ahora), token).ConfigureAwait(false);
                }

                return checkout.PedidoALiberar is { } aLiberar && checkout.ReservaALiberarDesde is not null ? aLiberar : Guid.Empty;
            },
            cancelacion).ConfigureAwait(false);

        if (cambiado is { } liberar && liberar != Guid.Empty)
        {
            await new LiberadorDeReservas(servicios).Liberar(liberar, cancelacion).ConfigureAwait(false);
        }

        return cambiado is null ? 0 : 1;
    }

    private async Task<int> PurgarCuentasInactivas(DateTimeOffset antes, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var eliminadas = 0;
        foreach (var cuenta in await carrito.Carritos.CuentasSinMovimientoDesde(antes, TrabajosDelCarrito.MaximoPorCorrida, cancelacion).ConfigureAwait(false))
        {
            if (!await carrito.CuentaYaNoEstaActiva(cuenta, cancelacion).ConfigureAwait(false))
            {
                continue;
            }

            await carrito.Unidad.Ejecutar(
                async token =>
                {
                    await new EliminarCarritoYContacto(carrito).Eliminar(cuenta, "cuenta-inactiva", token).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
            eliminadas++;
        }

        return eliminadas;
    }
}

/// <summary>
/// <c>RecuperarCheckoutsReservando</c> (D20; T23): un checkout <c>reservando</c> de más de 2 minutos está atascado por un corte entre los pasos. Lo aborta con la regla de T18, <b>condicional al <c>pedidoId</c> y la generación leídos</b>, de modo que no toca uno que ya
/// pasó a <c>en_pago</c> (cancela la intención por si 3a la creó y libera la reserva después de confirmar, y aplica los retiros pendientes al volver a <c>en_edicion</c>, con el carrito siempre después del checkout).
/// </summary>
internal sealed class RecuperarCheckoutsReservando(ServiciosDelCheckout servicios) : ICasoDeUso<TrabajoDelCarritoEntrada, ResumenDeTrabajoDelCarrito>
{
    public async Task<Resultado<ResumenDeTrabajoDelCarrito>> Ejecutar(TrabajoDelCarritoEntrada entrada, CancellationToken cancelacion = default)
    {
        var carrito = servicios.Carrito;
        if (await carrito.Autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResumenDeTrabajoDelCarrito>.Fallo(rechazo);
        }

        var atascados = await carrito.Checkouts.ReservandoDesdeAntesDe(carrito.Reloj.Ahora - TrabajosDelCarrito.ReservandoAtascado, TrabajosDelCarrito.MaximoPorCorrida, cancelacion).ConfigureAwait(false);
        var recuperados = 0;
        foreach (var atascado in atascados)
        {
            var desenlace = await new AbortarElPasoAlPago(servicios)
                .Ejecutar(new SolicitudDeAborto(atascado.CuentaId, EstadoDelCheckout.Reservando, atascado.PedidoId, atascado.Generacion, atascado.IntencionPudoCrearse, "recuperacion", null), cancelacion)
                .ConfigureAwait(false);
            if (desenlace == DesenlaceDelAborto.Abortado)
            {
                recuperados++;
            }
        }

        return Resultado<ResumenDeTrabajoDelCarrito>.Exito(new ResumenDeTrabajoDelCarrito(recuperados));
    }
}

/// <summary><c>RevisarCheckoutsPendientes</c> (R03, AUD-31): publica una alerta técnica si hay checkouts con el pago pendiente de más de 45 minutos, que Pagos debía resolver a los 30.</summary>
internal sealed class RevisarCheckoutsPendientes(ServiciosDelCheckout servicios) : ICasoDeUso<TrabajoDelCarritoEntrada, ResumenDeTrabajoDelCarrito>
{
    public async Task<Resultado<ResumenDeTrabajoDelCarrito>> Ejecutar(TrabajoDelCarritoEntrada entrada, CancellationToken cancelacion = default)
    {
        var carrito = servicios.Carrito;
        if (await carrito.Autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResumenDeTrabajoDelCarrito>.Fallo(rechazo);
        }

        var pendientes = await carrito.Checkouts.ContarConElPagoPendienteDesdeAntesDe(carrito.Reloj.Ahora - TrabajosDelCarrito.PagoPendienteAlertable, cancelacion).ConfigureAwait(false);
        if (pendientes > 0)
        {
            await servicios.Alertas.Publicar(TipoDeAlertaDelCarrito.CheckoutsConElPagoPendienteDemasiado, pendientes, cancelacion).ConfigureAwait(false);
        }

        return Resultado<ResumenDeTrabajoDelCarrito>.Exito(new ResumenDeTrabajoDelCarrito(pendientes));
    }
}
