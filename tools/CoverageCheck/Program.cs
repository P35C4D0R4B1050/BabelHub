using System.Text;
using BabelHub.Tools.CoverageCheck;

// Verifica la cobertura de líneas de cada módulo de cada servicio según su criticidad (PLA-14): los mínimos y la lista de módulos
// están en PoliticaDeCobertura.cs, sin código generado ni migraciones. Uso:
//   dotnet run --project tools/CoverageCheck -- <archivo-o-carpeta>... [--servicios Ecommerce,Inventario] [--html reporte.html]
// Cada carpeta se recorre buscando informes *.cobertura.xml (el formato de coverlet). Sale con 0 si todos los módulos cumplen, 1 si
// alguno no y 2 si el uso o los informes son inválidos. Los mínimos no se pueden cambiar desde la línea de órdenes.
Console.OutputEncoding = Encoding.UTF8;
var rutas = new List<string>();
var servicios = new List<string> { "Ecommerce", "Inventario" };
string? archivoHtml = null;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--html" when i + 1 < args.Length:
            archivoHtml = args[i + 1];
            i++;
            break;
        case "--servicios" when i + 1 < args.Length:
            servicios = [.. args[i + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
            i++;
            break;
        default:
            if (args[i].StartsWith("--", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Opción desconocida o sin valor: {args[i]}");
                return 2;
            }

            rutas.Add(args[i]);
            break;
    }
}

if (rutas.Count == 0)
{
    Console.Error.WriteLine("Indica al menos un informe *.cobertura.xml o una carpeta que los contenga.");
    return 2;
}

if (servicios.Count == 0)
{
    Console.Error.WriteLine("La lista de servicios no puede estar vacía.");
    return 2;
}

var informes = new List<string>();
foreach (var ruta in rutas)
{
    if (File.Exists(ruta))
    {
        informes.Add(ruta);
    }
    else if (Directory.Exists(ruta))
    {
        informes.AddRange(Directory.EnumerateFiles(ruta, "*.cobertura.xml", SearchOption.AllDirectories));
    }
    else
    {
        Console.Error.WriteLine($"No existe: {ruta}");
        return 2;
    }
}

if (informes.Count == 0)
{
    Console.Error.WriteLine("No se encontró ningún informe de cobertura *.cobertura.xml.");
    return 2;
}

var politica = PoliticaDeCobertura.Vigente;
IReadOnlyList<CoberturaDeModulo> resultado;
try
{
    var lineasLeidas = Cobertura.Leer(informes.Select(File.ReadAllText));
    resultado = Cobertura.Calcular(lineasLeidas, politica);
    if (archivoHtml is not null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(archivoHtml))!);
        File.WriteAllText(archivoHtml, ReporteHtml.Generar(lineasLeidas, servicios, politica));
        Console.WriteLine($"Reporte HTML: {archivoHtml}");
    }
}
catch (System.Xml.XmlException error)
{
    Console.Error.WriteLine($"Un informe de cobertura no es un XML válido: {error.Message}");
    return 2;
}

Console.WriteLine($"Informes leídos: {informes.Count}");
Console.WriteLine($"{"Módulo",-30} {"Criticidad",-14} {"Dominio y Aplicación",22} {"Total",18}");
foreach (var modulo in resultado)
{
    var umbrales = politica.UmbralesDe(modulo.Criticidad);
    var dominioYAplicacion = modulo.TieneDominioYAplicacion
        ? $"{Cobertura.Formato(modulo.PorcentajeDominioYAplicacion)} % (>= {umbrales.DominioYAplicacion})"
        : "sin líneas";
    var total = $"{Cobertura.Formato(modulo.PorcentajeTotal)} % (>= {umbrales.Total})";
    var criticidad = modulo.Criticidad == Criticidad.SinClasificar ? "sin clasificar" : Cobertura.Rotulo(modulo.Criticidad);
    Console.WriteLine($"{modulo.Nombre,-30} {criticidad,-14} {dominioYAplicacion,22} {total,18}");
}

var incumplimientos = Cobertura.Verificar(resultado, servicios, politica);
if (incumplimientos.Count == 0)
{
    Console.WriteLine($"OK: cada módulo de {string.Join(", ", servicios)} cumple los mínimos de su criticidad.");
    return 0;
}

foreach (var incumplimiento in incumplimientos)
{
    Console.Error.WriteLine($"ERROR: {incumplimiento}");
}

return 1;
