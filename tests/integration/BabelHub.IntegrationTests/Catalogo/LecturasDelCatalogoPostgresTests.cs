using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using static BabelHub.IntegrationTests.Catalogo.RepositoriosDelCatalogoPostgresTests;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>La búsqueda y las lecturas del catálogo contra PostgreSQL real (T15; CAT-09, AC-CAT-06): filtros sobre una misma edición, texto sin tildes y con comodines escapados, orden estable y tope de candidatas.</summary>
[Collection(ColeccionPostgresDelCatalogo.Nombre)]
public sealed class LecturasDelCatalogoPostgresTests(PostgresDelCatalogoFixture bd) : IAsyncLifetime
{
    private readonly LecturasDelCatalogoPostgres _lecturas = new(bd.AccesoAlEsquema);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static FiltrosDeObras Filtros(string? texto = null, string? autor = null, string? editorial = null, string? categoria = null, FormatoDeEdicion? formato = null, decimal? min = null, decimal? max = null) =>
        new(texto, autor, editorial, categoria, formato, min, max);

    private async Task<Obra> Guardar(string titulo, string categoria, string autor, params Edicion[] ediciones)
    {
        var obra = Obra.Registrar(
            Guid.NewGuid(),
            new DatosDeObra(titulo, autor, "Editorial Andina", categoria),
            new PortadaDeObra(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Jpeg, Huella()),
            ediciones.Length == 0 ? [Impresa()] : ediciones,
            new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5))).Valor;
        Assert.Equal(ResultadoDeEscrituraDeObra.Escrita, await bd.Obras.Agregar(obra));
        return obra;
    }

    private async Task DarDeBaja(Obra obra)
    {
        var cargada = (await bd.Obras.Obtener(obra.Id, false))!;
        var versionLeida = cargada.Version;
        cargada.DarDeBaja(DateTimeOffset.UtcNow);
        await bd.Obras.Guardar(cargada, versionLeida);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task De_16_obras_digital_tecnico_y_hasta_100_solo_salen_las_que_cumplen_las_tres_en_la_misma_edicion()
    {
        var esperadas = new List<Guid>();
        for (var i = 0; i < 16; i++)
        {
            var categoria = i % 2 == 0 ? "Técnico" : "Novela";
            var precioDigital = i % 4 < 2 ? 80m : 150m;
            var obra = await Guardar($"Libro {i:D2}", categoria, "Ana", Impresa(precio: 40m), Digital());
            await bd.Ejecutar($"UPDATE catalogo.ediciones SET precio = {precioDigital.ToString(System.Globalization.CultureInfo.InvariantCulture)} WHERE obra_id = '{obra.Id}' AND tipo = 'digital'");
            if (categoria == "Técnico" && precioDigital <= 100m)
            {
                esperadas.Add(obra.Id);
            }
        }

        await Guardar("Barata y cara", "Técnico", "Ana", Impresa(precio: 10m), Digital());
        await bd.Ejecutar("UPDATE catalogo.ediciones SET precio = 500 WHERE obra_id = (SELECT id FROM catalogo.obras WHERE titulo = 'Barata y cara') AND tipo = 'digital'");

        var pagina = await _lecturas.Buscar(Filtros(categoria: "técnico", formato: FormatoDeEdicion.Digital, max: 100m), 1, 50);

        Assert.Equal(esperadas.Order(), pagina.Obras.Select(o => o.Id).Order());
        Assert.Equal(4, pagina.Total);
        Assert.All(pagina.Obras, o => Assert.Equal(2, o.Ediciones.Count));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task El_texto_busca_sin_tildes_ni_mayusculas_y_los_comodines_se_escapan()
    {
        await Guardar("Álgebra lineal", "Técnico", "Ana");
        await Guardar("Cálculo 100% práctico", "Técnico", "Beto");
        await Guardar("Cálculo_diferencial", "Técnico", "Beto");
        await Guardar("Calculo integral", "Técnico", "Beto");
        await Guardar("Ruta \\ crítica", "Técnico", "Carlos");

        Assert.Equal(["Álgebra lineal"], (await _lecturas.Buscar(Filtros(texto: "ALGEBRA"), 1, 20)).Obras.Select(o => o.Titulo));
        Assert.Equal(["Cálculo 100% práctico"], (await _lecturas.Buscar(Filtros(texto: "100%"), 1, 20)).Obras.Select(o => o.Titulo));
        Assert.Equal(["Cálculo_diferencial"], (await _lecturas.Buscar(Filtros(texto: "o_d"), 1, 20)).Obras.Select(o => o.Titulo));
        Assert.Equal(["Ruta \\ crítica"], (await _lecturas.Buscar(Filtros(texto: "\\"), 1, 20)).Obras.Select(o => o.Titulo));
        Assert.Equal(3, (await _lecturas.Buscar(Filtros(texto: "calculo"), 1, 20)).Total);
        Assert.Equal(3, (await _lecturas.Buscar(Filtros(autor: "beto"), 1, 20)).Total);
        Assert.Empty((await _lecturas.Buscar(Filtros(texto: "'; DROP TABLE catalogo.obras; --"), 1, 20)).Obras);
        Assert.Equal(5, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task El_orden_es_fijo_la_paginacion_no_repite_ni_omite_y_las_dadas_de_baja_no_salen()
    {
        for (var i = 0; i < 7; i++)
        {
            await Guardar($"Título {i}", "Técnico", "Ana");
        }

        var baja = await Guardar("Título baja", "Técnico", "Ana");
        await DarDeBaja(baja);

        var paginas = new List<Guid>();
        for (var p = 1; p <= 4; p++)
        {
            paginas.AddRange((await _lecturas.Buscar(Filtros(), p, 3)).Obras.Select(o => o.Id));
        }

        Assert.Equal(7, paginas.Count);
        Assert.Equal(7, paginas.Distinct().Count());
        Assert.DoesNotContain(baja.Id, paginas);
        Assert.Equal(7, (await _lecturas.Buscar(Filtros(), 1, 3)).Total);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task Contar_las_candidatas_cuenta_obras_y_ediciones_que_cumplen_formato_y_precio_para_el_tope_de_500()
    {
        for (var i = 0; i < 10; i++)
        {
            await Guardar($"Obra {i}", "Técnico", "Ana", Impresa(), Digital());
        }

        var todas = await _lecturas.ContarCandidatas(Filtros());
        var digitales = await _lecturas.ContarCandidatas(Filtros(formato: FormatoDeEdicion.Digital));
        var caras = await _lecturas.ContarCandidatas(Filtros(min: 100m));

        Assert.Equal((10, 20), todas);
        Assert.Equal((10, 10), digitales);
        Assert.Equal((10, 10), caras);
        Assert.Equal(10, (await _lecturas.TodasLasQueCumplen(Filtros())).Count);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public async Task Una_obra_dada_de_baja_sigue_legible_por_identificador_con_su_titulo_y_las_ediciones_por_id_incluyen_las_de_baja()
    {
        var obra = await Guardar("Con pedidos", "Técnico", "Ana", Impresa(), Digital());
        await DarDeBaja(obra);

        var ids = obra.Ediciones.Select(e => e.Id).Append(Guid.NewGuid()).ToList();
        var ediciones = await _lecturas.EdicionesPorId(ids);
        var porId = await _lecturas.ObraPorId(obra.Id);

        Assert.Equal(2, ediciones.Count);
        Assert.All(ediciones, e =>
        {
            Assert.Equal("Con pedidos", e.Obra.Titulo);
            Assert.Equal(EstadoDeEdicion.Baja, e.Edicion.Estado);
        });
        Assert.Equal(EstadoDeObra.Baja, porId!.Estado);
        Assert.Empty((await _lecturas.Buscar(Filtros(), 1, 20)).Obras);
        Assert.Null(await _lecturas.ObraPorId(Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task La_portada_se_busca_solo_por_la_huella_de_una_portada_de_obra_y_nunca_por_la_de_un_libro()
    {
        var obra = await Guardar("Con libro", "Técnico", "Ana", Digital());
        var libro = obra.Ediciones[0].Archivo!;

        var portada = await _lecturas.PortadaPorHuella(obra.Portada.Huella);
        var deLibro = await _lecturas.PortadaPorHuella(libro.Huella);

        Assert.Equal(obra.Portada.ClaveDelAlmacen, portada!.ClaveDelAlmacen);
        Assert.Equal(TipoDeArchivo.Jpeg, portada.TipoDetectado);
        Assert.Null(deLibro);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-07")]
    public async Task El_muestreo_al_azar_excluye_las_vistas_y_devuelve_obras_completas()
    {
        for (var i = 0; i < 6; i++)
        {
            await Guardar($"Azar {i}", "Técnico", "Ana", Impresa(), Digital());
        }

        var primera = await _lecturas.ObrasAlAzar(4, []);
        var resto = await _lecturas.ObrasAlAzar(10, primera.Select(o => o.Id).ToList());

        Assert.Equal(4, primera.Count);
        Assert.Equal(2, resto.Count);
        Assert.Empty(primera.Select(o => o.Id).Intersect(resto.Select(o => o.Id)));
        Assert.All(primera.Concat(resto), o => Assert.Equal(2, o.Ediciones.Count));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-11")]
    public async Task La_busqueda_de_administracion_incluye_las_dadas_de_baja_si_se_pide_y_busca_en_titulo_autor_y_editorial()
    {
        await Guardar("Visible", "Técnico", "Ana");
        var baja = await Guardar("Retirada del todo", "Técnico", "Beto");
        await DarDeBaja(baja);

        var visibles = await _lecturas.BuscarParaAdministracion(null, false, 1, 20);
        var todas = await _lecturas.BuscarParaAdministracion(null, true, 1, 20);
        var porAutor = await _lecturas.BuscarParaAdministracion("BETO", true, 1, 20);

        Assert.Equal(1, visibles.Total);
        Assert.Equal(2, todas.Total);
        Assert.Equal(["Retirada del todo"], porAutor.Obras.Select(o => o.Titulo));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task Los_indices_de_trigramas_existen_para_titulo_autor_y_editorial()
    {
        var indices = await bd.Consultar<long>("SELECT count(*) FROM pg_indexes WHERE schemaname = 'catalogo' AND indexname IN ('obras_titulo_trgm', 'obras_autor_trgm', 'obras_editorial_trgm')");

        Assert.Equal(3, indices);
    }
}
