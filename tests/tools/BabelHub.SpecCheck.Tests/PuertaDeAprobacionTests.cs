using System.Diagnostics;
using System.Text;

namespace BabelHub.SpecCheck.Tests;

/// <summary>
/// Prueba la puerta de la aprobación delegada (docs/07-proceso-de-desarrollo/04-aprobacion-delegada.md) con violaciones
/// deliberadas sobre una copia temporal del repositorio. Ejecutan el SpecCheck real: no reimplementan sus reglas.
/// </summary>
public sealed class PuertaDeAprobacionTests : IDisposable
{
    private const string Spec = "transversal-plataforma";
    private readonly string _raiz = BuscarRaiz();
    private readonly string _copia = Path.Combine(Path.GetTempPath(), "speccheck-" + Guid.NewGuid().ToString("N"));

    public PuertaDeAprobacionTests()
    {
        Directory.CreateDirectory(_copia);
        foreach (var carpeta in new[] { "docs", "specs", "progress" })
        {
            CopiarCarpeta(Path.Combine(_raiz, carpeta), Path.Combine(_copia, carpeta));
        }

        // Registro de hallazgos vacío: la prueba decide qué filas existen.
        File.WriteAllText(
            Path.Combine(_copia, "progress", "hallazgos.md"),
            "# Hallazgos\n\n| Fecha | Spec | Hallazgo | Gravedad | Dueño | Fase | Estado |\n| :--- | :--- | :--- | :--- | :--- | :--- | :--- |\n",
            new UTF8Encoding(false));

        // La spec de ejemplo pasa a Approved firmada por el agente delegado.
        var ruta = RutaSpec();
        var t = File.ReadAllText(ruta).Replace("\r\n", "\n", StringComparison.Ordinal);
        t = t.Replace("\nestado: Draft", "\nestado: Approved\naprobado_por: Claude (delegado por Diego Amiquero)\naprobado_en: 2026-11-12", StringComparison.Ordinal);
        File.WriteAllText(ruta, t, new UTF8Encoding(false));
        Ejecutar("--write-matrix");
    }

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
            Path.Combine(_copia, "progress", "hallazgos.md"),
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

    [Fact]
    public void ElRepositorioActual_PasaSpecCheck()
    {
        var p = Proceso(_raiz, "--repo", _raiz);
        Assert.True(p.codigo == 0, p.salida);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_copia, recursive: true);
        }
        catch (IOException)
        {
            // Limpieza de una carpeta temporal: no debe hacer fallar la prueba.
        }
    }

    private string RutaSpec() => Path.Combine(_copia, "specs", Spec, "spec.md");

    private string Huella() => Ejecutar("--huella", Spec, "spec").salida.Trim();

    private void Registrar(string auditor, string arq, string seg, string graves, string huella, string menores = "0")
    {
        var fila = $"| 2026-11-12 | spec | {Spec} | Claude (delegado) | 0/0 | {auditor} | {arq} | {seg} | {graves} | {menores} | {huella} |";
        File.AppendAllText(Path.Combine(_copia, "progress", "aprobaciones.md"), fila + "\n");
    }

    private (int codigo, string salida) Ejecutar(params string[] extra) =>
        Proceso(_raiz, new[] { "--repo", _copia }.Concat(extra).ToArray());

    private (int codigo, string salida) Proceso(string directorio, params string[] argumentos)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = directorio,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in new[] { "run", "--project", Path.Combine("tools", "SpecCheck"), "--no-build", "--" }.Concat(argumentos))
        {
            info.ArgumentList.Add(a);
        }

        using var p = Process.Start(info)!;
        var salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();
        return (p.ExitCode, salida);
    }

    private static string BuscarRaiz()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "BabelHub.sln")))
        {
            d = d.Parent;
        }

        return d?.FullName ?? throw new InvalidOperationException("No se encontró BabelHub.sln");
    }

    private static void CopiarCarpeta(string origen, string destino)
    {
        Directory.CreateDirectory(destino);
        foreach (var f in Directory.GetFiles(origen))
        {
            if (Path.GetExtension(f) is ".pptx" or ".docx")
            {
                continue;
            }

            File.Copy(f, Path.Combine(destino, Path.GetFileName(f)), overwrite: true);
        }

        foreach (var d in Directory.GetDirectories(origen))
        {
            CopiarCarpeta(d, Path.Combine(destino, Path.GetFileName(d)));
        }
    }
}
