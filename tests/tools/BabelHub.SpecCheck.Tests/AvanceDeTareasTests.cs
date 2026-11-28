using System.Text;

namespace BabelHub.SpecCheck.Tests;

/// <summary>Lo que cada estado de avance exige a los criterios de las tareas terminadas.</summary>
public sealed class AvanceDeTareasTests : RepositorioDePrueba
{
    [Fact]
    public void EnEjecucion_sinTareasTerminadas_NoExigePruebasDeCriterios()
    {
        PasarAEnEjecucion();

        var (codigo, salida) = Ejecutar();

        Assert.True(codigo == 0, salida);
    }

    [Fact]
    public void EnEjecucion_conUnaTareaTerminadaCuyoCriterioNingunaPruebaNombra_Falla()
    {
        PasarAEnEjecucion();
        MarcarTarea("T11");

        var (codigo, salida) = Ejecutar();

        Assert.NotEqual(0, codigo);
        Assert.Contains("AC-PLA-01", salida, StringComparison.Ordinal);
        Assert.Contains("ninguna prueba", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void EnEjecucion_conUnaTareaTerminadaCuyoCriterioUnaPruebaNombra_Pasa()
    {
        PasarAEnEjecucion();
        MarcarTarea("T11");
        var pruebas = EnLaCopia("tests", "unit");
        Directory.CreateDirectory(pruebas);
        File.WriteAllText(Path.Combine(pruebas, "Ejemplo.cs"), "[Trait(\"Criterio\", \"AC-PLA-01\")]\n", new UTF8Encoding(false));

        var (codigo, salida) = Ejecutar();

        Assert.True(codigo == 0, salida);
    }

    [Fact]
    public void Hecha_conCriteriosSinPrueba_Falla()
    {
        PasarAEnEjecucion();
        var ruta = RutaSpec();
        var t = File.ReadAllText(ruta).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(ruta, t.Replace("\nestado: InProgress", "\nestado: Done", StringComparison.Ordinal), new UTF8Encoding(false));

        var (codigo, salida) = Ejecutar();

        Assert.NotEqual(0, codigo);
        Assert.Contains("ninguna prueba", salida, StringComparison.Ordinal);
    }
}
