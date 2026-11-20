using System.Globalization;
using System.Xml.Linq;

namespace BabelHub.Tools.CoverageCheck;

/// <summary>Los mínimos de cobertura de líneas por servicio (PLA-14).</summary>
public sealed record Umbrales(int DominioYAplicacion = 85, int Total = 70);

/// <summary>La cobertura de líneas de un servicio: la de Dominio y Aplicación y la del total de sus cuatro capas.</summary>
public sealed record CoberturaDeServicio(string Servicio, int DominioYAplicacionCubiertas, int DominioYAplicacionTotales, int TotalCubiertas, int TotalTotales)
{
    public bool TieneDominioYAplicacion => DominioYAplicacionTotales > 0;

    public double PorcentajeDominioYAplicacion => Porcentaje(DominioYAplicacionCubiertas, DominioYAplicacionTotales);

    public double PorcentajeTotal => Porcentaje(TotalCubiertas, TotalTotales);

    private static double Porcentaje(int cubiertas, int totales) => totales == 0 ? 0 : 100.0 * cubiertas / totales;
}

/// <summary>
/// Calcula la cobertura de líneas por servicio a partir de informes Cobertura de <c>coverlet</c> (uno o varios: de las pruebas
/// unitarias, de integración, etc.). Una línea cuenta como cubierta si algún informe la cubre. No cuenta el código generado ni las
/// migraciones. La comparación con el mínimo es exacta, sin redondear: 84,96 % no es 85 %.
/// </summary>
public static class Cobertura
{
    private static readonly string[] Capas = ["Domain", "Application", "Infrastructure", "Api"];

