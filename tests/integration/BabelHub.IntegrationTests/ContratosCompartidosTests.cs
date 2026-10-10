namespace BabelHub.IntegrationTests;

public class ContratosCompartidosTests
{
    [Fact]
    public void Shared_expone_su_ensamblado()
    {
        Assert.Equal("BabelHub.Shared", BabelHub.Shared.AssemblyMarker.Assembly.GetName().Name);
    }
}
