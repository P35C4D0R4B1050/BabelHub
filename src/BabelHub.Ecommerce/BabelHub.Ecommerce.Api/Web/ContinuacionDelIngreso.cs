namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// Volver a la compra tras ingresar (AC-CAR-13, CAR-16; O-11). Lo que viaja <b>no es un destino</b> sino una marca de una lista cerrada (<c>?continuar=compra</c> y el mismo campo en los formularios del ingreso): el destino es una constante del servidor, así que
/// nada que llegue por la consulta, un formulario o una cabecera puede redirigir fuera ni a otra pantalla. Solo la usa un cliente de la tienda; vuelve a ver su carrito, donde continuar la compra sigue siendo una acción suya (nunca se reejecuta).
/// </summary>
public static class ContinuacionDelIngreso
{
    public const string Campo = "continuar";

    public const string Compra = "compra";

    /// <summary>El ingreso que recuerda que la persona estaba comprando.</summary>
    public const string RutaDelIngresoDeLaCompra = RespuestasDeSesion.RutaDelIngreso + "?" + Campo + "=" + Compra;

    /// <summary>A donde vuelve el cliente que estaba comprando.</summary>
    public const string DestinoDeLaCompra = "/carrito";

    private const string RutasDeLaCompra = "/checkout";

    public static bool EsLaCompra(string? valor) => string.Equals(valor, Compra, StringComparison.Ordinal);

    /// <summary>El ingreso al que va quien necesita sesión en esta solicitud: el de la compra si estaba en el checkout.</summary>
    public static string RutaDelIngresoPara(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        return contexto.Request.Path.StartsWithSegments(RutasDeLaCompra, StringComparison.OrdinalIgnoreCase) ? RutaDelIngresoDeLaCompra : RespuestasDeSesion.RutaDelIngreso;
    }
}
