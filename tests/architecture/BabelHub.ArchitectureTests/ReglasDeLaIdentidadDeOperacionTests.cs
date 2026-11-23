using BabelHub.Shared.Contratos;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// La identidad de operación «carga inicial» (transversal-carga-inicial, CIN-06, AC-CIN-13): no es una cuenta, así que solo las órdenes de operación la construyen como actor y solo los guardias de credencial de las dos Api la
/// nombran para rechazarla. Ningún otro archivo de <c>src/</c> puede usarla, ni el Dominio ni la Aplicación (que además no referencian <c>Shared</c>).
/// </summary>
public sealed class ReglasDeLaIdentidadDeOperacionTests
{
    private static readonly string[] Permitidos =
    [
        Path.Combine("BabelHub.Inventario", "BabelHub.Inventario.Api", "Plataforma", "ModoCargarStock.cs"),
        Path.Combine("BabelHub.Inventario", "BabelHub.Inventario.Api", "Plataforma", "CredencialDeUsuario.cs"),
        Path.Combine("BabelHub.Ecommerce", "BabelHub.Ecommerce.Api", "Plataforma", "ModoCargarCatalogo.cs"),
        Path.Combine("BabelHub.Ecommerce", "BabelHub.Ecommerce.Api", "Web", "SesionWebHandler.cs"),
        Path.Combine("BabelHub.Shared", "Contratos", "IdentidadesDeOperacion.cs"),
    ];

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public void Solo_las_ordenes_de_operacion_y_los_guardias_de_credencial_nombran_la_identidad_de_la_carga_inicial()
    {
        var src = Path.Combine(Proyectos.Raiz, "src");
        var usos = Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories)
            .Where(r => !r.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !r.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(r => File.ReadAllText(r).Contains(nameof(IdentidadesDeOperacion), StringComparison.Ordinal))
            .Select(r => Path.GetRelativePath(src, r))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(usos);
        Assert.All(usos, uso => Assert.Contains(uso, Permitidos));
    }

    [Fact]
    [Trait("Criterio", "AC-CIN-13")]
    public void La_identidad_de_la_carga_inicial_es_un_identificador_fijo_que_no_es_vacio()
    {
        Assert.True(Guid.TryParse(IdentidadesDeOperacion.CargaInicial, out var id));
        Assert.NotEqual(Guid.Empty, id);
    }
}
