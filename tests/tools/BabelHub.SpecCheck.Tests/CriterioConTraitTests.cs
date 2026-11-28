using System.Text;

namespace BabelHub.SpecCheck.Tests;

/// <summary>El atributo <c>Trait</c> cuenta en cualquiera de sus formas válidas.</summary>
public sealed class CriterioConTraitTests : RepositorioDePrueba
{
    [Theory]
    [InlineData("[Trait(\"Criterio\", \"AC-PLA-01\")]\n")]
    [InlineData("[Fact, Trait( \"Criterio\" ,\n    \"AC-PLA-01\" )]\n")]
    [InlineData("[Fact]\n[Trait(\"Criterio\", \"AC-SEG-01\")]\n[Trait(\"Criterio\", \"AC-PLA-01\")]\n")]
    [InlineData("// otro comentario\n[Trait(\"Criterio\", \"AC-PLA-01\")] // y este también\n")]
    [Trait("Criterio", "AC-PLA-02")]
    public void EnEjecucion_conElAtributoTraitEnCualquierFormaValida_Pasa(string contenido)
    {
        PasarAEnEjecucion();
        MarcarTarea("T11");
        var pruebas = EnLaCopia("tests", "unit");
        Directory.CreateDirectory(pruebas);
        File.WriteAllText(Path.Combine(pruebas, "Ejemplo.cs"), contenido, new UTF8Encoding(false));

        var (codigo, salida) = Ejecutar();

        Assert.True(codigo == 0, salida);
    }
}
