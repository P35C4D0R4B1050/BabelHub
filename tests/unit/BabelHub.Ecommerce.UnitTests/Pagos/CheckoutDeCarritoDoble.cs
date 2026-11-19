using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>El checkout de Carrito: solo <c>ObtenerParaPagar</c> responde (con lo que la prueba fije); lo demás no se usa desde Pagos.</summary>
internal sealed class CheckoutDeCarritoDoble : ICheckoutDelCliente
{
    public Resultado<CheckoutVivoParaPagar>? Respuesta { get; set; }

    public bool Falla { get; set; }

    public int Consultas { get; private set; }

    public Task<Resultado<CheckoutVivoParaPagar>> ObtenerParaPagar(CancellationToken cancelacion = default)
    {
        Consultas++;
        if (Falla)
        {
            throw new InvalidOperationException("Carrito no responde.");
        }

        return Task.FromResult(Respuesta ?? Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(InstantaneasDeCompraDePrueba.Pedido, EstadoVisibleDelCheckout.EnPago)));
    }

    public Task<Resultado<VistaDelCheckout>> Iniciar(IniciarCheckoutEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCheckout>> Obtener(CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCheckout>> ElegirEntrega(ElegirEntregaEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCheckout>> RegistrarAceptaciones(RegistrarAceptacionesEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<ResultadoDePasarAlPago>> PasarAlPago(PasarAlPagoEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCarrito>> VolverAlCarrito(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<VistaDelCarrito>> Reiniciar(SalirDelCheckoutEntrada entrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
}
