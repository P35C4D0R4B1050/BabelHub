using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

public sealed record ObtenerCheckoutParaPagarEntrada;

/// <summary>
/// <c>ObtenerParaPagar</c> (D13, DD-07; <c>ICheckoutDelCliente</c>): el <c>pedidoId</c> del checkout vivo de quien pide <b>solo</b> en <c>en_pago</c> y <c>pago_pendiente</c>, y en <c>en_pago</c> <b>no</b> mientras el carrito tenga una línea con retiro
/// pendiente (la edición ya no se puede pagar: el siguiente intento la quita). Es una lectura <b>sin bloqueo y sin efectos</b>; el alcance es el de la cuenta de quien pide (nunca recibe una cuenta), y cualquier otro estado, la falta de checkout vivo o
/// una cuenta sin carrito responden «no encontrado» uniforme, sin revelar el estado interno.
/// </summary>
internal sealed class ObtenerCheckoutParaPagar(ServiciosDelCheckout servicios) : ICasoDeUso<ObtenerCheckoutParaPagarEntrada, CheckoutVivoParaPagar>
{
    public async Task<Resultado<CheckoutVivoParaPagar>> Ejecutar(ObtenerCheckoutParaPagarEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var entro = await servicios.EntrarAlCheckout(null, cancelacion).ConfigureAwait(false);
        if (!entro.EsExito)
        {
            return Resultado<CheckoutVivoParaPagar>.Fallo(entro.Error!);
        }

        var (_, vivo, delCarrito) = entro.Valor;
        var pagable = vivo.PedidoId is not null
            && (vivo.Estado == EstadoDelCheckout.PagoPendiente || (vivo.Estado == EstadoDelCheckout.EnPago && delCarrito is { TieneRetirosPendientes: false }));
        return pagable
            ? Resultado<CheckoutVivoParaPagar>.Exito(new CheckoutVivoParaPagar(vivo.PedidoId!.Value, ServiciosDelCheckout.DeEstado(vivo.Estado)))
            : Resultado<CheckoutVivoParaPagar>.Fallo(servicios.Carrito.Autorizador.NoEncontrado());
    }
}
