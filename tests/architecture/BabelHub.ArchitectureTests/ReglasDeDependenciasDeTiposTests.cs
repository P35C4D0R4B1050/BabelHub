using System.Reflection;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Comprueba a nivel de tipos que el Dominio y la Aplicación no dependen de infraestructura.
/// Complementa a ReglasDeReferenciasTests: las referencias de proyecto se verifican allí; aquí se
/// verifica el código compilado, para que un `using` indebido no pase desapercibido.
/// </summary>
public class ReglasDeDependenciasDeTiposTests
{
    private static readonly string[] Infraestructura =
    [
        "Microsoft.EntityFrameworkCore",
        "Npgsql",
        "StackExchange.Redis",
        "Microsoft.AspNetCore",
        "System.Net.Http",
        "Amazon",
        "Culqi"
    ];

    public static TheoryData<string> NombresDeEnsamblados => new(
        "BabelHub.Inventario.Domain",
        "BabelHub.Inventario.Application",
        "BabelHub.Ecommerce.Domain",
        "BabelHub.Ecommerce.Application");

    [Theory]
    [MemberData(nameof(NombresDeEnsamblados))]
    public void Dominio_y_aplicacion_no_dependen_de_infraestructura(string ensamblado)
    {
        var resultado = Types.InAssembly(Cargar(ensamblado))
            .ShouldNot()
            .HaveDependencyOnAny(Infraestructura)
            .GetResult();

        Assert.True(resultado.IsSuccessful, $"{ensamblado} depende de infraestructura: {string.Join(", ", resultado.FailingTypeNames ?? [])}");
    }

    [Theory]
    [InlineData("BabelHub.Inventario.Domain")]
    [InlineData("BabelHub.Ecommerce.Domain")]
    public void El_dominio_no_depende_de_su_aplicacion(string ensamblado)
    {
        var aplicacion = ensamblado.Replace(".Domain", ".Application", StringComparison.Ordinal);
        var resultado = Types.InAssembly(Cargar(ensamblado))
            .ShouldNot()
            .HaveDependencyOn(aplicacion)
            .GetResult();

        Assert.True(resultado.IsSuccessful);
    }

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre)
        ?? Assembly.Load(nombre);
}
