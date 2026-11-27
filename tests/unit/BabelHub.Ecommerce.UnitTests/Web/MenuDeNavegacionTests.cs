using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>
/// El menú por rol y alcance (web-base WEB-02; AC-WEB-02): en cada área solo las opciones que el rol puede usar, sin Administración para un encargado, sin pedidos ni biblioteca para un visitante, con el código de la sucursal en las
/// opciones de una sucursal y vacío para un rol que no ve el área. El acceso real lo decide el servidor en cada ruta; el menú solo evita ofrecer lo que respondería «sin permiso».
/// </summary>
public sealed class MenuDeNavegacionTests
{
    private static readonly IMenuDeNavegacion Menu = new ResolverMenu(new AreasDeInterfaz());

    private static string[] Textos(RolWeb rol, AreaWeb area, string? codigo = null) => [.. Menu.Resolver(rol, area, codigo).Select(o => o.Texto)];

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public void Un_encargado_no_ve_ninguna_opcion_de_administracion_ni_en_su_menu_ni_en_el_area()
    {
        Assert.Empty(Menu.Resolver(RolWeb.Encargado, AreaWeb.Administracion));
        var suyo = Textos(RolWeb.Encargado, AreaWeb.Sucursal, "ayacucho");
        Assert.DoesNotContain("Usuarios y roles", suyo);
        Assert.DoesNotContain("Auditoría", suyo);
        Assert.All(Menu.Resolver(RolWeb.Encargado, AreaWeb.Sucursal, "ayacucho"), o => Assert.DoesNotContain("/administracion", o.Ruta, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public void La_administracion_central_ve_todas_sus_pantallas_de_gestion()
    {
        var textos = Textos(RolWeb.AdministradorCentral, AreaWeb.Administracion);

        Assert.Equal(["Catálogo nacional", "Pedidos", "Devoluciones", "Reembolsos", "Costos de envío", "Ayudas de la biblioteca", "Hojas de reclamación", "Usuarios y roles", "Solicitudes de datos", "Auditoría", "Alertas de seguridad"], textos);
        Assert.All(Menu.Resolver(RolWeb.AdministradorCentral, AreaWeb.Administracion), o => Assert.StartsWith("/administracion/", o.Ruta, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public void Un_visitante_ve_el_catalogo_y_el_carrito_pero_no_pedidos_ni_biblioteca_y_un_cliente_ve_los_cuatro()
    {
        Assert.Equal(["Catálogo", "Carrito"], Textos(RolWeb.Visitante, AreaWeb.Tienda));
        Assert.Equal(["Catálogo", "Carrito", "Mis pedidos", "Mi biblioteca"], Textos(RolWeb.Cliente, AreaWeb.Tienda));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public void El_encargado_ve_lo_de_su_sucursal_con_su_codigo_y_sin_pedidos_a_domicilio_y_logistica_ve_envios_y_domicilios()
    {
        var encargado = Menu.Resolver(RolWeb.Encargado, AreaWeb.Sucursal, "ayacucho");

        Assert.Equal(["Pedidos de recojo", "Devoluciones", "Envíos"], encargado.Select(o => o.Texto));
        Assert.Contains(encargado, o => o.Ruta == "/sucursal/ayacucho/pedidos");
        Assert.Contains(encargado, o => o.Ruta == "/sucursal/ayacucho/devoluciones");
        Assert.Equal(["Envíos", "Pedidos a domicilio"], Textos(RolWeb.Logistica, AreaWeb.Sucursal));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("../x")]
    [InlineData("ayacucho'><script>")]
    public void Sin_un_codigo_de_sucursal_valido_no_se_ofrecen_las_opciones_que_lo_necesitan_ni_se_arma_una_ruta_con_el_texto_recibido(string? codigo)
    {
        var menu = Menu.Resolver(RolWeb.Encargado, AreaWeb.Sucursal, codigo);

        Assert.Equal(["Envíos"], menu.Select(o => o.Texto));
        Assert.DoesNotContain(menu, o => o.Ruta.Contains('{', StringComparison.Ordinal) || o.Ruta.Contains('<', StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(RolWeb.Visitante, AreaWeb.Sucursal)]
    [InlineData(RolWeb.Visitante, AreaWeb.Administracion)]
    [InlineData(RolWeb.Cliente, AreaWeb.Sucursal)]
    [InlineData(RolWeb.Cliente, AreaWeb.Administracion)]
    [InlineData(RolWeb.Logistica, AreaWeb.Administracion)]
    [Trait("Criterio", "AC-WEB-02")]
    public void Un_rol_que_no_ve_el_area_recibe_un_menu_vacio(RolWeb rol, AreaWeb area) => Assert.Empty(Menu.Resolver(rol, area, "ayacucho"));
}
