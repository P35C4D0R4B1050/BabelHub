using System.Diagnostics;

namespace BabelHub.SpecCheck.Tests;

/// <summary>
/// Ejecuta el SpecCheck compilado junto con las pruebas. Se lanza su ensamblado directamente: <c>dotnet run</c> evaluaba el proyecto en
/// cada llamada (más de un segundo, decenas de veces por corrida) y tomaba la compilación Debug aunque las pruebas fueran de otra
/// configuración.
/// </summary>
internal static class ProcesoDeSpecCheck
{
    public static string Raiz { get; } = BuscarRaiz();

    private static readonly string Ensamblado = BuscarElEnsamblado();

    public static (int codigo, string salida) Ejecutar(params string[] argumentos)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Raiz,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in argumentos.Prepend(Ensamblado))
        {
            info.ArgumentList.Add(a);
        }

        using var p = Process.Start(info)!;
        var errores = p.StandardError.ReadToEndAsync();
        var salida = p.StandardOutput.ReadToEnd() + errores.GetAwaiter().GetResult();
        p.WaitForExit();
        return (p.ExitCode, salida);
    }

    private static string BuscarElEnsamblado()
    {
        // bin/<configuración>/<marco>/ de estas pruebas: la herramienta se compiló con la misma configuración y el mismo marco.
        var marco = new DirectoryInfo(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory));
        var ruta = Path.Combine(Raiz, "tools", "SpecCheck", "bin", marco.Parent!.Name, marco.Name, "SpecCheck.dll");
        return File.Exists(ruta) ? ruta : throw new InvalidOperationException($"No se encontró SpecCheck compilado en {ruta}");
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
}
