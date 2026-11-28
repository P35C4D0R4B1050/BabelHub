using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>BuscarObras, ObtenerObra, ObtenerEdiciones, ObtenerObraAlAzar y ConsultarDisponibilidad (T11; CAT-09, CAT-10, CAT-12, CAT-21, AC-CAT-04, 06, 07, 09, 17): filtros sobre una misma edición, caché de 30 s, respaldo de 10 minutos y tope de 500 candidatas.</summary>
public sealed class ConsultaDeCatalogoTests
{
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static string IsbnNumero(int n)
    {
        var doce = "978" + n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var suma = doce.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return doce + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static int _siguiente = 1000;

    private static HuellaDeArchivo Huella() => HuellaDeArchivo.Desde(SHA256.HashData(Guid.NewGuid().ToByteArray()));

    private static Edicion Impresa(decimal precio = 50m) =>
        Edicion.Impresa(Guid.NewGuid(), Isbn.Crear(IsbnNumero(Interlocked.Increment(ref _siguiente))), Precio.Crear(precio));

    private static Edicion Digital(EntornoDelCatalogo e, decimal precio = 30m, DateOnly? vigenteHasta = null, string[]? territorios = null)
    {
        var licencia = LicenciaDigital.Rehidratar(TipoDeLicencia.Comercial, "Editorial", territorios ?? ["PE"], vigenteHasta ?? e.Hoy.AddYears(1), FormatoDigital.Epub);
        return Edicion.Digital(Guid.NewGuid(), Isbn.Crear(IsbnNumero(Interlocked.Increment(ref _siguiente))), Precio.Crear(precio), licencia, new ArchivoDeEdicion(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Epub, 100, Huella(), e.Reloj.Ahora, Ana));
    }

    private static Obra Sembrar(EntornoDelCatalogo e, string titulo, string categoria, params Edicion[] ediciones)
    {
        var obra = Obra.Registrar(Guid.NewGuid(), new DatosDeObra(titulo, "Autora", "Editorial", categoria), new PortadaDeObra(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Jpeg, Huella()), ediciones, e.Reloj.Ahora).Valor;
        e.Obras.Sembrar(obra);
        return obra;
    }

    private static Edicion Impr(Obra o) => o.Ediciones.Single(x => !x.EsDigital);

    private static Edicion Digi(Obra o) => o.Ediciones.Single(x => x.EsDigital);

    // ---------- Búsqueda ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task De_16_obras_el_filtro_de_digital_tecnico_y_hasta_100_muestra_solo_las_que_cumplen_las_tres_en_la_misma_edicion()
    {
        var e = new EntornoDelCatalogo();
        var esperadas = new List<Guid>();
        for (var i = 0; i < 16; i++)
        {
            var categoria = i % 2 == 0 ? "Técnico" : "Novela";
            var precioDigital = i % 4 < 2 ? 80m : 150m;
            var obra = Sembrar(e, $"Libro {i:D2}", categoria, Impresa(40m), Digital(e, precioDigital));
            if (categoria == "Técnico" && precioDigital <= 100m)
            {
                esperadas.Add(obra.Id);
            }
        }

        // Una obra con impresa barata y digital cara no cumple «digital hasta 100»: el precio es el de la misma edición digital.
        Sembrar(e, "Barata y cara", "Técnico", Impresa(10m), Digital(e, 500m));

        var resultado = await e.Consulta.BuscarObras(new BusquedaDeObras(null, null, null, "Técnico", FormatoDeEdicion.Digital, null, 100m));

        Assert.True(resultado.EsExito);
        Assert.Equal(esperadas.Order(), resultado.Valor.Obras.Select(o => o.ObraId).Order());
        Assert.Equal(esperadas.Count, resultado.Valor.Total);
        Assert.Equal(4, esperadas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task Al_filtrar_por_disponibilidad_una_impresa_sin_stock_en_la_red_no_aparece()
    {
        var e = new EntornoDelCatalogo();
        var conStock = Sembrar(e, "Con stock", "Técnico", Impresa());
        var agotada = Sembrar(e, "Agotada", "Técnico", Impresa());
        var sinFicha = Sembrar(e, "Sin ficha", "Técnico", Impresa());
        e.Inventario.ConStock(Impr(conStock).Id, 7);
        e.Inventario.ConStock(Impr(agotada).Id, 0);

        var resultado = await e.Consulta.BuscarObras(new BusquedaDeObras(SoloDisponibles: true));

        Assert.Equal([conStock.Id], resultado.Valor.Obras.Select(o => o.ObraId).ToList());
        Assert.Equal(1, resultado.Valor.Total);
        Assert.NotNull(sinFicha);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-17")]
    public async Task Una_impresa_agotada_se_muestra_agotada_sigue_en_el_catalogo_y_su_digital_sigue_disponible()
    {
        var e = new EntornoDelCatalogo();
        var obra = Sembrar(e, "Agotada con digital", "Técnico", Impresa(), Digital(e));
        e.Inventario.ConStock(Impr(obra).Id, 0);

        var catalogo = await e.Consulta.BuscarObras(new BusquedaDeObras());
        var disponibles = await e.Consulta.BuscarObras(new BusquedaDeObras(SoloDisponibles: true));

        var impresa = catalogo.Valor.Obras.Single().Ediciones.Single(x => x.Formato == FormatoDeEdicion.Impresa);
        Assert.Equal(EstadoDeDisponibilidad.Agotada, impresa.Disponibilidad.Estado);
        Assert.Equal(0, impresa.Disponibilidad.VendibleEnLaRed);
        Assert.Equal(EstadoDeDisponibilidad.DisponibleAlInstante, catalogo.Valor.Obras.Single().Ediciones.Single(x => x.Formato == FormatoDeEdicion.Digital).Disponibilidad.Estado);
        Assert.Single(disponibles.Valor.Obras);
        Assert.Equal(EstadoDeEdicionConsultado.Activa, (await e.Consulta.ObtenerEdiciones([impresa.EdicionId])).Valor.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-17")]
    public async Task Una_impresa_que_el_inventario_no_conoce_se_muestra_agotada()
    {
        var e = new EntornoDelCatalogo();
        Sembrar(e, "Sin registrar", "Técnico", Impresa());

        var resultado = await e.Consulta.BuscarObras(new BusquedaDeObras());

        var disponibilidad = resultado.Valor.Obras.Single().Ediciones.Single().Disponibilidad;
        Assert.Equal(EstadoDeDisponibilidad.Agotada, disponibilidad.Estado);
        Assert.Equal(FrescuraDeLaDisponibilidad.Reciente, disponibilidad.Frescura);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task El_texto_busca_sin_distinguir_mayusculas_ni_tildes_el_orden_es_por_titulo_y_la_paginacion_es_estable()
    {
        var e = new EntornoDelCatalogo();
        foreach (var titulo in new[] { "Zeta", "Álgebra lineal", "Algoritmos", "Base de datos", "Cálculo" })
        {
            Sembrar(e, titulo, "Técnico", Impresa());
        }

        var texto = await e.Consulta.BuscarObras(new BusquedaDeObras("ALGEBRA"));
        var primera = await e.Consulta.BuscarObras(new BusquedaDeObras(Pagina: 1, Tamano: 2));
        var segunda = await e.Consulta.BuscarObras(new BusquedaDeObras(Pagina: 2, Tamano: 2));
        var tercera = await e.Consulta.BuscarObras(new BusquedaDeObras(Pagina: 3, Tamano: 2));

        Assert.Equal(["Álgebra lineal"], texto.Valor.Obras.Select(o => o.Titulo).ToList());
        Assert.Equal(5, primera.Valor.Total);
        var titulos = primera.Valor.Obras.Concat(segunda.Valor.Obras).Concat(tercera.Valor.Obras).Select(o => o.Titulo).ToList();
        Assert.Equal(["Algoritmos", "Base de datos", "Cálculo", "Zeta", "Álgebra lineal"], titulos.Order(StringComparer.Ordinal).ToList());
        Assert.Equal(5, titulos.Distinct().Count());
        Assert.Single(tercera.Valor.Obras);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task Una_obra_dada_de_baja_y_una_edicion_dada_de_baja_no_salen_en_la_busqueda()
    {
        var e = new EntornoDelCatalogo();
        var baja = Sembrar(e, "Dada de baja", "Técnico", Impresa());
        var conEdicionDeBaja = Sembrar(e, "Con una edición de baja", "Técnico", Impresa(), Digital(e));
        var copia = RepositorioDeObrasEnMemoria.Clonar(baja);
        copia.DarDeBaja(e.Reloj.Ahora);
        e.Obras.Sembrar(copia);
        var otra = RepositorioDeObrasEnMemoria.Clonar(conEdicionDeBaja);
        otra.DarDeBajaEdicion(Digi(otra).Id, e.Reloj.Ahora);
        e.Obras.Sembrar(otra);

        var resultado = await e.Consulta.BuscarObras(new BusquedaDeObras());

        var obra = Assert.Single(resultado.Valor.Obras);
        Assert.Equal(conEdicionDeBaja.Id, obra.ObraId);
        Assert.Single(obra.Ediciones);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task Mas_de_500_ediciones_candidatas_con_el_filtro_de_disponibilidad_se_rechazan_y_sin_el_filtro_no()
    {
        var e = new EntornoDelCatalogo();
        for (var i = 0; i < 251; i++)
        {
            Sembrar(e, $"Obra {i:D3}", "Técnico", Impresa(), Digital(e));
        }

        var amplia = await e.Consulta.BuscarObras(new BusquedaDeObras(SoloDisponibles: true));
        var lotesTrasLaAmplia = e.Inventario.LotesConsultados.Count;
        var sinFiltro = await e.Consulta.BuscarObras(new BusquedaDeObras(Tamano: 50));
        var acotada = await e.Consulta.BuscarObras(new BusquedaDeObras(Formato: FormatoDeEdicion.Digital, SoloDisponibles: true, Tamano: 50));

        Assert.Equal(ClavesDelCatalogo.BusquedaDemasiadoAmplia, amplia.Error!.Clave);
        Assert.Equal(0, lotesTrasLaAmplia);
        Assert.True(sinFiltro.EsExito);
        Assert.Equal(251, sinFiltro.Valor.Total);
        Assert.True(acotada.EsExito);
        Assert.Equal(251, acotada.Valor.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task El_inventario_se_consulta_por_lotes_de_50_ediciones_impresas_y_nunca_las_digitales()
    {
        var e = new EntornoDelCatalogo();
        for (var i = 0; i < 120; i++)
        {
            var obra = Sembrar(e, $"Obra {i:D3}", "Técnico", Impresa(), Digital(e));
            e.Inventario.ConStock(Impr(obra).Id, 3);
        }

        var resultado = await e.Consulta.BuscarObras(new BusquedaDeObras(Formato: FormatoDeEdicion.Impresa, SoloDisponibles: true, Tamano: 50));

        Assert.Equal(120, resultado.Valor.Total);
        Assert.Equal([50, 50, 20], e.Inventario.LotesConsultados.Select(l => l.Count).ToList());
        Assert.All(e.Inventario.LotesConsultados.SelectMany(l => l), id => Assert.Contains(e.Obras.Todas.SelectMany(o => o.Ediciones), x => x.Id == id && !x.EsDigital));
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-06")]
    [InlineData("a")]
    [InlineData("a\0b")]
    public async Task Una_busqueda_invalida_responde_validacion_sin_consultar_nada(string texto)
    {
        var e = new EntornoDelCatalogo();

        var resultado = await e.Consulta.BuscarObras(new BusquedaDeObras(texto));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(e.Inventario.LotesConsultados);
    }

    // ---------- Respaldo y caché (CAT-12) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Con_el_inventario_caido_un_dato_de_menos_de_10_minutos_se_muestra_como_posiblemente_desactualizado()
    {
        var e = new EntornoDelCatalogo();
        var obra = Sembrar(e, "Con respaldo", "Técnico", Impresa());
        e.Inventario.ConStock(Impr(obra).Id, 9);
        await e.Consulta.BuscarObras(new BusquedaDeObras());
        e.Inventario.Caido = true;
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(9).AddSeconds(59);

        var resultado = await e.Consulta.BuscarObras(new BusquedaDeObras());

        Assert.True(resultado.EsExito);
        var dato = resultado.Valor.Obras.Single().Ediciones.Single().Disponibilidad;
        Assert.Equal(FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada, dato.Frescura);
        Assert.Equal(EstadoDeDisponibilidad.Disponible, dato.Estado);
        Assert.Equal(9, dato.VendibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Con_el_inventario_caido_una_edicion_sin_dato_o_sin_confirmar_no_cumple_el_filtro_de_disponibilidad_y_la_digital_si()
    {
        var e = new EntornoDelCatalogo();
        var conDato = Sembrar(e, "Con dato", "Técnico", Impresa());
        var sinDato = Sembrar(e, "Sin dato", "Técnico", Impresa());
        var digital = Sembrar(e, "Digital", "Técnico", Digital(e));
        e.Inventario.ConStock(Impr(conDato).Id, 4);
        await e.Consulta.BuscarObras(new BusquedaDeObras());
        e.Inventario.Caido = true;
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(2);

        var resultado = await e.Consulta.BuscarObras(new BusquedaDeObras(SoloDisponibles: true));

        Assert.Equal(new[] { conDato.Id, digital.Id }.Order(), resultado.Valor.Obras.Select(o => o.ObraId).Order());
        Assert.DoesNotContain(resultado.Valor.Obras, o => o.ObraId == sinDato.Id);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task La_cache_dura_30_segundos_y_una_invalidacion_obliga_a_consultar_pero_conserva_el_respaldo()
    {
        var e = new EntornoDelCatalogo();
        var obra = Sembrar(e, "Cache", "Técnico", Impresa());
        var edicion = Impr(obra).Id;
        e.Inventario.ConStock(edicion, 5);

        await e.Consulta.ObtenerObra(obra.Id);
        e.Reloj.Ahora = e.Reloj.Ahora.AddSeconds(29);
        await e.Consulta.ObtenerObra(obra.Id);
        Assert.Single(e.Inventario.LotesConsultados);

        e.Reloj.Ahora = e.Reloj.Ahora.AddSeconds(1);
        await e.Consulta.ObtenerObra(obra.Id);
        Assert.Equal(2, e.Inventario.LotesConsultados.Count);

        e.Inventario.ConStock(edicion, 35);
        e.Memoria.Invalidar(edicion);
        var nueva = await e.Consulta.ObtenerObra(obra.Id);
        Assert.Equal(3, e.Inventario.LotesConsultados.Count);
        Assert.Equal(35, nueva.Valor.Ediciones.Single().Disponibilidad.VendibleEnLaRed);

        e.Memoria.Invalidar(edicion);
        e.Inventario.Caido = true;
        var respaldo = await e.Consulta.ObtenerObra(obra.Id);
        Assert.Equal(FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada, respaldo.Valor.Ediciones.Single().Disponibilidad.Frescura);
    }

    // ---------- Ficha ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-07")]
    public async Task La_ficha_muestra_las_dos_ediciones_la_disponibilidad_por_sucursal_de_la_impresa_y_disponible_al_instante_la_digital()
    {
        var e = new EntornoDelCatalogo();
        var obra = Sembrar(e, "Ficha", "Técnico", Impresa(129m), Digital(e, 49m));
        e.Inventario.ConStock(Impr(obra).Id, 5, "LIMA", "Lima");
        e.Inventario.ConStock(Impr(obra).Id, 2, "AYAC", "Ayacucho");

        var resultado = await e.Consulta.ObtenerObra(obra.Id);

        Assert.True(resultado.EsExito);
        var ficha = resultado.Valor;
        Assert.Equal(obra.Portada.Huella.Hex, ficha.PortadaHuella);
        var impresa = ficha.Ediciones.Single(x => x.Formato == FormatoDeEdicion.Impresa);
        Assert.Equal(7, impresa.Disponibilidad.VendibleEnLaRed);
        Assert.Equal(["AYAC", "LIMA"], impresa.Disponibilidad.PorFuente.Select(f => f.CodigoDeFuente).Order().ToList());
        Assert.Equal(129m, impresa.Precio);
        var digital = ficha.Ediciones.Single(x => x.Formato == FormatoDeEdicion.Digital);
        Assert.Equal(EstadoDeDisponibilidad.DisponibleAlInstante, digital.Disponibilidad.Estado);
        Assert.Equal(49m, digital.Precio);
        Assert.Empty(digital.Disponibilidad.PorFuente);
        Assert.DoesNotContain(e.Inventario.LotesConsultados.SelectMany(l => l), id => id == digital.EdicionId);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public async Task Una_digital_con_licencia_vencida_y_otra_que_no_incluye_peru_no_se_ofrecen_y_cada_una_explica_su_motivo()
    {
        var e = new EntornoDelCatalogo();
        var vencida = Sembrar(e, "Vencida", "Técnico", Digital(e, vigenteHasta: e.Hoy.AddDays(-1)));
        var sinPeru = Sembrar(e, "Sin Perú", "Técnico", Digital(e, territorios: ["CL", "AR"]));

        var a = await e.Consulta.ObtenerObra(vencida.Id);
        var b = await e.Consulta.ObtenerObra(sinPeru.Id);

        Assert.Equal(EstadoDeDisponibilidad.NoSeOfrece, a.Valor.Ediciones.Single().Disponibilidad.Estado);
        Assert.Equal(MotivoDeNoDisponibleDeEdicion.LicenciaVencida, a.Valor.Ediciones.Single().Disponibilidad.Motivo);
        Assert.Equal(EstadoDeDisponibilidad.NoSeOfrece, b.Valor.Ediciones.Single().Disponibilidad.Estado);
        Assert.Equal(MotivoDeNoDisponibleDeEdicion.SinPeru, b.Valor.Ediciones.Single().Disponibilidad.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public async Task La_ficha_de_una_obra_dada_de_baja_o_inexistente_no_se_encuentra_pero_sus_ediciones_siguen_legibles_con_su_titulo()
    {
        var e = new EntornoDelCatalogo();
        var obra = Sembrar(e, "Con pedidos históricos", "Técnico", Impresa(), Digital(e));
        var copia = RepositorioDeObrasEnMemoria.Clonar(obra);
        copia.DarDeBaja(e.Reloj.Ahora);
        e.Obras.Sembrar(copia);

        var ficha = await e.Consulta.ObtenerObra(obra.Id);
        var inexistente = await e.Consulta.ObtenerObra(Guid.NewGuid());
        var ediciones = await e.Consulta.ObtenerEdiciones(obra.Ediciones.Select(x => x.Id).ToList());

        Assert.Equal(CategoriaDeError.NoEncontrado, ficha.Error!.Categoria);
        Assert.Equal(ClavesDelCatalogo.ObraNoEncontrada, inexistente.Error!.Clave);
        Assert.Equal(2, ediciones.Valor.Count);
        Assert.All(ediciones.Valor, x =>
        {
            Assert.Equal("Con pedidos históricos", x.Titulo);
            Assert.Equal(EstadoDeEdicionConsultado.DadaDeBaja, x.Estado);
            Assert.Equal(MotivoDeNoDisponibleDeEdicion.DadaDeBaja, x.MotivoDeNoDisponible);
            Assert.Equal(obra.Id, x.ObraId);
        });
        Assert.Equal(["impresa", "digital"], ediciones.Valor.OrderBy(x => x.Formato).Select(x => x.Formato == FormatoDeEdicion.Impresa ? "impresa" : "digital").ToList());
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public async Task Las_ediciones_por_identificador_incluyen_las_retiradas_con_su_motivo_y_omiten_las_inexistentes()
    {
        var e = new EntornoDelCatalogo();
        var obra = Sembrar(e, "Retirada", "Técnico", Impresa(), Digital(e));
        var copia = RepositorioDeObrasEnMemoria.Clonar(obra);
        copia.RetirarEdicionDigital(Digi(copia).Id, "Retiro", e.Reloj.Ahora);
        e.Obras.Sembrar(copia);

        var resultado = await e.Consulta.ObtenerEdiciones([Digi(obra).Id, Impr(obra).Id, Guid.NewGuid()]);

        Assert.Equal(2, resultado.Valor.Count);
        var digital = resultado.Valor.Single(x => x.Formato == FormatoDeEdicion.Digital);
        Assert.Equal(EstadoDeEdicionConsultado.RetiradaDeVenta, digital.Estado);
        Assert.Equal(MotivoDeNoDisponibleDeEdicion.RetiradaDeLaVenta, digital.MotivoDeNoDisponible);
        Assert.Null(resultado.Valor.Single(x => x.Formato == FormatoDeEdicion.Impresa).MotivoDeNoDisponible);
        Assert.Equal(Digi(obra).Isbn.Isbn13, digital.Isbn);
    }

    // ---------- Disponibilidad ----------

    // ---------- Sorpréndeme ----------

    [Fact]
    public async Task Con_el_inventario_caido_sorprendeme_solo_ofrece_digitales_o_ninguna()
    {
        var e = new EntornoDelCatalogo();
        var impresa = Sembrar(e, "Impresa", "Técnico", Impresa());
        e.Inventario.ConStock(Impr(impresa).Id, 10);
        await e.Consulta.ObtenerObra(impresa.Id);
        e.Inventario.Caido = true;

        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(1);
        var ninguna = await e.Consulta.ObtenerObraAlAzar();
        var digital = Sembrar(e, "Digital", "Técnico", Digital(e));
        var una = await e.Consulta.ObtenerObraAlAzar();

        Assert.True(ninguna.EsExito);
        Assert.Null(ninguna.Valor);
        Assert.Equal(digital.Id, una.Valor!.ObraId);
    }

    [Fact]
    public async Task Sorprendeme_con_una_impresa_con_stock_la_ofrece_y_sin_obras_disponibles_responde_ninguna()
    {
        var e = new EntornoDelCatalogo();
        var vacio = await e.Consulta.ObtenerObraAlAzar();
        var agotada = Sembrar(e, "Agotada", "Técnico", Impresa());
        e.Inventario.ConStock(Impr(agotada).Id, 0);
        var sinStock = await e.Consulta.ObtenerObraAlAzar();
        var conStock = Sembrar(e, "Con stock", "Técnico", Impresa());
        e.Inventario.ConStock(Impr(conStock).Id, 2);

        var elegida = await e.Consulta.ObtenerObraAlAzar();

        Assert.Null(vacio.Valor);
        Assert.Null(sinStock.Valor);
        Assert.Equal(conStock.Id, elegida.Valor!.ObraId);
    }
}
