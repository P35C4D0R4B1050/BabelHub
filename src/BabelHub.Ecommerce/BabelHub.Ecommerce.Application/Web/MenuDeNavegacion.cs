namespace BabelHub.Ecommerce.Application.Web;

/// <summary>Una opción del menú de un área: el texto, la ruta y el nombre del icono del sistema de diseño.</summary>
public sealed record OpcionDeMenu(string Texto, string Ruta, string Icono);

/// <summary>
/// Contrato público del menú (WEB-02, D03): <b>solo las opciones que el rol y el alcance de quien mira pueden usar</b> en cada área (Tienda, Sucursal y Administración). El acceso real lo decide el servidor en cada ruta (esto solo evita
/// ofrecer lo que daría «sin permiso»); un rol que no ve el área recibe un menú vacío. Las opciones de una sucursal llevan el código de la sucursal del encargado; sin él, esas opciones no se ofrecen.
/// </summary>
public interface IMenuDeNavegacion
{
    IReadOnlyList<OpcionDeMenu> Resolver(RolWeb rol, AreaWeb area, string? codigoDeSucursal = null);
}
