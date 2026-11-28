using System.Globalization;
using System.Xml.Linq;

namespace BabelHub.Tools.CoverageCheck;

/// <summary>La cobertura de líneas de un módulo de un servicio: la de Dominio y Aplicación y la del total de sus cuatro capas.</summary>
public sealed record CoberturaDeModulo(
    string Servicio, string Modulo, Criticidad Criticidad, int DominioYAplicacionCubiertas, int DominioYAplicacionTotales, int TotalCubiertas, int TotalTotales)
{
    public string Nombre => $"{Servicio}/{Modulo}";

    public bool TieneDominioYAplicacion => DominioYAplicacionTotales > 0;

    public double PorcentajeDominioYAplicacion => Cobertura.Porcentaje(DominioYAplicacionCubiertas, DominioYAplicacionTotales);

    public double PorcentajeTotal => Cobertura.Porcentaje(TotalCubiertas, TotalTotales);

    // Comparación exacta con enteros: cubiertas / totales >= referencia / 100.
    public bool AlcanzaDominioYAplicacion(Referencia referencia) => DominioYAplicacionCubiertas * 100L >= (long)referencia.DominioYAplicacion * DominioYAplicacionTotales;

    public bool AlcanzaTotal(Referencia referencia) => TotalCubiertas * 100L >= (long)referencia.Total * TotalTotales;
}

/// <summary>
/// Calcula la cobertura de líneas por módulo a partir de informes Cobertura de <c>coverlet</c> (uno o varios: de las pruebas
/// unitarias, de integración, etc.). Una línea cuenta como cubierta si algún informe la cubre. No cuenta el código generado ni las
/// migraciones. El módulo es la carpeta de primer nivel dentro de la carpeta de la capa. La comparación con la referencia es exacta,
/// sin redondear: 84,96 % no es 85 %.
/// </summary>
public static class Cobertura
{
    internal static readonly string[] Capas = ["Domain", "Application", "Infrastructure", "Api"];

