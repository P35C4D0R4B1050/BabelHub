using System.Text;

namespace BabelHub.SpecCheck.Tests;

/// <summary>La huella del plan aprobado ignora las casillas, pero no las tareas nuevas.</summary>
public sealed class PlanAprobadoTests : RepositorioDePrueba
{
    [Fact]
    public void PlanAprobado_conUnaTareaMarcadaComoHecha_Pasa()
    {
        AprobarPlan();
        var tareas = EnLaCopia("specs", Spec, "tasks.md");
        var texto = File.ReadAllText(tareas);
        var marcado = System.Text.RegularExpressions.Regex.Replace(texto, @"(?m)^- \[ \] T01 ", "- [x] T01 ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));
        Assert.NotEqual(texto, marcado);
        File.WriteAllText(tareas, marcado, new UTF8Encoding(false));
        var (codigo, salida) = Ejecutar();
        Assert.True(codigo == 0, salida);
    }

    [Fact]
    public void PlanAprobado_conUnaTareaNuevaAgregada_Falla()
    {
        AprobarPlan();
        File.AppendAllText(
            EnLaCopia("specs", Spec, "tasks.md"),
            "- [ ] T99 [Pruebas] Tarea agregada después de aprobar (cubre: AC-PLA-01, PLA-01)\n");
        var (codigo, salida) = Ejecutar();
        Assert.NotEqual(0, codigo);
        Assert.Contains("el contenido cambi", salida, StringComparison.Ordinal);
    }
}
