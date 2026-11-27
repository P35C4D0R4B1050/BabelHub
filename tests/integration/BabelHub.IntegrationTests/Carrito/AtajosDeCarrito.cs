using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>Atajo de las pruebas: una línea por edición en la sede central, con la edición como identificador de la línea.</summary>
internal static class AtajosDeCarrito
{
    public const string Sucursal = "sede-central";

    public static ResultadoDeLinea Agregar(this CarritoDeDominio carrito, Guid edicionId, bool esDigital, int cantidad, DateTimeOffset ahora) =>
        carrito.Agregar(edicionId, edicionId, esDigital, esDigital ? null : Sucursal, cantidad, ahora);
}
