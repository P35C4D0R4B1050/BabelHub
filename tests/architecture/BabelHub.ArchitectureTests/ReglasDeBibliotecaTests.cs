using System.Reflection;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Las reglas propias de Biblioteca (<c>ecommerce-biblioteca-digital</c> T24; AGENTS §3, reglas 10 a 13): un libro digital <b>no consulta al Inventario ni genera reservas</b> (regla 12), el módulo <b>no usa el Notificador</b> (un aviso nunca lleva el enlace de descarga, y una falla del Notificador no debe poder
/// interrumpirlo, regla 10) y el Dominio de Biblioteca no depende de nada externo. Que no dependa del Dominio, de la Infraestructura ni del <c>Interno</c> de otro módulo lo comprueban <c>ReglasDeModulosTests</c> (regla 13) y que <c>IReferenciaDeArchivosDelCatalogo</c> solo se use desde Biblioteca,
/// <c>ContratoPublicoDeCatalogoTests</c>.
/// </summary>
public class ReglasDeBibliotecaTests
{
    private static readonly string[] Prohibidos =
    [
        "BabelHub.Ecommerce.Application.Plataforma.IClienteDelInventario",
        "BabelHub.Ecommerce.Application.Notificaciones",
        "BabelHub.Inventario",
    ];

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre) ?? Assembly.Load(nombre);

    [Theory]
    [InlineData("Application")]
    [InlineData("Infrastructure")]
    [InlineData("Domain")]
    public void Biblioteca_no_usa_el_Inventario_ni_el_Notificador(string capa)
    {
        var ensamblado = Cargar($"BabelHub.Ecommerce.{capa}");

        foreach (var prohibido in Prohibidos)
        {
            var resultado = Types.InAssembly(ensamblado)
                .That().ResideInNamespaceStartingWith($"BabelHub.Ecommerce.{capa}.Biblioteca")
                .ShouldNot().HaveDependencyOn(prohibido)
                .GetResult();

            Assert.True(resultado.IsSuccessful, $"Biblioteca ({capa}) depende de {prohibido} (un libro digital no consulta al Inventario ni usa el Notificador): {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
        }
    }

    [Fact]
    public void El_dominio_de_biblioteca_solo_usa_tipos_del_lenguaje()
    {
        var dominio = Cargar("BabelHub.Ecommerce.Domain");

        var resultado = Types.InAssembly(dominio)
            .That().ResideInNamespaceStartingWith("BabelHub.Ecommerce.Domain.Biblioteca")
            .ShouldNot().HaveDependencyOnAny("BabelHub.Ecommerce.Application", "BabelHub.Ecommerce.Infrastructure", "BabelHub.Shared", "Npgsql", "Amazon", "Microsoft.Extensions", "Microsoft.AspNetCore")
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"El Dominio de Biblioteca depende de algo externo: {string.Join(", ", resultado.FailingTypeNames ?? [])}.");
    }

    [Fact]
    public void Solo_la_infraestructura_de_biblioteca_toca_el_almacen_de_objetos_y_la_firma_de_la_url()
    {
        foreach (var capa in new[] { "Application", "Domain", "Api" })
        {
            var resultado = Types.InAssembly(Cargar($"BabelHub.Ecommerce.{capa}"))
                .That().ResideInNamespaceStartingWith($"BabelHub.Ecommerce.{capa}.Biblioteca")
                .ShouldNot().HaveDependencyOnAny("Amazon", "System.Security.Cryptography.HMACSHA256")
                .GetResult();

            Assert.True(resultado.IsSuccessful, $"Biblioteca ({capa}) toca el almacén o la firma: {string.Join(", ", resultado.FailingTypeNames ?? [])}. La firma y el SDK viven solo en la Infraestructura (el archivo nunca pasa por el servidor, BIB-14).");
        }
    }
}
