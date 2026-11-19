using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// <see cref="ICarritoDelCliente"/> (T26b): cada operación del contrato público es el caso de uso de su nombre, sin lógica propia; cada caso de uso valida su entrada, comprueba el alcance y maneja sus propias transacciones cortas (la red va fuera de
/// ellas, D19). Se registra por alcance de solicitud.
/// </summary>
internal sealed class CarritoDelCliente(ServiciosDelCarrito servicios) : ICarritoDelCliente
{
    public Task<Resultado<ResultadoDeCarrito>> Obtener(SolicitudSobreElCarrito solicitud, CancellationToken cancelacion = default) =>
        new ObtenerCarrito(servicios).Ejecutar(solicitud, cancelacion);

    public Task<Resultado<ResultadoDeCarrito>> AgregarLinea(AgregarLineaEntrada entrada, CancellationToken cancelacion = default) =>
        new AgregarLinea(servicios).Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeCarrito>> ModificarLinea(ModificarLineaEntrada entrada, CancellationToken cancelacion = default) =>
        new ModificarLinea(servicios).Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeCarrito>> EliminarLinea(EliminarLineaEntrada entrada, CancellationToken cancelacion = default) =>
        new EliminarLinea(servicios).Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDeFusionDeCarrito>> FusionarCarritoDeVisitante(string cookieDelVisitante, CancellationToken cancelacion = default) =>
        new FusionarCarritoDeVisitante(servicios).Ejecutar(new FusionarCarritoEntrada(cookieDelVisitante), cancelacion);

    public Task<Resultado<ResultadoDeCarrito>> MarcarAvisosComoVistos(IReadOnlyCollection<Guid> avisoIds, CancellationToken cancelacion = default) =>
        new MarcarAvisosComoVistos(servicios).Ejecutar(new MarcarAvisosEntrada(avisoIds), cancelacion);
}

/// <summary><see cref="ICheckoutDelCliente"/> (T26b): cada operación es el caso de uso de su nombre; <c>VolverAlCarrito</c> y <c>Reiniciar</c> son el mismo caso de uso con su variante. Se registra por alcance de solicitud.</summary>
internal sealed class CheckoutDelCliente(ServiciosDelCheckout servicios) : ICheckoutDelCliente
{
    public Task<Resultado<VistaDelCheckout>> Iniciar(IniciarCheckoutEntrada entrada, CancellationToken cancelacion = default) =>
        new IniciarCheckout(servicios).Ejecutar(entrada, cancelacion);

    public Task<Resultado<VistaDelCheckout>> Obtener(CancellationToken cancelacion = default) =>
        new ObtenerCheckout(servicios).Ejecutar(new ObtenerCheckoutEntrada(), cancelacion);

    public Task<Resultado<VistaDelCheckout>> ElegirEntrega(ElegirEntregaEntrada entrada, CancellationToken cancelacion = default) =>
        new ElegirEntrega(servicios).Ejecutar(entrada, cancelacion);

    public Task<Resultado<VistaDelCheckout>> RegistrarAceptaciones(RegistrarAceptacionesEntrada entrada, CancellationToken cancelacion = default) =>
        new RegistrarAceptaciones(servicios).Ejecutar(entrada, cancelacion);

    public Task<Resultado<ResultadoDePasarAlPago>> PasarAlPago(PasarAlPagoEntrada entrada, CancellationToken cancelacion = default) =>
        new PasarAlPago(servicios).Ejecutar(entrada, cancelacion);

    public Task<Resultado<VistaDelCarrito>> VolverAlCarrito(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default) =>
        new SalirDelCheckout(servicios, reiniciar: false).Ejecutar(entrada, cancelacion);

    public Task<Resultado<VistaDelCarrito>> Reiniciar(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default) =>
        new SalirDelCheckout(servicios, reiniciar: true).Ejecutar(entrada, cancelacion);

    public Task<Resultado<CheckoutVivoParaPagar>> ObtenerParaPagar(CancellationToken cancelacion = default) =>
        new ObtenerCheckoutParaPagar(servicios).Ejecutar(new ObtenerCheckoutParaPagarEntrada(), cancelacion);
}
