namespace BabelHub.Inventario.Domain.Reservas;

/// <summary>El estado de una línea de reserva: pendiente → confirmada → cerrada; pendiente o confirmada → liberada; pendiente → expirada (D06, spec §5).</summary>
public enum EstadoDeLinea
{
    Pendiente,
    Confirmada,
    Cerrada,
    Liberada,
    Expirada,
}

/// <summary>Por qué se cerró una línea confirmada: la entrega en recojo, el paso a en reparto de un envío o el despacho de un traslado vinculado (que la vuelve a crear en el destino).</summary>
public enum CausaDeCierre
{
    Entrega,
    Reparto,
    Traslado,
}

/// <summary>Cómo recibe el cliente el pedido: recojo en una sucursal o domicilio (CAR-08).</summary>
public enum TipoDeEntrega
{
    Recojo,
    Domicilio,
}

/// <summary>
/// Se intentó una transición que el estado de la línea o de la reserva no admite (liberar una línea cerrada, confirmar una vencida, cerrar una liberada…). La Aplicación la traduce a
/// <c>INV-RESERVA-ESTADO-INVALIDO</c> (SEL-28, SEL-29) sin cambiar el stock. Nunca lleva datos de la solicitud.
/// </summary>
public sealed class TransicionDeReservaInvalidaException : Exception
{
    public TransicionDeReservaInvalidaException()
        : base("La transición no es válida para el estado actual de la reserva.")
    {
    }

    public TransicionDeReservaInvalidaException(string message)
        : base(message)
    {
    }

    public TransicionDeReservaInvalidaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    internal static TransicionDeReservaInvalidaException De(EstadoDeLinea desde, string accion) =>
        new($"No se puede {accion} una línea {desde.ToString().ToLowerInvariant()}.");
}