    /// <summary>Lee los informes y devuelve, por clave (ensamblado, archivo, línea), si alguna ejecución cubrió la línea.</summary>
    public static Dictionary<(string Ensamblado, string Archivo, int Linea), bool> Leer(IEnumerable<string> contenidosXml)
    {
        ArgumentNullException.ThrowIfNull(contenidosXml);
        var lineas = new Dictionary<(string, string, int), bool>();

        foreach (var contenido in contenidosXml)
        {
            var documento = XDocument.Parse(contenido);
            var raices = documento.Descendants("source").Select(s => s.Value.Trim()).Where(s => s.Length > 0).ToList();
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
                    // de integración «Servicio/Ensamblado/Carpeta/A.cs»): se une por la parte que empieza en la carpeta del ensamblado. Si
                    // la raíz del informe ya está dentro de esa carpeta, se antepone la raíz para no perder el ensamblado ni el módulo.
                    if (!ContieneLaCarpeta(archivo, ensamblado) && raices.Count == 1)
                    {
                        var completa = Normalizar(raices[0].Replace('\\', '/').TrimEnd('/') + archivo);
                        archivo = ContieneLaCarpeta(completa, ensamblado) ? completa : archivo;
                    }

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

    /// <summary>La cobertura de cada módulo de los ensamblados <c>BabelHub.&lt;Servicio&gt;.&lt;Capa&gt;</c> (los demás no cuentan).</summary>
    public static IReadOnlyList<CoberturaDeModulo> Calcular(Dictionary<(string Ensamblado, string Archivo, int Linea), bool> lineas, PoliticaDeCobertura politica)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(politica);

        var porModulo = new Dictionary<(string Servicio, string Modulo), (int DaCubiertas, int DaTotales, int TCubiertas, int TTotales)>();
        foreach (var ((ensamblado, archivo, _), cubierta) in lineas)
        {
            if (!TryServicioYCapa(ensamblado, out var servicio, out var capa))
            {
                continue;
            }

            var clave = (servicio, ModuloDe(archivo, ensamblado));
            var (daCubiertas, daTotales, tCubiertas, tTotales) = porModulo.GetValueOrDefault(clave);
            tTotales++;
            tCubiertas += cubierta ? 1 : 0;
            if (capa is "Domain" or "Application")
            {
                daTotales++;
                daCubiertas += cubierta ? 1 : 0;
            }

            porModulo[clave] = (daCubiertas, daTotales, tCubiertas, tTotales);
        }

        return [.. porModulo
            .OrderBy(p => p.Key.Servicio, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Modulo, StringComparer.Ordinal)
            .Select(p => new CoberturaDeModulo(
                p.Key.Servicio, p.Key.Modulo, politica.CriticidadDe(p.Key.Servicio, p.Key.Modulo),
                p.Value.DaCubiertas, p.Value.DaTotales, p.Value.TCubiertas, p.Value.TTotales))];
    }

    /// <summary>Los servicios esperados de los que no llegó ningún dato de Dominio y Aplicación: el informe está incompleto.</summary>
    public static IReadOnlyList<string> ServiciosSinDatos(IReadOnlyList<CoberturaDeModulo> modulos, IReadOnlyCollection<string> servicios)
    {
        ArgumentNullException.ThrowIfNull(modulos);
        ArgumentNullException.ThrowIfNull(servicios);

        return [.. servicios
            .Where(s => !modulos.Any(m => string.Equals(m.Servicio, s, StringComparison.Ordinal) && m.TieneDominioYAplicacion))
            .Select(s => $"{s}: no hay datos de cobertura de su Dominio y su Aplicación.")];
    }

    /// <summary>
    /// Lo que conviene mirar de los servicios con datos, uno por línea y en español: los módulos bajo su referencia y los módulos
    /// críticos declarados sin datos (un renombrado o un informe parcial). Son avisos: no hacen fallar nada.
    /// </summary>
    public static IReadOnlyList<string> Avisos(IReadOnlyList<CoberturaDeModulo> modulos, IReadOnlyCollection<string> servicios, PoliticaDeCobertura politica)
    {
        ArgumentNullException.ThrowIfNull(modulos);
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(politica);

        var avisos = new List<string>();
        foreach (var servicio in servicios)
        {
            var propios = modulos.Where(m => string.Equals(m.Servicio, servicio, StringComparison.Ordinal)).ToList();
            if (!propios.Any(m => m.TieneDominioYAplicacion))
            {
                continue;
            }

            foreach (var declarado in politica.ModulosCriticos.Where(c => c.StartsWith(servicio + "/", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
            {
                if (!propios.Any(m => m.Nombre == declarado && m.TieneDominioYAplicacion))
                {
                    avisos.Add($"{declarado} (crítico): no hay datos de cobertura de su Dominio y su Aplicación.");
                }
            }

            foreach (var modulo in propios)
            {
                var referencia = politica.ReferenciaDe(modulo.Criticidad);
                var rotulo = $"{modulo.Nombre} ({Rotulo(modulo.Criticidad)})";
                if (modulo.TieneDominioYAplicacion && !modulo.AlcanzaDominioYAplicacion(referencia))
                {
                    avisos.Add($"{rotulo}: la cobertura de Dominio y Aplicación es {Formato(modulo.PorcentajeDominioYAplicacion)} % y la referencia es {referencia.DominioYAplicacion} %.");
                }

                if (!modulo.AlcanzaTotal(referencia))
                {
                    avisos.Add($"{rotulo}: la cobertura total es {Formato(modulo.PorcentajeTotal)} % y la referencia es {referencia.Total} %.");
                }
            }
        }

        return avisos;
    }

    public static string Rotulo(Criticidad criticidad) => criticidad switch
    {
        Criticidad.Critico => "crítico",
        Criticidad.NoCritico => "no crítico",
        _ => "sin clasificar, con la referencia de crítico",
    };

    public static string Formato(double porcentaje) =>
        (Math.Floor(porcentaje * 10) / 10).ToString("F1", CultureInfo.InvariantCulture);

    internal static double Porcentaje(int cubiertas, int totales) => totales == 0 ? 0 : 100.0 * cubiertas / totales;

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

    private static bool ContieneLaCarpeta(string rutaNormalizada, string ensamblado) =>
        rutaNormalizada.Contains("/" + ensamblado + "/", StringComparison.OrdinalIgnoreCase);

    /// <summary>La ruta desde la carpeta que se llama como el ensamblado (si no la hay, la ruta completa).</summary>
    internal static string DesdeLaCarpetaDelEnsamblado(string rutaNormalizada, string ensamblado)
    {
        var indice = rutaNormalizada.IndexOf("/" + ensamblado + "/", StringComparison.OrdinalIgnoreCase);
        return indice < 0 ? rutaNormalizada : rutaNormalizada[indice..];
    }

    /// <summary>
    /// El módulo de un archivo ya reducido con <see cref="DesdeLaCarpetaDelEnsamblado"/>. Si la ruta no pasa por la carpeta del
    /// ensamblado no se puede saber: queda como un módulo sin clasificar, con la referencia de crítico.
    /// </summary>
    internal static string ModuloDe(string archivo, string ensamblado)
    {
        var prefijo = "/" + ensamblado + "/";
        if (!archivo.StartsWith(prefijo, StringComparison.OrdinalIgnoreCase))
        {
            return "(ruta desconocida)";
        }

        var resto = archivo[prefijo.Length..];
        var barra = resto.IndexOf('/', StringComparison.Ordinal);
        return PoliticaDeCobertura.ModuloDeLaCarpeta(barra < 0 ? string.Empty : resto[..barra]);
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
