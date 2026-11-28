using System.Text;
using BabelHub.Tools.CoverageCheck;

// Informa la cobertura de líneas de cada módulo de cada servicio frente a la referencia de su criticidad (PLA-14): las referencias y la
// lista de módulos están en PoliticaDeCobertura.cs, sin código generado ni migraciones. Uso:
//   dotnet run --project tools/CoverageCheck -- <archivo-o-carpeta>... [--servicios Ecommerce,Inventario] [--html reporte.html]
// Cada carpeta se recorre buscando informes *.cobertura.xml (el formato de coverlet). Los porcentajes son referenciales: quedar por
// debajo solo se avisa. Sale con 0 si pudo informar y con 2 si el uso es inválido, si no puede leer los informes o si no tienen datos
// de un servicio (PLA-45).
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
catch (Exception error) when (error is IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"No se pudo leer o escribir un informe: {error.Message}");
    return 2;
}

Console.WriteLine($"Informes leídos: {informes.Count}");
Console.WriteLine($"{"Módulo",-30} {"Criticidad",-14} {"Dominio y Aplicación (ref.)",28} {"Total (ref.)",18}");
foreach (var modulo in resultado)
{
    var referencia = politica.ReferenciaDe(modulo.Criticidad);
    var dominioYAplicacion = modulo.TieneDominioYAplicacion
        ? $"{Cobertura.Formato(modulo.PorcentajeDominioYAplicacion)} % ({referencia.DominioYAplicacion})"
        : "sin líneas";
    var total = $"{Cobertura.Formato(modulo.PorcentajeTotal)} % ({referencia.Total})";
    var criticidad = modulo.Criticidad == Criticidad.SinClasificar ? "sin clasificar" : Cobertura.Rotulo(modulo.Criticidad);
    Console.WriteLine($"{modulo.Nombre,-30} {criticidad,-14} {dominioYAplicacion,28} {total,18}");
}

var avisos = Cobertura.Avisos(resultado, servicios, politica);
foreach (var aviso in avisos)
{
    Console.WriteLine($"AVISO: {aviso}");
}

var sinDatos = Cobertura.ServiciosSinDatos(resultado, servicios);
if (sinDatos.Count > 0)
{
    foreach (var error in sinDatos)
    {
        Console.Error.WriteLine($"ERROR: {error}");
    }

    return 2;
}

Console.WriteLine($"Informe de {string.Join(", ", servicios)} completo, con {avisos.Count} aviso(s). Los porcentajes son referenciales y no hacen fallar la verificación.");
return 0;
