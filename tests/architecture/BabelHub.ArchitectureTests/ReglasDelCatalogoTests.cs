using System.Reflection;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Shared.Contratos;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// Reglas del módulo <c>Catalogo</c> que no caben en una prueba unitaria (<c>ecommerce-catalogo</c> T06): la Aplicación no referencia <c>Shared</c> (regla 6), así que su constante de rol se compara aquí con
/// <c>RolesDeUsuario.AdministradorCentral</c>, y los DTO públicos del catálogo no llevan ningún campo interno (CAT-14, plan §6).
/// </summary>
public class ReglasDelCatalogoTests
{
    private static readonly Assembly Aplicacion = typeof(IConsultaDeCatalogo).Assembly;

    [Fact]
    public void La_constante_de_rol_de_la_administracion_del_catalogo_es_la_de_Shared()
    {
        var roles = Aplicacion.GetType("BabelHub.Ecommerce.Application.Catalogo.Interno.RolesDelCatalogo", throwOnError: true)!;
        var campo = roles.GetField("AdministradorCentral", BindingFlags.Public | BindingFlags.Static)!;

        Assert.True(campo.IsLiteral, "El rol debe ser una constante.");
        Assert.Equal(RolesDeUsuario.AdministradorCentral, (string?)campo.GetRawConstantValue());
    }

    /// <summary>Los campos que un cliente nunca ve: titular, territorios, vigencia, claves y huellas del almacén, versión, motivo de retiro, licencia, ISBN tal como se ingresó y datos de quién subió.</summary>
    private static readonly string[] FragmentosInternos =
    [
        "Titular", "Territorio", "Vigente", "Licencia", "Clave", "Version", "MotivoDeRetiro", "IsbnIngresado", "SubidoPor", "UsuarioId", "Archivo",
    ];

    /// <summary>Los tipos del contrato que, por diseño, llevan datos internos: las entradas de la administración, sus salidas «DeAdministracion» y los dos de archivos para la Biblioteca y la subida.</summary>
    private static bool EsPublico(Type tipo) =>
        tipo is { IsPublic: true } && tipo.Namespace == "BabelHub.Ecommerce.Application.Catalogo"
        && !tipo.IsInterface
        && !tipo.Name.EndsWith("Entrada", StringComparison.Ordinal)
        && !tipo.Name.Contains("DeAdministracion", StringComparison.Ordinal)
        && tipo.Name is not (nameof(ReferenciaDeArchivoDigital) or nameof(ArchivoSubido) or nameof(ClaveDeIngresoEmitida) or nameof(PortadaServida)
            or nameof(ResultadoDeAlta) or nameof(ResultadoDeEscritura) or nameof(IngresoDeAlta) or nameof(ResultadoDeIngreso) or nameof(HistorialDeIngresos) or nameof(IngresoDeLaLista));

    [Fact]
    public void Los_modelos_publicos_de_consulta_no_llevan_ningun_campo_interno()
    {
        var infractores = Aplicacion.GetTypes()
            .Where(EsPublico)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(p => FragmentosInternos.Any(f => p.Name.Contains(f, StringComparison.Ordinal)))
                .Select(p => $"{t.Name}.{p.Name}"))
            .ToList();

        Assert.True(infractores.Count == 0, $"Estos modelos públicos del catálogo exponen un campo interno: {string.Join(", ", infractores)}.");
    }

    [Fact]
    public void Los_modelos_de_administracion_son_tipos_distintos_de_los_publicos()
    {
        var publicos = new[] { typeof(ResumenDeObra), typeof(EdicionDeObra), typeof(EdicionConsultada), typeof(DisponibilidadDeEdicion) };
        var administracion = new[] { typeof(ObraDeAdministracion), typeof(EdicionDeAdministracion), typeof(LicenciaDeEdicionDeAdministracion), typeof(ArchivoDeEdicionDeAdministracion) };

        Assert.Empty(publicos.Intersect(administracion));
        Assert.Contains(typeof(EdicionDeAdministracion).GetProperties(), p => p.Name == nameof(EdicionDeAdministracion.Licencia));
        Assert.DoesNotContain(typeof(EdicionDeObra).GetProperties(), p => p.Name.Contains("Licencia", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(ResumenDeObra).GetProperties(), p => p.Name.Contains("Version", StringComparison.Ordinal));
    }

    [Fact]
    public void La_regla_de_campos_internos_detecta_un_modelo_publico_que_los_expone()
    {
        var campos = typeof(ModeloQueFiltraElTitular).GetProperties().Where(p => FragmentosInternos.Any(f => p.Name.Contains(f, StringComparison.Ordinal))).ToList();

        Assert.Single(campos);
    }

    private sealed record ModeloQueFiltraElTitular(string Titulo, string TitularDeDerechos);
}
