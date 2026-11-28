using System.Text;

namespace BabelHub.SpecCheck.Tests;

/// <summary>Nombrar un criterio sin el atributo <c>Trait</c> no demuestra que haya una prueba que lo verifique.</summary>
public sealed class CriterioSinTraitTests : RepositorioDePrueba
{
    [Theory]
    [InlineData("// AC-PLA-01: se prueba aquí\n")]
    [InlineData("/* AC-PLA-01 */\n")]
    [InlineData("// [Trait(\"Criterio\", \"AC-PLA-01\")]\n")]
    [InlineData("[Fact]\npublic void AC_PLA_01_Nombre() { var texto = \"AC-PLA-01\"; }\n")]
    [InlineData("[Trait(\"Categoria\", \"AC-PLA-01\")]\n")]
    [Trait("Criterio", "AC-PLA-23")]
    public void EnEjecucion_conElCriterioSoloNombradoSinElAtributoTrait_FallaEIndicaElCriterio(string contenido)
    {
        PasarAEnEjecucion();
        MarcarTarea("T11");
        var pruebas = EnLaCopia("tests", "unit");
        Directory.CreateDirectory(pruebas);
        File.WriteAllText(Path.Combine(pruebas, "Ejemplo.cs"), contenido, new UTF8Encoding(false));

        var (codigo, salida) = Ejecutar();

        Assert.NotEqual(0, codigo);
        Assert.Contains("AC-PLA-01", salida, StringComparison.Ordinal);
        Assert.Contains("ninguna prueba", salida, StringComparison.Ordinal);
    }
}
