namespace BabelHub.SpecCheck.Tests;

/// <summary>El repositorio tal como está pasa el SpecCheck real; no necesita la copia temporal.</summary>
public sealed class RepositorioActualTests
{
    [Fact]
    public void ElRepositorioActual_PasaSpecCheck()
    {
        var p = ProcesoDeSpecCheck.Ejecutar("--repo", ProcesoDeSpecCheck.Raiz);
        Assert.True(p.codigo == 0, p.salida);
    }
}
