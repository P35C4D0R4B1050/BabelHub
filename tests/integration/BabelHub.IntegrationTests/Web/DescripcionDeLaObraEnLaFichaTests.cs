using System.Net;
using BabelHub.Ecommerce.Api.Web.Formato;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// La descripción de la obra en la ficha pública (T32 y T33; CAT-24, CAT-25, AC-CAT-20): portada, descripción y precio de lo que se ofrece, sin bloque cuando falta, saltos de línea conservados,
/// el marcado mostrado tal como se escribió (escapado por la vista, nunca ejecutado) y la lista del catálogo sin la descripción (docs/06 §2.1).
/// </summary>
public sealed class DescripcionDeLaObraEnLaFichaTests
{
    private static readonly Guid ObraId = Guid.Parse("11111111-2222-3333-4444-5555555555a1");
    private static readonly string Huella = new('b', 64);

    private sealed class CatalogoDePrueba(ResumenDeObra obra) : IConsultaDeCatalogo
    {
        public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default) =>
            Task.FromResult(Resultado<PaginaDeObras>.Exito(new PaginaDeObras([obra], busqueda.Pagina, busqueda.Tamano, 1)));

        public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => Task.FromResult(Resultado<ResumenDeObra>.Exito(obra));

        public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private sealed class ListaDePrueba : IListaDeSucursales
    {
        public Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default) => Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([new("LIM", "Lima")], true));
    }

    private static DisponibilidadDeEdicion Disp(EstadoDeDisponibilidad estado, int? vendible, MotivoDeNoDisponibleDeEdicion? motivo = null) =>
        new(Guid.NewGuid(), estado, vendible, vendible is null ? [] : [new("LIM", "Lima", vendible.Value)], FrescuraDeLaDisponibilidad.Reciente, DateTimeOffset.UtcNow, motivo);

    private static ResumenDeObra Obra(string? descripcion, bool digitalVigente = true) =>
        new(
            ObraId,
            "Estructuras de datos",
            "Ana Quispe",
            "Editorial Andina",
            "Técnico",
            Huella,
            [
                new EdicionDeObra(Guid.NewGuid(), FormatoDeEdicion.Impresa, "9780000000019", 129.90m, Disp(EstadoDeDisponibilidad.Disponible, 7)),
                new EdicionDeObra(
                    Guid.NewGuid(),
                    FormatoDeEdicion.Digital,
                    "9790000000018",
                    49m,
                    digitalVigente ? Disp(EstadoDeDisponibilidad.DisponibleAlInstante, null) : Disp(EstadoDeDisponibilidad.NoSeOfrece, null, MotivoDeNoDisponibleDeEdicion.LicenciaVencida)),
            ],
            descripcion);

    private static Task<AnfitrionWeb> Iniciar(ResumenDeObra obra) =>
        AnfitrionWeb.Iniciar(servicios =>
        {
            servicios.AddSingleton<IConsultaDeCatalogo>(new CatalogoDePrueba(obra));
            servicios.AddSingleton<IListaDeSucursales>(new ListaDePrueba());
        });

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task La_ficha_muestra_la_portada_la_descripcion_con_sus_saltos_de_linea_y_el_precio_de_lo_que_se_ofrece()
    {
        await using var anfitrion = await Iniciar(Obra("Una sinopsis breve.\nSegunda línea del texto."));

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{ObraId}");
        var crudo = await respuesta.Content.ReadAsStringAsync();
        var html = WebUtility.HtmlDecode(crudo);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("alt=\"Portada de Estructuras de datos\"", html, StringComparison.Ordinal);
        Assert.Contains($"/portadas/{Huella}", html, StringComparison.Ordinal);
        Assert.Contains("Una sinopsis breve.\nSegunda línea del texto.", html, StringComparison.Ordinal);
        Assert.Contains("class=\"bh-descripcion", crudo, StringComparison.Ordinal);
        Assert.Contains(FormatoPeruano.Soles(129.90m), html, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-20")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sin_descripcion_la_ficha_muestra_portada_y_precio_sin_un_bloque_vacio(string? descripcion)
    {
        await using var anfitrion = await Iniciar(Obra(descripcion));

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{ObraId}");
        var crudo = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain("bh-descripcion", crudo, StringComparison.Ordinal);
        Assert.DoesNotContain("h-descripcion", crudo, StringComparison.Ordinal);
        Assert.Contains("alt=\"Portada de Estructuras de datos\"", crudo, StringComparison.Ordinal);
        Assert.Contains(FormatoPeruano.Soles(129.90m), WebUtility.HtmlDecode(crudo), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Una_descripcion_con_script_o_con_entidades_se_ve_tal_como_se_escribio_sin_ejecutarse_ni_transformarse()
    {
        await using var anfitrion = await Iniciar(Obra("<script>alert(1)</script> y &lt;b&gt; \"comillas\""));

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{ObraId}");
        var crudo = await respuesta.Content.ReadAsStringAsync();

        // En el HTML crudo no hay un <script> de la descripción y cada carácter de marcado va escapado; al decodificar, el texto es exactamente el escrito.
        Assert.DoesNotContain("<script>alert(1)", crudo, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", crudo, StringComparison.Ordinal);
        Assert.Contains("&amp;lt;b&amp;gt;", crudo, StringComparison.Ordinal);
        Assert.Contains("<script>alert(1)</script> y &lt;b&gt; \"comillas\"", WebUtility.HtmlDecode(crudo), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task Una_digital_que_no_se_ofrece_no_muestra_su_precio_y_explica_el_motivo()
    {
        await using var anfitrion = await Iniciar(Obra("Con descripción.", digitalVigente: false));

        using var respuesta = await anfitrion.Cliente.GetAsync($"/catalogo/obras/{ObraId}?edicion=digital");
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("La licencia de esta edición digital venció y ya no se vende.", html, StringComparison.Ordinal);
        Assert.DoesNotContain(FormatoPeruano.Soles(49m), html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public async Task La_lista_del_catalogo_no_muestra_la_descripcion()
    {
        await using var anfitrion = await Iniciar(Obra("Texto único de la descripción que solo va en la ficha."));

        using var respuesta = await anfitrion.Cliente.GetAsync("/catalogo");
        var html = WebUtility.HtmlDecode(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("Estructuras de datos", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Texto único de la descripción", html, StringComparison.Ordinal);
    }
}
