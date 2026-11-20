using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Carrito;

/// <summary>
/// El carrito del cliente o del visitante (CAR-01 a CAR-04, CAR-20, CAR-24, CAR-25, CAR-35): lo usa la web por este contrato, sin tocar el Dominio. Con sesión la cuenta sale del actor y un identificador ajeno
/// nunca llega de afuera; sin sesión, la cookie opaca identifica el carrito. Cada operación valida su entrada al inicio, comprueba el alcance en la Aplicación y audita en la misma transacción; ninguna llamada de red
/// va dentro de una transacción (D19). Un <c>GET</c> no tiene efectos.
/// </summary>
public interface ICarritoDelCliente
{
    /// <summary>El carrito con el precio vigente, la disponibilidad (con frescura) y los avisos; sin carrito o con una cookie desconocida o mal formada es «sin carrito» (vacío), sin crear nada.</summary>
    Task<Resultado<ResultadoDeCarrito>> Obtener(SolicitudSobreElCarrito solicitud, CancellationToken cancelacion = default);

    /// <summary>Agrega una edición (1 a 20; una digital, una sola; rechaza la ya poseída solo con sesión) y crea el carrito del visitante si no existe, con la cookie nueva en el resultado.</summary>
    Task<Resultado<ResultadoDeCarrito>> AgregarLinea(AgregarLineaEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeCarrito>> ModificarLinea(ModificarLineaEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeCarrito>> EliminarLinea(EliminarLineaEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>
    /// Fusiona el carrito del visitante con el de la cuenta al terminar el ingreso o el registro, de forma idempotente (CAR-24): lo llama la Presentación y nunca impide el ingreso. Con una compra en curso se
    /// aplaza y se reintenta en el siguiente <c>POST</c> autenticado de la misma sesión.
    /// </summary>
    Task<Resultado<ResultadoDeFusionDeCarrito>> FusionarCarritoDeVisitante(string cookieDelVisitante, CancellationToken cancelacion = default);

    /// <summary>Marca como vistos los avisos del carrito de la cuenta.</summary>
    Task<Resultado<ResultadoDeCarrito>> MarcarAvisosComoVistos(IReadOnlyCollection<Guid> avisoIds, CancellationToken cancelacion = default);
}

/// <summary>
/// El checkout del cliente (CAR-05 a CAR-18, CAR-26 a CAR-30): solo con sesión y cuenta activa. Iniciar, elegir la entrega, aceptar los textos, pasar al pago, volver y reiniciar. La cuenta sale del actor; un
/// <c>pedidoId</c> oculto que no es el del checkout vivo responde «no encontrado» uniforme y registra el acceso denegado. <b>Pasar al pago</b> no cobra, no crea el pedido ni dibuja la pantalla de pago: congela la
/// compra, reserva el stock en el Inventario y deja la intención en Pagos.
/// </summary>
public interface ICheckoutDelCliente
{
    /// <summary>Crea el checkout si no hay uno vivo (si lo hay, lo devuelve) con la disponibilidad real del Inventario; sin respuesta no avanza (CAR-06).</summary>
    Task<Resultado<VistaDelCheckout>> Iniciar(IniciarCheckoutEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>El checkout vivo con su revisión actual; sin checkout vivo es «no encontrado». No tiene efectos.</summary>
    Task<Resultado<VistaDelCheckout>> Obtener(CancellationToken cancelacion = default);

    Task<Resultado<VistaDelCheckout>> ElegirEntrega(ElegirEntregaEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<VistaDelCheckout>> RegistrarAceptaciones(RegistrarAceptacionesEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Congela la compra, reserva y deja la intención de pago; devuelve el <c>pedidoId</c> y el estado (<c>en_pago</c> o <c>pago_pendiente</c>). Pagos solo habilita un cobro nuevo con <c>en_pago</c>.</summary>
    Task<Resultado<ResultadoDePasarAlPago>> PasarAlPago(PasarAlPagoEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Vuelve al carrito: cancela la intención, elimina el contacto y libera la reserva después de confirmar; con el pago pendiente se rechaza.</summary>
    Task<Resultado<VistaDelCarrito>> VolverAlCarrito(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Reinicia el checkout (carrito conservado, contacto eliminado, reserva liberada).</summary>
    Task<Resultado<VistaDelCarrito>> Reiniciar(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>
    /// El <c>pedidoId</c> del checkout vivo de quien pide <b>solo</b> en <c>en_pago</c> y <c>pago_pendiente</c>, y en <c>en_pago</c> no con una línea retirada (D13): Pagos lo exige para resolver la intención de
    /// <c>GET /checkout/pago</c>, <c>RegistrarIntento</c> y cada token. Es «no encontrado» si no hay checkout vivo en esos estados.
    /// </summary>
    Task<Resultado<CheckoutVivoParaPagar>> ObtenerParaPagar(CancellationToken cancelacion = default);
}
