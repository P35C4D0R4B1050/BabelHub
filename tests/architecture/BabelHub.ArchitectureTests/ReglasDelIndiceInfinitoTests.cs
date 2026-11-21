using System.Reflection;
using BabelHub.Ecommerce.Application.Web;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El índice infinito (web-indice-infinito T14; AC-IND-05, IND-06, IND-08; spec §5): vive en el módulo <c>Web</c> como una función pura. Sus tipos (<c>Domain.Web.Indice</c> y <c>Application.Web.Indice</c>) <b>no referencian persistencia</b> (Npgsql, Redis, EF Core,
/// <c>IUnidadDeTrabajo</c>), <c>Application.Plataforma</c> ni ningún módulo de negocio (Catálogo, Carrito, Pedidos, Notificaciones y los demás: nada de lo generado puede aparecer en el buscador, el carrito ni los avisos), salvo el puerto de bytes aleatorios
/// <c>IAleatoriosSeguros</c>; y el contrato público de <c>Web</c> para el índice no expone tipos del Dominio, de la Infraestructura, de <c>Shared</c> ni del <c>Interno</c>.
/// </summary>
public class ReglasDelIndiceInfinitoTests
{
    private const string DominioDelIndice = "BabelHub.Ecommerce.Domain.Web.Indice";
    private const string AplicacionDelIndice = "BabelHub.Ecommerce.Application.Web.Indice";

    private static readonly string[] Persistencia =
    [
        "Npgsql",
        "StackExchange.Redis",
        "Microsoft.EntityFrameworkCore",
        "System.Data",
        "BabelHub.Ecommerce.Application.Plataforma",
        "BabelHub.Ecommerce.Infrastructure",
    ];

    private static readonly string[] ModulosDeNegocio =
    [
        "Catalogo", "Carrito", "Pagos", "Pedidos", "Envios", "Devoluciones", "Biblioteca", "Reclamaciones", "Identidad", "Asistente", "Auditoria", "Notificaciones",
    ];

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Ecommerce.Domain",
        "BabelHub.Ecommerce.Infrastructure",
        "BabelHub.Shared",
        "BabelHub.Ecommerce.Application.Web.Interno",
        "BabelHub.Ecommerce.Application.Web.Indice",
    ];

    private static Assembly Aplicacion => typeof(IIndiceInfinito).Assembly;

    private static Assembly Dominio => typeof(BabelHub.Ecommerce.Domain.Web.Indice.DireccionDelIndice).Assembly;

    private static IEnumerable<string> DependenciasProhibidasDeLosModulos(string capa) =>
        ModulosDeNegocio.SelectMany(m => new[] { $"BabelHub.Ecommerce.{capa}.{m}" });

    [Fact]
    public void Los_tipos_del_Dominio_del_indice_no_referencian_persistencia_la_aplicacion_ni_otros_modulos()
    {
        var resultado = Types.InAssembly(Dominio).That().ResideInNamespaceStartingWith(DominioDelIndice)
            .ShouldNot().HaveDependencyOnAny([.. Persistencia, "BabelHub.Ecommerce.Application", .. DependenciasProhibidasDeLosModulos("Domain")])
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"El Dominio del índice depende de algo que no debe: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    [Fact]
    public void El_dominio_del_indice_existe_y_tiene_los_tipos_del_plan()
    {
        var nombres = Types.InAssembly(Dominio).That().ResideInNamespace(DominioDelIndice).GetTypes().Select(t => t.Name).ToHashSet();

        Assert.Contains("DireccionDelIndice", nombres);
        Assert.Contains("DireccionAlAzar", nombres);
        Assert.Contains("GeneradorDePaginasDelIndice", nombres);
    }

    [Fact]
    public void Los_tipos_de_la_Aplicacion_del_indice_no_referencian_persistencia_ni_otros_modulos_ni_Plataforma()
    {
        var tipos = Types.InAssembly(Aplicacion).That().ResideInNamespaceStartingWith(AplicacionDelIndice);
        Assert.NotEmpty(tipos.GetTypes());

        var resultado = tipos
            .ShouldNot().HaveDependencyOnAny([.. Persistencia, "BabelHub.Ecommerce.Infrastructure", .. DependenciasProhibidasDeLosModulos("Application")])
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"La Aplicación del índice depende de algo que no debe: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    [Fact]
    public void La_unica_dependencia_del_caso_de_uso_del_indice_en_el_interior_de_Web_es_el_puerto_de_bytes_aleatorios()
    {
        var caso = Aplicacion.GetTypes().Single(t => t.Namespace == AplicacionDelIndice && t.Name == "ConsultarIndiceInfinito");

        var parametros = caso.GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToList();
        var campos = caso.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Select(f => f.FieldType.Name).ToList();

        Assert.Equal(["IAleatoriosSeguros"], parametros);
        Assert.DoesNotContain(campos, c => c is "IUnidadDeTrabajo" or "IConexionDeLaUnidadDeTrabajo");
    }

    [Fact]
    public void El_contrato_publico_del_indice_no_expone_tipos_del_dominio_de_la_infraestructura_de_Shared_ni_del_interior()
    {
        var tipos = ContratoDelIndice().ToList();

        var violaciones = ContratoPublicoDeFuentesTests.Violaciones(tipos, Prohibidos);

        Assert.True(violaciones.Count == 0, $"El contrato del índice expone tipos que los otros módulos no pueden usar: {string.Join("; ", violaciones)}.");
        Assert.Contains(tipos, t => t == typeof(IIndiceInfinito));
    }

    [Fact]
    public void El_contrato_publico_del_indice_solo_tiene_interfaces_y_modelos_inmutables()
    {
        var mutables = ContratoDelIndice()
            .Where(t => !t.IsInterface && !t.IsEnum)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => p.SetMethod is { IsPublic: true } set && !set.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.Name == "IsExternalInit"))
                .Select(p => $"{t.FullName}.{p.Name}"))
            .ToList();

        Assert.True(mutables.Count == 0, $"Estas propiedades del contrato son mutables: {string.Join(", ", mutables)}.");
    }

    [Fact]
    public void La_Presentacion_usa_el_contrato_del_indice_y_nunca_su_caso_de_uso_ni_el_Dominio()
    {
        var api = typeof(BabelHub.Ecommerce.Api.AssemblyMarker).Assembly;

        var resultado = Types.InAssembly(api).That().HaveNameStartingWith("IndiceInfinito").Or().HaveNameStartingWith("CabecerasDelIndiceInfinito")
            .ShouldNot().HaveDependencyOnAny("BabelHub.Ecommerce.Domain", AplicacionDelIndice, "BabelHub.Ecommerce.Infrastructure", "BabelHub.Ecommerce.Application.Web.Interno")
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"La Presentación del índice depende de algo que no debe: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    [Fact]
    public void Ningun_otro_modulo_depende_del_indice()
    {
        foreach (var capa in new[] { "Domain", "Application", "Infrastructure" })
        {
            var ensamblado = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == $"BabelHub.Ecommerce.{capa}") ?? Assembly.Load($"BabelHub.Ecommerce.{capa}");
            foreach (var modulo in ModulosDeNegocio)
            {
                var resultado = Types.InAssembly(ensamblado).That().ResideInNamespaceStartingWith($"BabelHub.Ecommerce.{capa}.{modulo}")
                    .ShouldNot().HaveDependencyOnAny(DominioDelIndice, AplicacionDelIndice, "BabelHub.Ecommerce.Application.Web.IIndiceInfinito")
                    .GetResult();

                Assert.True(resultado.IsSuccessful, $"El módulo {modulo} ({capa}) depende del índice infinito: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
            }
        }
    }

    /// <summary>El contrato público del índice: la interfaz y sus modelos (con los modelos anidados), todos en la raíz del módulo <c>Web</c>.</summary>
    private static IEnumerable<Type> ContratoDelIndice()
    {
        string[] nombres = [nameof(IIndiceInfinito), nameof(PaginaDelIndice), nameof(ResultadoDelIndice)];
        foreach (var tipo in Aplicacion.GetTypes().Where(t => t.IsPublic && t.Namespace == "BabelHub.Ecommerce.Application.Web" && nombres.Contains(t.Name)))
        {
            yield return tipo;
            foreach (var anidado in tipo.GetNestedTypes(BindingFlags.Public))
            {
                yield return anidado;
            }
        }
    }
}
