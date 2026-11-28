using System.Text;

namespace BabelHub.CoverageCheck.Tests;

/// <summary>Informes Cobertura de prueba, con la forma de los de <c>coverlet</c>.</summary>
internal static class Informes
{
    /// <summary>Un informe de coverlet de un ensamblado: una clase por archivo con tantas líneas como se indique, las primeras cubiertas.</summary>
    public static string Paquete(string ensamblado, string archivo, int totales, int cubiertas, bool conMetodos = false)
    {
        var lineas = new StringBuilder();
        for (var i = 1; i <= totales; i++)
        {
            lineas.Append($"<line number=\"{i}\" hits=\"{(i <= cubiertas ? 1 : 0)}\" branch=\"False\" />");
        }

        var metodos = conMetodos ? $"<methods><method name=\"M\"><lines>{lineas}</lines></method></methods>" : "<methods />";
        return $"<package name=\"{ensamblado}\"><classes><class name=\"{archivo}\" filename=\"{archivo}\">{metodos}<lines>{lineas}</lines></class></classes></package>";
    }

    public static string Informe(params string[] paquetes) =>
        $"<?xml version=\"1.0\"?><coverage line-rate=\"0\"><packages>{string.Concat(paquetes)}</packages></coverage>";

    /// <summary>Un módulo con 100 líneas en Dominio y Aplicación (la mitad en cada una) y 100 más en Infraestructura y Api.</summary>
    public static string Modulo(string nombre, int cubiertasDominioYAplicacion, int cubiertasOtras)
    {
        var (servicio, modulo) = (nombre.Split('/')[0], nombre.Split('/')[1]);
        var dominio = Math.Min(50, cubiertasDominioYAplicacion);
        var infraestructura = Math.Min(50, cubiertasOtras);
        return string.Concat(
            Capa("Domain", dominio),
            Capa("Application", cubiertasDominioYAplicacion - dominio),
            Capa("Infrastructure", infraestructura),
            Capa("Api", cubiertasOtras - infraestructura));

        string Capa(string capa, int cubiertas) =>
            Paquete($"BabelHub.{servicio}.{capa}", $"BabelHub.{servicio}.{capa}/{modulo}/A.cs", 50, cubiertas);
    }
}
