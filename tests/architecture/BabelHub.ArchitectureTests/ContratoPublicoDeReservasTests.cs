using System.Reflection;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El contrato público del módulo <c>Reservas</c> (la raíz de su espacio de nombres en la Aplicación) lo usan la Api y <c>Traslados</c>: no puede exponer el Dominio, la Infraestructura, <c>Shared</c> ni su
/// propio <c>Interno</c> (regla 13), y solo tiene interfaces, enumeraciones y modelos inmutables.
/// </summary>
public class ContratoPublicoDeReservasTests
{
    private const string Raiz = "BabelHub.Inventario.Application.Reservas";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Inventario.Domain",
        "BabelHub.Inventario.Infrastructure",
        "BabelHub.Shared",
        "BabelHub.Inventario.Application.Reservas.Interno",
    ];

    private static Assembly Aplicacion =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "BabelHub.Inventario.Application") ?? Assembly.Load("BabelHub.Inventario.Application");

    private static IEnumerable<Type> TiposDelContrato() => Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == Raiz);

    [Fact]
    public void El_contrato_publico_de_Reservas_declara_los_puertos_del_plan()
    {
        var nombres = TiposDelContrato().Select(t => t.Name).ToHashSet();

        foreach (var esperado in new[] { "IGeneradorDeTrasladosDePedido", "IAlLiberarPedido", "IGeneradorDeIdentificadorAnonimo", "IReservasParaTraslados", "ReservarEntrada", "ReservaDelPedido" })
        {
            Assert.Contains(esperado, nombres);
        }
    }

    [Fact]
    public void El_contrato_publico_de_Reservas_no_expone_el_dominio_la_infraestructura_Shared_ni_su_interior()
    {
        var violaciones = ContratoPublicoDeFuentesTests.Violaciones(TiposDelContrato(), Prohibidos);

        Assert.True(violaciones.Count == 0, $"El contrato de Reservas expone tipos que los otros módulos no pueden usar: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void El_contrato_publico_de_Reservas_solo_tiene_interfaces_enumeraciones_y_modelos_inmutables()
    {
        var mutables = TiposDelContrato()
            .Where(t => !t.IsInterface && !t.IsEnum)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.SetMethod is { IsPublic: true } set && !set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.True(mutables.Count == 0, $"Hay modelos con propiedades mutables: {string.Join(", ", mutables)}.");
    }
}
