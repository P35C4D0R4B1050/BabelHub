using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>Qué hay que hacer dentro de la transacción de una edición, según lo que se decidió antes de bloquear.</summary>
internal enum DecisionDeLaPuerta
{
    /// <summary>No hay checkout vivo o está en edición: se edita con normalidad.</summary>
    Libre,

    /// <summary><c>Reabrir</c> dejó la intención abierta y obsoleta: dentro de la transacción el checkout pasa de <c>en_pago</c> a <c>en_edicion</c> conservando el <c>pedidoId</c> y la reserva.</summary>
    Reabierta,

    /// <summary><c>Reabrir</c> respondió <c>Cerrada</c> o <c>NoExiste</c>: dentro de la transacción se aborta desde <c>en_pago</c> (el <c>pedidoId</c> pasa a descartado, sube la generación y se anota la liberación).</summary>
    AbortarDesdeEnPago,
}

/// <summary>Lo decidido antes de bloquear: la decisión y el <c>pedidoId</c> y la generación que el checkout tenía, para que lo que ocurra dentro de la transacción sea condicional a ellos.</summary>
internal sealed record PuertaDeEdicion(DecisionDeLaPuerta Decision, Guid? PedidoId = null, int? Generacion = null)
{
    public static PuertaDeEdicion Libre { get; } = new(DecisionDeLaPuerta.Libre);
}

/// <summary>
/// La edición con el checkout vivo, común al carrito, la entrega, el contacto, las aceptaciones y la fusión (CAR-26, D10, D19, D21; plan §1 «Editar el carrito durante el pago»). Primero, <b>fuera de toda transacción y antes
/// de bloquear</b>: con <c>en_pago</c> llama a <c>Reabrir</c> (la intención queda abierta, sin contacto y obsoleta, de modo que una pestaña vieja no puede pagarla); con <c>pago_pendiente</c> o un intento activo
/// rechaza; con <c>reservando</c> responde «en curso». Después, <b>dentro de la transacción</b> y ya con el checkout y el carrito bloqueados (en ese orden), aplica la transición. Un cambio sin variación de valores es un
/// no-op y <b>no</b> llega aquí: quien llama lo decide antes. El retiro de una edición no pasa por aquí (lo hace T13 sin llamar a Pagos).
/// </summary>
internal sealed class EdicionConCheckoutVivo(ICreadorDeIntencionesDePago pagos, IRepositorioDeCheckouts checkouts)
{
    /// <summary>Pagos, para el aborto (<c>Cancelar</c>) que reutiliza esta clase junto con la edición.</summary>
    public ICreadorDeIntencionesDePago Pagos => pagos;

    /// <summary>Se llama antes de abrir la transacción, solo para un cliente con sesión.</summary>
    public async Task<Resultado<PuertaDeEdicion>> PrepararAntesDeBloquear(Guid cuentaId, CancellationToken cancelacion)
    {
        var estado = await checkouts.LeerEstadoSinBloqueo(cuentaId, cancelacion).ConfigureAwait(false);
        switch (estado)
        {
            case null or EstadoDelCheckout.EnEdicion:
                return Resultado<PuertaDeEdicion>.Exito(PuertaDeEdicion.Libre);
            case EstadoDelCheckout.PagoPendiente:
                return Resultado<PuertaDeEdicion>.Fallo(Cerrada(ClavesDelCarrito.PagoPendiente));
            case EstadoDelCheckout.Reservando:
                return Resultado<PuertaDeEdicion>.Fallo(Cerrada(ClavesDelCarrito.EnCurso));
        }

        // en_pago: hay una intención viva. Sin bloqueo, solo para saber de qué pedido y generación se trata.
        var vivo = await checkouts.ObtenerVivoDeCuenta(cuentaId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (vivo is not { Estado: EstadoDelCheckout.EnPago, PedidoId: { } pedido })
        {
            // Cambió entre las dos lecturas: la solicitud se rehace desde el principio.
            return Resultado<PuertaDeEdicion>.Fallo(Cerrada(ClavesDelCarrito.EnCurso));
        }

        ResultadoDeReabrirIntencion respuesta;
        try
        {
            respuesta = await pagos.Reabrir(cuentaId, pedido, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Pagos no respondió: se falla cerrado, no se sabe si hay un cobro vivo (CAR-26).
            return Resultado<PuertaDeEdicion>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible));
        }

        return respuesta switch
        {
            ResultadoDeReabrirIntencion.Reabierta => Resultado<PuertaDeEdicion>.Exito(new PuertaDeEdicion(DecisionDeLaPuerta.Reabierta, pedido, vivo.GeneracionDeReserva)),
            ResultadoDeReabrirIntencion.NoCancelable => Resultado<PuertaDeEdicion>.Fallo(Cerrada(ClavesDelCarrito.PagoPendiente)),
            _ => Resultado<PuertaDeEdicion>.Exito(new PuertaDeEdicion(DecisionDeLaPuerta.AbortarDesdeEnPago, pedido, vivo.GeneracionDeReserva)),
        };
    }

    /// <summary>
    /// Dentro de la transacción, con el checkout y el carrito ya bloqueados y <b>releído el estado</b>: aplica la transición que se decidió o rechaza si el checkout cambió entre tanto. Devuelve las ediciones retiradas que
    /// se quitaron del carrito (para dejar su aviso) y el error, si lo hay.
    /// </summary>
    public (ErrorDeAplicacion? Error, IReadOnlyList<EdicionRetirada> Retiradas) AplicarDentroDeLaTransaccion(Checkout? checkout, CarritoDeDominio carrito, PuertaDeEdicion puerta, DateTimeOffset ahora)
    {
        if (checkout is null)
        {
            return (null, []);
        }

        switch (checkout.Estado)
        {
            case EstadoDelCheckout.EnEdicion:
                // También el caso de otra solicitud que ya lo devolvió a edición: nada que hacer.
                return (null, []);
            case EstadoDelCheckout.PagoPendiente:
                return (Cerrada(ClavesDelCarrito.PagoPendiente), []);
            case EstadoDelCheckout.EnPago when puerta.Decision == DecisionDeLaPuerta.Reabierta && checkout.PedidoId == puerta.PedidoId:
            {
                var editado = checkout.Editar(carrito, ahora);
                return editado.EsExito ? (null, editado.Retiradas) : (Cerrada(ClavesDelCarrito.EnCurso), []);
            }

            case EstadoDelCheckout.EnPago when puerta.Decision == DecisionDeLaPuerta.AbortarDesdeEnPago && puerta.PedidoId is { } pedido && puerta.Generacion is { } generacion:
            {
                var abortado = checkout.AbortarPasoAlPago(EstadoDelCheckout.EnPago, pedido, generacion, carrito, ahora);
                return abortado.Efecto == EfectoDeTransicion.Aplicada ? (null, abortado.Retiradas) : (Cerrada(ClavesDelCarrito.EnCurso), []);
            }

            default:
                return (Cerrada(ClavesDelCarrito.EnCurso), []);
        }
    }

    private static ErrorDeAplicacion Cerrada(string clave) => new(CategoriaDeError.Conflicto, clave);
}
