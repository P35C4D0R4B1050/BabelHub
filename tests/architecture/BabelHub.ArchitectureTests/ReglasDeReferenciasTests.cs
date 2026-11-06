namespace BabelHub.ArchitectureTests;

/// <summary>
/// Hace cumplir las reglas 1, 2, 5 y 6 de AGENTS.md sobre las referencias entre proyectos.
/// Hay dos servicios, cada uno con cuatro capas: BabelHub.&lt;Servicio&gt;.&lt;Capa&gt;. Los módulos son carpetas
/// dentro de las capas y no son proyectos aparte (regla 13: ver <see cref="ReglasDeModulosTests"/>).
/// </summary>
public class ReglasDeReferenciasTests
{
    private const string Shared = "BabelHub.Shared";

    private static readonly string[] Servicios = ["BabelHub.Inventario", "BabelHub.Ecommerce"];

    public static TheoryData<string> NombresDeServicios => new(Servicios);

    [Fact]
    public void Existen_todos_los_proyectos_esperados()
    {
        var nombres = Proyectos.DeSrc.Select(p => p.Nombre).ToHashSet();
        var esperados = new List<string> { Shared };
        foreach (var s in Servicios)
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

    [Fact]
    public void Solo_existen_los_dos_servicios_y_shared()
    {
        // Identidad, Asistente y la interfaz web son módulos del E-commerce, no proyectos aparte (AD02, AD15).
        var ajenos = Proyectos.DeSrc
            .Select(p => p.Nombre)
            .Where(n => n != Shared && !Servicios.Any(s => n.StartsWith(s + ".", StringComparison.Ordinal)))
            .ToList();
        Assert.True(ajenos.Count == 0, $"Hay proyectos fuera de los dos servicios: {string.Join(", ", ajenos)}.");
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void El_dominio_no_referencia_ningun_proyecto_ni_paquete(string servicio)
    {
        var dominio = Obtener($"{servicio}.Domain");
        Assert.Empty(dominio.ProyectosReferenciados);
        Assert.Empty(dominio.Paquetes);
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void La_aplicacion_solo_referencia_al_dominio(string servicio)
    {
        var aplicacion = Obtener($"{servicio}.Application");
        Assert.Equal([$"{servicio}.Domain"], aplicacion.ProyectosReferenciados.ToArray());
        Assert.Empty(aplicacion.Paquetes);
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
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
    [MemberData(nameof(NombresDeServicios))]
    public void La_presentacion_solo_referencia_su_servicio_y_shared(string servicio)
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

    private static Proyecto Obtener(string nombre) =>
        Proyectos.DeSrc.Single(p => p.Nombre == nombre);

    /// <summary>BabelHub.Inventario.Domain → BabelHub.Inventario.</summary>
    private static string ServicioDe(string proyecto)
    {
        var partes = proyecto.Split('.');
        return partes.Length >= 2 ? $"{partes[0]}.{partes[1]}" : proyecto;
    }
}
