using DominioMedioDePago = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>La traducción entre el medio de pago de la raíz del módulo (que usa la Api) y el del Dominio: el contrato público no expone el Dominio.</summary>
internal static class ConversionesDeMedioDePago
{
    public static DominioMedioDePago ADominio(this MedioDePago medio) => medio switch
    {
        MedioDePago.Yape => DominioMedioDePago.Yape,
        MedioDePago.Tarjeta => DominioMedioDePago.Tarjeta,
        _ => throw new ArgumentOutOfRangeException(nameof(medio)),
    };

    public static MedioDePago ALaAplicacion(this DominioMedioDePago medio) => medio switch
    {
        DominioMedioDePago.Yape => MedioDePago.Yape,
        DominioMedioDePago.Tarjeta => MedioDePago.Tarjeta,
        _ => throw new ArgumentOutOfRangeException(nameof(medio)),
    };
}
