using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El sistema de diseño (AD21, D09): dos temas con los mismos tokens, contraste de texto de al menos 4,5 a 1 en ambos (WEB-11), ningún
/// color suelto fuera de los tokens, movimiento reducido (WEB-12), fuentes y htmx autoalojados y todo lo que no pasa por NuGet fijado
/// por versión y huella SHA-256 (PLA-26).
/// </summary>
public sealed partial class SistemaDeDisenoTests
{
    private static readonly string[] TokensObligatorios =
    [
        "base-100", "base-200", "base-300", "base-content", "primary", "primary-content", "secondary", "secondary-content",
        "accent", "accent-content", "neutral", "neutral-content", "info", "info-content", "success", "success-content",
        "warning", "warning-content", "error", "error-content",
    ];

    private static string Css => File.ReadAllText(RaizDelRepositorio.DeLaApi("Web/Estilos/app.css"));

    [GeneratedRegex(@"@plugin\s+""[^""]*daisyui-theme\.mjs""\s*\{(?<cuerpo>.*?)\}", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex BloqueDeTema();

    [GeneratedRegex(@"--(?<token>[a-z0-9-]+)\s*:\s*(?<valor>[^;]+);", RegexOptions.CultureInvariant)]
    private static partial Regex Variable();

    [GeneratedRegex(@"name\s*:\s*""(?<nombre>[^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex NombreDelTema();

    private static Dictionary<string, Dictionary<string, string>> Temas()
    {
        var temas = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (Match bloque in BloqueDeTema().Matches(Css))
        {
            var cuerpo = bloque.Groups["cuerpo"].Value;
            var nombre = NombreDelTema().Match(cuerpo).Groups["nombre"].Value;
            var variables = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match v in Variable().Matches(cuerpo))
            {
                variables[v.Groups["token"].Value] = v.Groups["valor"].Value.Trim();
            }

            temas[nombre] = variables;
        }

        return temas;
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-06")]
    public void Hay_dos_temas_babelhub_y_babelhub_dark_con_los_mismos_tokens()
    {
        var temas = Temas();

        Assert.Equal(["babelhub", "babelhub-dark"], temas.Keys.Order(StringComparer.Ordinal).ToArray());
        foreach (var (nombre, variables) in temas)
        {
            foreach (var token in TokensObligatorios)
            {
                Assert.True(variables.ContainsKey($"color-{token}"), $"El tema {nombre} no define --color-{token}.");
            }
        }

        Assert.Equal(
            temas["babelhub"].Keys.Order(StringComparer.Ordinal),
            temas["babelhub-dark"].Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-06")]
    public void El_tema_oscuro_es_el_del_dispositivo_cuando_no_hay_eleccion_y_el_claro_es_el_predeterminado()
    {
        var bloques = BloqueDeTema().Matches(Css).Select(m => m.Groups["cuerpo"].Value).ToArray();

        var claro = Assert.Single(bloques, b => b.Contains("\"babelhub\"", StringComparison.Ordinal));
        var oscuro = Assert.Single(bloques, b => b.Contains("\"babelhub-dark\"", StringComparison.Ordinal));
        Assert.Contains("default: true", claro, StringComparison.Ordinal);
        Assert.Contains("prefersdark: true", oscuro, StringComparison.Ordinal);
    }

    public static TheoryData<string, string, string> ParesDeTexto()
    {
        var datos = new TheoryData<string, string, string>();
        foreach (var tema in new[] { "babelhub", "babelhub-dark" })
        {
            foreach (var par in new[]
            {
                ("base-content", "base-100"), ("base-content", "base-200"), ("base-content", "base-300"), ("primary-content", "primary"),
                ("secondary-content", "secondary"), ("accent-content", "accent"), ("neutral-content", "neutral"), ("info-content", "info"),
                ("success-content", "success"), ("warning-content", "warning"), ("error-content", "error"),
            })
            {
                datos.Add(tema, par.Item1, par.Item2);
            }
        }

        return datos;
    }

    [Theory]
    [MemberData(nameof(ParesDeTexto))]
    [Trait("Criterio", "AC-WEB-07")]
    public void El_texto_cumple_el_contraste_AA_en_ambos_temas(string tema, string texto, string fondo)
    {
        var variables = Temas()[tema];

        var contraste = Contraste(Color(variables[$"color-{texto}"]), Color(variables[$"color-{fondo}"]));

        Assert.True(contraste >= 4.5, $"{tema}: {texto} sobre {fondo} tiene un contraste de {contraste:0.00} (mínimo 4,5).");
    }

    [Theory]
    [InlineData("babelhub", "base-100")]
    [InlineData("babelhub-dark", "base-100")]
    [Trait("Criterio", "AC-WEB-07")]
    public void El_foco_visible_se_distingue_del_fondo(string tema, string fondo)
    {
        var variables = Temas()[tema];
        var foco = Css.Contains("--bh-foco", StringComparison.Ordinal) ? variables["bh-foco"] : throw new Xunit.Sdk.XunitException("Falta --bh-foco.");

        // El indicador de foco es un componente gráfico: WCAG 1.4.11 pide 3 a 1 como mínimo.
        Assert.True(Contraste(Color(foco), Color(variables[$"color-{fondo}"])) >= 3.0, $"El foco del tema {tema} no se distingue del fondo.");
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-07")]
    public void Ninguna_hoja_ni_vista_ni_script_propio_usa_colores_sueltos()
    {
        var sueltos = new List<string>();
        var sinTemas = BloqueDeTema().Replace(Css, string.Empty);
        sueltos.AddRange(ColoresSueltos("Web/Estilos/app.css", sinTemas));

        foreach (var archivo in ArchivosPropios())
        {
            sueltos.AddRange(ColoresSueltos(archivo, File.ReadAllText(RaizDelRepositorio.DeLaApi(archivo))));
        }

        Assert.True(sueltos.Count == 0, "Colores sueltos (usa tokens semánticos): " + string.Join("; ", sueltos));
    }

    [Theory]
    [InlineData("color: #FF665A;", true)]
    [InlineData("background: rgb(0 0 0 / .2);", true)]
    [InlineData("fill: hsl(10 20% 30%);", true)]
    [InlineData("color: var(--color-primary);", false)]
    [InlineData("background: color-mix(in oklab, var(--color-base-content) 20%, transparent);", false)]
    [InlineData("class=\"btn btn-primary text-error\"", false)]
    [InlineData("class=\"text-[#ff0000]\"", true)]
    public void La_revision_de_colores_sueltos_los_detecta(string texto, bool esperado) =>
        Assert.Equal(esperado, ColoresSueltos("prueba", texto).Count > 0);

    [Fact]
    [Trait("Criterio", "AC-WEB-08")]
    public void Con_movimiento_reducido_se_eliminan_las_animaciones_y_transiciones()
    {
        var css = Css;

        var regla = Regex.Match(css, @"@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{(?<cuerpo>.*?)\n\}", RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.True(regla.Success, "Falta la regla de prefers-reduced-motion: reduce.");
        Assert.Contains("animation", regla.Groups["cuerpo"].Value, StringComparison.Ordinal);
        Assert.Contains("transition", regla.Groups["cuerpo"].Value, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-08")]
    public void Las_animaciones_solo_se_declaran_si_la_persona_no_pidio_reducirlas()
    {
        var sinReducido = Regex.Replace(Css, @"@media\s*\(prefers-reduced-motion:\s*no-preference\)\s*\{.*?\n\}", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);
        sinReducido = Regex.Replace(sinReducido, @"@media\s*\(prefers-reduced-motion:\s*reduce\)\s*\{.*?\n\}", string.Empty, RegexOptions.Singleline | RegexOptions.CultureInvariant);

        Assert.DoesNotMatch(@"\banimation\s*:", sinReducido);
        Assert.DoesNotMatch(@"\btransition\s*:", sinReducido);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-06")]
    public void Las_fuentes_se_autoalojan_y_no_hay_recursos_externos_en_la_hoja()
    {
        var css = Css;
        // El espacio de nombres de un SVG incrustado no es un recurso externo.
        var sinEspaciosDeNombres = Regex.Replace(css, "xmlns='[^']*'", string.Empty, RegexOptions.CultureInvariant);

        Assert.DoesNotContain("http://", sinEspaciosDeNombres, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", sinEspaciosDeNombres, StringComparison.Ordinal);
        Assert.DoesNotContain("fonts.googleapis.com", css, StringComparison.Ordinal);
        foreach (Match url in Regex.Matches(css, @"url\(""?(?<ruta>/lib/fuentes/[^"")]+)""?\)", RegexOptions.CultureInvariant))
        {
            var ruta = url.Groups["ruta"].Value.TrimStart('/');
            Assert.True(File.Exists(RaizDelRepositorio.DeLaApi("wwwroot/" + ruta)), $"Falta la fuente {ruta}.");
        }

        Assert.Contains("DM Serif Display", css, StringComparison.Ordinal);
        Assert.Contains("Inter", css, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-06")]
    public void Lo_que_el_SBOM_no_ve_esta_fijado_por_version_y_huella_y_los_archivos_coinciden()
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(RaizDelRepositorio.DeLaApi("Web/versiones.json")));
        var raiz = documento.RootElement;

        foreach (var nombre in new[] { "tailwindcss", "daisyui", "htmx", "dm-serif-display", "inter" })
        {
            var entrada = raiz.GetProperty(nombre);
            Assert.False(string.IsNullOrWhiteSpace(entrada.GetProperty("version").GetString()), $"{nombre} sin versión.");
            Assert.False(string.IsNullOrWhiteSpace(entrada.GetProperty("licencia").GetString()), $"{nombre} sin licencia.");
        }

        foreach (var huella in HuellasDeDescarga(raiz))
        {
            Assert.Matches("^[0-9a-f]{64}$", huella.Valor);
        }

        // Lo que sí se versiona (htmx y las fuentes) se compara con el archivo real.
        foreach (var (archivo, valor) in HuellasDeArchivos(raiz))
        {
            var ruta = RaizDelRepositorio.DeLaApi(archivo);
            Assert.True(File.Exists(ruta), $"Falta {archivo}.");
            Assert.Equal(valor, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(ruta))));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-06")]
    public void El_Dockerfile_verifica_la_huella_de_la_CLI_de_Tailwind_y_de_daisyUI_antes_de_usarlas()
    {
        using var documento = JsonDocument.Parse(File.ReadAllText(RaizDelRepositorio.DeLaApi("Web/versiones.json")));
        var dockerfile = File.ReadAllText(RaizDelRepositorio.DeLaApi("Dockerfile"));

        foreach (var huella in HuellasDeDescarga(documento.RootElement).Where(h => h.Nombre.Contains("linux", StringComparison.Ordinal) || h.Nombre.Contains("daisyui", StringComparison.Ordinal)))
        {
            Assert.True(dockerfile.Contains(huella.Valor, StringComparison.Ordinal), $"El Dockerfile no fija la huella de {huella.Nombre}.");
        }

        Assert.Contains("sha256sum -c", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("npm ", dockerfile, StringComparison.Ordinal);
        Assert.DoesNotContain("npx ", dockerfile, StringComparison.Ordinal);
    }

    private static IEnumerable<(string Nombre, string Valor)> HuellasDeDescarga(JsonElement raiz)
    {
        foreach (var herramienta in new[] { "tailwindcss", "daisyui" })
        {
            foreach (var descarga in raiz.GetProperty(herramienta).GetProperty("descargas").EnumerateObject())
            {
                yield return ($"{herramienta}:{descarga.Name}", descarga.Value.GetProperty("sha256").GetString()!);
            }
        }
    }

    private static IEnumerable<(string Archivo, string Valor)> HuellasDeArchivos(JsonElement raiz)
    {
        foreach (var nombre in new[] { "htmx", "dm-serif-display", "inter" })
        {
            foreach (var archivo in raiz.GetProperty(nombre).GetProperty("archivos").EnumerateObject())
            {
                yield return (archivo.Name, archivo.Value.GetProperty("sha256").GetString()!);
            }
        }
    }

    private static IEnumerable<string> ArchivosPropios()
    {
        foreach (var carpeta in new[] { "Web", "Areas", "Views", "wwwroot/js" })
        {
            var ruta = RaizDelRepositorio.DeLaApi(carpeta);
            if (!Directory.Exists(ruta))
            {
                continue;
            }

            foreach (var archivo in Directory.EnumerateFiles(ruta, "*.*", SearchOption.AllDirectories))
            {
                var relativa = Path.GetRelativePath(RaizDelRepositorio.Api, archivo).Replace('\\', '/');
                if (relativa is "Web/Estilos/app.css" or "Web/versiones.json" || relativa.Contains("/obj/", StringComparison.Ordinal))
                {
                    continue;
                }

                if (relativa.EndsWith(".cshtml", StringComparison.Ordinal) || relativa.EndsWith(".css", StringComparison.Ordinal) || relativa.EndsWith(".js", StringComparison.Ordinal)
                    || relativa.EndsWith(".cs", StringComparison.Ordinal))
                {
                    yield return relativa;
                }
            }
        }
    }

    private static List<string> ColoresSueltos(string archivo, string texto)
    {
        var sueltos = new List<string>();
        foreach (Match m in Regex.Matches(texto, @"(?<![\w&%])#[0-9a-fA-F]{3,8}\b(?!\w)|\b(?:rgb|rgba|hsl|hsla|oklch|oklab|hwb)\(", RegexOptions.CultureInvariant))
        {
            // Los marcadores de posición de un identificador (por ejemplo href="#contenido") no son colores: se descartan los que siguen a «="» o «'» de un enlace.
            var inicio = Math.Max(0, m.Index - 8);
            var contexto = texto[inicio..m.Index];
            if (contexto.Contains("href=", StringComparison.Ordinal) || contexto.Contains("id=", StringComparison.Ordinal) || contexto.Contains("for=", StringComparison.Ordinal)
                || contexto.Contains("aria-", StringComparison.Ordinal) || contexto.Contains("to=", StringComparison.Ordinal))
            {
                continue;
            }

            sueltos.Add($"{archivo}: {m.Value}");
        }

        return sueltos;
    }

    private static (double R, double G, double B) Color(string valor)
    {
        var hex = valor.Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => $"{c}{c}"));
        }

        return (
            int.Parse(hex[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0,
            int.Parse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
    }

    private static double Luminancia((double R, double G, double B) c)
    {
        static double Lineal(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return (0.2126 * Lineal(c.R)) + (0.7152 * Lineal(c.G)) + (0.0722 * Lineal(c.B));
    }

    private static double Contraste((double R, double G, double B) a, (double R, double G, double B) b)
    {
        var (claro, oscuro) = Luminancia(a) >= Luminancia(b) ? (Luminancia(a), Luminancia(b)) : (Luminancia(b), Luminancia(a));
        return (claro + 0.05) / (oscuro + 0.05);
    }
}
