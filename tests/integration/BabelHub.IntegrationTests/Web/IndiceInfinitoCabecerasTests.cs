using System.Net;
using System.Text.RegularExpressions;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// Las cabeceras y el navegador del índice infinito (web-indice-infinito T12; AC-IND-04, IND-05; D06): <c>X-Robots-Tag: noindex, nofollow</c>, <c>Cache-Control: no-store</c>, las cuatro cabeceras de seguridad y la CSP sin <c>unsafe-inline</c> en 200, 200 amable,
/// 302 y un error inesperado forzado (el 429 se prueba en <see cref="IndiceInfinitoSinBaseYConLimitesTests"/>); <c>Location</c> con la forma exacta; <c>robots.txt</c> con el <c>Disallow</c>; y una prueba estática de las vistas y del script (sin
/// <c>Html.Raw</c>, <c>style=</c>, script en línea ni manejadores <c>on*=</c>). En memoria y sin Docker.
/// </summary>
public sealed partial class IndiceInfinitoCabecerasTests
{
    public static TheoryData<string, HttpStatusCode> Respuestas => new()
    {
        { "/indice-infinito/s12-p3-e4-v5-pg100", HttpStatusCode.OK },
        { "/indice-infinito/Mayuscula", HttpStatusCode.OK },
        { "/indice-infinito", HttpStatusCode.Found },
        { "/indice-infinito/acciones/al-azar?desde=s1-p1-e1-v1-pg1", HttpStatusCode.Found },
        { "/indice-infinito/acciones/error-forzado", HttpStatusCode.InternalServerError },
        { "/Indice-Infinito/ABC", HttpStatusCode.OK },
    };

    [Theory]
    [MemberData(nameof(Respuestas))]
    [Trait("Criterio", "AC-IND-04")]
    public async Task Toda_respuesta_del_indice_no_se_indexa_no_se_guarda_y_lleva_las_cabeceras_de_seguridad(string ruta, HttpStatusCode esperado)
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var respuesta = await host.Cliente.GetAsync(ruta);

        Assert.Equal(esperado, respuesta.StatusCode);
        Assert.Equal("noindex, nofollow", respuesta.Headers.GetValues("X-Robots-Tag").Single());
        Assert.Equal("no-store", respuesta.Headers.CacheControl?.ToString());
        Assert.Equal("nosniff", respuesta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(respuesta.Headers.Contains("Strict-Transport-Security"));
        Assert.True(respuesta.Headers.Contains("Referrer-Policy"));
        Assert.True(respuesta.Headers.Contains("Permissions-Policy"));
        var csp = respuesta.Headers.GetValues("Content-Security-Policy").Single();
        Assert.DoesNotContain("unsafe-inline", csp, StringComparison.Ordinal);
        Assert.DoesNotContain("unsafe-eval", csp, StringComparison.Ordinal);
        Assert.Contains("script-src", csp, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/indice-infinito/acciones/al-azar?desde=s1-p1-e1-v1-pg1")]
    [InlineData("/indice-infinito")]
    [InlineData("/indice-infinito?direccion=s12-p3-e4-v5-pg100")]
    [InlineData("/indice-infinito/")]
    [Trait("Criterio", "AC-IND-06")]
    public async Task La_ubicacion_de_toda_redireccion_tiene_la_forma_exacta_de_una_ruta_propia(string ruta)
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var respuesta = await host.Cliente.GetAsync(ruta);

        Assert.Equal(HttpStatusCode.Found, respuesta.StatusCode);
        Assert.Matches(AnfitrionDelIndice.FormaDeLaUbicacion(), respuesta.Headers.Location!.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-04")]
    public async Task El_prefijo_se_compara_por_segmento_las_demas_rutas_no_llevan_las_cabeceras_del_indice()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var otraRuta = await host.Cliente.GetAsync("/indice-infinito-otro");
        var robots = await host.Cliente.GetAsync("/robots.txt");

        Assert.Equal(HttpStatusCode.NotFound, otraRuta.StatusCode);
        Assert.False(otraRuta.Headers.Contains("X-Robots-Tag"));
        Assert.False(robots.Headers.Contains("X-Robots-Tag"));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-04")]
    public async Task El_robots_txt_es_un_archivo_estatico_con_el_disallow_del_indice()
    {
        await using var host = await AnfitrionDelIndice.Iniciar();

        var respuesta = await host.Cliente.GetAsync("/robots.txt");
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Matches(@"(?m)^User-agent: \*\s*$", cuerpo);
        Assert.Matches(@"(?m)^Disallow: /indice-infinito\s*$", cuerpo);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-04")]
    public async Task Los_enlaces_al_indice_desde_el_inicio_y_la_pagina_llevan_nofollow()
    {
        var inicio = File.ReadAllText(RaizDelRepositorio.DeLaApi("Areas/Tienda/Views/Inicio/Index.cshtml"));
        await using var host = await AnfitrionDelIndice.Iniciar();
        var pagina = await (await host.Cliente.GetAsync("/indice-infinito/s1-p1-e1-v1-pg1")).Content.ReadAsStringAsync();

        Assert.Matches("href=\"/indice-infinito\"[^>]*rel=\"nofollow\"", inicio);
        Assert.Matches("href=\"/indice-infinito/acciones/al-azar\\?desde=s1-p1-e1-v1-pg1\"[^>]*rel=\"nofollow\"", pagina);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-04")]
    public void Las_vistas_y_el_script_no_tienen_html_crudo_estilos_en_linea_scripts_en_linea_ni_manejadores()
    {
        var archivos = Directory.GetFiles(RaizDelRepositorio.DeLaApi("Areas/Tienda/Views/IndiceInfinito"), "*.cshtml").Append(RaizDelRepositorio.DeLaApi("wwwroot/js/indice-infinito.js")).ToList();
        Assert.NotEmpty(archivos);

        foreach (var archivo in archivos)
        {
            var texto = File.ReadAllText(archivo);
            Assert.DoesNotContain("Html.Raw", texto, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"\sstyle\s*=", texto);
            Assert.DoesNotMatch(@"\son[a-z]+\s*=", texto);
            Assert.DoesNotMatch(@"<script(?![^>]*\ssrc=)", texto);
            Assert.DoesNotContain("eval(", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("innerHTML", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("document.write", texto, StringComparison.Ordinal);
        }
    }
}
