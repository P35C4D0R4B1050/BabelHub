using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>Pagos llamó a Carrito y Carrito no pudo aplicar el aviso (por ejemplo, <c>Reabrir</c> falló): Pagos lo reintenta desde <c>avisos_a_carrito</c>; no se cambia nada.</summary>
public sealed class NoSePudoAplicarElAvisoException(string motivo) : Exception($"Carrito no pudo aplicar el aviso de Pagos: {motivo}.");

/// <summary>
/// <c>IProgresoDelCheckout</c> (D13, D16, D21; T21; AC-CAR-23, AC-CAR-30; CAR-25, CAR-34): lo que Pagos informa <b>después de confirmar</b> su transacción. Todos los avisos llevan el <c>pedidoId</c> y son <b>condicionales a él</b> (uno de un pedido descartado no
/// toca el checkout nuevo), idempotentes por pedido y estado, devuelven «aún no» mientras el checkout está <c>reservando</c> (Pagos reintenta) y <b>éxito idempotente si el checkout ya no existe</b> (una cuenta cancelada). La fuente de los instantes es Pagos. Bloquean el
/// checkout y después el carrito (D10). <b>No consultan a Pagos</b> salvo <c>Reabrir</c> cuando hay un <c>retiro_pendiente</c>, fuera de transacción y antes de bloquear.
/// </summary>
internal sealed class ProgresoDelCheckout(ServiciosDelCheckout servicios) : IProgresoDelCheckout
{
    public Task<ResultadoDelAvisoDePago> PagoPendiente(Guid pedidoId, DateTimeOffset limiteEn, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default) =>
        Aplicar(pedidoId, (checkout, _, ahora) => checkout.PagoPendiente(pedidoId, limiteEn, reservaExpiraEn, ahora), cancelacion);

    /// <summary>
    /// <c>pagado</c> (desde <c>pago_pendiente</c> o <c>en_pago</c>): elimina el contacto, aplica los retiros pendientes (con su aviso) y quita del carrito <b>solo la cantidad comprada</b> de las líneas pagadas (D16); las no pagadas se conservan.
    /// </summary>
    public Task<ResultadoDelAvisoDePago> PagoAprobado(Guid pedidoId, CancellationToken cancelacion = default) =>
        Aplicar(
            pedidoId,
            (checkout, carrito, ahora) =>
            {
                var comprado = checkout.Lineas.Select(l => (l.LineaDeCarritoId, l.EdicionId, Cantidad: l.Cantidad.Valor)).ToList();
                var efecto = checkout.PagoAprobado(pedidoId, carrito, ahora);
                if (efecto.Efecto == EfectoDeTransicion.Aplicada)
                {
                    QuitarLoComprado(carrito, comprado, ahora);
                }

                return efecto;
            },
            cancelacion);

