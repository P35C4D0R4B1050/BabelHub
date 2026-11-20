using BabelHub.Tools.CoverageCheck;

namespace BabelHub.CoverageCheck.Tests;

public sealed class ReporteHtmlTests
{
    private static Dictionary<(string Ensamblado, string Archivo, int Linea), bool> Lineas(string ensamblado, string archivo, int totales, int cubiertas)
    {
        var d = new Dictionary<(string, string, int), bool>();
        for (var i = 1; i <= totales; i++)
        {
            d[(ensamblado, archivo, i)] = i <= cubiertas;
        }

        return d;
    }

    [Fact]
    public void ElReporte_MuestraPorServicioYCapaLosPorcentajesYElEstadoDeLosUmbrales()
    {
        var lineas = Lineas("BabelHub.Inventario.Domain", "/BabelHub.Inventario.Domain/A.cs", 10, 9);
        foreach (var (k, v) in Lineas("BabelHub.Inventario.Api", "/BabelHub.Inventario.Api/B.cs", 10, 5))
        {
            lineas[k] = v;
        }

        var html = ReporteHtml.Generar(lineas, ["Inventario"], new Umbrales(85, 70));

        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Inventario", html, StringComparison.Ordinal);
        Assert.Contains("90.0 %", html, StringComparison.Ordinal);   // Dominio: 9 de 10
        Assert.Contains("50.0 %", html, StringComparison.Ordinal);   // Api: 5 de 10
        Assert.Contains("70.0 %", html, StringComparison.Ordinal);   // total: 14 de 20
        Assert.Contains("Cumple", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ElReporte_ListaLosArchivosConSusLineasPorCubrirOrdenadosDePeorACubierto()
    {
        var lineas = Lineas("BabelHub.Inventario.Domain", "/BabelHub.Inventario.Domain/Buena.cs", 10, 8);
        foreach (var (k, v) in Lineas("BabelHub.Inventario.Domain", "/BabelHub.Inventario.Domain/Mala.cs", 10, 2))
        {
            lineas[k] = v;
        }

        var html = ReporteHtml.Generar(lineas, ["Inventario"], new Umbrales());

        Assert.True(html.IndexOf("Mala.cs", StringComparison.Ordinal) < html.IndexOf("Buena.cs", StringComparison.Ordinal), "El peor archivo va primero.");
        Assert.Contains("8 sin cubrir", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ElReporte_MarcaElIncumplimientoYEscapaElHtmlDeLosNombres()
    {
        var lineas = Lineas("BabelHub.Inventario.Domain", "/BabelHub.Inventario.Domain/<script>.cs", 10, 1);

        var html = ReporteHtml.Generar(lineas, ["Inventario"], new Umbrales(85, 70));

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("No cumple", html, StringComparison.Ordinal);
    }
}
