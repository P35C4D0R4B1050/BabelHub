using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>La lista de sucursales que alimenta la ficha y la validación de la sucursal de una línea, con la sede central del Inventario simulado.</summary>
public sealed class SucursalesDelCarrito : IListaDeSucursales
{
    public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) =>
        Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([new(AtajosDeCarrito.Sucursal, "Sede central"), new("cusco", "Cusco")], true));

    public static IPreferenciaDeSucursal Preferencia() => new PreferenciaDeSucursal(new SucursalesDelCarrito());
}
