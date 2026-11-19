using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// La liberación de una reserva <b>después de confirmar</b> la transacción que la anotó (D18, D21): <c>liberar</c> direccionando por el <c>pedidoId</c> (el vigente o el descartado) con el motivo <c>checkout-cancelado</c>. Liberada, ya
/// liberada o <c>INV-NO-ENCONTRADO</c> (nunca existió) limpia <c>reserva_a_liberar_desde</c>; <c>INV-RESERVA-ESTADO-INVALIDO</c> (el pedido ya está confirmado) <b>no se reintenta</b>: se limpia y se publica una alerta técnica; sin respuesta la
/// anotación queda para <c>LiberarReservasPendientes</c> y la reserva expira sola a los 15 minutos. Nada de esto interrumpe el caso de uso que la invoca.
/// </summary>
internal sealed class LiberadorDeReservas(ServiciosDelCheckout servicios)
{
    public async Task<ResultadoDeLiberar> Liberar(Guid pedidoId, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        ResultadoDeLiberar liberada;
        try
        {
            liberada = await servicios.Inventario.Liberar(pedidoId, "checkout-cancelado", cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeLiberar.NoSePudo;
        }

        if (liberada == ResultadoDeLiberar.NoSePudo)
        {
            return liberada;
        }

        if (liberada == ResultadoDeLiberar.PedidoConfirmado)
        {
            await servicios.Alertas.Publicar(TipoDeAlertaDelCarrito.LiberacionDeUnPedidoConfirmado, 1, cancelacion).ConfigureAwait(false);
        }

        await carrito.Unidad.Ejecutar(
            async token =>
            {
                var checkout = await carrito.Checkouts.ObtenerPorPedido(pedidoId, conBloqueo: true, token).ConfigureAwait(false);
                if (checkout is { ReservaALiberarDesde: not null } c && (c.PedidoDescartado == pedidoId || c.PedidoId == pedidoId))
                {
                    c.LiberacionConfirmada();
                    await carrito.Checkouts.Guardar(c, token).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion).ConfigureAwait(false);
        return liberada;
    }
}

/// <summary>
/// <c>VolverAlCarrito</c> y <c>ReiniciarCheckout</c> (CAR-11, CAR-19, CAR-26; T20; AC-CAR-10, AC-CAR-17): son <c>POST</c> (no un <c>GET</c>). Cancelan la <b>intención</b> primero y fuera de transacción (con <c>NoCancelable</c> hay un cobro vivo y se
/// rechaza; si Pagos no responde con el checkout <c>en_pago</c>, no hay cambio local), después, en una transacción con el orden de bloqueo de D10 (checkout, carrito), pasan el checkout a <c>cancelado</c> o <c>reiniciado</c>, <b>eliminan el contacto</b>,
/// aplican los retiros pendientes (con su aviso) y anotan la reserva a liberar, y <b>después de confirmar</b> llaman a <c>liberar</c>. Con el pago pendiente se rechazan y con <c>reservando</c> responden «en curso». Repetirlos sobre un checkout que ya terminó es un
/// éxito idempotente; un <c>pedidoId</c> oculto ajeno es «no encontrado» uniforme. El carrito se conserva siempre.
/// </summary>
internal sealed class SalirDelCheckout(ServiciosDelCheckout servicios, bool reiniciar) : ICasoDeUso<SalirDelCheckoutEntrada, VistaDelCarrito>
{
    public async Task<Resultado<VistaDelCarrito>> Ejecutar(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var carrito = servicios.Carrito;

        // 1. Alcance, cuenta activa, límite de revisión, pedido oculto y el checkout vivo (que puede no existir).
        var entro = await servicios.EntrarSinExigirCheckout(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (!entro.EsExito)
        {
            return Resultado<VistaDelCarrito>.Fallo(entro.Error!);
        }

        var (cuenta, vivo, delCarrito) = entro.Valor;
        if (ServiciosDelCarrito.ErrorDeLimite(await carrito.Limites.VolverOReiniciar(cuenta, cancelacion).ConfigureAwait(false)) is { } limite)
        {
            return Resultado<VistaDelCarrito>.Fallo(limite);
        }

        if (vivo is null)
        {
            // Un doble envío sobre un checkout ya terminado: éxito idempotente con el carrito conservado.
            return Resultado<VistaDelCarrito>.Exito(await carrito.ConstruirVista(delCarrito, cuenta, null, cancelacion).ConfigureAwait(false));
        }

        if (ServiciosDelCheckout.ErrorDeEstado(vivo.Estado) is { } estado)
        {
            return Resultado<VistaDelCarrito>.Fallo(estado);
        }

        // 2. Cancelar la intención de Pagos, fuera de transacción y antes de bloquear el checkout (D10).
        if (vivo.PedidoId is { } pedido)
        {
            ResultadoDeCancelarIntencion cancelada;
            try
            {
                cancelada = await carrito.Edicion.Pagos.Cancelar(cuenta, pedido, cancelacion).ConfigureAwait(false);
            }
            catch (Exception excepcion) when (excepcion is not OperationCanceledException)
            {
                // Con el checkout en pago no se sabe si hay un cobro vivo: no hay cambio local. Con uno en edición la intención ya estaba reabierta y expira sola.
                if (vivo.Estado == EstadoDelCheckout.EnPago)
                {
                    return Resultado<VistaDelCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible));
                }

                cancelada = ResultadoDeCancelarIntencion.Cancelada;
            }

            if (cancelada == ResultadoDeCancelarIntencion.NoCancelable)
            {
                return Resultado<VistaDelCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.PagoPendiente));
            }
        }

        // 3. La transacción: el checkout primero y el carrito después (D10).
        var resultado = await carrito.Unidad.Ejecutar(token => EnLaTransaccion(cuenta, vivo.Id, token), cancelacion).ConfigureAwait(false);
        if (!resultado.EsExito)
        {
            return Resultado<VistaDelCarrito>.Fallo(resultado.Error!);
        }

        // 4. Después de confirmar: liberar la reserva (si la hay) y devolver el carrito.
        if (resultado.Valor.PedidoALiberar is { } aLiberar)
        {
            await new LiberadorDeReservas(servicios).Liberar(aLiberar, cancelacion).ConfigureAwait(false);
        }

        return Resultado<VistaDelCarrito>.Exito(await carrito.ConstruirVista(resultado.Valor.Carrito, cuenta, null, cancelacion).ConfigureAwait(false));
    }

