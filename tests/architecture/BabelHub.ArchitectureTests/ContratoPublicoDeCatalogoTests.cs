using System.Reflection;
using BabelHub.Ecommerce.Application.Catalogo;
using Microsoft.Extensions.DependencyInjection;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El contrato público del módulo <c>Catalogo</c> (la raíz de su espacio de nombres en la Aplicación; <c>ecommerce-catalogo</c> T05) lo usan la web, Carrito, Pedidos, Biblioteca y el Asistente (regla 13):
/// sus modelos son propios, así que no expone tipos del Dominio, de la Infraestructura, de <c>Shared</c> ni de su <c>Interno</c>, y solo tiene interfaces, enumeraciones y modelos inmutables.
/// Además, <c>IReferenciaDeArchivosDelCatalogo</c> (la clave del archivo de una digital) la usa solo <c>Biblioteca</c> (D13, D17), y mientras <c>Carrito</c> no exista el retiro de carritos es nulo
/// (D07): en cuanto aparezca una implementación real, la prueba falla hasta quitar el nulo de la composición.
/// </summary>
public class ContratoPublicoDeCatalogoTests
{
    private const string Raiz = "BabelHub.Ecommerce.Application.Catalogo";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Ecommerce.Domain",
        "BabelHub.Ecommerce.Infrastructure",
        "BabelHub.Shared",
        "BabelHub.Ecommerce.Application.Catalogo.Interno",
    ];

    private static readonly string[] Esperados =
    [
        "IConsultaDeCatalogo",
        "IDisponibilidadDeEdiciones",
        "IReferenciaDeArchivosDelCatalogo",
        "IRetiroDeEdicionDeCarritos",
        "IAdministracionDelCatalogo",
        "IArchivosDelCatalogo",
        "IIngresosDeMercaderia",
    ];

    private static Assembly Aplicacion => typeof(IConsultaDeCatalogo).Assembly;

    private static IEnumerable<Type> TiposDelContrato() => Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == Raiz);

    [Fact]
    public void El_contrato_publico_de_Catalogo_declara_las_interfaces_del_plan()
    {
        var nombres = TiposDelContrato().Select(t => t.Name).ToHashSet();

        foreach (var esperado in Esperados)
        {
            Assert.Contains(esperado, nombres);
        }
    }

    [Fact]
    public void El_contrato_publico_de_Catalogo_no_expone_tipos_del_dominio_de_la_infraestructura_de_Shared_ni_de_su_interior()
    {
        var violaciones = ContratoPublicoDeFuentesTests.Violaciones(TiposDelContrato(), Prohibidos);

        Assert.True(violaciones.Count == 0, $"El contrato de Catalogo expone tipos que los otros módulos no pueden usar: {string.Join("; ", violaciones)}.");
    }

    [Fact]
    public void El_contrato_publico_de_Catalogo_solo_tiene_interfaces_enumeraciones_y_modelos_inmutables()
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
    public void Solo_Biblioteca_y_Catalogo_dependen_de_la_referencia_de_archivos_del_catalogo()
    {
        foreach (var capa in new[] { "Application", "Infrastructure", "Api" })
        {
            var ensamblado = Cargar($"BabelHub.Ecommerce.{capa}");

            // La web (Api) no tiene exenciones: la clave del archivo de una digital nunca llega a la interfaz (D13, D17), tampoco a una vista o controlador de Biblioteca o Catálogo.
            var tipos = capa == "Api"
                ? Types.InAssembly(ensamblado).That().ResideInNamespaceStartingWith($"BabelHub.Ecommerce.{capa}")
                : Types.InAssembly(ensamblado).That().DoNotResideInNamespaceStartingWith($"BabelHub.Ecommerce.{capa}.Biblioteca").And().DoNotResideInNamespaceStartingWith($"BabelHub.Ecommerce.{capa}.Catalogo");

            var resultado = tipos
                .ShouldNot().HaveDependencyOn($"{Raiz}.{nameof(IReferenciaDeArchivosDelCatalogo)}")
                .GetResult();

            Assert.True(
                resultado.IsSuccessful,
                $"Un módulo que no es Biblioteca ni Catálogo depende de IReferenciaDeArchivosDelCatalogo (la clave del archivo de una digital): {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-15")]
    public void Mientras_Carrito_no_exista_el_retiro_de_carritos_es_el_nulo_y_la_composicion_lo_registra_sin_pisar_a_otro()
    {
        var infraestructura = Cargar("BabelHub.Ecommerce.Infrastructure");
        var implementaciones = infraestructura.GetTypes()
            .Concat(Aplicacion.GetTypes())
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IRetiroDeEdicionDeCarritos).IsAssignableFrom(t))
            .ToList();

        Assert.True(
            implementaciones.Count == 1 && implementaciones[0].Name == "RetiroDeEdicionDeCarritosNulo",
            "Ya existe una implementación real de IRetiroDeEdicionDeCarritos (de Carrito): quita AgregarRetiroNuloDeCarritos de la composición, borra RetiroDeEdicionDeCarritosNulo y actualiza esta prueba. "
            + $"Implementaciones: {string.Join(", ", implementaciones.Select(i => i.FullName))}.");

        var servicios = new ServiceCollection();
        Infraestructura.AgregarRetiroNuloDeCarritos(servicios);
        using var proveedor = servicios.BuildServiceProvider();
        Assert.Equal("RetiroDeEdicionDeCarritosNulo", proveedor.GetRequiredService<IRetiroDeEdicionDeCarritos>().GetType().Name);

        var conReal = new ServiceCollection();
        conReal.AddSingleton<IRetiroDeEdicionDeCarritos, RetiroReal>();
        Infraestructura.AgregarRetiroNuloDeCarritos(conReal);
        using var proveedorConReal = conReal.BuildServiceProvider();
        Assert.IsType<RetiroReal>(proveedorConReal.GetRequiredService<IRetiroDeEdicionDeCarritos>());
    }

    [Fact]
    public void La_regla_de_la_referencia_de_archivos_detecta_a_un_modulo_ajeno_que_la_usa()
    {
        var resultado = Types.InAssembly(typeof(ContratoPublicoDeCatalogoTests).Assembly)
            .That().HaveName(nameof(ModuloAjenoQueUsaLaReferencia))
            .ShouldNot().HaveDependencyOn($"{Raiz}.{nameof(IReferenciaDeArchivosDelCatalogo)}")
            .GetResult();

        Assert.False(resultado.IsSuccessful);
    }

    private sealed class ModuloAjenoQueUsaLaReferencia(IReferenciaDeArchivosDelCatalogo referencia)
    {
        public IReferenciaDeArchivosDelCatalogo Referencia { get; } = referencia;
    }

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre)
        ?? Assembly.Load(nombre);

    private static class Infraestructura
    {
        public static IServiceCollection AgregarRetiroNuloDeCarritos(IServiceCollection servicios) =>
            BabelHub.Ecommerce.Infrastructure.Catalogo.CatalogoPendienteExtensiones.AgregarRetiroNuloDeCarritos(servicios);
    }

    private sealed class RetiroReal : IRetiroDeEdicionDeCarritos
    {
        public Task Retirar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => Task.CompletedTask;
    }
}
