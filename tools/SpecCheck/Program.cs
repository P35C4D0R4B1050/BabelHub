using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// SpecCheck: valida specs, planes, tareas, puertas del ciclo SDD y trazabilidad.
// Uso: dotnet run --project tools/SpecCheck [-- --repo <ruta>] [--write-matrix]

var repo = ResolverRepo(args);
var escribirMatriz = args.Contains("--write-matrix");
var errores = new List<string>();
var avisos = new List<string>();

var cfg = Config.Cargar(Path.Combine(repo, "specs", "speccheck.json"));
var idsValidos = CargarIdsDeDocs(Path.Combine(repo, "docs"));
var specsDir = Path.Combine(repo, "specs");
var specs = new List<Spec>();

foreach (var dir in Directory.GetDirectories(specsDir).OrderBy(d => d, StringComparer.Ordinal))
{
    var nombre = Path.GetFileName(dir);
    if (nombre.StartsWith('_'))
    {
        continue;
    }

    var spec = Spec.Leer(dir, cfg, idsValidos, errores, avisos);
    if (spec is not null)
    {
        specs.Add(spec);
    }
}

ValidarGlobal(specs, cfg, idsValidos, repo, errores, avisos);

var matriz = GenerarMatriz(specs, idsValidos, cfg);
var rutaMatriz = Path.Combine(specsDir, "TRACEABILITY.md");
if (escribirMatriz)
{
    File.WriteAllText(rutaMatriz, matriz, new UTF8Encoding(false));
    Console.WriteLine($"Matriz escrita en {Path.GetRelativePath(repo, rutaMatriz)}");
}
else if (!File.Exists(rutaMatriz) || Normalizar(File.ReadAllText(rutaMatriz)) != Normalizar(matriz))
{
    errores.Add("specs/TRACEABILITY.md está desactualizado: ejecuta `dotnet run --project tools/SpecCheck -- --write-matrix` y súbelo.");
}

Console.WriteLine();
Console.WriteLine($"{"Spec",-34} {"Estado",-10} {"Req",4} {"AC",4} {"Preg.",5}");
foreach (var s in specs)
{
    Console.WriteLine($"{s.Id,-34} {s.Estado,-10} {s.Requisitos.Count,4} {s.Criterios.Count,4} {s.PreguntasAbiertas,5}");
}

Console.WriteLine();
foreach (var a in avisos)
{
    Console.WriteLine($"AVISO  {a}");
}

foreach (var e in errores)
{
    Console.WriteLine($"ERROR  {e}");
}

Console.WriteLine();
Console.WriteLine(errores.Count == 0
    ? $"OK: {specs.Count} specs, {avisos.Count} avisos."
    : $"FALLÓ: {errores.Count} errores, {avisos.Count} avisos en {specs.Count} specs.");
return errores.Count == 0 ? 0 : 1;

// ---------------------------------------------------------------------------------------------

static string Normalizar(string s) => s.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

static string ResolverRepo(string[] args)
{
    var i = Array.IndexOf(args, "--repo");
    if (i >= 0 && i + 1 < args.Length)
    {
        return Path.GetFullPath(args[i + 1]);
    }

    var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) && Directory.Exists(Path.Combine(dir.FullName, "specs")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    throw new InvalidOperationException("No se encontró la raíz del repositorio (AGENTS.md y specs/). Usa --repo <ruta>.");
}

static HashSet<string> CargarIdsDeDocs(string docs)
{
    var ids = new HashSet<string>(StringComparer.Ordinal);
    foreach (var archivo in Directory.GetFiles(docs, "*.md").Where(a => Regex.IsMatch(Path.GetFileName(a), "^0[123]-")))
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(archivo), @"\*\*((?:NN|RF|RNF|RC|AC)\d{2})\*\*"))
        {
            ids.Add(m.Groups[1].Value);
        }
    }

    return ids;
}

