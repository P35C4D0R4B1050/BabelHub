using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Identidad;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// Las ayudas de los campos de contraseña tal como las entrega el servidor, sin ejecutar guiones (web-acceso ACC-01, ACC-02, ACC-08, ACC-09 y ACC-17): la ayuda bajo el primer campo de una contraseña nueva, ningún
/// <c>minlength</c>, nada inerte, las ayudas de escritura desactivadas en <b>todos</b> los campos y los colores de la barra en los dos temas.
/// </summary>
public sealed class AyudasDeContrasenaTests : IAsyncLifetime
{
    private static readonly string[] AyudasDeEscrituraDesactivadas = ["spellcheck=\"false\"", "autocapitalize=\"none\"", "autocorrect=\"off\"", "writingsuggestions=\"false\""];

    private static readonly string CookieDelDesafio = $"{BabelHub.Ecommerce.Api.Web.CookieDelDesafio.Nombre}={new string('d', 43)}";

    private AccesoApiHost _host = null!;

    public async Task InitializeAsync() => _host = await AccesoApiHost.Crear();

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private async Task<string> Pantalla(string cual)
    {
        using var respuesta = cual switch
        {
            "ingreso" => await _host.Cliente.SendAsync(_host.Pedir("/ingreso")),
            "registro" => await _host.Cliente.SendAsync(_host.Pedir("/registro")),
            "recuperacion" => await _host.Enviar("/recuperacion", [new("correo", "nadie@correo.example")]),
            _ => await Temporal(),
        };
        return await respuesta.Content.ReadAsStringAsync();
    }

    private async Task<HttpResponseMessage> Temporal()
    {
        _host.Ingreso.Paso = PasoPendienteDelIngreso.CambiarContrasenaTemporal;
        return await _host.Cliente.SendAsync(_host.Pedir("/ingreso", CookieDelDesafio));
    }

