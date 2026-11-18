using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>El verificador de contenido (T17; CAT-16, AC-CAT-12): estructura de un EPUB y de un PDF por lecturas por rango, y dimensiones de una imagen por su cabecera. Sin almacén ni red.</summary>
public sealed class VerificadorDeContenidoTests
{
    private static Task<MotivoDeRechazoDeArchivo?> Verificar(TipoDeArchivo tipo, byte[] datos) => VerificadorDeContenido.VerificarEstructura(tipo, new LectorDeRangosEnMemoria(datos));

    // ---------- EPUB ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_epub_valido_se_acepta_y_solo_se_lee_el_directorio_central_y_el_mimetype()
    {
        var epub = Epub(rellenoEnBytes: 10 * 1024 * 1024);
        var lector = new LectorDeRangosEnMemoria(epub);

        var motivo = await VerificadorDeContenido.VerificarEstructura(TipoDeArchivo.Epub, lector);

        Assert.Null(motivo);
        Assert.True(lector.BytesLeidos < 100 * 1024, $"Se leyeron {lector.BytesLeidos} bytes de {epub.Length}.");
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_zip_truncado_o_sin_fin_de_directorio_se_rechaza()
    {
        var epub = Epub();

        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, epub[..^40]));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, epub[..30]));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, [0x50, 0x4B, 0x03, 0x04]));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, []));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_bomba_de_compresion_con_un_total_descomprimido_declarado_enorme_se_rechaza()
    {
        var bomba = Zip([Mimetype(), new EntradaZip("a.bin", new byte[100], Metodo: 8, DescomprimidoDeclarado: 900_000_000), new EntradaZip("b.bin", new byte[100], Metodo: 8, DescomprimidoDeclarado: 900_000_000)]);

        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, bomba));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_total_descomprimido_justo_bajo_el_tope_se_acepta()
    {
        var epub = Zip([Mimetype(), new EntradaZip("a.bin", new byte[100], Metodo: 8, DescomprimidoDeclarado: 500_000_000)]);

        Assert.Null(await Verificar(TipoDeArchivo.Epub, epub));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task El_tope_de_2000_entradas_se_acepta_y_la_entrada_2001_se_rechaza()
    {
        List<EntradaZip> Con(int cuantas) => [Mimetype(), .. Enumerable.Range(1, cuantas - 1).Select(i => new EntradaZip($"f{i}", [1]))];

        Assert.Null(await Verificar(TipoDeArchivo.Epub, Zip(Con(2000))));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, Zip(Con(2001))));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_directorio_central_de_mas_de_1_MiB_se_rechaza_sin_leerlo()
    {
        var entradas = new List<EntradaZip> { Mimetype() };
        entradas.AddRange(Enumerable.Range(1, 1999).Select(i => new EntradaZip($"f{i}", [1], ExtraEnElDirectorio: 600)));
        var zip = Zip(entradas);
        var lector = new LectorDeRangosEnMemoria(zip);

        var motivo = await VerificadorDeContenido.VerificarEstructura(TipoDeArchivo.Epub, lector);

        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, motivo);
        Assert.True(lector.BytesLeidos <= 22 + 65535);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Tamanos_declarados_incoherentes_se_rechazan()
    {
        // Almacenado con tamaños distintos.
        var distintos = Zip([Mimetype(), new EntradaZip("a", new byte[10], Metodo: 0, ComprimidoDeclarado: 10, DescomprimidoDeclarado: 20)]);

        // Un dato declarado más grande de lo que cabe antes del directorio.
        var desbordado = Zip([Mimetype(), new EntradaZip("a", new byte[10], Metodo: 0, ComprimidoDeclarado: 4_000_000, DescomprimidoDeclarado: 4_000_000)]);

        // Un método de compresión desconocido.
        var metodo = Zip([Mimetype(), new EntradaZip("a", new byte[10], Metodo: 12)]);

        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, distintos));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, desbordado));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, metodo));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task El_mimetype_debe_existir_estar_almacenado_pesar_hasta_64_bytes_y_decir_application_epub_zip()
    {
        var sin = Zip([new EntradaZip("otro", [1, 2, 3])]);
        var distinto = Zip([Mimetype("application/zip")]);
        var conSalto = Zip([Mimetype("application/epub+zip\n")]);
        var comprimido = Zip([new EntradaZip("mimetype", "application/epub+zip"u8.ToArray(), Metodo: 8)]);
        var largo = Zip([Mimetype(new string('x', 65))]);
        var vacio = Zip([new EntradaZip("mimetype", [])]);

        foreach (var caso in new[] { sin, distinto, conSalto, comprimido, largo, vacio })
        {
            Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, caso));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_epub_con_comentario_al_final_se_acepta_y_el_zip64_o_varios_discos_se_rechazan()
    {
        Assert.Null(await Verificar(TipoDeArchivo.Epub, Zip([Mimetype()], comentario: 300)));

        var zip64 = Zip([Mimetype()]);
        zip64[zip64.Length - 22 + 10] = 0xFF;
        zip64[zip64.Length - 22 + 11] = 0xFF;
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, zip64));

        var discos = Zip([Mimetype()]);
        discos[discos.Length - 22 + 4] = 1;
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Epub, discos));
    }

    // ---------- PDF ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_pdf_con_cabecera_y_cierre_se_acepta_y_sin_cierre_o_sin_cabecera_se_rechaza()
    {
        Assert.Null(await Verificar(TipoDeArchivo.Pdf, Pdf()));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Pdf, Pdf(conCierre: false)));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Pdf, "%%EOF\nsin cabecera"u8.ToArray()));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, await Verificar(TipoDeArchivo.Pdf, []));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Del_pdf_solo_se_lee_la_cabecera_y_la_cola()
    {
        var pdf = Pdf().AsSpan(0, Pdf().Length - 6).ToArray().Concat(new byte[5 * 1024 * 1024]).Concat("%%EOF\n"u8.ToArray()).ToArray();
        var lector = new LectorDeRangosEnMemoria(pdf);

        var motivo = await VerificadorDeContenido.VerificarEstructura(TipoDeArchivo.Pdf, lector);

        Assert.Null(motivo);
        Assert.True(lector.BytesLeidos <= 1024 + 5);
    }

    // ---------- Imágenes ----------

    [Theory]
    [Trait("Criterio", "AC-CAT-12")]
    [InlineData(640, 480)]
    [InlineData(4096, 3900)]
    [InlineData(1, 1)]
    public void Las_dimensiones_se_leen_de_la_cabecera_de_png_jpeg_y_webp(int ancho, int alto)
    {
        Assert.Equal((ancho, alto), VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Png, Png(ancho, alto)));
        Assert.Equal((ancho, alto), VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Jpeg, Jpeg(ancho, alto)));
        Assert.Equal((ancho, alto), VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Webp, Webp(ancho, alto)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Un_jpeg_con_un_segmento_de_metadatos_grande_antes_del_cuadro_se_lee_igual()
    {
        Assert.Equal((800, 600), VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Jpeg, Jpeg(800, 600, bytesDeApp: 60_000)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Una_cabecera_truncada_o_mal_formada_no_da_dimensiones()
    {
        Assert.Null(VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Png, Png(10, 10)[..20]));
        Assert.Null(VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Jpeg, [0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Null(VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Webp, Webp(10, 10)[..20]));
        Assert.Null(VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Epub, [1, 2, 3]));
        var pngSinIhdr = Png(10, 10);
        pngSinIhdr[12] = (byte)'X';
        Assert.Null(VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Png, pngSinIhdr));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Una_imagen_enorme_declarada_en_la_cabecera_no_desborda()
    {
        var png = Png(1, 1);
        png[16] = 0xFF;
        png[17] = 0xFF;
        png[18] = 0xFF;
        png[19] = 0xFF;

        var dimensiones = VerificadorDeContenido.DimensionesDeImagen(TipoDeArchivo.Png, png);

        Assert.NotNull(dimensiones);
        Assert.False(PoliticaDeArchivos.DimensionesValidas(dimensiones.Value.Ancho, dimensiones.Value.Alto));
    }
}
