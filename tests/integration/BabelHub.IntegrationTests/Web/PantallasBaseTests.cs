using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Las pantallas de T26: textos legales, preferencias de tema y sucursal, «Sorpréndeme» y las de error, sin permiso y servicio no disponible.</summary>
public sealed partial class PantallasBaseTests
{
    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"(?<token>[^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex TokenDelFormulario();

    private sealed class CatalogoDePrueba(Func<Resultado<ObraAlAzar?>> respuesta) : IConsultaDeCatalogo
    {
        public int Llamadas { get; private set; }

        public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default)
        {
            Llamadas++;
            return Task.FromResult(respuesta());
        }

        public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class ListaDePrueba(ListaDeSucursales? lista) : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult(lista);
    }

    private static readonly ListaDeSucursales AyacuchoYLima = new([new("AYA", "Ayacucho"), new("LIM", "Lima")], true);

    private static async Task<string> Texto(HttpResponseMessage respuesta) => WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

    // ---- Textos legales ----

    [Theory]
    [InlineData("terminos", "Términos y condiciones")]
    [InlineData("privacidad", "Política de privacidad")]
    [InlineData("cambios-y-devoluciones", "Cambios y devoluciones")]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Cada_texto_legal_muestra_su_version_y_su_vigencia_sin_marca_de_provisional(string clave, string titulo)
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        using var respuesta = await anfitrion.Cliente.GetAsync($"/legal/{clave}");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains(titulo, html, StringComparison.Ordinal);
        Assert.Matches(@"Versión\s*<strong>0\.1-provisional</strong>", html);
        Assert.Matches(@"Vigente desde\s*<time datetime=""2026-11-14"">14/11/2026</time>", html);
        // La persona responsable aprobó los textos definitivos (DD-115): la marca ya no debe verse (la versión de este anfitrión es la de desarrollo).
        Assert.DoesNotContain("Texto provisional", html, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(html, "<h1[ >]"));
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task La_privacidad_incluye_las_cookies_como_lista()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var html = await Texto(await anfitrion.Cliente.GetAsync("/legal/privacidad"));

        Assert.Contains("<li>__Host-bh-tienda y __Host-bh-gestion:", html, StringComparison.Ordinal);
        Assert.Contains("<li>bh-sucursal:", html, StringComparison.Ordinal);
        Assert.Equal(Regex.Matches(html, "<ul[ >]").Count, Regex.Matches(html, "</ul>").Count);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Un_texto_inexistente_es_un_404_con_el_estado_de_no_encontrado()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        using var respuesta = await anfitrion.Cliente.GetAsync("/legal/cualquier-cosa");

        Assert.Equal(HttpStatusCode.NotFound, respuesta.StatusCode);
        Assert.Contains("No encontramos ese texto", await Texto(respuesta), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-24")]
    public async Task Sin_la_version_de_Identidad_el_texto_es_servicio_no_disponible_con_reintento()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar(s => s.AddSingleton<IVersionesDeTextosLegales>(new SinVersiones()));

        using var respuesta = await anfitrion.Cliente.GetAsync("/legal/terminos");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Contains("No podemos mostrar este texto ahora.", html, StringComparison.Ordinal);
        Assert.Contains("Reintentar", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Vigente desde", html, StringComparison.Ordinal);
    }

    private sealed class SinVersiones : IVersionesDeTextosLegales
    {
        public Task<VersionVigenteDeTexto?> ObtenerVigente(TipoDeTextoLegal tipo, CancellationToken cancelacion = default) => Task.FromResult<VersionVigenteDeTexto?>(null);
    }

    // ---- Pantallas de estado ----

    [Theory]
    [InlineData("/error", HttpStatusCode.InternalServerError, "Algo salió mal")]
    [InlineData("/sin-permiso", HttpStatusCode.Forbidden, "Tu cuenta no tiene acceso a esta sección.")]
    [InlineData("/no-disponible", HttpStatusCode.ServiceUnavailable, "No podemos mostrar esto ahora")]
    [Trait("Criterio", "AC-WEB-04")]
    public async Task Las_pantallas_de_error_sin_permiso_y_no_disponible_usan_el_componente_unico_dentro_del_layout(string ruta, HttpStatusCode codigo, string texto)
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        using var respuesta = await anfitrion.Cliente.GetAsync(ruta);
        var html = await Texto(respuesta);

        Assert.Equal(codigo, respuesta.StatusCode);
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.Contains("class=\"bh-estado", html, StringComparison.Ordinal);
        Assert.Contains("Saltar al contenido", html, StringComparison.Ordinal);
        Assert.Contains("no-store", string.Join(',', respuesta.Headers.CacheControl?.ToString() ?? string.Empty), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-02")]
    public async Task El_sin_permiso_ofrece_salidas_y_no_revela_nada_del_recurso()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var html = await Texto(await anfitrion.Cliente.GetAsync("/sin-permiso"));

        Assert.Contains("href=\"/\"", html, StringComparison.Ordinal);
        Assert.Contains("Ir al inicio", html, StringComparison.Ordinal);
        Assert.Contains("Ver mi cuenta", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-21")]
    public async Task El_reintento_de_no_disponible_es_un_enlace_GET_a_la_misma_pantalla_sin_destino()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        var html = await Texto(await anfitrion.Cliente.GetAsync("/no-disponible"));

        Assert.Matches(@"<a[^>]*href=""/no-disponible""[^>]*>", html);
        Assert.DoesNotContain("returnUrl", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("destino=", html, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Sorpréndeme ----

    [Fact]
    [Trait("Criterio", "AC-WEB-13")]
    public async Task Sorprendeme_lleva_a_la_ficha_de_un_libro_disponible_que_elige_el_catalogo()
    {
        var disponible = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var catalogo = new CatalogoDePrueba(() => Resultado<ObraAlAzar?>.Exito(new ObraAlAzar(disponible)));
        await using var anfitrion = await AnfitrionWeb.Iniciar(s => s.AddSingleton<IConsultaDeCatalogo>(catalogo));

        using var respuesta = await anfitrion.Cliente.GetAsync("/sorprendeme");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal($"/catalogo/obras/{disponible}", respuesta.Headers.Location?.OriginalString);
        Assert.Equal(1, catalogo.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-13")]
    public async Task Sorprendeme_sin_libros_disponibles_lo_dice_y_ofrece_el_catalogo()
    {
        var catalogo = new CatalogoDePrueba(() => Resultado<ObraAlAzar?>.Exito(null));
        await using var anfitrion = await AnfitrionWeb.Iniciar(s => s.AddSingleton<IConsultaDeCatalogo>(catalogo));

        using var respuesta = await anfitrion.Cliente.GetAsync("/sorprendeme");
        var html = await Texto(respuesta);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Por ahora no hay libros para sorprenderte", html, StringComparison.Ordinal);
        Assert.Contains("href=\"/catalogo\"", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-13")]
    public async Task Sorprendeme_con_el_catalogo_caido_es_servicio_no_disponible()
    {
        var catalogo = new CatalogoDePrueba(() => Resultado<ObraAlAzar?>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "x")));
        await using var anfitrion = await AnfitrionWeb.Iniciar(s => s.AddSingleton<IConsultaDeCatalogo>(catalogo));

        using var respuesta = await anfitrion.Cliente.GetAsync("/sorprendeme");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.Contains("No podemos elegir un libro ahora.", await Texto(respuesta), StringComparison.Ordinal);
    }

    // ---- Preferencias ----

    private static async Task<(string Token, string CookieDeToken)> ObtenerToken(AnfitrionWeb anfitrion)
    {
        using var respuesta = await anfitrion.Cliente.GetAsync("/");
        var html = await respuesta.Content.ReadAsStringAsync();
        var token = TokenDelFormulario().Match(html).Groups["token"].Value;
        var cookies = respuesta.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]);
        return (token, string.Join("; ", cookies));
    }

    private static async Task<HttpResponseMessage> Publicar(AnfitrionWeb anfitrion, string ruta, Dictionary<string, string> campos, bool conToken = true, string? referente = null)
    {
        var (token, cookie) = await ObtenerToken(anfitrion);
        if (conToken)
        {
            campos["__RequestVerificationToken"] = token;
        }

        using var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = new FormUrlEncodedContent(campos) };
        solicitud.Headers.Add("Cookie", cookie);
        if (referente is not null)
        {
            solicitud.Headers.Referrer = new Uri(referente);
        }

        return await anfitrion.Cliente.SendAsync(solicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-06")]
    public async Task Elegir_el_tema_oscuro_lo_recuerda_en_una_cookie_persistente_sin_datos_sensibles()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        using var respuesta = await Publicar(anfitrion, "/preferencias/tema", new() { ["tema"] = "babelhub-dark" }, referente: "http://localhost/legal/terminos");

        Assert.Equal(HttpStatusCode.Redirect, respuesta.StatusCode);
        Assert.Equal("/legal/terminos", respuesta.Headers.Location?.OriginalString);
        var cookie = respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("bh-tema=", StringComparison.Ordinal));
        Assert.StartsWith("bh-tema=babelhub-dark", cookie, StringComparison.Ordinal);
        Assert.Contains("max-age=31536000", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("rojo")]
    [InlineData("babelhub; Domain=.otro.com")]
    [Trait("Criterio", "AC-WEB-06")]
    public async Task Un_tema_desconocido_o_dispositivo_borra_la_eleccion_y_nunca_se_guarda(string tema)
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        using var respuesta = await Publicar(anfitrion, "/preferencias/tema", new() { ["tema"] = tema });

        var cookie = respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("bh-tema=", StringComparison.Ordinal));
        Assert.StartsWith("bh-tema=;", cookie, StringComparison.Ordinal);
        Assert.Contains("expires=Thu, 01 Jan 1970", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("https://sitio-malo.example/ruta")]
    [InlineData("http://localhost//sitio-malo.example")]
    [Trait("Criterio", "AC-WEB-06")]
    public async Task El_retorno_tras_una_preferencia_nunca_sale_del_sitio(string referente)
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        using var respuesta = await Publicar(anfitrion, "/preferencias/tema", new() { ["tema"] = "babelhub" }, referente: referente);

        Assert.Equal("/", respuesta.Headers.Location?.OriginalString);
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-03")]
    public async Task Las_preferencias_sin_token_antifalsificacion_se_rechazan_y_no_guardan_nada()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar();

        using var respuesta = await Publicar(anfitrion, "/preferencias/tema", new() { ["tema"] = "babelhub-dark" }, conToken: false);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.False(respuesta.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("bh-tema=", StringComparison.Ordinal)));
    }

    // ---------- Sucursal preferida (WEB-16): sin selector en la cabecera ----------

    [Fact]
    [Trait("Criterio", "AC-WEB-12")]
    public async Task La_cabecera_no_tiene_selector_de_sucursal_ni_existe_su_ruta()
    {
        await using var anfitrion = await AnfitrionWeb.Iniciar(s => s.AddSingleton<IListaDeSucursales>(new ListaDePrueba(AyacuchoYLima)));
        using var conCookie = new HttpRequestMessage(HttpMethod.Get, "/");
        conCookie.Headers.Add("Cookie", "bh-sucursal=AYA");

        var html = await Texto(await anfitrion.Cliente.SendAsync(conCookie));
        using var eleccion = await Publicar(anfitrion, "/preferencias/sucursal", new() { ["sucursal"] = "AYA" });

        Assert.DoesNotContain("bh-sucursal", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/preferencias/sucursal", html, StringComparison.Ordinal);
        Assert.DoesNotContain("En tu sucursal", html, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.NotFound, eleccion.StatusCode);
        Assert.False(eleccion.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(c => c.StartsWith("bh-sucursal=", StringComparison.Ordinal)));
    }
}
