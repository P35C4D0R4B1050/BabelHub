using BabelHub.Tools.CoverageCheck;

namespace BabelHub.CoverageCheck.Tests;

public sealed class ReporteHtmlTests
{
    private static readonly PoliticaDeCobertura Politica = new(["Inventario/Reservas"], ["Inventario/Ajustes"]);

    private static Dictionary<(string Ensamblado, string Archivo, int Linea), bool> Lineas(string capa, string archivo, int totales, int cubiertas)
    {
        var ensamblado = $"BabelHub.Inventario.{capa}";
        var d = new Dictionary<(string, string, int), bool>();
        for (var i = 1; i <= totales; i++)
        {
            d[(ensamblado, $"/{ensamblado}/{archivo}", i)] = i <= cubiertas;
        }

        return d;
    }

    private static Dictionary<(string Ensamblado, string Archivo, int Linea), bool> Unir(params Dictionary<(string Ensamblado, string Archivo, int Linea), bool>[] partes) =>
        partes.SelectMany(p => p).ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void ElReporte_MuestraPorModuloYCapaLosPorcentajesLaCriticidadYElEstado()
    {
        var lineas = Unir(Lineas("Domain", "Reservas/A.cs", 10, 9), Lineas("Api", "Reservas/B.cs", 10, 5), Lineas("Domain", "Ajustes/C.cs", 10, 7));

        var html = ReporteHtml.Generar(lineas, ["Inventario"], Politica);

        Assert.Contains("<!doctype html>", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<td>Inventario/Reservas</td><td>crítico</td><td>90.0 %</td><td>70.0 %</td><td class=\"ok\">Cumple (85 / 70)</td>", html, StringComparison.Ordinal);
        Assert.Contains("<td>Inventario/Ajustes</td><td>no crítico</td><td>70.0 %</td><td>70.0 %</td><td class=\"ok\">Cumple (60 / 40)</td>", html, StringComparison.Ordinal);
        Assert.Contains("50.0 %", html, StringComparison.Ordinal);   // Api: 5 de 10
        Assert.Contains("<td>Inventario</td><td>80.0 %</td><td>70.0 %</td>", html, StringComparison.Ordinal);   // servicio: 16 de 20 y 21 de 30
    }

    [Fact]
    public void ElReporte_ListaLosArchivosConSusLineasPorCubrirOrdenadosDePeorACubierto()
    {
        var lineas = Unir(Lineas("Domain", "Reservas/Buena.cs", 10, 8), Lineas("Domain", "Reservas/Mala.cs", 10, 2));

        var html = ReporteHtml.Generar(lineas, ["Inventario"], Politica);

        Assert.True(html.IndexOf("Mala.cs", StringComparison.Ordinal) < html.IndexOf("Buena.cs", StringComparison.Ordinal), "El peor archivo va primero.");
        Assert.Contains("8 sin cubrir", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ElReporte_MarcaElIncumplimientoYEscapaElHtmlDeLosNombres()
    {
        var lineas = Lineas("Domain", "Reservas/<script>.cs", 10, 1);

        var html = ReporteHtml.Generar(lineas, ["Inventario"], Politica);

        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("No cumple", html, StringComparison.Ordinal);
    }
}
