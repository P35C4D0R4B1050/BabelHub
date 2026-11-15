using System.Security.Cryptography;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>Los archivos del catálogo (T04; CAT-16, AC-CAT-12): tipo por los primeros bytes, topes con bytes reales, dimensiones, huella y las reglas de consumo de una referencia.</summary>
public sealed class ArchivosDelCatalogoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Beto = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private static HuellaDeArchivo UnaHuella(string semilla = "x") => HuellaDeArchivo.Desde(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(semilla)));

    private static ReferenciaDeArchivo ListaDeLibro(TipoDeArchivo tipo = TipoDeArchivo.Epub, Guid? usuario = null)
    {
        var referencia = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), usuario ?? Ana, FinalidadDeArchivo.Libro, Ahora);
        referencia.MarcarLista(tipo, 1_000, UnaHuella());
        return referencia;
    }

    private static ReferenciaDeArchivo ListaDePortada(Guid? usuario = null)
    {
        var referencia = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), usuario ?? Ana, FinalidadDeArchivo.Portada, Ahora);
        referencia.MarcarLista(TipoDeArchivo.Png, 500, UnaHuella("portada"));
        return referencia;
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void El_tipo_se_decide_por_los_primeros_bytes()
    {
        Assert.Equal(TipoDeArchivo.Epub, TiposDeArchivo.PorPrimerosBytes([0x50, 0x4B, 0x03, 0x04, 0x14, 0x00]));
        Assert.Equal(TipoDeArchivo.Pdf, TiposDeArchivo.PorPrimerosBytes("%PDF-1.7\n"u8));
        Assert.Equal(TipoDeArchivo.Jpeg, TiposDeArchivo.PorPrimerosBytes([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10]));
        Assert.Equal(TipoDeArchivo.Png, TiposDeArchivo.PorPrimerosBytes([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00]));
        Assert.Equal(TipoDeArchivo.Webp, TiposDeArchivo.PorPrimerosBytes("RIFF\0\0\0\0WEBPVP8 "u8));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Un_ejecutable_renombrado_como_epub_no_tiene_un_tipo_valido()
    {
        // «MZ» es la cabecera de un ejecutable de Windows; el nombre «libro.epub» no cuenta.
        Assert.Null(TiposDeArchivo.PorPrimerosBytes([0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]));
        Assert.Null(TiposDeArchivo.PorPrimerosBytes("#!/bin/sh\n"u8));
        Assert.Null(TiposDeArchivo.PorPrimerosBytes("RIFF\0\0\0\0WAVEfmt "u8));
        Assert.Null(TiposDeArchivo.PorPrimerosBytes("RIFF\0\0\0\0WEB"u8));
        Assert.Null(TiposDeArchivo.PorPrimerosBytes([]));
        Assert.Null(TiposDeArchivo.PorPrimerosBytes(" %PDF-1.4"u8));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Los_topes_son_de_50_y_2_mebibytes_y_el_tope_exacto_se_acepta()
    {
        Assert.Equal(52_428_800, PoliticaDeArchivos.TopeDeLibroEnBytes);
        Assert.Equal(2_097_152, PoliticaDeArchivos.TopeDePortadaEnBytes);
        Assert.False(PoliticaDeArchivos.SuperaElTope(FinalidadDeArchivo.Libro, 52_428_800));
        Assert.True(PoliticaDeArchivos.SuperaElTope(FinalidadDeArchivo.Libro, 52_428_801));
        Assert.True(PoliticaDeArchivos.SuperaElTope(FinalidadDeArchivo.Libro, 60L * 1024 * 1024));
        Assert.False(PoliticaDeArchivos.SuperaElTope(FinalidadDeArchivo.Portada, 2_097_152));
        Assert.True(PoliticaDeArchivos.SuperaElTope(FinalidadDeArchivo.Portada, 3L * 1024 * 1024));
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-12")]
    [InlineData(1, 1, true)]
    [InlineData(4000, 4000, true)]
    [InlineData(4096, 3906, true)]
    [InlineData(4096, 4096, false)]
    [InlineData(4097, 10, false)]
    [InlineData(10, 4097, false)]
    [InlineData(0, 100, false)]
    [InlineData(100, -1, false)]
    public void Las_dimensiones_de_una_imagen_no_pasan_de_4096_por_lado_ni_de_16_millones_de_pixeles(int ancho, int alto, bool valido) =>
        Assert.Equal(valido, PoliticaDeArchivos.DimensionesValidas(ancho, alto));

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Cada_tipo_corresponde_a_una_finalidad_y_un_libro_a_un_formato()
    {
        Assert.Equal(FinalidadDeArchivo.Libro, TiposDeArchivo.FinalidadDe(TipoDeArchivo.Pdf));
        Assert.Equal(FinalidadDeArchivo.Portada, TiposDeArchivo.FinalidadDe(TipoDeArchivo.Webp));
        Assert.True(TiposDeArchivo.EsAcordeAlFormato(TipoDeArchivo.Epub, FormatoDigital.Epub));
        Assert.False(TiposDeArchivo.EsAcordeAlFormato(TipoDeArchivo.Pdf, FormatoDigital.Epub));
        Assert.False(TiposDeArchivo.EsAcordeAlFormato(TipoDeArchivo.Png, FormatoDigital.Pdf));
        Assert.Equal("image/webp", TiposDeArchivo.TipoDeMedio(TipoDeArchivo.Webp));
        Assert.True(TiposDeArchivo.TryDesdeNombre("jpeg", out var tipo));
        Assert.Equal(TipoDeArchivo.Jpeg, tipo);
        Assert.False(TiposDeArchivo.TryDesdeNombre("JPEG", out _));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void La_huella_son_32_bytes_con_64_hexadecimales_y_una_huella_corta_de_16()
    {
        var huella = UnaHuella("hola");

        Assert.Equal(64, huella.Hex.Length);
        Assert.Equal(16, huella.Corta.Length);
        Assert.StartsWith(huella.Corta, huella.Hex, StringComparison.Ordinal);
        Assert.Throws<ArgumentException>(() => HuellaDeArchivo.Desde(new byte[31]));
        Assert.True(HuellaDeArchivo.TryDesdeHexadecimal(huella.Hex.ToUpperInvariant(), out var igual));
        Assert.Equal(huella, igual);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-12")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    [InlineData("00000000000000000000000000000000000000000000000000000000000000000")]
    [InlineData("../../etc/passwd")]
    public void Una_huella_que_no_tiene_exactamente_64_hexadecimales_se_rechaza(string? texto) =>
        Assert.False(HuellaDeArchivo.TryDesdeHexadecimal(texto, out _));

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void La_clave_del_almacen_son_128_bits_aleatorios_y_nunca_se_repite()
    {
        var claves = Enumerable.Range(0, 200).Select(_ => ClaveEnAlmacen.Nueva()).ToList();

        Assert.All(claves, c => Assert.True(ClaveEnAlmacen.EsValida(c)));
        Assert.Equal(200, claves.Distinct().Count());
        Assert.False(ClaveEnAlmacen.EsValida("libro.epub"));
        Assert.False(ClaveEnAlmacen.EsValida(new string('A', 32)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Una_referencia_nace_subiendo_con_su_vencimiento_a_las_24_horas_y_pasa_a_lista_al_verificarse()
    {
        var referencia = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), Ana, FinalidadDeArchivo.Libro, Ahora);

        Assert.Equal(EstadoDeReferencia.Subiendo, referencia.Estado);
        Assert.Equal(Ahora.AddHours(24), referencia.VenceEn);
        Assert.True(ClaveEnAlmacen.EsValida(referencia.ClaveDelAlmacen));

        referencia.MarcarLista(TipoDeArchivo.Pdf, 52_428_800, UnaHuella());

        Assert.Equal(EstadoDeReferencia.Lista, referencia.Estado);
        Assert.Equal(TipoDeArchivo.Pdf, referencia.TipoDetectado);
        Assert.Equal(52_428_800, referencia.TamanoEnBytes);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Marcar_lista_rechaza_un_tipo_de_otra_finalidad_un_tamano_sobre_el_tope_y_un_estado_que_no_es_subiendo()
    {
        var portada = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), Ana, FinalidadDeArchivo.Portada, Ahora);
        var libro = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), Ana, FinalidadDeArchivo.Libro, Ahora);

        Assert.Throws<ArgumentException>(() => portada.MarcarLista(TipoDeArchivo.Pdf, 100, UnaHuella()));
        Assert.Throws<ArgumentOutOfRangeException>(() => portada.MarcarLista(TipoDeArchivo.Png, 2_097_153, UnaHuella()));
        Assert.Throws<ArgumentOutOfRangeException>(() => libro.MarcarLista(TipoDeArchivo.Epub, 52_428_801, UnaHuella()));
        Assert.Throws<ArgumentOutOfRangeException>(() => libro.MarcarLista(TipoDeArchivo.Epub, 0, UnaHuella()));
        libro.MarcarLista(TipoDeArchivo.Epub, 100, UnaHuella());
        Assert.Throws<InvalidOperationException>(() => libro.MarcarLista(TipoDeArchivo.Epub, 100, UnaHuella()));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Consumir_una_referencia_propia_de_la_finalidad_y_vigente_la_marca_consumida_y_da_el_archivo()
    {
        var referencia = ListaDeLibro(TipoDeArchivo.Epub);

        var resultado = referencia.Consumir(Ana, FinalidadDeArchivo.Libro, FormatoDigital.Epub, Ahora.AddHours(23));

        Assert.Equal(ResultadoDeConsumo.Consumida, resultado);
        Assert.Equal(EstadoDeReferencia.Consumida, referencia.Estado);
        Assert.Equal(Ahora.AddHours(23), referencia.ConsumidoEn);
        var archivo = referencia.ComoArchivoDeEdicion();
        Assert.Equal(referencia.ClaveDelAlmacen, archivo.ClaveDelAlmacen);
        Assert.Equal(FormatoDigital.Epub, archivo.Formato);
        Assert.Equal(Ana, archivo.SubidoPor);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Una_referencia_ajena_no_se_consume_ni_se_gasta()
    {
        var referencia = ListaDePortada(Ana);

        Assert.Equal(ResultadoDeConsumo.NoEsDelUsuario, referencia.Consumir(Beto, FinalidadDeArchivo.Portada, null, Ahora.AddMinutes(1)));
        Assert.Equal(EstadoDeReferencia.Lista, referencia.Estado);
        Assert.Equal(ResultadoDeConsumo.Consumida, referencia.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddMinutes(2)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Una_referencia_vencida_se_rechaza_en_el_borde_de_las_24_horas()
    {
        var justoAntes = ListaDePortada();
        var exacto = ListaDePortada();
        var despues = ListaDePortada();

        Assert.Equal(ResultadoDeConsumo.Consumida, justoAntes.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddHours(24).AddTicks(-1)));
        Assert.Equal(ResultadoDeConsumo.Vencida, exacto.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddHours(24)));
        Assert.Equal(ResultadoDeConsumo.Vencida, despues.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddDays(2)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Una_referencia_se_usa_una_sola_vez()
    {
        var referencia = ListaDePortada();

        Assert.Equal(ResultadoDeConsumo.Consumida, referencia.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDeConsumo.NoDisponible, referencia.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddMinutes(2)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Una_portada_no_sirve_como_libro_ni_un_libro_como_portada()
    {
        var portada = ListaDePortada();
        var libro = ListaDeLibro();

        Assert.Equal(ResultadoDeConsumo.FinalidadDistinta, portada.Consumir(Ana, FinalidadDeArchivo.Libro, FormatoDigital.Epub, Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDeConsumo.FinalidadDistinta, libro.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddMinutes(1)));
        Assert.Equal(EstadoDeReferencia.Lista, portada.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Un_libro_solo_se_consume_con_el_formato_de_la_licencia()
    {
        var pdf = ListaDeLibro(TipoDeArchivo.Pdf);

        Assert.Equal(ResultadoDeConsumo.TipoNoCoincideConElFormato, pdf.Consumir(Ana, FinalidadDeArchivo.Libro, FormatoDigital.Epub, Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDeConsumo.TipoNoCoincideConElFormato, pdf.Consumir(Ana, FinalidadDeArchivo.Libro, null, Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDeConsumo.Consumida, pdf.Consumir(Ana, FinalidadDeArchivo.Libro, FormatoDigital.Pdf, Ahora.AddMinutes(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Una_fila_subiendo_o_en_descarte_no_es_consumible()
    {
        var subiendo = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), Ana, FinalidadDeArchivo.Portada, Ahora);
        var descarte = ListaDePortada();
        descarte.PasarADescarte();

        Assert.Equal(ResultadoDeConsumo.NoDisponible, subiendo.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDeConsumo.NoDisponible, descarte.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddMinutes(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void El_archivo_y_la_portada_solo_nacen_de_una_referencia_consumida_de_su_finalidad()
    {
        var sinConsumir = ListaDePortada();
        var consumida = ListaDePortada();
        consumida.Consumir(Ana, FinalidadDeArchivo.Portada, null, Ahora.AddMinutes(1));

        Assert.Throws<InvalidOperationException>(() => sinConsumir.ComoPortada());
        Assert.Throws<InvalidOperationException>(() => consumida.ComoArchivoDeEdicion());
        var portada = consumida.ComoPortada();
        Assert.Equal(TipoDeArchivo.Png, portada.TipoDetectado);
        Assert.Equal(consumida.Huella, portada.Huella);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void El_archivo_de_una_edicion_y_la_portada_validan_su_tipo_su_clave_y_su_tamano()
    {
        var clave = ClaveEnAlmacen.Nueva();
        var huella = UnaHuella();

        Assert.Throws<ArgumentException>(() => new ArchivoDeEdicion(clave, TipoDeArchivo.Png, 10, huella, Ahora, Ana));
        Assert.Throws<ArgumentException>(() => new ArchivoDeEdicion("libro.epub", TipoDeArchivo.Epub, 10, huella, Ahora, Ana));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArchivoDeEdicion(clave, TipoDeArchivo.Epub, 52_428_801, huella, Ahora, Ana));
        Assert.Throws<ArgumentException>(() => new PortadaDeObra(clave, TipoDeArchivo.Pdf, huella));
        Assert.Throws<ArgumentException>(() => new PortadaDeObra("../x", TipoDeArchivo.Png, huella));
        Assert.Equal(TipoDeArchivo.Pdf, new ArchivoDeEdicion(clave, TipoDeArchivo.Pdf, 52_428_800, huella, Ahora, Ana).TipoDetectado);
    }
}
