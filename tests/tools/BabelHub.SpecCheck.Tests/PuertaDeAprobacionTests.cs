using System.Text;

namespace BabelHub.SpecCheck.Tests;

/// <summary>
/// Prueba la puerta de la aprobación delegada (docs/07-proceso-de-desarrollo/04-aprobacion-delegada.md) con violaciones
/// deliberadas sobre una copia temporal del repositorio.
/// </summary>
public sealed class PuertaDeAprobacionTests : RepositorioDePrueba
{
    [Fact]
    public void Aprobada_porDelegacion_sinRegistro_Falla()
    {
        var (codigo, salida) = Ejecutar();
        Assert.NotEqual(0, codigo);
        Assert.Contains("no hay fila de", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void Aprobada_porDelegacion_conAuditorQueNecesitaOtraVuelta_Falla()
    {
        Registrar("Necesita otra vuelta", "Cumple", "Cumple", "0", Huella());
        var (codigo, salida) = Ejecutar();
        Assert.NotEqual(0, codigo);
        Assert.Contains("el auditor debe dar", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void Aprobada_porDelegacion_conHallazgosAbiertos_Falla()
    {
        Registrar("Lista para aprobar", "Cumple", "No cumple", "2", Huella());
        var (codigo, salida) = Ejecutar();
        Assert.NotEqual(0, codigo);
        Assert.Contains("seguridad debe dar", salida, StringComparison.Ordinal);
        Assert.Contains("no puede haber hallazgos graves", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void Aprobada_porDelegacion_conRegistroLimpio_Pasa()
    {
        Registrar("Lista para aprobar", "Cumple", "Cumple", "0", Huella());
        var (codigo, salida) = Ejecutar();
        Assert.True(codigo == 0, salida);
    }

    [Fact]
    public void Aprobada_porDelegacion_conObservacionesMenores_sinRegistrarlas_Falla()
    {
        Registrar("Con cambios menores", "Cumple con observaciones", "Cumple con observaciones", "0", Huella(), menores: "3");
        var (codigo, salida) = Ejecutar();
        Assert.NotEqual(0, codigo);
        Assert.Contains("deben figurar en progress/hallazgos.md", salida, StringComparison.Ordinal);
    }

    [Fact]
    public void Aprobada_porDelegacion_conObservacionesMenores_registradas_Pasa()
    {
        Registrar("Con cambios menores", "Cumple con observaciones", "Cumple con observaciones", "0", Huella(), menores: "3");
        File.AppendAllText(
            EnLaCopia("progress", "hallazgos.md"),
            $"| 2026-11-12 | {Spec} | Tres menores de ejemplo | Menor | Agente | Fase 3 | Abierto |\n");
        var (codigo, salida) = Ejecutar();
        Assert.True(codigo == 0, salida);
    }

    [Fact]
    public void Aprobada_porDelegacion_yEditadaDespues_Falla()
    {
        Registrar("Lista para aprobar", "Cumple", "Cumple", "0", Huella());
        File.AppendAllText(RutaSpec(), "\n- cambio silencioso\n");
        var (codigo, salida) = Ejecutar();
        Assert.NotEqual(0, codigo);
        Assert.Contains("el contenido cambi", salida, StringComparison.Ordinal);
    }
}