static void ValidarGlobal(List<Spec> specs, Config cfg, HashSet<string> idsValidos, string repo, List<string> errores, List<string> avisos)
{
    // Identificadores de requisito y de criterio únicos en todo el repositorio.
    var vistos = new Dictionary<string, string>(StringComparer.Ordinal);
    foreach (var s in specs)
    {
        foreach (var id in s.Requisitos.Select(r => r.Id).Concat(s.Criterios.Select(c => c.Id)))
        {
            if (!vistos.TryAdd(id, s.Id))
            {
                errores.Add($"{id} está repetido en {vistos[id]} y {s.Id}.");
            }
        }
    }

    // Prefijos únicos.
    foreach (var grupo in specs.GroupBy(s => s.Prefijo).Where(g => g.Count() > 1))
    {
        errores.Add($"El prefijo {grupo.Key} lo usan varias specs: {string.Join(", ", grupo.Select(s => s.Id))}.");
    }

    // Dependencias: existen y no forman ciclos.
    var porId = specs.ToDictionary(s => s.Id, StringComparer.Ordinal);
    foreach (var s in specs)
    {
        foreach (var d in s.Depende.Where(d => !porId.ContainsKey(d)))
        {
            errores.Add($"{s.Id}: depende de «{d}», que no existe.");
        }
    }

    foreach (var s in specs)
    {
        if (TieneCiclo(s.Id, porId, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal)))
        {
            errores.Add($"{s.Id}: las dependencias forman un ciclo.");
        }
    }

    // Cobertura de requisitos funcionales del negocio.
    var cubiertos = specs.SelectMany(s => s.Origen).ToHashSet(StringComparer.Ordinal);
    foreach (var rf in idsValidos.Where(i => i.StartsWith("RF", StringComparison.Ordinal) && !i.StartsWith("RNF", StringComparison.Ordinal)).OrderBy(i => i, StringComparer.Ordinal))
    {
        if (!cubiertos.Contains(rf) && !cfg.Diferidos.Contains(rf))
        {
            errores.Add($"{rf} no está cubierto por ninguna spec (ni figura como diferido en specs/speccheck.json).");
        }
    }

    foreach (var rnf in idsValidos.Where(i => i.StartsWith("RNF", StringComparison.Ordinal)).OrderBy(i => i, StringComparer.Ordinal))
    {
        if (!specs.SelectMany(s => s.Calidad.Concat(s.Origen)).Contains(rnf))
        {
            avisos.Add($"{rnf} no se cita en ninguna spec.");
        }
    }

    // Trazabilidad hacia las pruebas: toda spec en ejecución debe tener sus criterios en tests/.
    var idsEnPruebas = EscanearPruebas(Path.Combine(repo, "tests"));
    foreach (var s in specs.Where(s => s.IndiceEstado >= cfg.Indice("InProgress")))
    {
        foreach (var ac in s.Criterios.Where(c => !idsEnPruebas.Contains(c.Id)))
        {
            errores.Add($"{s.Id}: el criterio {ac.Id} no aparece en ninguna prueba de tests/ ([Trait(\"Criterio\", \"{ac.Id}\")]).");
        }
    }
}

static bool TieneCiclo(string id, Dictionary<string, Spec> porId, HashSet<string> visitando, HashSet<string> hecho)
{
    if (hecho.Contains(id))
    {
        return false;
    }

    if (!visitando.Add(id))
    {
        return true;
    }

    if (porId.TryGetValue(id, out var spec))
    {
        foreach (var d in spec.Depende)
        {
            if (TieneCiclo(d, porId, visitando, hecho))
            {
                return true;
            }
        }
    }

    visitando.Remove(id);
    hecho.Add(id);
    return false;
}

static HashSet<string> EscanearPruebas(string testsDir)
{
    var ids = new HashSet<string>(StringComparer.Ordinal);
    if (!Directory.Exists(testsDir))
    {
        return ids;
    }

    foreach (var archivo in Directory.EnumerateFiles(testsDir, "*.cs", SearchOption.AllDirectories)
                 .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)))
    {
        foreach (Match m in Regex.Matches(File.ReadAllText(archivo), @"AC-[A-Z]{3}-\d{2}"))
        {
            ids.Add(m.Value);
        }
    }

    return ids;
}

