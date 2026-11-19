using System.Reflection;
using BabelHub.Ecommerce.Application.Carrito;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El contrato público del módulo <c>Carrito</c> (la raíz de su espacio de nombres en la Aplicación; <c>ecommerce-carrito-checkout</c> T07) lo usan la web, Pagos, Pedidos, Biblioteca e Identidad (regla 13): sus
/// modelos son propios, así que no expone tipos del Dominio, de la Infraestructura, de <c>Shared</c> ni de su <c>Interno</c>, y solo tiene interfaces, enumeraciones y modelos inmutables. Además, <c>IProgresoDelCheckout</c>
/// (los avisos de Pagos al checkout, D13) no lo referencia la Presentación: la web no puede marcar un checkout como pagado.
/// </summary>
public class ContratoPublicoDeCarritoTests
{
    private const string Raiz = "BabelHub.Ecommerce.Application.Carrito";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Ecommerce.Domain",
        "BabelHub.Ecommerce.Infrastructure",
        "BabelHub.Shared",
        "BabelHub.Ecommerce.Application.Carrito.Interno",
    ];

    private static readonly string[] Esperados =
    [
        "ICarritoDelCliente",
        "ICheckoutDelCliente",
        "IProgresoDelCheckout",
        "ICreadorDeIntencionesDePago",
        "IPosesionDeEdiciones",
        "InstantaneaDeCompra",
        "ContactoDeCompra",
    ];

    private static Assembly Aplicacion => typeof(ICarritoDelCliente).Assembly;

    private static IEnumerable<Type> TiposDelContrato() => Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == Raiz);

    [Fact]
    public void El_contrato_publico_de_Carrito_declara_las_interfaces_y_modelos_del_plan()
    {
        var nombres = TiposDelContrato().Select(t => t.Name).ToHashSet();

        foreach (var esperado in Esperados)
        {
            Assert.Contains(esperado, nombres);
        }
    }

    [Fact]
    public void El_contrato_publico_de_Carrito_no_expone_tipos_del_dominio_de_la_infraestructura_de_Shared_ni_de_su_interior()
    {
        var violaciones = ContratoPublicoDeFuentesTests.Violaciones(TiposDelContrato(), Prohibidos);

        Assert.True(violaciones.Count == 0, $"El contrato de Carrito expone tipos que los otros módulos no pueden usar: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void El_contrato_publico_de_Carrito_solo_tiene_interfaces_enumeraciones_y_modelos_inmutables()
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
        var interno = Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == $"{Raiz}.Interno").Select(t => t.Name).ToHashSet();

        foreach (var puerto in new[] { "IInventarioDelCarrito", "IRepositorioDeCarritos", "IRepositorioDeCheckouts", "IGeneradorDeIdentificadores" })
        {
            Assert.Contains(puerto, interno);
            Assert.DoesNotContain(puerto, TiposDelContrato().Select(t => t.Name));
        }
    }

    [Fact]
    public void La_Presentacion_no_referencia_IProgresoDelCheckout()
    {
        var api = Cargar("BabelHub.Ecommerce.Api");

        var resultado = Types.InAssembly(api)
            .That().ResideInNamespaceStartingWith("BabelHub.Ecommerce.Api")
            .ShouldNot().HaveDependencyOn($"{Raiz}.{nameof(IProgresoDelCheckout)}")
            .GetResult();

        Assert.True(
            resultado.IsSuccessful,
            $"La Presentación referencia IProgresoDelCheckout (solo Pagos avisa al checkout): {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    [Fact]
    public void La_regla_de_IProgresoDelCheckout_detecta_a_una_presentacion_que_lo_usa()
    {
        var resultado = Types.InAssembly(typeof(ContratoPublicoDeCarritoTests).Assembly)
            .That().HaveName(nameof(PresentacionQueMarcaPagos))
            .ShouldNot().HaveDependencyOn($"{Raiz}.{nameof(IProgresoDelCheckout)}")
            .GetResult();

        Assert.False(resultado.IsSuccessful);
    }

    private sealed class PresentacionQueMarcaPagos(IProgresoDelCheckout progreso)
    {
        public IProgresoDelCheckout Progreso { get; } = progreso;
    }

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre)
        ?? Assembly.Load(nombre);
}
