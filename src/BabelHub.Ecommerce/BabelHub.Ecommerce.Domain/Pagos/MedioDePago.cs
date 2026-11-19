namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>Los medios de pago que admite la compra (PAG-03). El texto es el valor estable que se guarda y que viaja entre capas.</summary>
public enum MedioDePago
{
    Yape,
    Tarjeta,
}

public static class MedioDePagoExtensiones
{
    public static string ATexto(this MedioDePago medio) => medio switch
    {
        MedioDePago.Yape => "yape",
        MedioDePago.Tarjeta => "tarjeta",
        _ => throw new ArgumentOutOfRangeException(nameof(medio)),
    };

    public static bool TryDesdeTexto(string? texto, out MedioDePago medio)
    {
        switch (texto)
        {
            case "yape":
                medio = MedioDePago.Yape;
                return true;
            case "tarjeta":
                medio = MedioDePago.Tarjeta;
                return true;
            default:
                medio = default;
                return false;
        }
    }
}
