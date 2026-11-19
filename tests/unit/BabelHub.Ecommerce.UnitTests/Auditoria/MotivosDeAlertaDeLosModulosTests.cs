using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria.Interno;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// Todo motivo de alerta técnica que publica un módulo debe estar en el catálogo cerrado (D06): uno que no lo está se degrada a «motivo no catalogado» y la administración pierde qué pasó. Los espías de las pruebas de
/// cada módulo no lo notan, por eso esta prueba recorre los motivos declarados por sus casos de uso.
/// </summary>
public sealed class MotivosDeAlertaDeLosModulosTests
{
    [Theory]
    [Trait("Criterio", "AC-AUD-26")]
    [InlineData("BabelHub.Ecommerce.Application.Pagos.Interno")]
    [InlineData("BabelHub.Ecommerce.Application.Reclamaciones.Interno")]
    public void Cada_motivo_de_alerta_que_declaran_los_casos_de_uso_del_modulo_esta_en_el_catalogo_cerrado(string espacio)
    {
        var catalogo = CatalogoDeAlertasDeAuditoria.Crear();
        var ensamblado = typeof(CatalogoDeAlertasDeAuditoria).Assembly;
        var motivos = ensamblado.GetTypes()
            .Where(t => t.Namespace == espacio)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
            .Where(f => f is { IsLiteral: true, IsInitOnly: false } && f.FieldType == typeof(string) && f.Name.StartsWith("MotivoDeAlerta", StringComparison.Ordinal))
            .Select(f => (string)f.GetRawConstantValue()!)
            .Distinct()
            .ToList();

        Assert.NotEmpty(motivos);
        Assert.All(motivos, motivo => Assert.True(catalogo.Contiene(motivo), $"El motivo de alerta «{motivo}» no está en el catálogo cerrado."));
    }
}
