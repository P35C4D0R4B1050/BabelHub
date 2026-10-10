namespace BabelHub.ArchitectureTests;

/// <summary>
/// Hace cumplir las reglas 1, 2, 5, 6 y 9 de AGENTS.md sobre las referencias entre proyectos.
/// Un servicio se identifica por su nombre: BabelHub.&lt;Servicio&gt;.&lt;Capa&gt; o BabelHub.&lt;Servicio&gt;.
/// </summary>
public class ReglasDeReferenciasTests
{
    private const string Shared = "BabelHub.Shared";

    private static readonly string[] ServiciosCompletos = ["BabelHub.Inventario", "BabelHub.Ecommerce"];

    public static TheoryData<string> Servicios => new(ServiciosCompletos);

    [Fact]
    public void Existen_todos_los_proyectos_esperados()
    {
        var nombres = Proyectos.DeSrc.Select(p => p.Nombre).ToHashSet();
        var esperados = new List<string> { Shared, "BabelHub.Web", "BabelHub.Identidad", "BabelHub.AsistenteCompra" };
        foreach (var s in ServiciosCompletos)
        {
            foreach (var capa in new[] { "Domain", "Application", "Infrastructure", "Api" })
            {
                esperados.Add($"{s}.{capa}");
            }
        }

        foreach (var esperado in esperados)
        {
            Assert.Contains(esperado, nombres);
        }
    }

    [Theory]
    [MemberData(nameof(Servicios))]
    public void El_dominio_no_referencia_ningun_proyecto_ni_paquete(string servicio)
    {
        var dominio = Obtener($"{servicio}.Domain");
        Assert.Empty(dominio.ProyectosReferenciados);
        Assert.Empty(dominio.Paquetes);
    }

    [Theory]
    [MemberData(nameof(Servicios))]
    public void La_aplicacion_solo_referencia_al_dominio(string servicio)
    {
        var aplicacion = Obtener($"{servicio}.Application");
        Assert.Equal([$"{servicio}.Domain"], aplicacion.ProyectosReferenciados.ToArray());
        Assert.Empty(aplicacion.Paquetes);
    }

    [Theory]
    [MemberData(nameof(Servicios))]
    public void La_infraestructura_solo_referencia_aplicacion_dominio_y_shared(string servicio)
    {
        var permitidas = new HashSet<string> { $"{servicio}.Application", $"{servicio}.Domain", Shared };
        var infra = Obtener($"{servicio}.Infrastructure");
        foreach (var r in infra.ProyectosReferenciados)
        {
            Assert.Contains(r, permitidas);
        }
    }

    [Theory]
    [MemberData(nameof(Servicios))]
    public void La_api_solo_referencia_su_servicio_y_shared(string servicio)
    {
        var permitidas = new HashSet<string> { $"{servicio}.Application", $"{servicio}.Domain", $"{servicio}.Infrastructure", Shared };
        var api = Obtener($"{servicio}.Api");
        foreach (var r in api.ProyectosReferenciados)
        {
            Assert.Contains(r, permitidas);
        }
    }

    [Fact]
    public void Ningun_servicio_referencia_a_otro_servicio()
    {
        foreach (var proyecto in Proyectos.DeSrc)
        {
            var propio = ServicioDe(proyecto.Nombre);
            var ajenos = proyecto.ProyectosReferenciados
                .Where(r => r != Shared && ServicioDe(r) != propio)
                .ToList();
            Assert.True(ajenos.Count == 0, $"{proyecto.Nombre} referencia proyectos de otro servicio: {string.Join(", ", ajenos)}.");
        }
    }

    [Fact]
    public void Shared_no_referencia_nada()
    {
        Assert.Empty(Obtener(Shared).ProyectosReferenciados);
    }

    [Fact]
    public void Dominio_y_aplicacion_no_referencian_shared()
    {
        foreach (var p in Proyectos.DeSrc.Where(p => p.Nombre.EndsWith(".Domain", StringComparison.Ordinal) || p.Nombre.EndsWith(".Application", StringComparison.Ordinal)))
        {
            Assert.DoesNotContain(Shared, p.ProyectosReferenciados);
        }
    }

    [Fact]
    public void La_web_solo_referencia_shared()
    {
        Assert.Equal([Shared], Obtener("BabelHub.Web").ProyectosReferenciados.ToArray());
    }

    [Theory]
    [InlineData("BabelHub.Identidad")]
    [InlineData("BabelHub.AsistenteCompra")]
    public void Los_servicios_reducidos_solo_referencian_shared(string servicio)
    {
        Assert.Equal([Shared], Obtener(servicio).ProyectosReferenciados.ToArray());
    }

    private static Proyecto Obtener(string nombre) =>
        Proyectos.DeSrc.Single(p => p.Nombre == nombre);

    /// <summary>BabelHub.Inventario.Domain → BabelHub.Inventario; BabelHub.Web → BabelHub.Web.</summary>
    private static string ServicioDe(string proyecto)
    {
        var partes = proyecto.Split('.');
        return partes.Length >= 2 ? $"{partes[0]}.{partes[1]}" : proyecto;
    }
}