static string GenerarMatriz(List<Spec> specs, HashSet<string> idsValidos, Config cfg)
{
    var sb = new StringBuilder();
    sb.AppendLine("# Matriz de trazabilidad");
    sb.AppendLine();
    sb.AppendLine("> Generada por `dotnet run --project tools/SpecCheck -- --write-matrix`. No se edita a mano.");
    sb.AppendLine();
    sb.AppendLine("## Requisitos funcionales → specs");
    sb.AppendLine();
    sb.AppendLine("| RF | Specs | Requisitos EARS | Criterios |");
    sb.AppendLine("| :--- | :--- | :--- | :--- |");
    foreach (var rf in idsValidos.Where(i => i.StartsWith("RF", StringComparison.Ordinal) && !i.StartsWith("RNF", StringComparison.Ordinal)).OrderBy(i => i, StringComparer.Ordinal))
    {
        var conRf = specs.Where(s => s.Origen.Contains(rf) || s.Requisitos.Any(r => r.Origen.Contains(rf))).ToList();
        if (conRf.Count == 0)
        {
            sb.AppendLine($"| {rf} | {(cfg.Diferidos.Contains(rf) ? "*diferido*" : "**sin cobertura**")} | | |");
            continue;
        }

        var reqs = conRf.SelectMany(s => s.Requisitos.Where(r => r.Origen.Contains(rf))).Select(r => r.Id).OrderBy(i => i, StringComparer.Ordinal).ToList();
        var acs = conRf.SelectMany(s => s.Criterios.Where(c => c.Cubre.Any(reqs.Contains))).Select(c => c.Id).Distinct().OrderBy(i => i, StringComparer.Ordinal).ToList();
        sb.AppendLine($"| {rf} | {string.Join(", ", conRf.Select(s => s.Id))} | {string.Join(", ", reqs)} | {string.Join(", ", acs)} |");
    }

    sb.AppendLine();
    sb.AppendLine("## Specs");
    sb.AppendLine();
    sb.AppendLine("| Spec | Servicio | Estado | Requisitos | Criterios | Depende de |");
    sb.AppendLine("| :--- | :--- | :--- | ---: | ---: | :--- |");
    foreach (var s in specs)
    {
        sb.AppendLine($"| {s.Id} | {s.Servicio} | {s.Estado} | {s.Requisitos.Count} | {s.Criterios.Count} | {(s.Depende.Count == 0 ? "—" : string.Join(", ", s.Depende))} |");
    }

    return sb.ToString();
}

internal sealed record Requisito(string Id, string Patron, string Texto, List<string> Origen);

internal sealed record Criterio(string Id, List<string> Cubre, string Escenario);

internal sealed class Config
{
    public List<string> Estados { get; set; } = [];

    public List<string> Patrones { get; set; } = [];

    public HashSet<string> Diferidos { get; set; } = [];

    public List<string> PalabrasProhibidas { get; set; } = [];

    public static Config Cargar(string ruta)
    {
        var opciones = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return JsonSerializer.Deserialize<Config>(File.ReadAllText(ruta), opciones)
               ?? throw new InvalidOperationException($"No se pudo leer {ruta}.");
    }

    public int Indice(string estado) => Estados.IndexOf(estado);
}

internal sealed class Spec
{
    private static readonly Regex Fecha = new(@"^\d{4}-\d{2}-\d{2}$", RegexOptions.Compiled);

    public string Id { get; private set; } = "";

    public string Estado { get; private set; } = "";

    public int IndiceEstado { get; private set; }

    public string Servicio { get; private set; } = "";

    public string Prefijo { get; private set; } = "";

    public List<string> Origen { get; private set; } = [];

    public List<string> Calidad { get; private set; } = [];

    public List<string> Depende { get; private set; } = [];

    public List<Requisito> Requisitos { get; } = [];

    public List<Criterio> Criterios { get; } = [];

    public int PreguntasAbiertas { get; private set; }

