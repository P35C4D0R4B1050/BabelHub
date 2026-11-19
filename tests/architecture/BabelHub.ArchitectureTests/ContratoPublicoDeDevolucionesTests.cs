using System.Reflection;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El contrato público del módulo <c>Devoluciones</c> (la raíz de su espacio de nombres en la Aplicación; <c>ecommerce-devoluciones</c> T04) lo usa la interfaz web y no depende del Dominio, la Infraestructura, <c>Shared</c> ni de su <c>Interno</c>.
/// Reglas de Devoluciones: un libro digital nunca consulta al Inventario (regla 12) y el módulo no usa el token del usuario de la sesión (regla 9, D15).
/// </summary>
public class ContratoPublicoDeDevolucionesTests
{
    private const string Raiz = "BabelHub.Ecommerce.Application.Devoluciones";

    private static readonly string[] Prohibidos =
    [
        "BabelHub.Ecommerce.Domain",
        "BabelHub.Ecommerce.Infrastructure",
        "BabelHub.Shared",
        "BabelHub.Ecommerce.Application.Devoluciones.Interno",
    ];

    private static readonly string[] CasosDeUsoDigitales = ["SolicitarReembolsoDeDigital", "ReportarArchivoDefectuoso", "DecidirReporte"];

    private static Assembly Aplicacion => typeof(BabelHub.Ecommerce.Application.Devoluciones.IDevolucionesDelCliente).Assembly;

    private static IEnumerable<Type> TiposDelModulo() => Aplicacion.GetTypes().Where(t => t.Namespace is not null && t.Namespace.StartsWith(Raiz, StringComparison.Ordinal));

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    public void La_raiz_del_contrato_no_depende_del_dominio_ni_de_la_infraestructura_ni_de_su_interior()
    {
        var resultado = Types.InAssembly(Aplicacion).That().ResideInNamespace(Raiz).And().DoNotResideInNamespace(Raiz + ".Interno").ShouldNot().HaveDependencyOnAny(Prohibidos).GetResult();

        Assert.True(resultado.IsSuccessful, string.Join(", ", resultado.FailingTypeNames ?? []));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    public void Los_casos_de_uso_digitales_no_dependen_del_puerto_del_Inventario_de_devoluciones()
    {
        const string Puerto = Raiz + ".Interno.IInventarioDeDevoluciones";
        var digitales = TiposDelModulo().Where(t => CasosDeUsoDigitales.Contains(t.Name, StringComparer.Ordinal)).Select(t => t.FullName!).ToList();

        var infractores = Types.InAssembly(Aplicacion).That().HaveDependencyOn(Puerto).GetTypes().Select(t => t.FullName!).Where(n => digitales.Any(d => n.StartsWith(d, StringComparison.Ordinal))).ToList();

        Assert.Empty(infractores);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-25")]
    public void El_modulo_de_devoluciones_no_usa_el_token_del_usuario_de_la_sesion()
    {
        const string Token = "BabelHub.Ecommerce.Application.Plataforma.ITokenDelUsuarioDeLaSesion";
        var infractores = Types.InAssembly(Aplicacion).That().ResideInNamespace(Raiz).And().HaveDependencyOn(Token).GetTypes().Select(t => t.FullName!).ToList();
        var enLaInfraestructura = Types.InAssembly(typeof(BabelHub.Ecommerce.Infrastructure.AssemblyMarker).Assembly).That().ResideInNamespace("BabelHub.Ecommerce.Infrastructure.Devoluciones").And().HaveDependencyOn(Token).GetTypes().Select(t => t.FullName!).ToList();

        Assert.Empty(infractores);
        Assert.Empty(enLaInfraestructura);
    }
}
