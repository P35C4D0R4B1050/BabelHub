namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// El menú por rol y alcance (WEB-02, AC-WEB-02): una tabla de opciones con los roles que pueden usarlas, filtrada por las áreas que el rol ve (<see cref="IAreasDeInterfaz"/>). Es presentación pura de lo que el servidor ya hace cumplir en cada
/// ruta: ninguna opción da acceso, solo evita ofrecer lo que respondería «sin permiso». El texto de la ruta de una sucursal lleva <c>{codigo}</c>, que se reemplaza por el código de la sucursal del encargado.
/// </summary>
internal sealed class ResolverMenu(IAreasDeInterfaz areas) : IMenuDeNavegacion
{
    private const string Codigo = "{codigo}";

    private static readonly RolWeb[] Todos = [RolWeb.Visitante, RolWeb.Cliente, RolWeb.Encargado, RolWeb.Logistica, RolWeb.AdministradorCentral];

    private static readonly RolWeb[] ConCuenta = [RolWeb.Cliente];

    private static readonly RolWeb[] Compradores = [RolWeb.Visitante, RolWeb.Cliente];

    private static readonly RolWeb[] ElPersonalDeSucursal = [RolWeb.Encargado, RolWeb.Logistica];

    private static readonly (AreaWeb Area, string Texto, string Ruta, string Icono, RolWeb[] Roles)[] Opciones =
    [
        // Tienda: el catálogo es de todos; el carrito, de quien compra; los pedidos y la biblioteca, solo de un cliente con sesión.
        (AreaWeb.Tienda, "Catálogo", "/catalogo", "libro", Todos),
        (AreaWeb.Tienda, "Carrito", "/carrito", "carrito", Compradores),
        (AreaWeb.Tienda, "Mis pedidos", "/cuenta/pedidos", "bandeja", ConCuenta),
        (AreaWeb.Tienda, "Mi biblioteca", "/biblioteca", "libro", ConCuenta),

        // Sucursal: el encargado ve lo de su sucursal (sin contacto de clientes); logística ve los envíos y los pedidos a domicilio.
        (AreaWeb.Sucursal, "Pedidos de recojo", $"/sucursal/{Codigo}/pedidos", "bandeja", [RolWeb.Encargado]),
        (AreaWeb.Sucursal, "Devoluciones", $"/sucursal/{Codigo}/devoluciones", "bandeja", [RolWeb.Encargado]),
        (AreaWeb.Sucursal, "Envíos", "/logistica/envios", "camion", ElPersonalDeSucursal),
        (AreaWeb.Sucursal, "Pedidos a domicilio", "/logistica/pedidos", "bandeja", [RolWeb.Logistica]),

        // Administración: solo la administración central.
        (AreaWeb.Administracion, "Catálogo nacional", "/administracion/obras", "libro", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Pedidos", "/administracion/pedidos", "bandeja", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Devoluciones", "/administracion/devoluciones", "bandeja", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Reembolsos", "/administracion/reembolsos", "bandeja", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Ayudas de la biblioteca", "/administracion/biblioteca/ayudas", "bandeja", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Hojas de reclamación", "/administracion/reclamaciones", "bandeja", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Usuarios y roles", "/administracion/usuarios", "usuario", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Solicitudes de datos", "/administracion/solicitudes", "usuario", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Auditoría", "/administracion/auditoria", "bandeja", [RolWeb.AdministradorCentral]),
        (AreaWeb.Administracion, "Alertas de seguridad", "/administracion/alertas", "aviso", [RolWeb.AdministradorCentral]),
    ];

    public IReadOnlyList<OpcionDeMenu> Resolver(RolWeb rol, AreaWeb area, string? codigoDeSucursal = null)
    {
        if (!areas.VeLaArea(rol, area))
        {
            return [];
        }

        var codigo = string.IsNullOrWhiteSpace(codigoDeSucursal) || codigoDeSucursal.Length > 40 || !codigoDeSucursal.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_') ? null : codigoDeSucursal;
        var menu = new List<OpcionDeMenu>();
        foreach (var (areaDeLaOpcion, texto, ruta, icono, roles) in Opciones)
        {
            if (areaDeLaOpcion != area || !roles.Contains(rol))
            {
                continue;
            }

            if (ruta.Contains(Codigo, StringComparison.Ordinal))
            {
                // Sin el código de la sucursal no hay a dónde llevar: la opción no se ofrece.
                if (codigo is null)
                {
                    continue;
                }

                menu.Add(new OpcionDeMenu(texto, ruta.Replace(Codigo, codigo, StringComparison.Ordinal), icono));
                continue;
            }

            menu.Add(new OpcionDeMenu(texto, ruta, icono));
        }

        return menu;
    }
}
