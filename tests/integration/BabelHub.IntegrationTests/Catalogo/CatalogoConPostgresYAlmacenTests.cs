using System.Net;
using System.Text.Json;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.IntegrationTests.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// El Catálogo completo contra PostgreSQL real y un almacén S3 real (T20; AC-CAT-06, AC-CAT-10, AC-CAT-12, AC-CAT-14): el ISBN único bajo concurrencia (también ISBN-10 contra ISBN-13), 16 obras de muestra filtradas por la consulta pública, una obra dada de baja legible por
/// identificador, las referencias de archivo ajena, vencida, usada, simultánea y de finalidad cruzada, y los límites de archivos de AC-CAT-12 por la subida HTTP (bomba de compresión, ZIP truncado, 50 MiB exactos y uno más).
/// </summary>
[Collection(ColeccionDeLaApiDelCatalogo.Nombre)]
public sealed class CatalogoConPostgresYAlmacenTests(PostgresDelCatalogoYAuditoriaFixture bd, AlmacenDeObjetosFixture almacen) : IAsyncLifetime
{
    private CatalogoApiHost _host = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _host = await CatalogoApiHost.Crear(bd, almacen.Cliente);
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    private static string Codigo(JsonElement problema) => problema.GetProperty("code").GetString()!;

    /// <summary>Un ISBN-13 válido y distinto para cada número (prefijo 979 y dígito de control calculado).</summary>
    private static string Isbn13(int n)
    {
        var cuerpo = "979" + n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var suma = cuerpo.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return cuerpo + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private object ObraImpresa(Guid portada, string isbn, string titulo = "Obra", string categoria = "Técnico") =>
        new { titulo, autor = "Ana", editorial = "Andina", categoria, portadaRef = portada, ediciones = new[] { new { formato = "impresa", isbn, precio = 40m } } };

    private static object Digital(string isbn, Guid archivoRef, decimal precio, string tipoDeLicencia = "dominioPublico", string formato = "epub") => new
    {
        formato = "digital",
        isbn,
        precio,
        licencia = new { tipo = tipoDeLicencia, titular = "Dominio público", territorios = new[] { "PE" }, formato },
        archivoRef,
    };

    private async Task<int> ObjetosEnElAlmacen()
    {
        var total = 0;
        foreach (var contenedor in new[] { CatalogoApiHost.Libros, CatalogoApiHost.Portadas })
        {
            var respuesta = await almacen.Cliente.ListObjectsV2Async(new ListObjectsV2Request { BucketName = contenedor });
            total += respuesta.S3Objects?.Count ?? 0;
        }

        return total;
    }

    // ---------- ISBN único (AC-CAT-14) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public async Task Ocho_altas_simultaneas_del_mismo_isbn_de_administradores_distintos_dejan_una_sola_obra_y_las_demas_reciben_409()
    {
        var administradores = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToList();
        var portadas = new List<Guid>();
        foreach (var admin in administradores)
        {
            portadas.Add(await _host.SubirYReferenciar("portada", Png(40, 40, 900), admin));
        }

        // Mitad con el ISBN-13 y mitad con su forma de ISBN-10: son la misma edición.
        var tareas = administradores.Select((admin, i) => _host.Json(HttpMethod.Post, "/administracion/obras", ObraImpresa(portadas[i], i % 2 == 0 ? "978-0-00-000001-9" : "0-00-000001-9", $"Obra {i}"), usuario: admin)).ToList();
        var respuestas = await Task.WhenAll(tareas);

        var creadas = respuestas.Count(r => r.StatusCode == HttpStatusCode.Created);
        var conflictos = respuestas.Count(r => r.StatusCode == HttpStatusCode.Conflict);
        foreach (var respuesta in respuestas.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            Assert.Equal("ECO-ISBN-DUPLICADO", Codigo(await CatalogoApiHost.Leer(respuesta)));
        }

        Assert.Equal(1, creadas);
        Assert.Equal(7, conflictos);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ediciones"));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.ediciones WHERE isbn13 = '9780000000019'"));
        foreach (var respuesta in respuestas)
        {
            respuesta.Dispose();
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public async Task Un_isbn_10_y_su_isbn_13_son_la_misma_edicion_en_cualquier_orden_y_el_isbn_de_una_obra_dada_de_baja_sigue_reservado()
    {
        var primera = await _host.RegistrarImpresa("Primera", "0-00-000001-9");
        var contra13 = await _host.Json(HttpMethod.Post, "/administracion/obras", ObraImpresa(await _host.SubirYReferenciar("portada", Png(40, 40, 900)), "978-0-00-000001-9", "Segunda"));

        using var baja = await _host.Json(HttpMethod.Post, $"/administracion/obras/{primera.GetProperty("obraId").GetGuid()}/baja", new { version = primera.GetProperty("version").GetInt64() });
        var trasLaBaja = await _host.Json(HttpMethod.Post, "/administracion/obras", ObraImpresa(await _host.SubirYReferenciar("portada", Png(40, 40, 900)), "9780000000019", "Tercera"));

        Assert.Equal(HttpStatusCode.Conflict, contra13.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, trasLaBaja.StatusCode);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        contra13.Dispose();
        trasLaBaja.Dispose();
    }

    // ---------- Búsqueda de 16 obras (AC-CAT-06) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-06")]
    public async Task De_16_obras_de_muestra_digital_tecnico_y_hasta_100_solo_salen_las_que_cumplen_las_tres_en_la_misma_edicion()
    {
        var esperadas = new List<Guid>();
        for (var i = 0; i < 16; i++)
        {
            var tecnico = i % 2 == 0;
            var precioDigital = i % 4 < 2 ? 80m : 150m;
            var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900));
            var libro = await _host.SubirYReferenciar("libro", Epub(rellenoEnBytes: 200 + i));
            using var alta = await _host.Json(HttpMethod.Post, "/administracion/obras", new
            {
                titulo = $"Libro {i:D2}",
                autor = "Ana",
                editorial = "Andina",
                categoria = tecnico ? "Técnico" : "Novela",
                portadaRef = portada,
                ediciones = new object[] { new { formato = "impresa", isbn = Isbn13(i * 2), precio = 40m }, Digital(Isbn13((i * 2) + 1), libro, precioDigital) },
            });
            Assert.Equal(HttpStatusCode.Created, alta.StatusCode);
            if (tecnico && precioDigital <= 100m)
            {
                esperadas.Add((await CatalogoApiHost.Leer(alta)).GetProperty("obraId").GetGuid());
            }
        }

