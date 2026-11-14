namespace BabelHub.ArchitectureTests;

/// <summary>
/// Pruebas de las reglas de dependencia con cambios hipotéticos que las violan a propósito (AC-PLA-13): cada regla debe fallar
/// señalando la dependencia. Los casos de módulos y de la interfaz web (tipos) están en <see cref="PruebasDeSensibilidadTests"/>.
/// </summary>
public class ReglasDeReferenciasSensibilidadTests
{
    private static Proyecto P(string nombre, string[]? referencias = null, string[]? paquetes = null) =>
        new(nombre, new HashSet<string>(referencias ?? [], StringComparer.Ordinal), new HashSet<string>(paquetes ?? [], StringComparer.Ordinal));

    /// <summary>Los dos servicios con sus referencias correctas: la base sobre la que cada caso introduce una violación.</summary>
    private static List<Proyecto> Correcto()
    {
        var lista = new List<Proyecto> { P("BabelHub.Shared") };
        foreach (var s in ReglasDeReferencias.Servicios)
        {
            lista.Add(P($"{s}.Domain"));
            lista.Add(P($"{s}.Application", [$"{s}.Domain"]));
            lista.Add(P($"{s}.Infrastructure", [$"{s}.Application", $"{s}.Domain", "BabelHub.Shared"]));
            lista.Add(P($"{s}.Api", [$"{s}.Application", $"{s}.Domain", $"{s}.Infrastructure", "BabelHub.Shared"]));
        }

        return lista;
    }

    private static List<Proyecto> Con(string proyecto, params string[] referenciasNuevas)
    {
        var lista = Correcto();
        var indice = lista.FindIndex(p => p.Nombre == proyecto);
        var actual = lista[indice];
        lista[indice] = actual with { ProyectosReferenciados = actual.ProyectosReferenciados.Concat(referenciasNuevas).ToHashSet(StringComparer.Ordinal) };
        return lista;
    }

    [Fact]
    public void Un_grafo_correcto_no_viola_ninguna_regla()
    {
        var lista = Correcto();

        Assert.Empty(ReglasDeReferencias.CapasInternas(lista));
        Assert.Empty(ReglasDeReferencias.ServiciosAjenos(lista));
        Assert.Empty(ReglasDeReferencias.ContratosCompartidos(lista));
        Assert.Empty(ReglasDeReferencias.HerramientaDeRestauracion(lista, []));
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-13")]
    [InlineData("BabelHub.Ecommerce.Domain", "BabelHub.Ecommerce.Application")]
    [InlineData("BabelHub.Ecommerce.Application", "BabelHub.Ecommerce.Infrastructure")]
    [InlineData("BabelHub.Inventario.Domain", "BabelHub.Inventario.Infrastructure")]
    [InlineData("BabelHub.Inventario.Application", "BabelHub.Inventario.Api")]
    [InlineData("BabelHub.Inventario.Infrastructure", "BabelHub.Inventario.Api")]
    public void Una_capa_interna_que_depende_de_una_externa_falla_y_senala_la_dependencia(string capa, string externa)
    {
        var violaciones = ReglasDeReferencias.CapasInternas(Con(capa, externa));

        var violacion = Assert.Single(violaciones);
        Assert.Contains(capa, violacion, StringComparison.Ordinal);
        Assert.Contains(externa, violacion, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-13")]
    public void El_dominio_con_un_paquete_falla()
    {
        var lista = Correcto();
        var indice = lista.FindIndex(p => p.Nombre == "BabelHub.Ecommerce.Domain");
        lista[indice] = lista[indice] with { Paquetes = new HashSet<string> { "Microsoft.EntityFrameworkCore" } };

        var violacion = Assert.Single(ReglasDeReferencias.CapasInternas(lista));

        Assert.Contains("Microsoft.EntityFrameworkCore", violacion, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-13")]
    [InlineData("BabelHub.Ecommerce.Api", "BabelHub.Inventario.Domain")]
    [InlineData("BabelHub.Ecommerce.Infrastructure", "BabelHub.Inventario.Application")]
    [InlineData("BabelHub.Inventario.Infrastructure", "BabelHub.Ecommerce.Infrastructure")]
    public void Un_servicio_que_referencia_las_capas_del_otro_falla_y_senala_la_dependencia(string proyecto, string ajena)
    {
        var violaciones = ReglasDeReferencias.ServiciosAjenos(Con(proyecto, ajena));

        var violacion = Assert.Single(violaciones);
        Assert.Contains(proyecto, violacion, StringComparison.Ordinal);
        Assert.Contains(ajena, violacion, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-13")]
    [InlineData("BabelHub.Ecommerce.Domain")]
    [InlineData("BabelHub.Ecommerce.Application")]
    [InlineData("BabelHub.Inventario.Domain")]
    [InlineData("BabelHub.Inventario.Application")]
    public void El_dominio_o_la_aplicacion_que_referencia_shared_falla_y_senala_la_dependencia(string proyecto)
    {
        var violaciones = ReglasDeReferencias.ContratosCompartidos(Con(proyecto, "BabelHub.Shared"));

        var violacion = Assert.Single(violaciones);
        Assert.Contains(proyecto, violacion, StringComparison.Ordinal);
        Assert.Contains("BabelHub.Shared", violacion, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-13")]
    public void Shared_que_referencia_un_proyecto_falla()
    {
        var violaciones = ReglasDeReferencias.ContratosCompartidos(Con("BabelHub.Shared", "BabelHub.Ecommerce.Domain"));

        Assert.Contains(violaciones, v => v.Contains("BabelHub.Ecommerce.Domain", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-13")]
    public void Un_servicio_que_referencia_la_herramienta_de_restauracion_falla()
    {
        var violaciones = ReglasDeReferencias.HerramientaDeRestauracion(Con("BabelHub.Inventario.Api", "RestauracionCheck"), []);

        var violacion = Assert.Single(violaciones);
        Assert.Contains("BabelHub.Inventario.Api", violacion, StringComparison.Ordinal);
        Assert.Contains("RestauracionCheck", violacion, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-13")]
    [InlineData("BabelHub.Ecommerce.Domain")]
    [InlineData("BabelHub.Inventario.Infrastructure")]
    [InlineData("BabelHub.Ecommerce.Api")]
    public void La_herramienta_de_restauracion_que_referencia_un_proyecto_de_un_servicio_falla(string referencia)
    {
        var herramientas = new List<Proyecto> { P("RestauracionCheck", [referencia]) };

        var violacion = Assert.Single(ReglasDeReferencias.HerramientaDeRestauracion(Correcto(), herramientas));

        Assert.Contains(referencia, violacion, StringComparison.Ordinal);
    }

    [Fact]
    public void La_herramienta_de_restauracion_sin_referencias_a_servicios_pasa()
    {
        var herramientas = new List<Proyecto> { P("RestauracionCheck", ["Otra.Biblioteca"]), P("CoverageCheck", ["BabelHub.Ecommerce.Domain"]) };

        Assert.Empty(ReglasDeReferencias.HerramientaDeRestauracion(Correcto(), herramientas));
    }
}
