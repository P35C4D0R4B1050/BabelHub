using System.Reflection;
using System.Text.RegularExpressions;
using NetArchTest.Rules;

namespace BabelHub.ArchitectureTests;

/// <summary>
/// El vínculo de las hojas con la cuenta no sale del módulo Libro de Reclamaciones (DD-26, REC-21; <c>ecommerce-libro-reclamaciones</c> T35): el identificador de la cuenta es un uuid opaco que vive solo en el Dominio, la Aplicación interna y el esquema <c>reclamaciones</c>;
/// ningún contrato público, pantalla ni otro módulo lo ve, y el módulo no lee el esquema de Identidad (la titularidad se resuelve con el correo verificado y la cuenta que entrega el contrato de Identidad). Son pruebas negativas: quien las rompa debe discutirlo.
/// </summary>
public class ReglasDelVinculoDeReclamacionesTests
{
    private const string EspacioDeLaAplicacion = "BabelHub.Ecommerce.Application.Reclamaciones";

    private static Assembly Cargar(string nombre) =>
        AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == nombre) ?? Assembly.Load(nombre);

    [Fact]
    public void Los_contratos_publicos_de_Reclamaciones_no_exponen_el_identificador_de_la_cuenta()
    {
        var expuestos = Cargar("BabelHub.Ecommerce.Application").GetTypes()
            .Where(t => t.IsPublic && t.Namespace == EspacioDeLaAplicacion)
            .SelectMany(t => t.GetProperties().Select(p => (Tipo: t, Nombre: p.Name, p.PropertyType))
                .Concat(t.GetConstructors().SelectMany(c => c.GetParameters().Select(p => (Tipo: t, Nombre: p.Name ?? string.Empty, PropertyType: p.ParameterType))))
                .Concat(t.GetMethods().Where(m => m.DeclaringType == t).SelectMany(m => m.GetParameters().Select(p => (Tipo: t, Nombre: p.Name ?? string.Empty, PropertyType: p.ParameterType)))))
            .Where(x => (x.PropertyType == typeof(Guid) || x.PropertyType == typeof(Guid?)) && Regex.IsMatch(x.Nombre, "cuenta|usuario", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .Select(x => $"{x.Tipo.Name}.{x.Nombre}")
            .ToList();

        Assert.Empty(expuestos);
    }

    [Theory]
    [InlineData("BabelHub.Ecommerce.Application", "BabelHub.Ecommerce.Application.Reclamaciones")]
    [InlineData("BabelHub.Ecommerce.Infrastructure", "BabelHub.Ecommerce.Infrastructure.Reclamaciones")]
    [InlineData("BabelHub.Ecommerce.Api", "BabelHub.Ecommerce.Api.Reclamaciones")]
    public void Ningun_tipo_fuera_del_modulo_usa_el_vinculo_ni_su_vinculador(string ensamblado, string espacioPropio)
    {
        var usuarios = Types.InAssembly(Cargar(ensamblado))
            .That().HaveDependencyOnAny(
                $"{EspacioDeLaAplicacion}.Interno.IVinculosDeHojas",
                $"{EspacioDeLaAplicacion}.Interno.VinculadorDeHojas",
                "BabelHub.Ecommerce.Domain.Reclamaciones.HojaDeReclamacion")
            .GetTypes()
            .Select(t => t.FullName!)
            .ToList();
        var fuera = usuarios
            .Where(nombre => !nombre.StartsWith(espacioPropio, StringComparison.Ordinal) && !nombre.StartsWith("BabelHub.Ecommerce.Infrastructure.Composicion", StringComparison.Ordinal))
            .ToList();

        // La prueba no es vacía: dentro del módulo sí hay quien los use (la Aplicación y la Infraestructura de Reclamaciones).
        if (ensamblado != "BabelHub.Ecommerce.Api")
        {
            Assert.NotEmpty(usuarios);
        }

        Assert.Empty(fuera);
    }

    [Fact]
    public void La_Infraestructura_de_Reclamaciones_no_lee_el_esquema_de_Identidad()
    {
        var carpeta = Path.Combine(Proyectos.Raiz, "src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Infrastructure", "Reclamaciones");
        var lecturas = Directory.EnumerateFiles(carpeta, "*.cs", SearchOption.AllDirectories)
            .Where(archivo => Regex.IsMatch(File.ReadAllText(archivo), @"\b(identidad|usuarios|credenciales)\.[a-z_]+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) && SqlDeIdentidad(File.ReadAllText(archivo)))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(lecturas);
    }

    /// <summary>Una referencia a una tabla del esquema <c>identidad</c> dentro de una sentencia SQL (<c>FROM identidad.…</c>, <c>JOIN identidad.…</c>, <c>REFERENCES identidad.…</c>).</summary>
    private static bool SqlDeIdentidad(string texto) =>
        Regex.IsMatch(texto, @"\b(from|join|update|into|references)\s+identidad\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    [Fact]
    public void La_hoja_no_tiene_una_clave_foranea_hacia_otro_esquema()
    {
        var sql = File.ReadAllText(Path.Combine(Proyectos.Raiz, "src", "BabelHub.Ecommerce", "BabelHub.Ecommerce.Infrastructure", "Reclamaciones", "DefinicionDeReclamaciones.cs"));

        // El único REFERENCES permitido sería uno interno al esquema reclamaciones: el uuid de la cuenta es opaco, sin clave foránea (REC-21, D14).
        Assert.DoesNotMatch(new Regex(@"REFERENCES\s+(?!reclamaciones\.)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), sql);
    }
}
