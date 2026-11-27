using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Carrito;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// La ficha permite comprar (CAR-02 a CAR-04, CAT-12; tarea T38 de <c>ecommerce-carrito-checkout</c>). Las pruebas <b>no arman el <c>POST</c> a mano</b>: piden la ficha, toman el formulario que la página ofrece (ruta, método,
/// campos ocultos, token antifalsificación y cookie) y lo envían como lo haría un navegador sin JavaScript. Si la ficha deja de ofrecer el formulario o cambia un campo, estas pruebas fallan.
/// </summary>
public sealed partial class ComprarDesdeLaFichaTests
{
    private static readonly Guid Obra = Guid.Parse("11111111-2222-3333-4444-555555555601");
    private static readonly Guid Impresa = Guid.Parse("e0000000-0000-0000-0000-0000000000b1");
    private static readonly Guid Digital = Guid.Parse("e0000000-0000-0000-0000-0000000000b2");

    private sealed class CatalogoDeUnaObra(ResumenDeObra obra) : IConsultaDeCatalogo
    {
        public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => Task.FromResult(Resultado<ResumenDeObra>.Exito(obra));

        public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class ListaDePrueba : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([new("AYA", "Ayacucho")], true));
    }

    private static DisponibilidadDeEdicion Disp(Guid edicion, EstadoDeDisponibilidad estado, int? vendible, FrescuraDeLaDisponibilidad frescura = FrescuraDeLaDisponibilidad.Reciente, MotivoDeNoDisponibleDeEdicion? motivo = null) =>
        new(edicion, estado, vendible, vendible is { } v ? [new DisponibilidadPorFuente("AYA", "Ayacucho", v)] : [], frescura, DateTimeOffset.UtcNow, motivo);

    private static ResumenDeObra ObraDe(DisponibilidadDeEdicion? impresa = null, DisponibilidadDeEdicion? digital = null) => new(
        Obra,
        "Estructuras de datos",
        "Ana Quispe",
        "Editorial Andina",
        "Técnico",
        new string('a', 64),
        [
            new EdicionDeObra(Impresa, FormatoDeEdicion.Impresa, "9780000000019", 129.90m, impresa ?? Disp(Impresa, EstadoDeDisponibilidad.Disponible, 3)),
            new EdicionDeObra(Digital, FormatoDeEdicion.Digital, "9790000000018", 49m, digital ?? Disp(Digital, EstadoDeDisponibilidad.DisponibleAlInstante, null)),
        ]);

    private static Task<AnfitrionWeb> Iniciar(ResumenDeObra obra, CarritoDelClienteFalso carrito, bool textosPendientes = false) =>
        AnfitrionWeb.Iniciar(
            servicios =>
            {
                servicios.AddSingleton<IConsultaDeCatalogo>(new CatalogoDeUnaObra(obra));
                servicios.AddSingleton<ICarritoDelCliente>(carrito);
                servicios.AddSingleton<IListaDeSucursales>(new ListaDePrueba());
                servicios.AddSingleton<IActorActual>(new ActorAnonimo());
                servicios.AgregarProteccionContraFalsificacion();
                servicios.AddSingleton(new OpcionesDelHostWeb(HostWeb.Tienda, []));
            },
            antesDeLasRutas: textosPendientes
                ? app => app.Use((contexto, siguiente) =>
                {
                    contexto.Items[ContextoDePagina.ClaveTextosPendientes] = true;
                    return siguiente(contexto);
                })
                : null);

    private sealed class ActorAnonimo : IActorActual
    {
        public ActorActual? Actual { get; } = ActorActual.Anonimo();
    }

    /// <summary>La ficha tal como la recibe el navegador: el HTML decodificado y la cookie antifalsificación que trae.</summary>
    private sealed record Ficha(string Html, string CookieAntifalsificacion)
    {
        public IReadOnlyList<Formulario> FormulariosDeCompra =>
            [.. Formularios().Matches(Html).Select(m => Formulario.De(m)).Where(f => f.Accion == "/carrito/lineas")];
    }

    private sealed record Formulario(string Accion, string Metodo, IReadOnlyDictionary<string, string> Campos, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Atributos, string Cuerpo)
    {
        public static Formulario De(Match formulario)
        {
            var apertura = AtributosDe(formulario.Groups["apertura"].Value);
            var cuerpo = formulario.Groups["cuerpo"].Value;
            var controles = Entradas().Matches(cuerpo).Select(m => AtributosDe(m.Groups["atributos"].Value)).Where(a => a.ContainsKey("name")).ToList();
            return new Formulario(
                apertura.GetValueOrDefault("action", string.Empty),
                apertura.GetValueOrDefault("method", "get"),
                controles.ToDictionary(a => a["name"], a => a.GetValueOrDefault("value", string.Empty), StringComparer.Ordinal),
                controles.ToDictionary(a => a["name"], a => a, StringComparer.Ordinal),
                cuerpo);
        }

        private static IReadOnlyDictionary<string, string> AtributosDe(string texto) =>
            Atributo().Matches(texto).ToDictionary(m => m.Groups["nombre"].Value, m => m.Groups["valor"].Value, StringComparer.OrdinalIgnoreCase);
    }

    [GeneratedRegex("<form(?<apertura>[^>]*)>(?<cuerpo>.*?)</form>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Formularios();

    [GeneratedRegex("<input(?<atributos>[^>]*?)/?>", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Entradas();

    [GeneratedRegex("(?<nombre>[A-Za-z_:][A-Za-z0-9_:\\-]*)(?:\\s*=\\s*\"(?<valor>[^\"]*)\")?", RegexOptions.CultureInvariant)]
    private static partial Regex Atributo();

    private static async Task<Ficha> Abrir(AnfitrionWeb anfitrion, string edicion = "impresa")
    {
        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{Obra:D}?edicion={edicion}");
        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        var cookie = respuesta.Headers.TryGetValues("Set-Cookie", out var cookies)
            ? cookies.Single(c => c.StartsWith(ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal)).Split(';')[0]
            : string.Empty;
        return new Ficha(WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync()), cookie);
    }

    /// <summary>Envía el formulario de la página como un navegador sin JavaScript; <paramref name="escrito"/> es lo que la persona teclea en un campo visible.</summary>
    private static async Task<HttpResponseMessage> Enviar(AnfitrionWeb anfitrion, Ficha ficha, Formulario formulario, params (string Campo, string Valor)[] escrito)
    {
        var campos = new Dictionary<string, string>(formulario.Campos, StringComparer.Ordinal);
        foreach (var (campo, valor) in escrito)
        {
            Assert.True(campos.ContainsKey(campo), $"El formulario de la ficha no ofrece el campo «{campo}».");
            campos[campo] = valor;
        }

        using var solicitud = new HttpRequestMessage(new HttpMethod(formulario.Metodo.ToUpperInvariant()), formulario.Accion) { Content = new FormUrlEncodedContent(campos) };
        solicitud.Headers.TryAddWithoutValidation("Accept", "text/html");
        solicitud.Headers.TryAddWithoutValidation("Origin", "https://localhost");
        solicitud.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
        solicitud.Headers.TryAddWithoutValidation("Cookie", ficha.CookieAntifalsificacion);
        return await anfitrion.Cliente.SendAsync(solicitud);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-02")]
    [Trait("Criterio", "AC-CAT-07")]
    public async Task El_formulario_de_la_edicion_impresa_agrega_la_cantidad_escrita_y_lleva_al_carrito()
    {
        var carrito = new CarritoDelClienteFalso();
        await using var anfitrion = await Iniciar(ObraDe(), carrito);
        var ficha = await Abrir(anfitrion);

        var formulario = Assert.Single(ficha.FormulariosDeCompra);
        Assert.Contains("Agregar impreso al carrito", formulario.Cuerpo, StringComparison.Ordinal);
        Assert.Equal("1", formulario.Campos["cantidad"]);
        using var respuesta = await Enviar(anfitrion, ficha, formulario, ("cantidad", "2"));

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Equal("/carrito", respuesta.Headers.Location?.OriginalString);
        var entrada = Assert.IsType<AgregarLineaEntrada>(Assert.Single(carrito.Entradas));
        Assert.Equal(Impresa, entrada.EdicionId);
        Assert.Equal(2, entrada.Cantidad);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-02")]
    public async Task El_formulario_de_la_edicion_digital_agrega_una_sola_unidad_sin_pedir_cantidad()
    {
        var carrito = new CarritoDelClienteFalso();
        await using var anfitrion = await Iniciar(ObraDe(), carrito);
        var ficha = await Abrir(anfitrion, "digital");

        var formulario = Assert.Single(ficha.FormulariosDeCompra);
        Assert.Contains("Comprar digital", formulario.Cuerpo, StringComparison.Ordinal);
        Assert.Equal("hidden", formulario.Atributos["cantidad"]["type"]);
        using var respuesta = await Enviar(anfitrion, ficha, formulario);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        var entrada = Assert.IsType<AgregarLineaEntrada>(Assert.Single(carrito.Entradas));
        Assert.Equal(Digital, entrada.EdicionId);
        Assert.Equal(1, entrada.Cantidad);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-03")]
    [Trait("Criterio", "AC-CAR-32")]
    [InlineData(3, "3")]
    [InlineData(57, "20")]
    public async Task La_cantidad_de_un_impreso_se_limita_a_lo_vendible_en_la_red_y_a_veinte(int vendible, string maximo)
    {
        await using var anfitrion = await Iniciar(ObraDe(impresa: Disp(Impresa, EstadoDeDisponibilidad.Disponible, vendible)), new CarritoDelClienteFalso());

        var formulario = Assert.Single((await Abrir(anfitrion)).FormulariosDeCompra);

        var cantidad = formulario.Atributos["cantidad"];
        Assert.Equal("number", cantidad["type"]);
        Assert.Equal("1", cantidad["min"]);
        Assert.Equal(maximo, cantidad["max"]);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Sin_poder_confirmar_el_stock_se_puede_agregar_igual_y_se_envia_el_formulario()
    {
        var carrito = new CarritoDelClienteFalso();
        var sinConfirmar = Disp(Impresa, EstadoDeDisponibilidad.NoSePuedeConfirmar, null, FrescuraDeLaDisponibilidad.NoSePuedeConfirmar);
        await using var anfitrion = await Iniciar(ObraDe(impresa: sinConfirmar), carrito);
        var ficha = await Abrir(anfitrion);

        var formulario = Assert.Single(ficha.FormulariosDeCompra);
        Assert.Equal("20", formulario.Atributos["cantidad"]["max"]);
        using var respuesta = await Enviar(anfitrion, ficha, formulario);

        Assert.Equal(HttpStatusCode.SeeOther, respuesta.StatusCode);
        Assert.Contains("No podemos confirmar el stock ahora", ficha.Html, StringComparison.Ordinal);
        Assert.Equal(Impresa, Assert.IsType<AgregarLineaEntrada>(Assert.Single(carrito.Entradas)).EdicionId);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-03")]
    public async Task Un_impreso_agotado_en_toda_la_red_no_se_puede_agregar_y_la_digital_sigue_a_la_venta()
    {
        await using var anfitrion = await Iniciar(ObraDe(impresa: Disp(Impresa, EstadoDeDisponibilidad.Agotada, 0)), new CarritoDelClienteFalso());

        var impresa = await Abrir(anfitrion);
        var digital = await Abrir(anfitrion, "digital");

        Assert.Empty(impresa.FormulariosDeCompra);
        Assert.Matches("<button[^>]*\\sdisabled[^>]*>(?:(?!</button>).)*Agregar impreso al carrito", impresa.Html.ReplaceLineEndings(" "));
        Assert.Single(digital.FormulariosDeCompra);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-04")]
    [InlineData(MotivoDeNoDisponibleDeEdicion.LicenciaVencida)]
    [InlineData(MotivoDeNoDisponibleDeEdicion.SinPeru)]
    [InlineData(MotivoDeNoDisponibleDeEdicion.RetiradaDeLaVenta)]
    public async Task Una_digital_que_no_se_ofrece_no_se_puede_comprar(MotivoDeNoDisponibleDeEdicion motivo)
    {
        await using var anfitrion = await Iniciar(ObraDe(digital: Disp(Digital, EstadoDeDisponibilidad.NoSeOfrece, null, motivo: motivo)), new CarritoDelClienteFalso());

        var ficha = await Abrir(anfitrion, "digital");

        Assert.Empty(ficha.FormulariosDeCompra);
        Assert.DoesNotContain("Comprar digital", ficha.Html, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-WEB-36")]
    [InlineData("impresa")]
    [InlineData("digital")]
    public async Task Con_textos_legales_pendientes_la_ficha_no_ofrece_comprar(string edicion)
    {
        await using var anfitrion = await Iniciar(ObraDe(), new CarritoDelClienteFalso(), textosPendientes: true);

        var ficha = await Abrir(anfitrion, edicion);

        Assert.Empty(ficha.FormulariosDeCompra);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-03")]
    [InlineData("3", "El máximo disponible es de 3 unidades")]
    [InlineData("1", "El máximo disponible es de 1 unidad,")]
    [InlineData("0", "Ya no quedan unidades de este libro.")]
    public async Task Pedir_mas_unidades_que_las_vendibles_se_rechaza_en_pantalla_indicando_el_maximo(string maximo, string texto)
    {
        var carrito = new CarritoDelClienteFalso
        {
            Respuesta = Resultado<ResultadoDeCarrito>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "carrito-sin-stock", [new ErrorDeCampo("maximo", maximo)])),
        };
        await using var anfitrion = await Iniciar(ObraDe(), carrito);
        var ficha = await Abrir(anfitrion);

        using var respuesta = await Enviar(anfitrion, ficha, Assert.Single(ficha.FormulariosDeCompra), ("cantidad", "3"));
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Conflict, respuesta.StatusCode);
        Assert.Contains("No hay stock suficiente", html, StringComparison.Ordinal);
        Assert.Contains(texto, html, StringComparison.Ordinal);
        Assert.Contains("href=\"/carrito\"", html, StringComparison.Ordinal);
    }
}