    private sealed record Terminado(Domain.Carrito.Carrito? Carrito, Guid? PedidoALiberar);

    private async Task<Resultado<Terminado>> EnLaTransaccion(Guid cuenta, Guid checkoutId, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var ahora = carrito.Reloj.Ahora;
        var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(cuenta, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (checkout is null || checkout.Id != checkoutId || delCarrito is null)
        {
            // Otra solicitud ya lo terminó: éxito idempotente.
            return Resultado<Terminado>.Exito(new Terminado(delCarrito, null));
        }

        var efecto = reiniciar ? checkout.Reiniciar(delCarrito, ahora) : checkout.Volver(delCarrito, ahora);
        if (!efecto.EsExito)
        {
            return Resultado<Terminado>.Fallo(efecto.Rechazo switch
            {
                RechazoDeCheckout.PagoPendiente => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.PagoPendiente),
                _ => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso),
            });
        }

        await carrito.Checkouts.Guardar(checkout, cancelacion).ConfigureAwait(false);
        if (efecto.Retiradas.Count > 0)
        {
            await carrito.Carritos.Guardar(delCarrito, cancelacion).ConfigureAwait(false);
            await carrito.Carritos.AgregarAvisos(cuenta, carrito.AvisosDeRetiro(efecto.Retiradas, new Dictionary<Guid, EdicionConsultada>(), ahora), cancelacion).ConfigureAwait(false);
        }

        await carrito.Auditoria.CheckoutTerminado(reiniciar ? "reiniciar" : "volver", cancelacion).ConfigureAwait(false);
        return Resultado<Terminado>.Exito(new Terminado(delCarrito, checkout.ReservaALiberarDesde is null ? null : checkout.PedidoId ?? checkout.PedidoDescartado));
    }
}

/// <summary>
/// <c>LiberarReservasPendientes</c> (T20; trabajo de fondo como «sistema»): reintenta <c>liberar</c> de las reservas anotadas por <c>volver</c>, <c>reiniciar</c>, un aborto o una cancelación cuyo Inventario no respondió, direccionando por el
/// <c>pedidoId</c> o el <c>pedido_descartado</c>. Cada una sigue las reglas de <see cref="LiberadorDeReservas"/>: la liberada, ya liberada o inexistente se limpia; la de un pedido ya confirmado se limpia <b>sin reintento</b> y se alerta, de modo que no
/// reintenta sin fin; una falla queda para la corrida siguiente. Solo corre con el actor «sistema».
/// </summary>
internal sealed class LiberarReservasPendientes(ServiciosDelCheckout servicios) : ICasoDeUso<LiberarReservasPendientesEntrada, ResumenDeLiberaciones>
{
    public const int MaximoPorCorrida = 200;

    public async Task<Resultado<ResumenDeLiberaciones>> Ejecutar(LiberarReservasPendientesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var carrito = servicios.Carrito;
        if (await carrito.Autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResumenDeLiberaciones>.Fallo(rechazo);
        }

        var pendientes = await carrito.Checkouts.ConLiberacionPendiente(MaximoPorCorrida, cancelacion).ConfigureAwait(false);
        var liberador = new LiberadorDeReservas(servicios);
        int liberadas = 0, confirmadas = 0, sinRespuesta = 0;
        foreach (var pendiente in pendientes)
        {
            switch (await liberador.Liberar(pendiente.PedidoId, cancelacion).ConfigureAwait(false))
            {
                case ResultadoDeLiberar.Liberada or ResultadoDeLiberar.NoExistia:
                    liberadas++;
                    break;
                case ResultadoDeLiberar.PedidoConfirmado:
                    confirmadas++;
                    break;
                default:
                    sinRespuesta++;
                    break;
            }
        }

        return Resultado<ResumenDeLiberaciones>.Exito(new ResumenDeLiberaciones(pendientes.Count, liberadas, confirmadas, sinRespuesta));
    }
}

public sealed record LiberarReservasPendientesEntrada;

/// <summary>Lo que hizo una corrida de <c>LiberarReservasPendientes</c>: cuántas encontró, cuántas liberó, cuántas eran de un pedido ya confirmado y cuántas quedan para la próxima.</summary>
public sealed record ResumenDeLiberaciones(int Encontradas, int Liberadas, int PedidosConfirmados, int SinRespuesta);
