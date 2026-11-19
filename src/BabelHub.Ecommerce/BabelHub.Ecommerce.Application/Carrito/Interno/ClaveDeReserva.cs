using System.Globalization;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// La clave de idempotencia de <c>reservas.crear</c> (D05, D18): <c>res-&lt;pedidoId&gt;-&lt;generación de reserva&gt;</c>. Cumple el formato del Inventario (<c>^[A-Za-z0-9_-]{16,80}$</c>); como la generación sube
/// solo cuando cambia la huella de reserva, repetir el paso con las mismas líneas y entrega repite la clave (el Inventario devuelve la original) y editar A→B→A genera otra (el Inventario reemplaza la reserva, SEL-16).
/// </summary>
public static partial class ClaveDeReserva
{
    public static string Para(Guid pedidoId, int generacionDeReserva)
    {
        if (pedidoId == Guid.Empty)
        {
            throw new ArgumentException("El pedido no puede ser vacío.", nameof(pedidoId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(generacionDeReserva);
        var clave = string.Create(CultureInfo.InvariantCulture, $"res-{pedidoId:D}-{generacionDeReserva}");
        return FormatoDelInventario().IsMatch(clave) ? clave : throw new InvalidOperationException("La clave de reserva no cumple el formato del Inventario.");
    }

    [GeneratedRegex("^[A-Za-z0-9_-]{16,80}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex FormatoDelInventario();
}
