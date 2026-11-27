using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>Atajo de las pruebas: una línea por edición en la sede central, con la edición como identificador de la línea.</summary>
internal static class AtajosDeCarrito
{
    public const string Sucursal = "sede-central";

    /// <summary>Un solo importe de envío: se carga a la primera sucursal que atiende y las demás van en cero.</summary>
    public static ResultadoDeCheckout PrepararPasoAlPago(this Checkout checkout, Guid pedidoIdNuevo, CostoDeEnvio envio, Func<string, string> reserva, Func<string, string> instantanea, CarritoDeDominio carrito, DateTimeOffset ahora) =>
        checkout.PrepararPasoAlPago(pedidoIdNuevo, [.. checkout.SucursalesQueAtienden.Select((sucursal, i) => new EnvioDeSucursal(sucursal, i == 0 ? envio : CostoDeEnvio.Cero))], reserva, instantanea, carrito, ahora);

    public static ResultadoDeLinea Agregar(this CarritoDeDominio carrito, Guid edicionId, bool esDigital, int cantidad, DateTimeOffset ahora) =>
        carrito.Agregar(edicionId, edicionId, esDigital, esDigital ? null : Sucursal, cantidad, ahora);
}
