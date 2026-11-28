using System.Text;

namespace BabelHub.SpecCheck.Tests;

/// <summary>
/// El repositorio de prueba de la puerta de SpecCheck: una copia temporal de <c>docs</c>, <c>specs</c> y los registros de <c>progress</c>
/// con la spec de ejemplo aprobada por delegación. Las clases que lo usan ejecutan el SpecCheck real: no reimplementan sus reglas. Cada clase es su
/// propia colección de xUnit, así que corren en paralelo; cada caso trabaja sobre su copia y no escribe en el repositorio.
/// </summary>
public abstract class RepositorioDePrueba : IDisposable
{
    protected const string Spec = "transversal-plataforma";
    private readonly string _raiz = ProcesoDeSpecCheck.Raiz;
    private readonly string _copia = Path.Combine(Path.GetTempPath(), "speccheck-" + Guid.NewGuid().ToString("N"));

    protected RepositorioDePrueba()
    {
        Directory.CreateDirectory(_copia);
        foreach (var carpeta in new[] { "docs", "specs" })
        {
            CopiarCarpeta(Path.Combine(_raiz, carpeta), Path.Combine(_copia, carpeta));
        }

        // De progress/ SpecCheck solo lee el registro de aprobaciones y el de hallazgos; las evidencias pesan más que todo lo demás y
        // solo se copia de ellas lo que algún documento de la copia enlace.
        Directory.CreateDirectory(Path.Combine(_copia, "progress"));
        foreach (var archivo in new[] { "aprobaciones.md", "hallazgos.md" })
        {
            File.Copy(Path.Combine(_raiz, "progress", archivo), Path.Combine(_copia, "progress", archivo));
        }

        CopiarLosEnlacesQueSalenDeLaCopia();

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

    protected string RutaSpec() => Path.Combine(_copia, "specs", Spec, "spec.md");

    protected string Huella() => Ejecutar("--huella", Spec, "spec").salida.Trim();

    protected void Registrar(string auditor, string arq, string seg, string graves, string huella, string menores = "0")
    {
        var fila = $"| 2026-11-12 | spec | {Spec} | Claude (delegado) | 0/0 | {auditor} | {arq} | {seg} | {graves} | {menores} | {huella} |";
        File.AppendAllText(Path.Combine(_copia, "progress", "aprobaciones.md"), fila + "\n");
    }

    /// <summary>Deja la spec aprobada y su plan aprobado (Planned) por delegación, con las dos filas del registro.</summary>
    protected void AprobarPlan()
    {
        RegistrarElPlanAprobado();
        Ejecutar("--write-matrix");
    }

    private void RegistrarElPlanAprobado()
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
    }

    /// <summary>Aprueba el plan y pasa la spec a InProgress (la huella ignora el estado).</summary>
    protected void PasarAEnEjecucion()
    {
        RegistrarElPlanAprobado();
        var ruta = RutaSpec();
        var t = File.ReadAllText(ruta).Replace("\r\n", "\n", StringComparison.Ordinal);
        File.WriteAllText(ruta, t.Replace("\nestado: Planned", "\nestado: InProgress", StringComparison.Ordinal), new UTF8Encoding(false));
        Ejecutar("--write-matrix");
    }

    protected void MarcarTarea(string id)
    {
        var tareas = Path.Combine(_copia, "specs", Spec, "tasks.md");
        var texto = File.ReadAllText(tareas);
        var marcado = System.Text.RegularExpressions.Regex.Replace(texto, $@"(?m)^- \[ \] {id} ", $"- [x] {id} ", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));
        Assert.NotEqual(texto, marcado);
        File.WriteAllText(tareas, marcado, new UTF8Encoding(false));
    }

    protected (int codigo, string salida) Ejecutar(params string[] extra) =>
        ProcesoDeSpecCheck.Ejecutar(new[] { "--repo", _copia }.Concat(extra).ToArray());

    protected string EnLaCopia(params string[] partes) => Path.Combine([_copia, .. partes]);

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

    /// <summary>
    /// La copia solo trae <c>docs</c>, <c>specs</c> y los registros de <c>progress</c>, pero un documento puede enlazar a otro lugar del repositorio (<c>AGENTS.md</c>, <c>.github/workflows</c>, <c>tests/load</c>…): SpecCheck valida esos enlaces y la copia debe resolverlos
    /// como el repositorio real. Se copia lo que cada enlace relativo apunta y falta en la copia, sin depender de qué documento lo escriba.
    /// </summary>
    private void CopiarLosEnlacesQueSalenDeLaCopia()
    {
        var enlace = new System.Text.RegularExpressions.Regex(@"\]\(([^)\s#]+)(?:#[^)]*)?\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(5));
        foreach (var documento in Directory.GetFiles(_copia, "*.md", SearchOption.AllDirectories))
        {
            foreach (System.Text.RegularExpressions.Match m in enlace.Matches(File.ReadAllText(documento)))
            {
                var destino = m.Groups[1].Value;
                if (destino.Contains("://", StringComparison.Ordinal) || destino.StartsWith("mailto:", StringComparison.Ordinal))
                {
                    continue;
                }

                var enLaCopia = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(documento)!, destino));
                if (!enLaCopia.StartsWith(_copia, StringComparison.Ordinal) || File.Exists(enLaCopia) || Directory.Exists(enLaCopia))
                {
                    continue;
                }

                var real = Path.Combine(_raiz, Path.GetRelativePath(_copia, enLaCopia));
                if (File.Exists(real))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(enLaCopia)!);
                    File.Copy(real, enLaCopia, overwrite: true);
                }
                else if (Directory.Exists(real))
                {
                    CopiarCarpeta(real, enLaCopia);
                }
            }
        }
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
