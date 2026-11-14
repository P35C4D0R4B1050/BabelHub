using System.Globalization;
using BabelHub.Tools.CoverageCheck;

// Verifica los umbrales de cobertura de líneas por servicio (PLA-14): Dominio y Aplicación al menos 85 % y el total al menos 70 %,
// sin código generado ni migraciones. Uso:
//   dotnet run --project tools/CoverageCheck -- <archivo-o-carpeta>... [--minimo-dominio-aplicacion 85] [--minimo-total 70]
//                                                [--servicios Ecommerce,Inventario]
// Cada carpeta se recorre buscando informes *.cobertura.xml (el formato de coverlet). Sale con 0 si se cumplen, 1 si no y 2 si el
// uso o los informes son inválidos.
var rutas = new List<string>();
var dominioYAplicacion = 85;
var total = 70;
var servicios = new List<string> { "Ecommerce", "Inventario" };

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--minimo-dominio-aplicacion" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var da):
            dominioYAplicacion = da;
            i++;
            break;
        case "--minimo-total" when i + 1 < args.Length && int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var t):
            total = t;
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

IReadOnlyList<CoberturaDeServicio> resultado;
try
{
    resultado = Cobertura.Calcular(Cobertura.Leer(informes.Select(File.ReadAllText)));
}
catch (System.Xml.XmlException error)
{
    Console.Error.WriteLine($"Un informe de cobertura no es un XML válido: {error.Message}");
    return 2;
}

Console.WriteLine($"Informes leídos: {informes.Count}");
Console.WriteLine($"{"Servicio",-12} {"Dominio y Aplicación",22} {"Total",10}");
foreach (var servicio in resultado)
{
    Console.WriteLine($"{servicio.Servicio,-12} {Cobertura.Formato(servicio.PorcentajeDominioYAplicacion) + " %",22} {Cobertura.Formato(servicio.PorcentajeTotal) + " %",10}");
}

var incumplimientos = Cobertura.Verificar(resultado, servicios, new Umbrales(dominioYAplicacion, total));
if (incumplimientos.Count == 0)
{
    Console.WriteLine($"OK: cobertura de Dominio y Aplicación >= {dominioYAplicacion} % y total >= {total} % en {string.Join(", ", servicios)}.");
    return 0;
}

foreach (var incumplimiento in incumplimientos)
{
    Console.Error.WriteLine($"ERROR: {incumplimiento}");
}

return 1;