    public async Task<ResultadoDelAvisoDePago> PagoResuelto(Guid pedidoId, MotivoDePagoResueltoDeCompra motivo, CancellationToken cancelacion = default)
    {
        var carrito = servicios.Carrito;
        var domino = motivo switch
        {
            MotivoDePagoResueltoDeCompra.Vencido => MotivoDePagoResuelto.Vencido,
            MotivoDePagoResueltoDeCompra.Rechazado => MotivoDePagoResuelto.Rechazado,
            _ => MotivoDePagoResuelto.Compensado,
        };

        // Antes de bloquear (D10): con un retiro pendiente, un rechazo o un vencimiento con la reserva vigente pasan por Reabrir, que se llama fuera de transacción.
        ResultadoDeReabrirIntencion? reabrir = null;
        var antes = await carrito.Checkouts.ObtenerPorPedido(pedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (antes is { Estado: EstadoDelCheckout.PagoPendiente } pendiente && domino != MotivoDePagoResuelto.Compensado && pendiente.PedidoId == pedidoId)
        {
            var delCarrito = await carrito.Carritos.ObtenerDeCuenta(pendiente.CuentaId, conBloqueo: false, cancelacion).ConfigureAwait(false);
            if (delCarrito is { TieneRetirosPendientes: true } && pendiente.ReservaVigente(carrito.Reloj.Ahora))
            {
                try
                {
                    reabrir = await carrito.Edicion.Pagos.Reabrir(pendiente.CuentaId, pedidoId, cancelacion).ConfigureAwait(false);
                }
                catch (Exception excepcion) when (excepcion is not OperationCanceledException)
                {
                    throw new NoSePudoAplicarElAvisoException("Reabrir no respondió");
                }
            }
        }

        return await Aplicar(
            pedidoId,
            (checkout, delCarrito, ahora) =>
            {
                if (reabrir is { } respuesta && checkout.Estado == EstadoDelCheckout.PagoPendiente)
                {
                    return respuesta switch
                    {
                        ResultadoDeReabrirIntencion.Reabierta => checkout.VolverAEnEdicionTrasReabrir(delCarrito, ahora),
                        ResultadoDeReabrirIntencion.NoCancelable => checkout.VolverAEnPagoConservandoElRetiro(pedidoId, ahora),
                        _ => checkout.CancelarPorIntencionCerrada(pedidoId, delCarrito, ahora),
                    };
                }

                var efecto = checkout.PagoResuelto(pedidoId, domino, delCarrito, ahora);

                // El estado cambió entre la lectura previa y el bloqueo y ahora hay un retiro que exige Reabrir: no se aplica a ciegas, Pagos reintenta.
                return efecto.Efecto == EfectoDeTransicion.ConRetiroPendiente ? throw new NoSePudoAplicarElAvisoException("hay un retiro pendiente que exige reabrir la intención") : efecto;
            },
            cancelacion);
    }

    // ---------- Comunes ----------

    /// <summary>
    /// La transacción de un aviso: el checkout por su <c>pedidoId</c> con bloqueo y después el carrito de su cuenta (D10). Si el checkout o el carrito ya no existen (una cuenta cancelada), es un éxito idempotente. Guarda lo que cambió, los retiros
    /// aplicados con su aviso y devuelve qué pasó.
    /// </summary>
    private async Task<ResultadoDelAvisoDePago> Aplicar(Guid pedidoId, Func<Checkout, CarritoDeDominio, DateTimeOffset, ResultadoDeCheckout> operacion, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        return await carrito.Unidad.Ejecutar(
            async token =>
            {
                var ahora = carrito.Reloj.Ahora;
                var checkout = await carrito.Checkouts.ObtenerPorPedido(pedidoId, conBloqueo: true, token).ConfigureAwait(false);
                if (checkout is null)
                {
                    return ResultadoDelAvisoDePago.SinCambios;
                }

                var delCarrito = await carrito.Carritos.ObtenerDeCuenta(checkout.CuentaId, conBloqueo: true, token).ConfigureAwait(false);
                if (delCarrito is null)
                {
                    return ResultadoDelAvisoDePago.SinCambios;
                }

                var efecto = operacion(checkout, delCarrito, ahora);
                if (efecto.Efecto == EfectoDeTransicion.Aplicada)
                {
                    await carrito.Checkouts.Guardar(checkout, token).ConfigureAwait(false);
                    await carrito.Carritos.Guardar(delCarrito, token).ConfigureAwait(false);
                    if (efecto.Retiradas.Count > 0)
                    {
                        await carrito.Carritos.AgregarAvisos(checkout.CuentaId, carrito.AvisosDeRetiro(efecto.Retiradas, new Dictionary<Guid, EdicionConsultada>(), ahora), token).ConfigureAwait(false);
                    }
                }

                return efecto.Efecto switch
                {
                    EfectoDeTransicion.Aplicada => ResultadoDelAvisoDePago.Aplicado,
                    EfectoDeTransicion.SinCambios => ResultadoDelAvisoDePago.SinCambios,
                    EfectoDeTransicion.AunNo => ResultadoDelAvisoDePago.AunNo,
                    _ => ResultadoDelAvisoDePago.NoAplica,
                };
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Quita del carrito solo la cantidad comprada de cada línea pagada (D16), por la línea de carrito de la que salió; de un pago iniciado antes de la venta por sucursal, por la edición en una línea sin sucursal (CAR-50). Una línea que
    /// queda en cero se elimina y las no pagadas se conservan.
    /// </summary>
    private static void QuitarLoComprado(CarritoDeDominio carrito, IReadOnlyList<(Guid? LineaDeCarritoId, Guid EdicionId, int Cantidad)> comprado, DateTimeOffset ahora)
    {
        foreach (var (deCarrito, edicionId, cantidad) in comprado)
        {
            var linea = deCarrito is { } id
                ? carrito.Lineas.FirstOrDefault(l => l.LineaId == id)
                : carrito.Lineas.FirstOrDefault(l => l.EdicionId == edicionId && l.CodigoDeSucursal is null);
            if (linea is not null)
            {
                carrito.QuitarComprado(linea.LineaId, cantidad, ahora);
            }
        }
    }
}
