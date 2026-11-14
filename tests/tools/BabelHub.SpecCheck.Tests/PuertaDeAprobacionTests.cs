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

        // Ninguna tarea hecha en la copia: el avance real del repositorio no debe decidir qué criterios exigen prueba (la huella del
        // plan ignora las casillas); cada prueba marca las suyas.
        foreach (var tareas in Directory.GetFiles(Path.Combine(_copia, "specs"), "tasks.md", SearchOption.AllDirectories))
        {
            var original = File.ReadAllText(tareas);
            File.WriteAllText(tareas, System.Text.RegularExpressions.Regex.Replace(original, @"(?m)^- \[x\] ", "- [ ] ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5)), new UTF8Encoding(false));
        }

        // Registro de hallazgos vacío: la prueba decide qué filas existen.
        File.WriteAllText(
            Path.Combine(_copia, "progress", "hallazgos.md"),
            "# Hallazgos\n\n| Fecha | Spec | Hallazgo | Gravedad | Dueño | Fase | Estado |\n| :--- | :--- | :--- | :--- | :--- | :--- | :--- |\n",
            new UTF8Encoding(false));

        // Las specs que el repositorio real ya aprobó por delegación vuelven a Draft en la copia y el registro de
        // aprobaciones queda sin filas, para que cada prueba controle por completo qué está aprobado.
        foreach (var spec in Directory.GetFiles(Path.Combine(_copia, "specs"), "spec.md", SearchOption.AllDirectories))
        {
            var texto = File.ReadAllText(spec).Replace("\r\n", "\n", StringComparison.Ordinal);
            var revertido = RevertirAprobacionesDelegadas(texto);
            if (texto != revertido)
            {
                File.WriteAllText(spec, revertido, new UTF8Encoding(false));
            }
        }

        var registro = Path.Combine(_copia, "progress", "aprobaciones.md");
        var filas = File.ReadAllLines(registro).ToList();
        var corte = filas.FindIndex(l => l.StartsWith("| :---", StringComparison.Ordinal));
        File.WriteAllText(registro, string.Join("\n", filas.Take(corte + 1)) + "\n", new UTF8Encoding(false));

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
    public void PlanAprobado_conUnaTareaMarcadaComoHecha_Pasa()
    {
        AprobarPlan();
        var tareas = Path.Combine(_copia, "specs", Spec, "tasks.md");
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
            Path.Combine(_copia, "specs", Spec, "tasks.md"),
            "- [ ] T99 [Pruebas] Tarea agregada después de aprobar (cubre: AC-PLA-01, PLA-01)\n");
        var (codigo, salida) = Ejecutar();
        Assert.NotEqual(0, codigo);
        Assert.Contains("el contenido cambi", salida, StringComparison.Ordinal);
    }

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
        var pruebas = Path.Combine(_copia, "tests", "unit");
        Directory.CreateDirectory(pruebas);
        File.WriteAllText(Path.Combine(pruebas, "Ejemplo.cs"), "[Trait(\"Criterio\", \"AC-PLA-01\")]\n", new UTF8Encoding(false));

        var (codigo, salida) = Ejecutar();

        Assert.True(codigo == 0, salida);
    }

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
        var pruebas = Path.Combine(_copia, "tests", "unit");
        Directory.CreateDirectory(pruebas);
        File.WriteAllText(Path.Combine(pruebas, "Ejemplo.cs"), contenido, new UTF8Encoding(false));

        var (codigo, salida) = Ejecutar();

        Assert.NotEqual(0, codigo);
        Assert.Contains("AC-PLA-01", salida, StringComparison.Ordinal);
        Assert.Contains("ninguna prueba", salida, StringComparison.Ordinal);
    }

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
        var pruebas = Path.Combine(_copia, "tests", "unit");
        Directory.CreateDirectory(pruebas);
        File.WriteAllText(Path.Combine(pruebas, "Ejemplo.cs"), contenido, new UTF8Encoding(false));

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

    /// <summary>Deja la spec aprobada y su plan aprobado (Planned) por delegación, con las dos filas del registro.</summary>
    private void AprobarPlan()
    {
        Registrar("Lista para aprobar", "Cumple", "Cumple", "0", Huella());
        var huellaPlan = Ejecutar("--huella", Spec, "plan").salida.Trim();
        File.AppendAllText(
            Path.Combine(_copia, "progress", "aprobaciones.md"),
            $"| 2026-11-12 | plan | {Spec} | Claude (delegado) | 0/0 | Lista para aprobar | Cumple | Cumple | 0 | 0 | {huellaPlan} |\n");

        var ruta = RutaSpec();
        var t = File.ReadAllText(ruta).Replace("\r\n", "\n", StringComparison.Ordinal);
        t = t.Replace("\nestado: Approved", "\nestado: Planned", StringComparison.Ordinal)
             .Replace(
                 "\naprobado_en: 2026-11-12",
                 "\naprobado_en: 2026-11-12\nplan_aprobado_por: Claude (delegado por Diego Amiquero)\nplan_aprobado_en: 2026-11-12",
                 StringComparison.Ordinal);
        File.WriteAllText(ruta, t, new UTF8Encoding(false));
        Ejecutar("--write-matrix");
    }

    /// <summary>Aprueba el plan y pasa la spec a InProgress (la huella ignora el estado).</summary>
    private void PasarAEnEjecucion()
    {
        AprobarPlan();
        var ruta = RutaSpec();
        var t = File.ReadAllText(ruta).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(ruta, t.Replace("\nestado: Planned", "\nestado: InProgress", StringComparison.Ordinal), new UTF8Encoding(false));
        Ejecutar("--write-matrix");
    }

    private void MarcarTarea(string id)
    {
        var tareas = Path.Combine(_copia, "specs", Spec, "tasks.md");
        var texto = File.ReadAllText(tareas);
        var marcado = System.Text.RegularExpressions.Regex.Replace(texto, $@"(?m)^- \[ \] {id} ", $"- [x] {id} ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));
        Assert.NotEqual(texto, marcado);
        File.WriteAllText(tareas, marcado, new UTF8Encoding(false));
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

    /// <summary>
    /// Quita del encabezado de una spec toda aprobación firmada por el agente delegado, sin depender del orden de los campos:
    /// la firma de la spec devuelve el estado a Draft; la firma del plan, a Approved si la spec la firmó la persona (que no
    /// exige fila en el registro) o a Draft si también la firmó el agente.
    /// </summary>
    private static string RevertirAprobacionesDelegadas(string texto)
    {
        const string Delegado = "Claude (delegado";
        var limite = texto.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (!texto.StartsWith("---\n", StringComparison.Ordinal) || limite < 0)
        {
            return texto;
        }

        var lineas = texto[..limite].Split('\n').ToList();
        var especFirmadaPorElAgente = lineas.Any(l => l.StartsWith("aprobado_por: " + Delegado, StringComparison.Ordinal));
        var planFirmadoPorElAgente = lineas.Any(l => l.StartsWith("plan_aprobado_por: " + Delegado, StringComparison.Ordinal));
        if (!especFirmadaPorElAgente && !planFirmadoPorElAgente)
        {
            return texto;
        }

        lineas.RemoveAll(l => l.StartsWith("plan_aprobado_por:", StringComparison.Ordinal) || l.StartsWith("plan_aprobado_en:", StringComparison.Ordinal));
        if (especFirmadaPorElAgente)
        {
            lineas.RemoveAll(l => l.StartsWith("aprobado_por:", StringComparison.Ordinal) || l.StartsWith("aprobado_en:", StringComparison.Ordinal));
        }

        var estado = especFirmadaPorElAgente ? "Draft" : "Approved";
        for (var i = 0; i < lineas.Count; i++)
        {
            if (lineas[i].StartsWith("estado: ", StringComparison.Ordinal))
            {
                lineas[i] = "estado: " + estado;
            }
        }

        return string.Join("\n", lineas) + texto[limite..];
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
