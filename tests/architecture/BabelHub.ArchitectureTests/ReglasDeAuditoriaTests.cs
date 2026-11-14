using System.Reflection;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Los puertos que Auditoría define para que Identidad los implemente escriben, leen y depuran registros fuera del catálogo y de la lista blanca del
/// módulo: solo Auditoría (que los usa) e Identidad (que los implementa) pueden depender de ellos. Otro módulo que los inyectara podría falsificar la
/// auditoría con un actor arbitrario (AUD-23, AUD-03).
/// </summary>
public class ReglasDeAuditoriaTests
{
    private static readonly string[] Puertos =
    [
        "IAlmacenDeRegistrosAislados",
        "IFuenteDeRegistrosAislados",
        "IDepuradorDeRegistrosAislados",
        "ICuentasCanceladasPendientes",
    ];

    [Theory]
    [InlineData("Application")]
    [InlineData("Infrastructure")]
    [InlineData("Api")]
    public void Solo_Auditoria_e_Identidad_dependen_de_los_puertos_de_registros_aislados(string capa)
    {
        var ensamblado = Cargar($"BabelHub.Ecommerce.{capa}");
        var prohibidos = Puertos.Select(p => $"BabelHub.Ecommerce.Application.Auditoria.{p}").ToArray();

        var resultado = Types.InAssembly(ensamblado)
            .That().DoNotResideInNamespaceStartingWith($"BabelHub.Ecommerce.{capa}.Auditoria")
            .And().DoNotResideInNamespaceStartingWith($"BabelHub.Ecommerce.{capa}.Identidad")
            .ShouldNot().HaveDependencyOnAny(prohibidos)
            .GetResult();

        Assert.True(
            resultado.IsSuccessful,
            $"Un módulo que no es Auditoría ni Identidad depende de un puerto de registros aislados: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    [Fact]
    public void El_Inventario_no_tiene_puertos_de_registros_aislados()
    {
        var ensamblado = Cargar("BabelHub.Inventario.Application");

        var nombres = ensamblado.GetTypes().Select(t => t.Name).ToHashSet();

        Assert.DoesNotContain(nombres, n => Puertos.Contains(n));
    }

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre)
        ?? Assembly.Load(nombre);
}
