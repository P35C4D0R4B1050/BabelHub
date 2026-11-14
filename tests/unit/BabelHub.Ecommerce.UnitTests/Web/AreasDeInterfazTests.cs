using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>El contrato de áreas por rol (T04, WEB-02, WEB-03): traduce el Dominio a los tipos propios del contrato.</summary>
public sealed class AreasDeInterfazTests
{
    private readonly IAreasDeInterfaz _areas = new AreasDeInterfaz();

    [Theory]
    [InlineData(RolWeb.Encargado, AreaWeb.Administracion, false)]
    [InlineData(RolWeb.Encargado, AreaWeb.Sucursal, true)]
    [InlineData(RolWeb.AdministradorCentral, AreaWeb.Administracion, true)]
    [InlineData(RolWeb.Cliente, AreaWeb.Tienda, true)]
    [InlineData(RolWeb.Cliente, AreaWeb.Sucursal, false)]
    [Trait("Criterio", "AC-WEB-02")]
    public void Un_rol_ve_solo_sus_areas(RolWeb rol, AreaWeb area, bool esperado) =>
        Assert.Equal(esperado, _areas.VeLaArea(rol, area));

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public void La_lista_de_areas_de_un_encargado_no_incluye_Administracion() =>
        Assert.Equal([AreaWeb.Sucursal], _areas.AreasDe(RolWeb.Encargado));

    [Theory]
    [InlineData(AreaWeb.Tienda, HostWeb.Tienda)]
    [InlineData(AreaWeb.Sucursal, HostWeb.Gestion)]
    [InlineData(AreaWeb.Administracion, HostWeb.Gestion)]
    [Trait("Criterio", "AC-WEB-02")]
    public void Cada_area_tiene_su_host(AreaWeb area, HostWeb host) =>
        Assert.Equal(host, _areas.HostDeLaArea(area));

    [Theory]
    [InlineData(RolWeb.Cliente, HostWeb.Gestion, false)]
    [InlineData(RolWeb.Encargado, HostWeb.Tienda, false)]
    [InlineData(RolWeb.Logistica, HostWeb.Gestion, true)]
    [Trait("Criterio", "AC-WEB-23")]
    public void La_pareja_rol_y_host_que_no_corresponde_es_invalida(RolWeb rol, HostWeb host, bool valida) =>
        Assert.Equal(valida, _areas.ParejaDeRolYHostValida(rol, host));

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public void Una_credencial_en_memoria_no_se_imprime()
    {
        var credencial = new CredencialEnMemoria("eyJ.secreto.firma");

        Assert.DoesNotContain("secreto", $"{credencial}", StringComparison.Ordinal);
        Assert.DoesNotContain("secreto", new SesionIniciada("cookie-secreta", null).ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("eyJ", new IngresoCompletado(Guid.NewGuid(), RolWeb.Cliente, "eyJ.secreto.firma", DateTimeOffset.UnixEpoch, "renov-secreta").ToString(), StringComparison.Ordinal);
    }
}
