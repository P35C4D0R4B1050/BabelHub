using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>Implementa <see cref="IAreasDeInterfaz"/> sobre los permisos del Dominio, con los tipos propios del contrato.</summary>
public sealed class AreasDeInterfaz : IAreasDeInterfaz
{
    public IReadOnlyList<AreaWeb> AreasDe(RolWeb rol) => [.. PermisosDeArea.Areas(Aplicar(rol)).Select(Aplicar)];

    public bool VeLaArea(RolWeb rol, AreaWeb area) => PermisosDeArea.VeLaArea(Aplicar(rol), Aplicar(area));

    public HostWeb HostDeLaArea(AreaWeb area) => Aplicar(PermisosDeArea.HostDeLaArea(Aplicar(area)));

    public bool ParejaDeRolYHostValida(RolWeb rol, HostWeb host) => PermisosDeArea.ParejaDeRolYHostValida(Aplicar(rol), Aplicar(host));

    internal static RolDeInterfaz Aplicar(RolWeb rol) => rol switch
    {
        RolWeb.Visitante => RolDeInterfaz.Visitante,
        RolWeb.Cliente => RolDeInterfaz.Cliente,
        RolWeb.Encargado => RolDeInterfaz.Encargado,
        RolWeb.Logistica => RolDeInterfaz.Logistica,
        RolWeb.AdministradorCentral => RolDeInterfaz.AdministradorCentral,
        _ => throw new ArgumentOutOfRangeException(nameof(rol)),
    };

    internal static AreaDeInterfaz Aplicar(AreaWeb area) => area switch
    {
        AreaWeb.Tienda => AreaDeInterfaz.Tienda,
        AreaWeb.Sucursal => AreaDeInterfaz.Sucursal,
        AreaWeb.Administracion => AreaDeInterfaz.Administracion,
        _ => throw new ArgumentOutOfRangeException(nameof(area)),
    };

    internal static AreaWeb Aplicar(AreaDeInterfaz area) => area switch
    {
        AreaDeInterfaz.Tienda => AreaWeb.Tienda,
        AreaDeInterfaz.Sucursal => AreaWeb.Sucursal,
        AreaDeInterfaz.Administracion => AreaWeb.Administracion,
        _ => throw new ArgumentOutOfRangeException(nameof(area)),
    };

    internal static HostDeSesion Aplicar(HostWeb host) => host == HostWeb.Tienda ? HostDeSesion.Tienda : HostDeSesion.Gestion;

    internal static HostWeb Aplicar(HostDeSesion host) => host == HostDeSesion.Tienda ? HostWeb.Tienda : HostWeb.Gestion;
}
