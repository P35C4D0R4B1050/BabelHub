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
            .Where(nombre => !nombre.StartsWith($"{servicio}.Infrastructure.Plataforma.", StringComparison.Ordinal) && !nombre.StartsWith($"{servicio}.Infrastructure.Auditoria.", StringComparison.Ordinal))
            .ToList();

        Assert.True(infraestructura.Count == 0, $"Solo la Infraestructura de Plataforma y de Auditoría fijan el actor: {string.Join(", ", infraestructura)}.");
    }

    /// <summary>
    /// <c>IEjecutorComoSistema</c> pone «sistema» como actor mientras dura un trabajo dentro del flujo de otro actor (la liberación de vencidas se audita como sistema aunque la transacción sea de un cliente). Es un puerto
    /// estrecho a propósito: dentro del bloque una autorización vería a «sistema» y concedería, así que <b>solo la lista blanca</b> lo usa y ampliarla es una decisión explícita (aquí). Hoy: el expirador de <c>Reservas</c>
    /// y quien lo compone. Solo la Infraestructura de <c>Plataforma</c> lo implementa.
    /// </summary>
    [Fact]
    public void Solo_la_lista_blanca_depende_de_ejecutar_como_sistema_y_solo_plataforma_lo_implementa()
    {
        const string servicio = "BabelHub.Inventario";
        const string puerto = $"{servicio}.Application.Plataforma.IEjecutorComoSistema";
        string[] permitidos =
        [
            $"{servicio}.Application.Reservas.Interno.ExpiradorDeReservas",
            $"{servicio}.Application.Reservas.Interno.ReservasDeStock",
        ];

        var dependientes = new[] { "Domain", "Application" }
            .SelectMany(capa => Types.InAssembly(Cargar($"{servicio}.{capa}")).That().HaveDependencyOn(puerto).GetTypes())
            .Select(t => t.FullName ?? string.Empty)
            .Where(nombre => nombre != puerto && !permitidos.Any(p => nombre == p || nombre.StartsWith(p + "+", StringComparison.Ordinal) || nombre.StartsWith(p + "<", StringComparison.Ordinal)))
            .Where(nombre => !nombre.Contains("<>c", StringComparison.Ordinal) && !nombre.Contains("DisplayClass", StringComparison.Ordinal))
            .ToList();

        Assert.True(dependientes.Count == 0, $"Solo la lista blanca puede cambiar el actor a «sistema»; ampliarla es una decisión explícita: {string.Join(", ", dependientes)}.");

        var implementaciones = Types.InAssembly(Cargar($"{servicio}.Infrastructure")).That().ImplementInterface(Cargar($"{servicio}.Application").GetType(puerto)!).GetTypes()
            .Select(t => t.FullName ?? string.Empty)
            .Where(nombre => !nombre.StartsWith($"{servicio}.Infrastructure.Plataforma.", StringComparison.Ordinal))
            .ToList();

        Assert.True(implementaciones.Count == 0, $"Solo la Infraestructura de Plataforma implementa el ejecutor «sistema»: {string.Join(", ", implementaciones)}.");
    }

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre)
        ?? Assembly.Load(nombre);
}
