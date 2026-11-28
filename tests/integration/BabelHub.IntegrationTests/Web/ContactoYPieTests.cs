using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Controllers;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Api.Web.Contacto;
using BabelHub.Ecommerce.Api.Web.Estados;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Una página de prueba de cada área (Tienda, Sucursal y Administración) para comprobar que el pie es el mismo en todas (COM-01).</summary>
[Route("prueba-pie")]
public sealed class ControladorDePiePorAreaDePrueba(ContextoDePagina pagina) : Controller
{
    [HttpGet("{area}")]
    public async Task<IActionResult> Ver(string area, CancellationToken cancelacion)
    {
        var elegida = area switch { "sucursal" => AreaDeLaPagina.Sucursal, "admin" => AreaDeLaPagina.Admin, _ => AreaDeLaPagina.Tienda };
        var modelo = await pagina.Completar(new PantallaDeEstadoVista { Estado = EstadoVista.Exito("Prueba", "Página de prueba del pie.") }, "Prueba del pie", elegida, cancelacion);
        return View("~/Views/Estados/Pantalla.cshtml", modelo);
    }
}

/// <summary>El pie con el contacto del comercio, la página de contacto y las frases del comercio (COM-01, COM-06 a COM-09; AC-COM-01, AC-COM-05, AC-COM-07).</summary>
public sealed partial class ContactoYPieTests
{
    [GeneratedRegex(@"<footer[^>]*>(?<pie>.*?)</footer>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Pie();

    [GeneratedRegex(@"<main[^>]*>(?<contenido>.*?)</main>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Principal();

    private static async Task<(HttpResponseMessage Respuesta, string Html)> Pedir(AnfitrionWeb a, string ruta)
    {
        var respuesta = await a.Cliente.GetAsync(ruta);
        return (respuesta, WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()));
    }

    private static void ElPieTieneElContacto(string html)
    {
        var pie = Pie().Match(html).Groups["pie"].Value;
        Assert.NotEmpty(pie);
        Assert.Contains("href=\"tel:+51999000000\"", pie, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:contacto@ejemplo.test\"", pie, StringComparison.Ordinal);
        Assert.Contains("999 000 000", pie, StringComparison.Ordinal);
        Assert.Contains("contacto@ejemplo.test", pie, StringComparison.Ordinal);
        Assert.Contains("dato de ejemplo", pie, StringComparison.Ordinal);
        Assert.Contains("href=\"/contacto\"", pie, StringComparison.Ordinal);
        Assert.Contains("href=\"/reclamaciones\"", pie, StringComparison.Ordinal);
        Assert.Contains("href=\"/legal/cambios-y-devoluciones\"", pie, StringComparison.Ordinal);
        Assert.Contains("Cambios y devoluciones", pie, StringComparison.Ordinal);
        Assert.Contains("libros impresos y libros digitales", pie, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/legal/terminos")]
    [InlineData("/contacto")]
    [InlineData("/error")]
    [InlineData("/prueba-pie/sucursal")]
    [InlineData("/prueba-pie/admin")]
    [Trait("Criterio", "AC-COM-01")]
    public async Task El_pie_de_cada_pagina_muestra_el_contacto_y_enlaza_al_contacto_al_Libro_y_a_cambios_y_devoluciones(string ruta)
    {
        await using var a = await AnfitrionWeb.Iniciar(partes: [typeof(ContactoYPieTests).Assembly]);

        var (_, html) = await Pedir(a, ruta);

        ElPieTieneElContacto(html);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-05")]
    public async Task La_pagina_de_contacto_muestra_los_tres_datos_sin_formulario_ni_recursos_de_terceros()
    {
        await using var a = await AnfitrionWeb.Iniciar();

        var (respuesta, html) = await Pedir(a, "/contacto");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var principal = Principal().Match(html).Groups["contenido"].Value;
        Assert.Contains("<h1", principal, StringComparison.Ordinal);
        Assert.Contains("999 000 000", principal, StringComparison.Ordinal);
        Assert.Contains("href=\"tel:+51999000000\"", principal, StringComparison.Ordinal);
        Assert.Contains("contacto@ejemplo.test", principal, StringComparison.Ordinal);
        Assert.Contains("href=\"mailto:contacto@ejemplo.test\"", principal, StringComparison.Ordinal);
        Assert.Contains("Jr. Ejemplo 123, Ayacucho (dato de ejemplo)", principal, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", principal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<input", principal, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(@"(?i)(src|href|action)=""(https?:)?//", html);
        Assert.DoesNotContain("Horario", principal, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-04")]
    public async Task Con_datos_configurados_el_pie_y_la_pagina_muestran_los_configurados_y_los_codifican()
    {
        var contacto = LeerContacto("+51 987 654 321", "atencion@tienda.example", "Jr. Callao 123, Breña (esquina con ñ)");
        await using var a = await AnfitrionWeb.Iniciar(s => { s.RemoveAll<DatosDeContacto>(); s.AddSingleton(contacto); });

        var (_, inicio) = await Pedir(a, "/");
        var (_, pagina) = await Pedir(a, "/contacto");

        foreach (var html in new[] { inicio, pagina })
        {
            Assert.Contains("href=\"tel:+51987654321\"", html, StringComparison.Ordinal);
            Assert.Contains("+51 987 654 321", html, StringComparison.Ordinal);
            Assert.Contains("href=\"mailto:atencion@tienda.example\"", html, StringComparison.Ordinal);
            Assert.Contains("Jr. Callao 123, Breña", html, StringComparison.Ordinal);
            Assert.DoesNotContain("999 000 000", html, StringComparison.Ordinal);
            Assert.DoesNotContain("dato de ejemplo", html, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-COM-01")]
    public async Task Sin_el_contacto_registrado_el_pie_omite_el_bloque_y_la_pagina_responde_servicio_no_disponible_y_no_un_error()
    {
        await using var a = await AnfitrionWeb.Iniciar(s => s.RemoveAll<DatosDeContacto>());

        var (inicio, htmlInicio) = await Pedir(a, "/");
        var (contacto, _) = await Pedir(a, "/contacto");

        Assert.Equal(HttpStatusCode.OK, inicio.StatusCode);
        var pie = Pie().Match(htmlInicio).Groups["pie"].Value;
        Assert.NotEmpty(pie);
        Assert.DoesNotContain("tel:", pie, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, contacto.StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-07")]
    public async Task El_inicio_dice_que_el_comercio_vende_libros_impresos_y_libros_digitales()
    {
        await using var a = await AnfitrionWeb.Iniciar();

        var (_, html) = await Pedir(a, "/");

        var principal = Principal().Match(html).Groups["contenido"].Value;
        Assert.Contains("libros impresos y libros digitales", principal, StringComparison.Ordinal);
        Assert.Contains("libros impresos y libros digitales", Pie().Match(html).Groups["pie"].Value, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-08")]
    public async Task La_pagina_de_cambios_y_devoluciones_muestra_su_titulo_su_version_y_su_vigencia()
    {
        await using var a = await AnfitrionWeb.Iniciar();

        var (respuesta, html) = await Pedir(a, "/legal/cambios-y-devoluciones");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("<h1 id=\"titulo-legal\" class=\"font-display text-4xl\">Cambios y devoluciones</h1>", html, StringComparison.Ordinal);
        Assert.Matches(@"Versión\s*<strong>[^<]+</strong>", html);
        Assert.Matches(@"Vigente desde\s*<time datetime=""\d{4}-\d{2}-\d{2}"">", html);
    }

    [Fact]
    [Trait("Criterio", "AC-COM-09")]
    public async Task La_direccion_anterior_del_reembolso_redirige_de_forma_permanente_sin_copiar_la_consulta()
    {
        await using var a = await AnfitrionWeb.Iniciar();

        var respuesta = await a.Cliente.GetAsync("/legal/reembolso?destino=https://otro.example&x=1");

        Assert.Equal(HttpStatusCode.MovedPermanently, respuesta.StatusCode);
        Assert.Equal("/legal/cambios-y-devoluciones", respuesta.Headers.Location!.OriginalString);
    }

    private static DatosDeContacto LeerContacto(string telefono, string correo, string direccion)
    {
        var configuracion = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [DatosDeContacto.RutaDelTelefono] = telefono,
            [DatosDeContacto.RutaDelCorreo] = correo,
            [DatosDeContacto.RutaDeLaDireccion] = direccion,
        }).Build();
        var lector = new BabelHub.Ecommerce.Infrastructure.Plataforma.LectorDeSecretos(configuracion);
        var datos = DatosDeContacto.Leer(lector, permitirEjemplo: false);
        Assert.Empty(lector.Problemas);
        return datos!;
    }
}