    /// <summary>Lee los informes y devuelve, por clave (ensamblado, archivo, línea), si alguna ejecución cubrió la línea.</summary>
    public static Dictionary<(string Ensamblado, string Archivo, int Linea), bool> Leer(IEnumerable<string> contenidosXml)
    {
        ArgumentNullException.ThrowIfNull(contenidosXml);
        var lineas = new Dictionary<(string, string, int), bool>();

        foreach (var contenido in contenidosXml)
        {
            var documento = XDocument.Parse(contenido);
            foreach (var paquete in documento.Descendants("package"))
            {
                var ensamblado = (string?)paquete.Attribute("name") ?? string.Empty;
                foreach (var clase in paquete.Descendants("class"))
                {
                    var archivo = Normalizar((string?)clase.Attribute("filename") ?? string.Empty);
                    if (EsCodigoExcluido(archivo))
                    {
                        continue;
                    }

                    // Cada informe da la ruta relativa a su propia raíz de fuentes (las pruebas unitarias dan «Ensamblado/Carpeta/A.cs» y las
                    // de integración «Servicio/Ensamblado/Carpeta/A.cs»): se une por la parte que empieza en la carpeta del ensamblado.
                    archivo = DesdeLaCarpetaDelEnsamblado(archivo, ensamblado);

                    // Solo las líneas propias de la clase: las de sus métodos son las mismas y contarían doble.
                    foreach (var linea in clase.Elements("lines").Elements("line"))
                    {
                        if (!int.TryParse((string?)linea.Attribute("number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var numero)
                            || !int.TryParse((string?)linea.Attribute("hits"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var visitas))
                        {
                            continue;
                        }

                        var clave = (ensamblado, archivo, numero);
                        lineas[clave] = lineas.GetValueOrDefault(clave) || visitas > 0;
                    }
                }
            }
        }

        return lineas;
    }

    /// <summary>La cobertura de cada servicio de los ensamblados <c>BabelHub.&lt;Servicio&gt;.&lt;Capa&gt;</c> (las demás no cuentan).</summary>
    public static IReadOnlyList<CoberturaDeServicio> Calcular(Dictionary<(string Ensamblado, string Archivo, int Linea), bool> lineas)
    {
        ArgumentNullException.ThrowIfNull(lineas);

        var porServicio = new Dictionary<string, (int DaCubiertas, int DaTotales, int TCubiertas, int TTotales)>(StringComparer.Ordinal);
        foreach (var ((ensamblado, _, _), cubierta) in lineas)
        {
            if (!TryServicioYCapa(ensamblado, out var servicio, out var capa))
            {
                continue;
            }

            var (daCubiertas, daTotales, tCubiertas, tTotales) = porServicio.GetValueOrDefault(servicio);
            tTotales++;
            tCubiertas += cubierta ? 1 : 0;
            if (capa is "Domain" or "Application")
            {
                daTotales++;
                daCubiertas += cubierta ? 1 : 0;
            }

            porServicio[servicio] = (daCubiertas, daTotales, tCubiertas, tTotales);
        }

        return [.. porServicio
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new CoberturaDeServicio(p.Key, p.Value.DaCubiertas, p.Value.DaTotales, p.Value.TCubiertas, p.Value.TTotales))];
    }

    /// <summary>Los incumplimientos, uno por línea y en español; una lista vacía significa que se cumplen los mínimos.</summary>
    public static IReadOnlyList<string> Verificar(IReadOnlyList<CoberturaDeServicio> servicios, IReadOnlyCollection<string> esperados, Umbrales umbrales)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(esperados);
        ArgumentNullException.ThrowIfNull(umbrales);

        var incumplimientos = new List<string>();
        foreach (var nombre in esperados)
        {
            var servicio = servicios.FirstOrDefault(s => string.Equals(s.Servicio, nombre, StringComparison.Ordinal));
            if (servicio is null || !servicio.TieneDominioYAplicacion)
            {
                incumplimientos.Add($"{nombre}: no hay datos de cobertura de su Dominio y su Aplicación.");
                continue;
            }

            // Comparación exacta con enteros: cubiertas / totales >= mínimo / 100.
            if (servicio.DominioYAplicacionCubiertas * 100L < (long)umbrales.DominioYAplicacion * servicio.DominioYAplicacionTotales)
            {
                incumplimientos.Add($"{nombre}: la cobertura de Dominio y Aplicación es {Formato(servicio.PorcentajeDominioYAplicacion)} % y el mínimo es {umbrales.DominioYAplicacion} %.");
            }

            if (servicio.TotalCubiertas * 100L < (long)umbrales.Total * servicio.TotalTotales)
            {
                incumplimientos.Add($"{nombre}: la cobertura total es {Formato(servicio.PorcentajeTotal)} % y el mínimo es {umbrales.Total} %.");
            }
        }

        return incumplimientos;
    }

    public static string Formato(double porcentaje) =>
        (Math.Floor(porcentaje * 10) / 10).ToString("F1", CultureInfo.InvariantCulture);

    /// <summary>Código que no cuenta: las migraciones y lo generado (por el compilador, por un generador de código o por una herramienta).</summary>
    public static bool EsCodigoExcluido(string archivo)
    {
        var ruta = Normalizar(archivo);
        return ruta.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase)
            || ruta.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || ruta.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase)
            || ruta.EndsWith(".g.i.cs", StringComparison.OrdinalIgnoreCase)
            || ruta.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase)
            || ruta.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase)
            || ruta.EndsWith("ModelSnapshot.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalizar(string ruta) => "/" + ruta.Replace('\\', '/').TrimStart('/');

    /// <summary>La ruta desde la carpeta que se llama como el ensamblado (si no la hay, la ruta completa).</summary>
    internal static string DesdeLaCarpetaDelEnsamblado(string rutaNormalizada, string ensamblado)
    {
        var marca = "/" + ensamblado + "/";
        var indice = rutaNormalizada.IndexOf(marca, StringComparison.OrdinalIgnoreCase);
        return indice < 0 ? rutaNormalizada : rutaNormalizada[indice..];
    }

    internal static bool TryServicioYCapa(string ensamblado, out string servicio, out string capa)
    {
        servicio = string.Empty;
        capa = string.Empty;
        var partes = ensamblado.Split('.');
        if (partes.Length != 3 || partes[0] != "BabelHub" || !Capas.Contains(partes[2], StringComparer.Ordinal))
        {
            return false;
        }

        servicio = partes[1];
        capa = partes[2];
        return true;
    }
}
