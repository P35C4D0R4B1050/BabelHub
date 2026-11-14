using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Web;
using Microsoft.AspNetCore.Builder;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Los componentes únicos de estado (<c>bh-estado</c>) y de disponibilidad (<c>bh-disponibilidad</c>), y el indicador de textos pendientes (T25).</summary>
public sealed partial class ComponentesDeEstadoTests
{
    // Razor codifica las tildes como entidades (&#xE9;): para comparar textos se decodifican; la prueba de XSS usa el HTML crudo.
    private static async Task<string> PedirCrudo(AnfitrionWeb anfitrion, string ruta)
    {
        using var respuesta = await anfitrion.Cliente.GetAsync(ruta);
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        return await respuesta.Content.ReadAsStringAsync();
    }

    private static async Task<string> Pedir(AnfitrionWeb anfitrion, string ruta) => WebUtility.HtmlDecode(await PedirCrudo(anfitrion, ruta));

    private static Task<AnfitrionWeb> Iniciar() => AnfitrionWeb.Iniciar(
        partes: [typeof(ComponentesDeEstadoTests).Assembly],
        antesDeLasRutas: app => app.Use((contexto, siguiente) =>
        {
            if (contexto.Request.Query["pendientes"] == "true")
            {
                contexto.Items[ContextoDePagina.ClaveTextosPendientes] = true;
            }

            return siguiente();
        }));

    [GeneratedRegex(@"<(svg|span|p|h2|a|div)[^>]*>", RegexOptions.CultureInvariant)]
    private static partial Regex Etiqueta();

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task El_error_muestra_un_mensaje_simple_y_un_boton_de_reintento_con_role_alert_y_sin_detalles_tecnicos()
    {
        await using var anfitrion = await Iniciar();

        var html = await Pedir(anfitrion, "/prueba/estado/error");

        Assert.Contains("role=\"alert\"", html, StringComparison.Ordinal);
        Assert.Contains("Algo salió mal", html, StringComparison.Ordinal);
        Assert.Matches(@"<a[^>]*href=""/catalogo""[^>]*>[^<]*(<[^>]*>[^<]*)*Reintentar", html);
        Assert.DoesNotContain("Exception", html, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", html, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task El_soporte_con_el_codigo_y_el_seguimiento_va_en_un_detalle_plegable()
    {
        await using var anfitrion = await Iniciar();

        var html = await Pedir(anfitrion, "/prueba/estado/error-con-soporte");

        Assert.Matches(@"<details[^>]*>\s*<summary[^>]*>Soporte</summary>", html);
        Assert.Contains("ECO-ERROR-INESPERADO", html, StringComparison.Ordinal);
        Assert.Contains("abc123", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cargando", "role=\"status\"", "aria-busy=\"true\"")]
    [InlineData("vacio", "Tu carrito está vacío", "Explora el catálogo")]
    [InlineData("sin-permiso", "Tu cuenta no tiene acceso a esta sección.", "href=\"/\"")]
    [InlineData("no-disponible", "No podemos mostrar esto ahora", "No podemos validar el stock ahora.")]
    [InlineData("exito", "role=\"status\"", "Guardado")]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task Cada_estado_estandar_se_pinta_con_su_patron_y_su_aviso_accesible(string estado, string esperado1, string esperado2)
    {
        await using var anfitrion = await Iniciar();

        var html = await Pedir(anfitrion, $"/prueba/estado/{estado}");

        Assert.Contains(esperado1, html, StringComparison.Ordinal);
        Assert.Contains(esperado2, html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task El_servicio_no_disponible_ofrece_reintento_y_el_sin_permiso_ofrece_salidas()
    {
        await using var anfitrion = await Iniciar();

        var noDisponible = await Pedir(anfitrion, "/prueba/estado/no-disponible");
        var sinPermiso = await Pedir(anfitrion, "/prueba/estado/sin-permiso");

        Assert.Contains("Reintentar", noDisponible, StringComparison.Ordinal);
        Assert.Contains("Ir al inicio", sinPermiso, StringComparison.Ordinal);
        Assert.Contains("Ver mi cuenta", sinPermiso, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task Un_texto_del_modelo_se_codifica_en_el_estado()
    {
        await using var anfitrion = await Iniciar();

        var html = await PedirCrudo(anfitrion, "/prueba/estado/xss");

        Assert.DoesNotContain("<script>alert(1)</script>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-tu-sucursal", "En tu sucursal", "badge-success")]
    [InlineData("en-otras", "En 3 sucursales más", "badge-info")]
    [InlineData("agotado", "Agotado", "badge-error")]
    [InlineData("al-instante", "Disponible al instante", "badge-success")]
    [InlineData("sin-confirmar", "No se puede confirmar ahora", "badge-warning")]
    [Trait("Criterio", "AC-WEB-09")]
    public async Task La_disponibilidad_tiene_texto_icono_y_color(string clave, string texto, string color)
    {
        await using var anfitrion = await Iniciar();

        var html = await Pedir(anfitrion, $"/prueba/disponibilidad/{clave}");

        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.Contains(color, html, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
        Assert.Contains("aria-hidden=\"true\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-09")]
    public async Task Una_edicion_agotada_no_depende_solo_del_color()
    {
        await using var anfitrion = await Iniciar();

        var html = await Pedir(anfitrion, "/prueba/disponibilidad/agotado");

        // El texto visible está presente aunque se quitaran las clases de color y el icono.
        var sinEtiquetas = Etiqueta().Replace(html, " ");
        Assert.Contains("Agotado", sinEtiquetas, StringComparison.Ordinal);
        Assert.Contains("<svg", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-36")]
    public async Task El_layout_avisa_de_los_textos_pendientes_con_un_enlace_para_aceptarlos()
    {
        await using var anfitrion = await Iniciar();

        var html = await Pedir(anfitrion, "/?pendientes=true");

        Assert.Contains("Actualizamos nuestros términos", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/textos/aceptar\"", html, StringComparison.Ordinal);
    }
}
