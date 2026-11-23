using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El teléfono, el correo y la dirección del comercio no se escriben en el repositorio (COM-02, AC-COM-02; AGENTS.md §5): una búsqueda de patrones de correo y de teléfono sobre el código de producción
/// (<c>src</c>), la infraestructura (<c>infra</c> y <c>scripts</c>) y la documentación (<c>docs</c>) solo puede encontrar los valores ficticios de COM-05, dominios y números de ejemplo y las excepciones
/// declaradas aquí, una por par (archivo, valor) y cada una justificada. <c>tests/</c> queda fuera: AGENTS.md §5 y <c>docs/07-proceso-de-desarrollo/03-estrategia-de-pruebas.md</c> (punto 8) piden
/// datos de prueba de ejemplo y no hay un detector automático para ellos; <c>progress/</c>, <c>specs/</c> y <c>prototype/</c> son bitácoras y referencia con ejemplos ficticios por diseño.
/// </summary>
public sealed partial class ContactoFueraDelRepositorioTests
{
    private static readonly string[] Carpetas = ["src", "infra", "scripts", "docs"];

    private static readonly string[] Extensiones =
        [".cs", ".cshtml", ".json", ".md", ".yml", ".yaml", ".sh", ".txt", ".env", ".conf", ".cmd", ".csproj", ".props", ".js", ".ts", ".html", ".sql", ".xml", ".toml", ".ini", ".service", ".cnf", ".example", ".jsonc"];

    private static readonly string[] CarpetasExcluidas = ["bin", "obj", "node_modules", ".git", "wwwroot/lib", "wwwroot/css", "wwwroot/fonts"];

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex Correo();

    [GeneratedRegex(@"(?<![0-9A-Za-z])(\+?51[ -]?)?9[0-9]{2}[ -]?[0-9]{3}[ -]?[0-9]{3}(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex CelularPeruano();

    [GeneratedRegex(@"(?<![0-9A-Za-z])\+[0-9]{1,3}[ -]?[0-9][0-9 ()-]{5,14}[0-9](?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex TelefonoInternacional();

    /// <summary>Los ficticios de COM-05: valen en cualquier archivo.</summary>
    private static readonly string[] Ficticios = ["999 000 000", "999000000", "+51999000000", "contacto@ejemplo.test"];

    private static readonly string[] DominiosDeEjemplo = [".test", ".invalid", ".example"];

    /// <summary>Las excepciones: (final de la ruta relativa, valor, motivo). Ninguna es un dato real.</summary>
    private static readonly (string Archivo, string Valor, string Motivo)[] Excepciones =
    [
        ("Areas/Tienda/Views/Checkout/Index.cshtml", "987654321", "ejemplo de ayuda del campo «Celular de contacto» del checkout: un celular de forma válida inventado, no un dato del comercio"),
    ];

    private static IEnumerable<string> Archivos()
    {
        foreach (var carpeta in Carpetas)
        {
            var raiz = Path.Combine(RaizDelRepositorio.Raiz, carpeta);
            if (!Directory.Exists(raiz))
            {
                continue;
            }

            foreach (var archivo in Directory.EnumerateFiles(raiz, "*", SearchOption.AllDirectories))
            {
                var relativa = Path.GetRelativePath(RaizDelRepositorio.Raiz, archivo).Replace('\\', '/');
                if (CarpetasExcluidas.Any(c => relativa.Contains("/" + c + "/", StringComparison.Ordinal))
                    || relativa.EndsWith(".lock.json", StringComparison.Ordinal)
                    || !Extensiones.Contains(Path.GetExtension(archivo), StringComparer.OrdinalIgnoreCase) && !Path.GetFileName(archivo).EndsWith(".env", StringComparison.Ordinal))
                {
                    continue;
                }

                yield return relativa;
            }
        }
    }

    private static IEnumerable<(string Archivo, string Valor)> Coincidencias()
    {
        foreach (var relativa in Archivos())
        {
            var texto = File.ReadAllText(Path.Combine(RaizDelRepositorio.Raiz, relativa));
            foreach (Match m in Correo().Matches(texto))
            {
                // En una vista Razor `cantidad-@ayuda.AccesoCorto` es sintaxis, no un correo: su parte local termina en «-», «.» o «_».
                if (relativa.EndsWith(".cshtml", StringComparison.Ordinal) && m.Value.IndexOf('@') is var arroba and > 0 && m.Value[arroba - 1] is '-' or '.' or '_')
                {
                    continue;
                }

                if (DominiosDeEjemplo.Any(d => m.Value.EndsWith(d, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                yield return (relativa, m.Value);
            }

            foreach (var patron in new[] { CelularPeruano(), TelefonoInternacional() })
            {
                foreach (Match m in patron.Matches(texto))
                {
                    yield return (relativa, m.Value);
                }
            }
        }
    }

    [Fact]
    [Trait("Criterio", "AC-COM-02")]
    public void El_codigo_la_infraestructura_y_la_documentacion_no_traen_ningun_telefono_ni_correo_fuera_de_los_ficticios_y_de_la_lista_cerrada()
    {
        var sinJustificar = Coincidencias()
            .Where(c => !Ficticios.Contains(c.Valor, StringComparer.Ordinal))
            .Where(c => !Excepciones.Any(e => c.Archivo.EndsWith(e.Archivo, StringComparison.Ordinal) && e.Valor == c.Valor))
            .Select(c => $"{c.Archivo}: {c.Valor}")
            .Distinct()
            .ToList();

        Assert.True(sinJustificar.Count == 0, "Datos de contacto literales en el repositorio (deben salir de BABELHUB__Contacto__*): " + string.Join("; ", sinJustificar));
    }

    [Fact]
    [Trait("Criterio", "AC-COM-02")]
    public void Cada_excepcion_de_la_lista_cerrada_sigue_usandose()
    {
        var halladas = Coincidencias().ToList();

        foreach (var (archivo, valor, _) in Excepciones)
        {
            Assert.True(halladas.Any(c => c.Archivo.EndsWith(archivo, StringComparison.Ordinal) && c.Valor == valor), $"La excepción ({archivo}, {valor}) ya no se usa: quítela de la lista.");
        }
    }

    [Fact]
    [Trait("Criterio", "AC-COM-02")]
    public void Las_vistas_del_pie_y_de_contacto_no_escriben_ningun_dato_de_contacto_literal()
    {
        foreach (var vista in new[] { "Views/Shared/_Pie.cshtml", "Views/Contacto/Contacto.cshtml" })
        {
            var texto = File.ReadAllText(RaizDelRepositorio.DeLaApi(vista));

            Assert.DoesNotMatch(@"tel:[+0-9]", texto);
            Assert.DoesNotMatch(@"mailto:[A-Za-z0-9]", texto);
            Assert.DoesNotMatch(CelularPeruano().ToString(), texto);
            Assert.DoesNotMatch(@"[A-Za-z0-9]@[A-Za-z0-9-]+\.[A-Za-z]{2,}", texto);
        }
    }
}
