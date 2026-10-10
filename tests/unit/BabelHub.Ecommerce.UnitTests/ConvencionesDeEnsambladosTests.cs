namespace BabelHub.Ecommerce.UnitTests;

public class ConvencionesDeEnsambladosTests
{
    [Fact]
    public void Dominio_y_Aplicacion_siguen_la_convencion_de_nombres()
    {
        Assert.Equal("BabelHub.Ecommerce.Domain", BabelHub.Ecommerce.Domain.AssemblyMarker.Assembly.GetName().Name);
        Assert.Equal("BabelHub.Ecommerce.Application", BabelHub.Ecommerce.Application.AssemblyMarker.Assembly.GetName().Name);
    }
}
