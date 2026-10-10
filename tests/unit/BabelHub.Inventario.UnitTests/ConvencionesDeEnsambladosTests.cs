namespace BabelHub.Inventario.UnitTests;

public class ConvencionesDeEnsambladosTests
{
    [Fact]
    public void Dominio_y_Aplicacion_siguen_la_convencion_de_nombres()
    {
        Assert.Equal("BabelHub.Inventario.Domain", BabelHub.Inventario.Domain.AssemblyMarker.Assembly.GetName().Name);
        Assert.Equal("BabelHub.Inventario.Application", BabelHub.Inventario.Application.AssemblyMarker.Assembly.GetName().Name);
    }
}
