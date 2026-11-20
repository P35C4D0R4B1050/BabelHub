using System.Net;
using System.Text;

namespace BabelHub.Tools.CoverageCheck;

/// <summary>
/// El reporte HTML de la cobertura de líneas (PLA-14): por servicio y capa, el estado frente a los umbrales, y por archivo, las líneas sin cubrir, del peor al mejor. Es un solo archivo, sin
/// scripts ni recursos externos (se archiva tal cual en el CI y en <c>progress/evidencias/</c>); lo calcula sobre las mismas líneas que la verificación de umbrales, así que ambos coinciden.
/// </summary>
public static class ReporteHtml
{
    private static readonly string[] Capas = ["Domain", "Application", "Infrastructure", "Api"];

    public static string Generar(Dictionary<(string Ensamblado, string Archivo, int Linea), bool> lineas, IReadOnlyCollection<string> servicios, Umbrales umbrales)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(umbrales);

        var resumen = Cobertura.Calcular(lineas);
        var incumplimientos = Cobertura.Verificar(resumen, servicios, umbrales);
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Cobertura de BabelHub</title><style>")
            .Append(":root{--fondo:#fff;--texto:#1b1b1f;--borde:#d0d0d8;--ok:#0a6b2d;--mal:#a4161a;--suave:#f1f1f6}")
            .Append("@media(prefers-color-scheme:dark){:root{--fondo:#16161a;--texto:#e8e8ee;--borde:#3a3a44;--ok:#5fd388;--mal:#ff8a8d;--suave:#22222a}}")
            .Append("body{font:14px/1.45 system-ui,sans-serif;background:var(--fondo);color:var(--texto);margin:0 auto;max-width:72rem;padding:1rem 1rem 3rem}")
            .Append("table{border-collapse:collapse;width:100%;margin:.5rem 0 1.5rem}th,td{border:1px solid var(--borde);padding:.3rem .5rem;text-align:left}th{background:var(--suave)}")
            .Append(".ok{color:var(--ok);font-weight:600}.mal{color:var(--mal);font-weight:600}.barra{background:var(--suave);border:1px solid var(--borde);height:.6rem;min-width:6rem}.barra>i{display:block;height:100%;background:var(--ok)}")
            .Append("</style></head><body><h1>Cobertura de líneas</h1>");

        html.Append("<p>Mínimos: Dominio y Aplicación ").Append(umbrales.DominioYAplicacion).Append(" %, total ").Append(umbrales.Total)
            .Append(" %. Sin código generado ni migraciones. Una línea cuenta como cubierta si alguna prueba (unitaria, de integración o de arquitectura) la ejecuta.</p>");
        html.Append("<h2>Resumen por servicio</h2><table><thead><tr><th>Servicio</th><th>Dominio y Aplicación</th><th>Total</th><th>Umbrales</th></tr></thead><tbody>");
        foreach (var servicio in resumen)
        {
            var cumple = !incumplimientos.Any(i => i.StartsWith(servicio.Servicio + ":", StringComparison.Ordinal));
            html.Append("<tr><td>").Append(E(servicio.Servicio)).Append("</td><td>").Append(Pct(servicio.PorcentajeDominioYAplicacion)).Append("</td><td>")
                .Append(Pct(servicio.PorcentajeTotal)).Append("</td><td class=\"").Append(cumple ? "ok\">Cumple" : "mal\">No cumple").Append("</td></tr>");
        }

        html.Append("</tbody></table>");
        foreach (var i in incumplimientos)
        {
            html.Append("<p class=\"mal\">").Append(E(i)).Append("</p>");
        }

        var filas = lineas
            .Where(l => Cobertura.TryServicioYCapa(l.Key.Ensamblado, out _, out _))
            .GroupBy(l => (l.Key.Ensamblado, l.Key.Archivo))
            .Select(g =>
            {
                Cobertura.TryServicioYCapa(g.Key.Ensamblado, out var servicio, out var capa);
                return (Servicio: servicio, Capa: capa, g.Key.Archivo, Totales: g.Count(), Cubiertas: g.Count(l => l.Value));
            })
            .ToList();

        html.Append("<h2>Por capa</h2><table><thead><tr><th>Servicio</th><th>Capa</th><th>Líneas</th><th>Cobertura</th><th></th></tr></thead><tbody>");
        foreach (var grupo in filas.GroupBy(f => (f.Servicio, f.Capa)).OrderBy(g => g.Key.Servicio, StringComparer.Ordinal).ThenBy(g => Array.IndexOf(Capas, g.Key.Capa)))
        {
            var totales = grupo.Sum(f => f.Totales);
            var cubiertas = grupo.Sum(f => f.Cubiertas);
            var pct = totales == 0 ? 0 : 100.0 * cubiertas / totales;
            html.Append("<tr><td>").Append(E(grupo.Key.Servicio)).Append("</td><td>").Append(E(grupo.Key.Capa)).Append("</td><td>").Append(cubiertas).Append(" / ").Append(totales)
                .Append("</td><td>").Append(Pct(pct)).Append("</td><td><div class=\"barra\"><i style=\"width:").Append(Math.Round(pct, 1).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append("%\"></i></div></td></tr>");
        }

        html.Append("</tbody></table><h2>Archivos con líneas sin cubrir (del peor al mejor)</h2><table><thead><tr><th>Archivo</th><th>Cobertura</th><th>Líneas</th><th>Sin cubrir</th></tr></thead><tbody>");
        foreach (var f in filas.Where(f => f.Cubiertas < f.Totales).OrderBy(f => 1.0 * f.Cubiertas / f.Totales).ThenByDescending(f => f.Totales - f.Cubiertas).ThenBy(f => f.Archivo, StringComparer.Ordinal))
        {
            html.Append("<tr><td>").Append(E(f.Archivo)).Append("</td><td>").Append(Pct(100.0 * f.Cubiertas / f.Totales)).Append("</td><td>").Append(f.Totales).Append("</td><td>")
                .Append(f.Totales - f.Cubiertas).Append(" sin cubrir</td></tr>");
        }

        html.Append("</tbody></table></body></html>");
        return html.ToString();
    }

    private static string Pct(double porcentaje) => Cobertura.Formato(porcentaje) + " %";

    private static string E(string texto) => WebUtility.HtmlEncode(texto);
}
