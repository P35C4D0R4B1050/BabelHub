using System.Reflection;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Quién puede fijar el actor del contexto (AGENTS.md §3.4 y §5, SEG-15, AUD-23): <c>IEstablecedorDeActor</c> es un puerto de la Aplicación de <c>Plataforma</c> para que los trabajos de fondo fijen «sistema»
/// sin depender de la Infraestructura de otro módulo, pero <b>ningún caso de uso ni el Dominio</b> lo usa (podría elegir el actor que quiera): solo la Infraestructura de <c>Plataforma</c> y de <c>Auditoria</c> (sus trabajos
/// de fondo) y la Api (que lo fija desde la credencial verificada).
/// </summary>
public class ReglasDelActorTests
{
    public static TheoryData<string> Servicios => new("BabelHub.Ecommerce", "BabelHub.Inventario");

    [Theory]
    [MemberData(nameof(Servicios))]
    public void Solo_la_infraestructura_de_plataforma_y_auditoria_y_la_api_fijan_el_actor(string servicio)
    {
        var puerto = $"{servicio}.Application.Plataforma.IEstablecedorDeActor";

        foreach (var capa in new[] { "Domain", "Application" })
        {
            var tipos = Types.InAssembly(Cargar($"{servicio}.{capa}")).That().HaveDependencyOn(puerto).GetTypes()
                .Where(t => t.FullName != puerto)
                .Select(t => t.FullName)
                .ToList();

            Assert.True(tipos.Count == 0, $"{servicio}.{capa} no debe fijar el actor: {string.Join(", ", tipos)}.");
        }

        var infraestructura = Types.InAssembly(Cargar($"{servicio}.Infrastructure")).That().HaveDependencyOn(puerto).GetTypes()
            .Select(t => t.FullName ?? string.Empty)
            .Where(nombre => !nombre.StartsWith($"{servicio}.Infrastructure.Plataforma.", StringComparison.Ordinal) && !nombre.StartsWith($"{servicio}.Infrastructure.Auditoria.", StringComparison.Ordinal) && !nombre.StartsWith($"{servicio}.Infrastructure.Composicion.", StringComparison.Ordinal))
            .ToList();

        Assert.True(infraestructura.Count == 0, $"Solo la Infraestructura de Plataforma y de Auditoría (y la raíz de composición, que registra al ejecutor de trabajos de fondo) fijan el actor: {string.Join(", ", infraestructura)}.");
    }

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre)
        ?? Assembly.Load(nombre);
}