    [Theory]
    [Trait("Criterio", "AC-ACC-01")]
    [InlineData("registro", "contrasena", null)]
    [InlineData("recuperacion", "contrasenaNueva", "confirmacion")]
    [InlineData("temporal", "contrasenaNueva", "confirmacion")]
    public async Task SinGuiones_LaAyudaEstaBajoElPrimerCampoNuevo_SinMinimoDelNavegadorNiNadaInerte(string pantalla, string campoNuevo, string? repeticion)
    {
        var html = await Pantalla(pantalla);

        var texto = WebUtility.HtmlDecode(html);
        foreach (var frase in new[]
        {
            "<strong>Obligatorio:</strong> de 10 a 128 caracteres.",
            "<strong>Recomendado:</strong> usar mayúsculas, minúsculas, números y símbolos, y preferir una frase larga.",
            "Al guardar comprobamos que no sea una contraseña común o filtrada.",
        })
        {
            Assert.Contains(frase, texto, StringComparison.Ordinal);
        }

        var campos = GuionDeContrasenasTests.CamposDe(html);
        var nuevo = Assert.Single(campos, c => c.ContainsKey("data-bh-nueva"));
        Assert.Equal((campoNuevo, ReglasDeContrasena.LongitudMinima.ToString(CultureInfo.InvariantCulture), "ayuda-de-contrasena"), (nuevo["name"], nuevo["data-bh-nueva"], nuevo["aria-describedby"]));
        Assert.Equal(repeticion, campos.SingleOrDefault(c => c.ContainsKey("data-bh-repite"))?["name"]);
        // La ayuda va después del primer campo nuevo y antes de la repetición, y una sola vez.
        var ayuda = html.IndexOf("id=\"ayuda-de-contrasena\"", StringComparison.Ordinal);
        Assert.InRange(ayuda, html.IndexOf($"name=\"{campoNuevo}\"", StringComparison.Ordinal), repeticion is null ? html.Length : html.IndexOf($"name=\"{repeticion}\"", StringComparison.Ordinal));
        Assert.Equal(ayuda, html.LastIndexOf("id=\"ayuda-de-contrasena\"", StringComparison.Ordinal));
        Assert.DoesNotContain("minlength", html, StringComparison.OrdinalIgnoreCase);
        foreach (var inerte in new[] { "data-bh-ver", "data-bh-medidor", "data-bh-coincidencia", "bh-medidor", "Las contraseñas coinciden" })
        {
            Assert.DoesNotContain(inerte, texto, StringComparison.Ordinal);
        }

        Assert.Contains("<script src=\"/js/contrasenas.js\" defer></script>", html, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-ACC-16")]
    [InlineData("ingreso", 1)]
    [InlineData("registro", 1)]
    [InlineData("recuperacion", 2)]
    [InlineData("temporal", 2)]
    public async Task CadaCampoDeContrasenaDeLasPantallasDeAcceso_DesactivaLasAyudasDeEscritura_YSeNombraParaElBoton(string pantalla, int cuantos)
    {
        var campos = GuionDeContrasenasTests.CamposDe(await Pantalla(pantalla));

        Assert.Equal(cuantos, campos.Count);
        Assert.All(campos, campo =>
        {
            Assert.Equal(("false", "none", "off", "false"), (campo["spellcheck"], campo["autocapitalize"], campo["autocorrect"], campo["writingsuggestions"]));
            Assert.False(string.IsNullOrWhiteSpace(campo["data-bh-nombre"]));
            Assert.Contains(campo["autocomplete"], new[] { "current-password", "new-password" });
        });
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-16")]
    [Trait("Criterio", "AC-ACC-07")]
    public void SoloElCampoComunEscribeUnaContrasena_ConLasAyudasDeEscrituraDesactivadasEnCadaVariante()
    {
        // Así vale para toda pantalla presente y futura: la cuenta, la cancelación o cualquier otra usan el mismo parcial.
        var vistas = Directory.EnumerateFiles(RaizDelRepositorio.Api, "*.cshtml", SearchOption.AllDirectories)
            .Where(v => !v.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(v => Regex.IsMatch(File.ReadAllText(v), "type\\s*=\\s*[\"']password[\"']|Html\\.Password|PasswordFor", RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
            .Select(Path.GetFileName);

        Assert.Equal(["_CampoDeContrasena.cshtml"], vistas);
        var lineas = File.ReadAllLines(RaizDelRepositorio.DeLaApi("Views/Shared/_CampoDeContrasena.cshtml")).Where(l => l.Contains("type=\"password\"", StringComparison.Ordinal)).ToList();
        Assert.Equal(3, lineas.Count);
        Assert.All(lineas, linea => Assert.All(AyudasDeEscrituraDesactivadas, atributo => Assert.Contains(atributo, linea, StringComparison.Ordinal)));
    }

    // ---------- La hoja de estilos ----------

    private static Dictionary<string, string> Tema(string nombre)
    {
        var css = File.ReadAllText(RaizDelRepositorio.DeLaApi("Web/Estilos/app.css"));
        var bloque = Regex.Matches(css, @"daisyui-theme\.mjs""\s*\{(?<cuerpo>.*?)\}", RegexOptions.Singleline, TimeSpan.FromSeconds(2))
            .Select(m => m.Groups["cuerpo"].Value)
            .Single(c => c.Contains($"name: \"{nombre}\";", StringComparison.Ordinal));
        return Regex.Matches(bloque, @"--(?<token>[a-z0-9-]+)\s*:\s*(?<valor>#[0-9A-Fa-f]{6});", RegexOptions.None, TimeSpan.FromSeconds(2)).ToDictionary(m => m.Groups["token"].Value, m => m.Groups["valor"].Value, StringComparer.Ordinal);
    }

    private static double Luminancia(string color)
    {
        double Canal(int desde)
        {
            var v = int.Parse(color.AsSpan(desde, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        }

        return (0.2126 * Canal(1)) + (0.7152 * Canal(3)) + (0.0722 * Canal(5));
    }

    private static double Contraste(string uno, string otro)
    {
        var (a, b) = (Luminancia(uno), Luminancia(otro));
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    [Theory]
    [Trait("Criterio", "AC-ACC-08")]
    [InlineData("babelhub")]
    [InlineData("babelhub-dark")]
    public void LosColoresDeLaBarraSeDistinguenDelFondoEnCadaTema_YCadaNivelLlenaUnTramoMas(string tema)
    {
        var tokens = Tema(tema);
        var css = File.ReadAllText(RaizDelRepositorio.DeLaApi("Web/Estilos/app.css"));

        var niveles = new[] { "debil", "aceptable", "buena", "fuerte" };
        Assert.All(niveles, nivel => Assert.True(Contraste(tokens["bh-nivel-" + nivel], tokens["color-base-100"]) >= 3.0, $"{tema}: el color del nivel {nivel} no se distingue del fondo."));
        Assert.Equal(4, niveles.Select(n => tokens["bh-nivel-" + n]).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        // Sin color: cada nivel llena un tramo más (además de decir su nombre).
        for (var tramos = 1; tramos <= 3; tramos++)
        {
            Assert.Contains($".bh-medidor[data-nivel=\"{tramos}\"] .bh-medidor-barra > span:nth-child(-n + {tramos})", css, StringComparison.Ordinal);
        }

        // La transición de la barra solo existe si la persona no pidió reducir el movimiento.
        var conMovimiento = Regex.Match(css, @"@media \(prefers-reduced-motion: no-preference\) \{(?<cuerpo>.*?)\r?\n\}", RegexOptions.Singleline, TimeSpan.FromSeconds(2)).Groups["cuerpo"].Value;
        Assert.Contains(".bh-medidor-barra > span", conMovimiento, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-14")]
    public void ElCodigoQr_EsOscuroSobreClaroEnLosDosTemas_YMideAlMenos176PixelesSinDesbordar()
    {
        var css = File.ReadAllText(RaizDelRepositorio.DeLaApi("Web/Estilos/app.css"));
        var claro = Tema("babelhub");
        var oscuro = Tema("babelhub-dark");

        foreach (var tema in new[] { claro, oscuro })
        {
            Assert.True(Luminancia(tema["bh-qr-fondo"]) > 0.9 && Luminancia(tema["bh-qr-tinta"]) < 0.05);
            Assert.True(Contraste(tema["bh-qr-tinta"], tema["bh-qr-fondo"]) >= 15);
        }

        var regla = Regex.Match(css, @"\.bh-qr \{(?<cuerpo>[^}]*)\}", RegexOptions.None, TimeSpan.FromSeconds(2)).Groups["cuerpo"].Value;
        // 12 rem son 192 píxeles (al menos 176) y nunca más que el ancho disponible.
        Assert.Contains("width: min(100%, 12rem);", regla, StringComparison.Ordinal);
    }
}