    public static Spec? Leer(string dir, Config cfg, HashSet<string> idsValidos, List<string> errores, List<string> avisos)
    {
        var nombre = Path.GetFileName(dir);
        var ruta = Path.Combine(dir, "spec.md");
        if (!File.Exists(ruta))
        {
            errores.Add($"{nombre}: falta spec.md.");
            return null;
        }

        var lineas = File.ReadAllLines(ruta);
        var fm = Frontmatter(lineas);
        if (fm is null)
        {
            errores.Add($"{nombre}: spec.md no tiene frontmatter (--- ... ---).");
            return null;
        }

        foreach (var k in new[] { "id", "titulo", "estado", "servicio", "prefijo", "origen", "calidad", "depende" })
        {
            if (!fm.ContainsKey(k))
            {
                errores.Add($"{nombre}: el frontmatter no tiene «{k}».");
            }
        }

        var s = new Spec
        {
            Id = fm.GetValueOrDefault("id", ""),
            Estado = fm.GetValueOrDefault("estado", ""),
            Servicio = fm.GetValueOrDefault("servicio", ""),
            Prefijo = fm.GetValueOrDefault("prefijo", ""),
            Origen = Lista(fm.GetValueOrDefault("origen")),
            Calidad = Lista(fm.GetValueOrDefault("calidad")),
            Depende = Lista(fm.GetValueOrDefault("depende"))
        };
        s.IndiceEstado = cfg.Indice(s.Estado);

        if (s.Id != nombre)
        {
            errores.Add($"{nombre}: el id del frontmatter («{s.Id}») no coincide con la carpeta.");
        }

        if (s.IndiceEstado < 0)
        {
            errores.Add($"{nombre}: estado «{s.Estado}» inválido (usa {string.Join(", ", cfg.Estados)}).");
        }

        if (!Regex.IsMatch(s.Prefijo, "^[A-Z]{3}$"))
        {
            errores.Add($"{nombre}: el prefijo «{s.Prefijo}» debe tener 3 letras mayúsculas.");
        }

        if (s.Origen.Count == 0)
        {
            errores.Add($"{nombre}: «origen» no puede estar vacío.");
        }

        foreach (var id in s.Origen.Concat(s.Calidad).Where(id => !idsValidos.Contains(id)))
        {
            errores.Add($"{nombre}: «{id}» no existe en docs/01, docs/02 ni docs/03.");
        }

        s.LeerSecciones(lineas, nombre, cfg, idsValidos, errores, avisos);
        s.ValidarCobertura(nombre, errores);
        s.ValidarPuertas(dir, fm, nombre, cfg, errores, avisos);
        return s;
    }

    private static Dictionary<string, string>? Frontmatter(string[] lineas)
    {
        if (lineas.Length == 0 || lineas[0].Trim() != "---")
        {
            return null;
        }

        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 1; i < lineas.Length; i++)
        {
            if (lineas[i].Trim() == "---")
            {
                return d;
            }

            var m = Regex.Match(lineas[i], @"^([A-Za-z_]+):\s*(.*)$");
            if (m.Success)
            {
                d[m.Groups[1].Value] = m.Groups[2].Value.Trim();
            }
        }

