using System.Reflection;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El contrato público del módulo <c>Pagos</c> (la raíz de su espacio de nombres en la Aplicación; <c>ecommerce-pagos</c> T07) lo usan la web, Pedidos, Devoluciones e Identidad (regla 13): sus modelos son propios,
/// así que no expone tipos del Dominio, de la Infraestructura, de <c>Shared</c> ni de su <c>Interno</c>, y solo tiene interfaces, enumeraciones y modelos inmutables. Las interfaces de la pasarela, de las reservas y de
/// los repositorios, que usan tipos del Dominio, viven en el interior. La Presentación no referencia <c>IProgresoDelCheckout</c> (lo comprueba la prueba de Carrito).
/// </summary>
public class ContratoPublicoDePagosTests
{
    private const string Raiz = "BabelHub.Ecommerce.Application.Pagos";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Ecommerce.Domain",
        "BabelHub.Ecommerce.Infrastructure",
        "BabelHub.Shared",
        "BabelHub.Ecommerce.Application.Pagos.Interno",
    ];

    private static readonly string[] Esperados =
    [
        "IPagos",
        "IReembolsos",
        "ILiberacionDeContactoDePago",
        "IAvisosDeLaPasarela",
        "IConfirmacionDeCompra",
        "CompraAprobada",
        "ContactoDeLaCompra",
        "TokenDelMedioDePago",
    ];

    private static Assembly Aplicacion => typeof(IPagos).Assembly;

    private static IEnumerable<Type> TiposDelContrato() => Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == Raiz);

    [Fact]
    public void El_contrato_publico_de_Pagos_declara_las_interfaces_y_modelos_del_plan()
    {
        var nombres = TiposDelContrato().Select(t => t.Name).ToHashSet();

        foreach (var esperado in Esperados)
        {
            Assert.Contains(esperado, nombres);
        }
    }

    [Fact]
    public void El_contrato_publico_de_Pagos_no_expone_tipos_del_dominio_de_la_infraestructura_de_Shared_ni_de_su_interior()
    {
        var violaciones = ContratoPublicoDeFuentesTests.Violaciones(TiposDelContrato(), Prohibidos);

        Assert.True(violaciones.Count == 0, $"El contrato de Pagos expone tipos que los otros módulos no pueden usar: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void El_contrato_publico_de_Pagos_solo_tiene_interfaces_enumeraciones_y_modelos_inmutables()
    {
        var mutables = TiposDelContrato()
            .Where(t => !t.IsInterface && !t.IsEnum)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true } set && !set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.True(mutables.Count == 0, $"Estas propiedades del contrato son mutables: {string.Join(", ", mutables)}.");
    }

    [Fact]
    public void Los_puertos_con_tipos_del_dominio_viven_en_el_interior_del_modulo_y_no_en_la_raiz()
    {
        var interno = Aplicacion.GetTypes().Where(t => t.Namespace == $"{Raiz}.Interno").Select(t => t.Name).ToHashSet();

        foreach (var puerto in new[] { "IPasarelaDePago", "IReservasDelPago", "IRepositorioDePagos", "IRepositorioDeReembolsos", "ICuentaDelPagador" })
        {
            Assert.Contains(puerto, interno);
            Assert.DoesNotContain(puerto, TiposDelContrato().Select(t => t.Name));
        }
    }

    [Fact]
    public void La_confirmacion_de_compra_usa_los_modelos_publicos_de_Carrito_y_no_los_del_dominio_de_Carrito()
    {
        var expuestos = typeof(CompraAprobada).GetProperties().Select(p => p.PropertyType).Concat(typeof(CompraAprobada).GetConstructors().SelectMany(c => c.GetParameters().Select(p => p.ParameterType))).ToArray();

        Assert.Contains(typeof(TipoDeEntregaDeCompra), expuestos);
        Assert.DoesNotContain(expuestos, t => t.Namespace?.StartsWith("BabelHub.Ecommerce.Domain", StringComparison.Ordinal) == true);
    }
}
