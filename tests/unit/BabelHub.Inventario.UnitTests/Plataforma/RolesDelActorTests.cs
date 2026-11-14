using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.UnitTests.Plataforma;

/// <summary>Los roles de la Aplicación son los mismos textos que los de <c>Shared</c> (que la Aplicación no puede referenciar, regla 6) y el alcance del encargado es exactamente su sucursal.</summary>
public sealed class RolesDelActorTests
{
    [Fact]
    public void LosRolesDeLaAplicacion_CoincidenConLosDeShared()
    {
        Assert.Equal(RolesDeUsuario.Cliente, RolesDelActor.Cliente);
        Assert.Equal(RolesDeUsuario.EncargadoDeSucursal, RolesDelActor.EncargadoDeSucursal);
        Assert.Equal(RolesDeUsuario.PersonalDeLogistica, RolesDelActor.PersonalDeLogistica);
        Assert.Equal(RolesDeUsuario.AdministradorCentral, RolesDelActor.AdministradorCentral);
    }

    [Fact]
    public void SoloUnUsuarioConElRolEsAdministracionOEncargado()
    {
        var sucursal = Guid.NewGuid();

        Assert.True(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral).EsAdministracionCentral());
        Assert.False(ActorActual.DelSistema().EsAdministracionCentral());
        Assert.False(ActorActual.DeIntegracion("e-commerce").EsAdministracionCentral());
        Assert.False(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica).EsAdministracionCentral());
        Assert.True(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal).EsEncargado());
        Assert.False(ActorActual.Anonimo().EsEncargado());
    }

    [Fact]
    public void ElEncargadoSoloTieneAlcanceSobreSuSucursal_YUnaSucursalNulaNuncaEsComodin()
    {
        var suya = Guid.NewGuid();
        var encargado = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, suya);
        var sinSucursal = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal);
        var vacia = ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, Guid.Empty);

        Assert.True(encargado.EsEncargadoDe(suya));
        Assert.False(encargado.EsEncargadoDe(Guid.NewGuid()));
        Assert.False(sinSucursal.EsEncargadoDe(Guid.NewGuid()));
        Assert.False(sinSucursal.EsEncargadoDe(Guid.Empty));
        Assert.False(vacia.EsEncargadoDe(Guid.Empty));
        Assert.False(ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente, suya).EsEncargadoDe(suya));
    }
}