        using var alcance = _host.Servicios.CreateScope();
        var consulta = alcance.ServiceProvider.GetRequiredService<IConsultaDeCatalogo>();
        var resultado = await consulta.BuscarObras(new BusquedaDeObras(Categoria: "técnico", Formato: FormatoDeEdicion.Digital, PrecioMaximo: 100m, Tamano: 50));
        var todas = await consulta.BuscarObras(new BusquedaDeObras(Tamano: 50));

        Assert.True(resultado.EsExito);
        Assert.Equal(esperadas.Order(), resultado.Valor.Obras.Select(o => o.ObraId).Order());
        Assert.Equal(4, resultado.Valor.Total);
        Assert.Equal(16, todas.Valor.Total);
    }

    // ---------- Obra dada de baja (AC-CAT-10) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-10")]
    public async Task Una_obra_dada_de_baja_sale_de_la_busqueda_y_la_ficha_pero_sus_ediciones_siguen_legibles_por_identificador_y_su_portada_se_sirve()
    {
        var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900));
        var libro = await _host.SubirYReferenciar("libro", Epub());
        using var alta = await _host.Json(HttpMethod.Post, "/administracion/obras", new
        {
            titulo = "Con pedidos",
            autor = "Ana",
            editorial = "Andina",
            categoria = "Técnico",
            portadaRef = portada,
            ediciones = new object[] { new { formato = "impresa", isbn = Isbn13(1), precio = 40m }, Digital(Isbn13(2), libro, 20m) },
        });
        var datos = await CatalogoApiHost.Leer(alta);
        var obraId = datos.GetProperty("obraId").GetGuid();
        var edicionIds = datos.GetProperty("edicionIds").EnumerateArray().Select(e => e.GetGuid()).ToList();
        using var baja = await _host.Json(HttpMethod.Post, $"/administracion/obras/{obraId}/baja", new { version = datos.GetProperty("version").GetInt64() });
        var huella = Convert.ToHexStringLower((await bd.Consultar<byte[]>("SELECT portada_huella FROM catalogo.obras"))!);

        using var alcance = _host.Servicios.CreateScope();
        var consulta = alcance.ServiceProvider.GetRequiredService<IConsultaDeCatalogo>();
        var busqueda = await consulta.BuscarObras(new BusquedaDeObras());
        var ficha = await consulta.ObtenerObra(obraId);
        var ediciones = await consulta.ObtenerEdiciones(edicionIds);
        using var imagen = await _host.Cliente.GetAsync($"/portadas/{huella}");

        Assert.Equal(HttpStatusCode.NoContent, baja.StatusCode);
        Assert.Equal(0, busqueda.Valor.Total);
        Assert.False(ficha.EsExito);
        Assert.Equal(2, ediciones.Valor.Count);
        Assert.All(ediciones.Valor, e =>
        {
            Assert.Equal("Con pedidos", e.Titulo);
            Assert.Equal(EstadoDeEdicionConsultado.DadaDeBaja, e.Estado);
            Assert.Equal(MotivoDeNoDisponibleDeEdicion.DadaDeBaja, e.MotivoDeNoDisponible);
        });
        Assert.Equal(HttpStatusCode.OK, imagen.StatusCode);
    }

    // ---------- Referencias de archivo (CAT-16, D05) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_referencia_ajena_vencida_usada_o_de_finalidad_cruzada_es_422_y_no_crea_la_obra()
    {
        var ana = Guid.NewGuid();
        var beto = Guid.NewGuid();
        var portadaDeAna = await _host.SubirYReferenciar("portada", Png(40, 40, 900), ana);
        var libroDeAna = await _host.SubirYReferenciar("libro", Epub(), ana);

        async Task<HttpResponseMessage> Alta(Guid portada, Guid? archivo, Guid usuario, string isbn) =>
            await _host.Json(
                HttpMethod.Post,
                "/administracion/obras",
                archivo is { } libro
                    ? new { titulo = "X", autor = "A", editorial = "E", categoria = "C", portadaRef = portada, ediciones = new object[] { Digital(isbn, libro, 10m) } }
                    : ObraImpresa(portada, isbn),
                usuario: usuario);

        // Ajena: la portada es de Ana y la usa Beto.
        using var ajena = await Alta(portadaDeAna, null, beto, Isbn13(1));
        // Cruzada: un libro como portada y una portada como libro.
        var portadaDeBeto = await _host.SubirYReferenciar("portada", Png(40, 40, 900), beto);
        using var libroComoPortada = await Alta(libroDeAna, null, ana, Isbn13(2));
        using var portadaComoLibro = await Alta(portadaDeBeto, portadaDeBeto, beto, Isbn13(3));
        // Una referencia que no existe.
        using var inexistente = await Alta(Guid.NewGuid(), null, ana, Isbn13(4));

        foreach (var respuesta in new[] { ajena, libroComoPortada, portadaComoLibro, inexistente })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
            Assert.Equal("ECO-REFERENCIA-DE-ARCHIVO-INVALIDA", Codigo(await CatalogoApiHost.Leer(respuesta)));
        }

        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes WHERE estado = 'consumida'"));

        // Usada: la primera alta la consume y la segunda ya no puede.
        using var primera = await Alta(portadaDeAna, null, ana, Isbn13(5));
        using var segunda = await Alta(portadaDeAna, null, ana, Isbn13(6));
        Assert.Equal(HttpStatusCode.Created, primera.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, segunda.StatusCode);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_referencia_vencida_a_las_24_horas_exactas_es_422_y_un_minuto_antes_sirve()
    {
        var ana = Guid.NewGuid();
        var vigente = await _host.SubirYReferenciar("portada", Png(40, 40, 900), ana);
        var vencida = await _host.SubirYReferenciar("portada", Png(40, 40, 900), ana);

        _host.Reloj.Adelantar(TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1));
        using var aTiempo = await _host.Json(HttpMethod.Post, "/administracion/obras", ObraImpresa(vigente, Isbn13(1)), usuario: ana);
        _host.Reloj.Adelantar(TimeSpan.FromMinutes(2));
        using var tarde = await _host.Json(HttpMethod.Post, "/administracion/obras", ObraImpresa(vencida, Isbn13(2)), usuario: ana);

        Assert.Equal(HttpStatusCode.Created, aTiempo.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, tarde.StatusCode);
        Assert.Equal("ECO-REFERENCIA-DE-ARCHIVO-INVALIDA", Codigo(await CatalogoApiHost.Leer(tarde)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Dos_altas_simultaneas_con_la_misma_referencia_dejan_una_sola_obra_y_la_referencia_se_consume_una_vez()
    {
        var ana = Guid.NewGuid();
        var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900), ana);

        var respuestas = await Task.WhenAll(Enumerable.Range(1, 6).Select(i => _host.Json(HttpMethod.Post, "/administracion/obras", ObraImpresa(portada, Isbn13(i), $"Obra {i}"), usuario: ana)));

        Assert.Equal(1, respuestas.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, respuestas.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.obras"));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes WHERE estado = 'consumida'"));
        foreach (var respuesta in respuestas)
        {
            respuesta.Dispose();
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_libro_epub_con_una_licencia_en_formato_pdf_es_422_por_no_coincidir_el_tipo_con_el_formato()
    {
        var portada = await _host.SubirYReferenciar("portada", Png(40, 40, 900));
        var epub = await _host.SubirYReferenciar("libro", Epub());

        using var respuesta = await _host.Json(HttpMethod.Post, "/administracion/obras", new
        {
            titulo = "X",
            autor = "A",
            editorial = "E",
            categoria = "C",
            portadaRef = portada,
            ediciones = new object[] { Digital(Isbn13(1), epub, 10m, formato: "pdf") },
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
        Assert.Equal("ECO-REFERENCIA-DE-ARCHIVO-INVALIDA", Codigo(await CatalogoApiHost.Leer(respuesta)));
    }

    // ---------- Límites de archivos por la subida HTTP (AC-CAT-12) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_bomba_de_compresion_y_un_zip_truncado_son_422_y_no_dejan_objetos_en_el_almacen()
    {
        var antes = await ObjetosEnElAlmacen();
        var bomba = Zip([Mimetype(), new EntradaZip("a.bin", new byte[100], Metodo: 8, DescomprimidoDeclarado: 900_000_000), new EntradaZip("b.bin", new byte[100], Metodo: 8, DescomprimidoDeclarado: 900_000_000)]);
        var truncado = Epub(rellenoEnBytes: 5000)[..4000];

        using var conBomba = await _host.Subir("libro", bomba);
        using var conTruncado = await _host.Subir("libro", truncado);

        foreach (var respuesta in new[] { conBomba, conTruncado })
        {
            Assert.Equal(HttpStatusCode.UnprocessableEntity, respuesta.StatusCode);
            var problema = await CatalogoApiHost.Leer(respuesta);
            Assert.Equal("ECO-ARCHIVO-INVALIDO", Codigo(problema));
            Assert.Equal("estructura-invalida", problema.GetProperty("errores")[0].GetProperty("clave").GetString());
        }

        Assert.Equal(antes, await ObjetosEnElAlmacen());
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM catalogo.archivos_pendientes"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_libro_de_exactamente_50_MiB_se_acepta_y_uno_de_un_byte_mas_es_413_sin_dejar_objetos_ni_subidas_a_medias()
    {
        var exacto = EpubDe(52_428_800);
        var unByteMas = EpubDe(52_428_801);

        using var aceptado = await _host.Subir("libro", exacto);
        var objetosTrasElExacto = await ObjetosEnElAlmacen();
        using var excedido = await _host.Subir("libro", unByteMas);

        Assert.Equal(HttpStatusCode.Created, aceptado.StatusCode);
        Assert.Equal(52_428_800, (await CatalogoApiHost.Leer(aceptado)).GetProperty("tamanoEnBytes").GetInt64());
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, excedido.StatusCode);
        Assert.Equal("ECO-ARCHIVO-DEMASIADO-GRANDE", Codigo(await CatalogoApiHost.Leer(excedido)));
        Assert.Equal(objetosTrasElExacto, await ObjetosEnElAlmacen());
        var subidas = await almacen.Cliente.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = CatalogoApiHost.Libros });
        Assert.Empty(subidas.MultipartUploads ?? []);
    }
}
