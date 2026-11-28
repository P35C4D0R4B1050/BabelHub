using System.Net;
using System.Text;

namespace BabelHub.Tools.CoverageCheck;

/// <summary>
/// El reporte HTML de la cobertura de líneas (PLA-14): por módulo, su criticidad y cómo queda frente a su referencia, que es orientativa; los totales por servicio y capa, como información; y por archivo, las líneas sin cubrir, del peor al mejor. Es un solo archivo, sin
/// scripts ni recursos externos (se archiva tal cual en el CI y en <c>progress/evidencias/</c>); lo calcula sobre las mismas líneas que la salida de consola, así que ambos coinciden.
/// </summary>
public static class ReporteHtml
{
    public static string Generar(Dictionary<(string Ensamblado, string Archivo, int Linea), bool> lineas, IReadOnlyCollection<string> servicios, PoliticaDeCobertura politica)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(politica);

        var modulos = Cobertura.Calcular(lineas, politica);
        var sinDatos = Cobertura.ServiciosSinDatos(modulos, servicios);
        var avisos = Cobertura.Avisos(modulos, servicios, politica);
        var criticos = politica.ReferenciaDeCriticos;
        var noCriticos = politica.ReferenciaDeNoCriticos;
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\"><title>Cobertura de BabelHub</title><style>")
            .Append(":root{--fondo:#fff;--texto:#1b1b1f;--borde:#d0d0d8;--ok:#0a6b2d;--aviso:#7a4d00;--mal:#a4161a;--suave:#f1f1f6}")
            .Append("@media(prefers-color-scheme:dark){:root{--fondo:#16161a;--texto:#e8e8ee;--borde:#3a3a44;--ok:#5fd388;--aviso:#f0b849;--mal:#ff8a8d;--suave:#22222a}}")
            .Append("body{font:14px/1.45 system-ui,sans-serif;background:var(--fondo);color:var(--texto);margin:0 auto;max-width:72rem;padding:1rem 1rem 3rem}")
            .Append("table{border-collapse:collapse;width:100%;margin:.5rem 0 1.5rem}th,td{border:1px solid var(--borde);padding:.3rem .5rem;text-align:left}th{background:var(--suave)}")
            .Append(".ok{color:var(--ok);font-weight:600}.aviso{color:var(--aviso);font-weight:600}.mal{color:var(--mal);font-weight:600}.barra{background:var(--suave);border:1px solid var(--borde);height:.6rem;min-width:6rem}.barra>i{display:block;height:100%;background:var(--ok)}")
            .Append("</style></head><body><h1>Cobertura de líneas</h1>");

        html.Append("<p>Referencias por módulo, orientativas y no obligatorias. Críticos y sin clasificar: Dominio y Aplicación ").Append(criticos.DominioYAplicacion).Append(" %, total ").Append(criticos.Total)
            .Append(" %. No críticos: Dominio y Aplicación ").Append(noCriticos.DominioYAplicacion).Append(" %, total ").Append(noCriticos.Total)
            .Append(" %. Sin código generado ni migraciones. Una línea cuenta como cubierta si alguna prueba (unitaria, de integración o de arquitectura) la ejecuta.</p>");

        foreach (var error in sinDatos)
        {
            html.Append("<p class=\"mal\">").Append(E(error)).Append("</p>");
        }

        foreach (var aviso in avisos)
        {
            html.Append("<p class=\"aviso\">").Append(E(aviso)).Append("</p>");
        }

        html.Append("<h2>Resumen por módulo</h2><table><thead><tr><th>Módulo</th><th>Criticidad</th><th>Dominio y Aplicación</th><th>Total</th><th>Referencia</th></tr></thead><tbody>");
        foreach (var modulo in modulos)
        {
            var referencia = politica.ReferenciaDe(modulo.Criticidad);
            var alcanza = (!modulo.TieneDominioYAplicacion || modulo.AlcanzaDominioYAplicacion(referencia)) && modulo.AlcanzaTotal(referencia);
            html.Append("<tr><td>").Append(E(modulo.Nombre)).Append("</td><td>").Append(Cobertura.Rotulo(modulo.Criticidad)).Append("</td><td>")
                .Append(modulo.TieneDominioYAplicacion ? Pct(modulo.PorcentajeDominioYAplicacion) : "sin líneas").Append("</td><td>")
                .Append(Pct(modulo.PorcentajeTotal)).Append("</td><td class=\"").Append(alcanza ? "ok\">En la referencia" : "aviso\">Bajo la referencia")
                .Append(" (").Append(referencia.DominioYAplicacion).Append(" / ").Append(referencia.Total).Append(")</td></tr>");
        }

        html.Append("</tbody></table><h2>Totales por servicio (informativos)</h2><table><thead><tr><th>Servicio</th><th>Dominio y Aplicación</th><th>Total</th></tr></thead><tbody>");
        foreach (var servicio in modulos.GroupBy(m => m.Servicio))
        {
            html.Append("<tr><td>").Append(E(servicio.Key)).Append("</td><td>")
                .Append(Pct(Cobertura.Porcentaje(servicio.Sum(m => m.DominioYAplicacionCubiertas), servicio.Sum(m => m.DominioYAplicacionTotales)))).Append("</td><td>")
                .Append(Pct(Cobertura.Porcentaje(servicio.Sum(m => m.TotalCubiertas), servicio.Sum(m => m.TotalTotales)))).Append("</td></tr>");
        }

        html.Append("</tbody></table>");

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
        foreach (var grupo in filas.GroupBy(f => (f.Servicio, f.Capa)).OrderBy(g => g.Key.Servicio, StringComparer.Ordinal).ThenBy(g => Array.IndexOf(Cobertura.Capas, g.Key.Capa)))
        {
            var totales = grupo.Sum(f => f.Totales);
            var cubiertas = grupo.Sum(f => f.Cubiertas);
            var pct = Cobertura.Porcentaje(cubiertas, totales);
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
