using System.Net;
using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Web;

/// <summary>El layout de las áreas (T24): cabecera, menú, pie legal, saltar al contenido, tema y selector de sucursal.</summary>
public sealed partial class LayoutTests
{
    [GeneratedRegex(@"<footer[^>]*>(?<pie>.*?)</footer>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Pie();

    private static async Task<(HttpStatusCode Estado, string Html)> Pedir(AnfitrionWeb anfitrion, string ruta, string? cookie = null)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, ruta);
        if (cookie is not null)
        {
            solicitud.Headers.Add("Cookie", cookie);
        }

        using var respuesta = await anfitrion.Cliente.SendAsync(solicitud);
        return (respuesta.StatusCode, await respuesta.Content.ReadAsStringAsync());
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-05")]
    public async Task El_pie_de_la_tienda_tiene_el_Libro_de_Reclamaciones_y_los_tres_textos_legales()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var (estado, html) = await Pedir(anfitrion, "/");

        Assert.Equal(HttpStatusCode.OK, estado);
        var pie = Pie().Match(html).Groups["pie"].Value;
        Assert.Contains("href=\"/reclamaciones\"", pie, StringComparison.Ordinal);
        Assert.Contains("Libro de Reclamaciones", pie, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/terminos\"", pie, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/privacidad\"", pie, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/cambios-y-devoluciones\"", pie, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-07")]
    public async Task Cada_pagina_declara_el_idioma_es_PE_ofrece_saltar_al_contenido_y_tiene_un_unico_main()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var (_, html) = await Pedir(anfitrion, "/");

        Assert.Contains("<html lang=\"es-PE\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"#contenido\"", html, StringComparison.Ordinal);
        Assert.Contains("Saltar al contenido", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<main[ >]"));
        Assert.Contains("id=\"contenido\"", html, StringComparison.Ordinal);
        Assert.Contains("name=\"viewport\"", html, StringComparison.Ordinal);
        Assert.Contains("<nav aria-label=\"Principal\"", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("bh-tema=babelhub-dark", true)]
    [InlineData("bh-tema=babelhub", true)]
    [InlineData("bh-tema=cualquier-otra-cosa", false)]
    [Trait("Criterio", "AC-WEB-06")]
    public async Task El_tema_elegido_se_aplica_en_el_servidor_y_sin_eleccion_manda_el_dispositivo(string? cookie, bool fijaElTema)
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var (_, html) = await Pedir(anfitrion, "/", cookie);

        Assert.Equal(fijaElTema, html.Contains("data-theme=", StringComparison.Ordinal));
        if (cookie is "bh-tema=babelhub-dark")
        {
            Assert.Contains("data-theme=\"babelhub-dark\"", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-06")]
    public async Task La_pagina_usa_la_hoja_y_los_scripts_propios_sin_scripts_en_linea_ni_recursos_externos()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var (_, html) = await Pedir(anfitrion, "/");

        Assert.Contains("href=\"/css/app.css\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/lib/htmx/htmx.min.js\"", html, StringComparison.Ordinal);
        Assert.Contains("src=\"/js/bh.js\"", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"<script(?![^>]*\bsrc=)", html);
        Assert.DoesNotContain("https://", html, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"\sstyle=""", html);
        Assert.DoesNotMatch(@"\son[a-z]+=""", html);
        Assert.Contains("name=\"htmx-config\"", html, StringComparison.Ordinal);
        Assert.Contains("\"allowEval\":false", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-06")]
    public async Task El_selector_de_tema_ofrece_dispositivo_claro_y_oscuro_con_etiqueta()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var (_, html) = await Pedir(anfitrion, "/");

        Assert.Contains("action=\"/preferencias/tema\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"babelhub-dark\"", html, StringComparison.Ordinal);
        Assert.Contains("value=\"babelhub\"", html, StringComparison.Ordinal);
        Assert.Matches("<label[^>]*for=\"bh-tema\"", html);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public async Task La_tienda_no_muestra_opciones_de_Sucursal_ni_de_Administracion()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var (_, html) = await Pedir(anfitrion, "/");

        Assert.DoesNotContain("/admin", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/sucursal/", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Los_formularios_del_layout_llevan_el_token_antifalsificacion()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var (_, html) = await Pedir(anfitrion, "/");

        var formularios = Regex.Matches(html, "<form[^>]*method=\"post\"[^>]*>(?<cuerpo>.*?)</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        Assert.NotEmpty(formularios);
        Assert.All(formularios, f => Assert.Contains("__RequestVerificationToken", f.Value, StringComparison.Ordinal));
    }
}
