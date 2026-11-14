namespace BabelHub.ArchitectureTests;

/// <summary>
/// Hace cumplir las reglas 1, 2, 5 y 6 de AGENTS.md sobre las referencias entre proyectos y la separación de la herramienta de
/// restauración (PLA-13). Hay dos servicios, cada uno con cuatro capas: BabelHub.&lt;Servicio&gt;.&lt;Capa&gt;. Los módulos son carpetas
/// dentro de las capas y no son proyectos aparte (regla 13: ver <see cref="ReglasDeModulosTests"/>). Las reglas viven en
/// <see cref="ReglasDeReferencias"/> y se prueban a sí mismas en <see cref="ReglasDeReferenciasSensibilidadTests"/>.
/// </summary>
public class ReglasDeReferenciasTests
{
    private const string Shared = ReglasDeReferencias.Shared;

    public static TheoryData<string> NombresDeServicios => new(ReglasDeReferencias.Servicios);

    [Fact]
    public void Existen_todos_los_proyectos_esperados()
    {
        var nombres = Proyectos.DeSrc.Select(p => p.Nombre).ToHashSet();
        var esperados = new List<string> { Shared };
        foreach (var s in ReglasDeReferencias.Servicios)
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
            .Where(n => n != Shared && !ReglasDeReferencias.Servicios.Any(s => n.StartsWith(s + ".", StringComparison.Ordinal)))
            .ToList();
        Assert.True(ajenos.Count == 0, $"Hay proyectos fuera de los dos servicios: {string.Join(", ", ajenos)}.");
    }

    [Theory]
    [MemberData(nameof(NombresDeServicios))]
    public void La_aplicacion_referencia_al_dominio(string servicio)
    {
        Assert.Contains($"{servicio}.Domain", Obtener($"{servicio}.Application").ProyectosReferenciados);
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-13")]
    public void Ninguna_capa_interna_depende_de_una_externa_y_dominio_y_aplicacion_no_usan_paquetes()
    {
        var violaciones = ReglasDeReferencias.CapasInternas(Proyectos.DeSrc);
        Assert.True(violaciones.Count == 0, string.Join("\n", violaciones));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-13")]
    public void Ningun_servicio_referencia_a_otro_servicio()
    {
        var violaciones = ReglasDeReferencias.ServiciosAjenos(Proyectos.DeSrc);
        Assert.True(violaciones.Count == 0, string.Join("\n", violaciones));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-13")]
    public void Shared_solo_son_contratos_y_dominio_y_aplicacion_no_lo_referencian()
    {
        var violaciones = ReglasDeReferencias.ContratosCompartidos(Proyectos.DeSrc);
        Assert.True(violaciones.Count == 0, string.Join("\n", violaciones));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-13")]
    public void La_herramienta_de_restauracion_esta_fuera_de_los_servicios()
    {
        var violaciones = ReglasDeReferencias.HerramientaDeRestauracion(Proyectos.DeSrc, Proyectos.DeTools);
        Assert.True(violaciones.Count == 0, string.Join("\n", violaciones));
    }

    private static Proyecto Obtener(string nombre) =>
        Proyectos.DeSrc.Single(p => p.Nombre == nombre);
}