        return null;
    }

    private static List<string> Lista(string? valor)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return [];
        }

        var v = valor.Trim();
        if (v.StartsWith('[') && v.EndsWith(']'))
        {
            v = v[1..^1];
        }

        return v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static List<string> Ids(string celda) =>
        celda.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<string[]> Filas(IEnumerable<string> lineas)
    {
        var filas = new List<string[]>();
        foreach (var l in lineas.Where(l => l.TrimStart().StartsWith('|')))
        {
            var celdas = l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray();
            if (celdas.Length == 0 || celdas[0].StartsWith(":--", StringComparison.Ordinal) || celdas[0] == "ID" || celdas[0].StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            filas.Add(celdas);
        }

        return filas;
    }

    private static List<string> Seccion(string[] lineas, string titulo)
    {
        var inicio = Array.FindIndex(lineas, l => Regex.IsMatch(l, @"^##\s+\d+\.\s+" + titulo));
        if (inicio < 0)
        {
            return [];
        }

        var fin = Array.FindIndex(lineas, inicio + 1, l => l.StartsWith("## ", StringComparison.Ordinal));
        return lineas.Skip(inicio + 1).Take((fin < 0 ? lineas.Length : fin) - inicio - 1).ToList();
    }

    private void LeerSecciones(string[] lineas, string nombre, Config cfg, HashSet<string> idsValidos, List<string> errores, List<string> avisos)
    {
        var secReq = Seccion(lineas, "Requisitos");
        var secAc = Seccion(lineas, "Criterios de aceptación");
        var secPreg = Seccion(lineas, "Preguntas abiertas");
        if (secReq.Count == 0)
        {
            errores.Add($"{nombre}: falta la sección «Requisitos (EARS)».");
        }

        if (secAc.Count == 0)
        {
            errores.Add($"{nombre}: falta la sección «Criterios de aceptación».");
        }

        if (secPreg.Count == 0)
        {
            errores.Add($"{nombre}: falta la sección «Preguntas abiertas».");
        }

        var reglaId = new Regex($"^{Regex.Escape(Prefijo)}-\\d{{2}}$");
        foreach (var f in Filas(secReq))
        {
            if (f.Length < 4)
            {
                errores.Add($"{nombre}: fila de requisitos con menos de 4 columnas: «{string.Join(" | ", f)}».");
                continue;
            }

            var (id, patron, texto) = (f[0], f[1], f[2]);
            var origen = Ids(f[3]);
            Requisitos.Add(new Requisito(id, patron, texto, origen));
            if (!reglaId.IsMatch(id))
            {
                errores.Add($"{nombre}: el requisito «{id}» debe llamarse {Prefijo}-NN.");
            }

            if (!cfg.Patrones.Contains(patron))
            {
                errores.Add($"{id}: patrón «{patron}» inválido (usa {string.Join(", ", cfg.Patrones)}).");
            }

            if (!texto.Contains("deberá", StringComparison.Ordinal))
            {
                errores.Add($"{id}: el requisito EARS debe contener «deberá».");
            }

            var ok = patron switch
            {
                "Evento" => texto.StartsWith("Cuando ", StringComparison.Ordinal),
                "Estado" => texto.StartsWith("Mientras ", StringComparison.Ordinal),
                "No deseado" => texto.StartsWith("Si ", StringComparison.Ordinal) && texto.Contains(" entonces ", StringComparison.Ordinal),
                "Opcional" => texto.StartsWith("Donde ", StringComparison.Ordinal),
                "Ubicuo" => !Regex.IsMatch(texto, @"^(Cuando|Mientras|Si|Donde)\b"),
                _ => true
            };
            if (!ok)
            {
                errores.Add($"{id}: el texto no sigue la plantilla EARS «{patron}».");
            }

            foreach (var p in cfg.PalabrasProhibidas.Where(p => ContienePalabra(texto, p)))
            {
                errores.Add($"{id}: el requisito nombra tecnología («{p}»); eso va en el plan, no en la spec.");
            }

            if (origen.Count == 0)
            {
                errores.Add($"{id}: falta el origen.");
            }

            foreach (var o in origen.Where(o => !idsValidos.Contains(o)))
            {
                errores.Add($"{id}: el origen «{o}» no existe en docs/.");
            }

            var palabras = texto.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            if (palabras < 8)
            {
                avisos.Add($"{id}: requisito muy corto ({palabras} palabras); ¿es verificable?");
            }

            if (palabras > 70)
            {
                avisos.Add($"{id}: requisito muy largo ({palabras} palabras); ¿mezcla varios comportamientos?");
            }
        }

        var reglaAc = new Regex($"^AC-{Regex.Escape(Prefijo)}-\\d{{2}}$");
        foreach (var f in Filas(secAc))
        {
            if (f.Length < 3)
            {
                errores.Add($"{nombre}: fila de criterios con menos de 3 columnas: «{string.Join(" | ", f)}».");
                continue;
            }

            var cubre = Ids(f[1]);
            Criterios.Add(new Criterio(f[0], cubre, f[2]));
            if (!reglaAc.IsMatch(f[0]))
            {
                errores.Add($"{nombre}: el criterio «{f[0]}» debe llamarse AC-{Prefijo}-NN.");
            }

            var e = f[2].ToLowerInvariant();
            if (!e.Contains("dado", StringComparison.Ordinal) || !e.Contains("cuando", StringComparison.Ordinal) || !e.Contains("entonces", StringComparison.Ordinal))
            {
                errores.Add($"{f[0]}: el escenario debe tener Dado, cuando y entonces.");
            }

        }

        PreguntasAbiertas = secPreg.Count(l => Regex.IsMatch(l, @"^- \[ \]"));
    }

    private static bool ContienePalabra(string texto, string palabra)
    {
        var sensible = palabra == palabra.ToUpperInvariant();
        var opciones = sensible ? RegexOptions.None : RegexOptions.IgnoreCase;
        return Regex.IsMatch(texto, @"(?<![\p{L}\p{N}])" + Regex.Escape(palabra) + @"(?![\p{L}\p{N}])", opciones);
    }

    private void ValidarCobertura(string nombre, List<string> errores)
    {
        var ids = Requisitos.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var c in Criterios)
        {
            if (c.Cubre.Count == 0)
            {
                errores.Add($"{c.Id}: el criterio no indica qué requisitos cubre.");
            }

            foreach (var x in c.Cubre.Where(x => !ids.Contains(x)))
            {
                errores.Add($"{c.Id}: cubre «{x}», que no es un requisito de {nombre}.");
            }
        }

        var cubiertos = Criterios.SelectMany(c => c.Cubre).ToHashSet(StringComparer.Ordinal);
        foreach (var r in Requisitos.Where(r => !cubiertos.Contains(r.Id)))
        {
            errores.Add($"{r.Id}: ningún criterio de aceptación lo cubre.");
        }

        if (Requisitos.Count == 0)
        {
            errores.Add($"{nombre}: la spec no tiene requisitos.");
        }
    }

    private void ValidarPuertas(string dir, Dictionary<string, string> fm, string nombre, Config cfg, List<string> errores, List<string> avisos)
    {
        if (IndiceEstado < 0)
        {
            return;
        }

        if (IndiceEstado >= cfg.Indice("Approved"))
        {
            if (PreguntasAbiertas > 0)
            {
                errores.Add($"{nombre}: está {Estado} pero tiene {PreguntasAbiertas} pregunta(s) abierta(s).");
            }

            if (!fm.TryGetValue("aprobado_por", out var por) || string.IsNullOrWhiteSpace(por))
            {
                errores.Add($"{nombre}: {Estado} exige «aprobado_por» en el frontmatter (lo escribe una persona).");
            }

            if (!fm.TryGetValue("aprobado_en", out var en) || !Fecha.IsMatch(en))
            {
                errores.Add($"{nombre}: {Estado} exige «aprobado_en» con formato AAAA-MM-DD.");
            }
        }
        else if (PreguntasAbiertas > 0)
        {
            avisos.Add($"{nombre}: {PreguntasAbiertas} pregunta(s) abierta(s) por resolver antes de aprobar.");
        }

        var plan = Path.Combine(dir, "plan.md");
        var tasks = Path.Combine(dir, "tasks.md");
        if (IndiceEstado >= cfg.Indice("Planned"))
        {
            if (!fm.TryGetValue("plan_aprobado_en", out var pa) || !Fecha.IsMatch(pa))
            {
                errores.Add($"{nombre}: {Estado} exige «plan_aprobado_en» (AAAA-MM-DD).");
            }

            if (!File.Exists(plan))
            {
                errores.Add($"{nombre}: {Estado} exige plan.md.");
            }

            if (!File.Exists(tasks))
            {
                errores.Add($"{nombre}: {Estado} exige tasks.md.");
            }
            else
            {
                ValidarTareas(tasks, nombre, cfg, errores);
            }
        }
        else if (File.Exists(plan) || File.Exists(tasks))
        {
            avisos.Add($"{nombre}: hay plan.md o tasks.md pero la spec sigue en {Estado}.");
        }

        if (IndiceEstado >= cfg.Indice("Done"))
        {
            var ver = Path.Combine(dir, "verificacion.md");
            if (!File.Exists(ver) || !Regex.IsMatch(File.ReadAllText(ver), @"Resultado:\s*Converge", RegexOptions.IgnoreCase))
            {
                errores.Add($"{nombre}: Done exige verificacion.md con «Resultado: Converge».");
            }
        }
    }

    private void ValidarTareas(string ruta, string nombre, Config cfg, List<string> errores)
    {
        var tareas = File.ReadAllLines(ruta)
            .Select(l => Regex.Match(l, @"^- \[( |x)\] (T\d+) \[([^\]]+)\] (.+?) \(cubre: ([^)]+)\)\s*$"))
            .Where(m => m.Success)
            .ToList();
        if (tareas.Count == 0)
        {
            errores.Add($"{nombre}: tasks.md no tiene tareas con el formato «- [ ] T01 [Capa] Descripción (cubre: AC-XXX-01)».");
            return;
        }

        var cubiertos = tareas.SelectMany(m => Ids(m.Groups[5].Value)).ToHashSet(StringComparer.Ordinal);
        foreach (var c in Criterios.Where(c => !cubiertos.Contains(c.Id)))
        {
            errores.Add($"{nombre}: el criterio {c.Id} no está cubierto por ninguna tarea de tasks.md.");
        }

        if (IndiceEstado >= cfg.Indice("Done"))
        {
            foreach (var m in tareas.Where(m => m.Groups[1].Value == " "))
            {
                errores.Add($"{nombre}: está Done pero la tarea {m.Groups[2].Value} no está marcada.");
            }
        }
    }
}
