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

    /// <summary>
    /// Los tres adaptadores de Identidad sobre <c>identidad.registros</c> (T27, T36): viven en la composición porque unen las utilidades de Auditoría con la unidad de
    /// trabajo de Identidad, y no pueden estar en ninguno de los dos módulos sin violar la regla 13. Cualquier otro tipo de la composición sigue sin poder usar los puertos.
    /// </summary>
    private const string AdaptadoresDeIdentidad = "^Postgres(Almacen|Fuente|Depurador)DeRegistrosAislados$";

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
            .And().DoNotHaveNameMatching(capa == "Infrastructure" ? AdaptadoresDeIdentidad : "^$")
            .ShouldNot().HaveDependencyOnAny(prohibidos)
            .GetResult();

        Assert.True(
            resultado.IsSuccessful,
            $"Un módulo que no es Auditoría ni Identidad depende de un puerto de registros aislados: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    [Fact]
    public void Los_adaptadores_de_Identidad_exentados_viven_solo_en_la_composicion()
    {
        var resultado = Types.InAssembly(Cargar("BabelHub.Ecommerce.Infrastructure"))
            .That().HaveNameMatching(AdaptadoresDeIdentidad)
            .Should().ResideInNamespace("BabelHub.Ecommerce.Infrastructure.Composicion")
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"Un adaptador exentado vive fuera de la composición: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
        Assert.Equal(3, Types.InAssembly(Cargar("BabelHub.Ecommerce.Infrastructure")).That().HaveNameMatching(AdaptadoresDeIdentidad).GetTypes().Count());
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
