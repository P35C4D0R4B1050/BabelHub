using System.Security.Cryptography;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.Configuration;
using BabelHub.IntegrationTests.Plataforma;
using static BabelHub.IntegrationTests.Catalogo.ArchivosDeMuestra;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>
/// El almacén de archivos del Catálogo contra un almacén S3 real en un contenedor (T17; CAT-16, AC-CAT-12): tipo por contenido, tope por bytes realmente leídos (60 MB, 3 MB, 50 MiB exactos, tamaño declarado falso), estructura por rangos
/// (bomba de compresión, ZIP truncado), huella calculada en el recorrido, almacén caído y cancelación. No reproduce las políticas de Oracle (permisos por contenedor): eso es del spike T02.
/// </summary>
[Collection(AlmacenDeObjetosCollection.Nombre)]
public sealed class S3AlmacenDeArchivosTests : IAsyncLifetime
{
    private const string Libros = "libros-t17";
    private const string Portadas = "portadas-t17";

    private readonly AlmacenDeObjetosFixture _almacen;
    private readonly S3AlmacenDeArchivos _sut;

    public S3AlmacenDeArchivosTests(AlmacenDeObjetosFixture almacen)
    {
        _almacen = almacen;
        _sut = new S3AlmacenDeArchivos(almacen.Cliente, Libros, Portadas);
    }

    public async Task InitializeAsync()
    {
        foreach (var contenedor in new[] { Libros, Portadas })
        {
            try
            {
                await _almacen.Cliente.PutBucketAsync(contenedor);
            }
            catch (AmazonS3Exception error) when (error.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
            }
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static string Clave() => ClaveEnAlmacen.Nueva();

    private async Task<byte[]?> Leer(string contenedor, string clave)
    {
        try
        {
            using var respuesta = await _almacen.Cliente.GetObjectAsync(contenedor, clave);
            using var memoria = new MemoryStream();
            await respuesta.ResponseStream.CopyToAsync(memoria);
            return memoria.ToArray();
        }
        catch (AmazonS3Exception error) when (error.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<int> SubidasSinTerminar(string contenedor) =>
        (await _almacen.Cliente.ListMultipartUploadsAsync(new ListMultipartUploadsRequest { BucketName = contenedor })).MultipartUploads?.Count ?? 0;

    /// <summary>Un flujo que entrega <paramref name="largo"/> bytes empezando por <paramref name="inicio"/> sin tenerlos todos en memoria; su <c>Length</c> miente si se pide.</summary>
    private sealed class FlujoGrande(byte[] inicio, long largo, long? largoDeclarado = null) : Stream
    {
        private long _posicion;

        public long BytesEntregados => _posicion;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => largoDeclarado ?? largo;

        public override long Position
        {
            get => _posicion;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var restante = largo - _posicion;
            var n = (int)Math.Min(count, restante);
            for (var i = 0; i < n; i++)
            {
                buffer[offset + i] = _posicion + i < inicio.Length ? inicio[_posicion + i] : (byte)0;
            }

            _posicion += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // ---------- Libros ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_epub_valido_se_guarda_en_el_contenedor_de_libros_con_la_huella_y_el_tamano_reales()
    {
        var epub = Epub(rellenoEnBytes: 3000);
        var clave = Clave();

        var resultado = await _sut.Escribir(clave, FinalidadDeArchivo.Libro, new MemoryStream(epub));

        Assert.True(resultado.Escrito);
        Assert.Equal(TipoDeArchivo.Epub, resultado.Archivo!.Tipo);
        Assert.Equal(epub.Length, resultado.Archivo.TamanoEnBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(epub)), resultado.Archivo.Huella.Hex);
        Assert.Equal(epub, await Leer(Libros, clave));
        Assert.Null(await Leer(Portadas, clave));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_pdf_valido_se_guarda_y_uno_sin_cierre_se_rechaza_y_se_borra()
    {
        var buena = Clave();
        var mala = Clave();

        var ok = await _sut.Escribir(buena, FinalidadDeArchivo.Libro, new MemoryStream(Pdf()));
        var sinCierre = await _sut.Escribir(mala, FinalidadDeArchivo.Libro, new MemoryStream(Pdf(conCierre: false)));

        Assert.Equal(TipoDeArchivo.Pdf, ok.Archivo!.Tipo);
        Assert.NotNull(await Leer(Libros, buena));
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, sinCierre.Rechazo);
        Assert.Null(await Leer(Libros, mala));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_ejecutable_renombrado_como_epub_se_rechaza_antes_de_escribir()
    {
        var clave = Clave();
        var ejecutable = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 }.Concat(new byte[2000]).ToArray();

        var resultado = await _sut.Escribir(clave, FinalidadDeArchivo.Libro, new MemoryStream(ejecutable));

        Assert.Equal(MotivoDeRechazoDeArchivo.TipoNoValido, resultado.Rechazo);
        Assert.Null(await Leer(Libros, clave));
        Assert.Null(await Leer(Portadas, clave));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_libro_con_la_firma_de_un_zip_pero_truncado_se_rechaza_y_no_queda_el_objeto()
    {
        var clave = Clave();
        var truncado = Epub(rellenoEnBytes: 5000)[..4000];

        var resultado = await _sut.Escribir(clave, FinalidadDeArchivo.Libro, new MemoryStream(truncado));

        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, resultado.Rechazo);
        Assert.Null(await Leer(Libros, clave));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_bomba_de_compresion_se_rechaza_por_el_directorio_central_sin_descomprimir_nada()
    {
        var clave = Clave();
        var bomba = Zip([Mimetype(), new EntradaZip("bomba.bin", new byte[200], Metodo: 8, DescomprimidoDeclarado: 700_000_000), new EntradaZip("bomba2.bin", new byte[200], Metodo: 8, DescomprimidoDeclarado: 700_000_000)]);

        var resultado = await _sut.Escribir(clave, FinalidadDeArchivo.Libro, new MemoryStream(bomba));

        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, resultado.Rechazo);
        Assert.Null(await Leer(Libros, clave));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_libro_de_60_MB_se_corta_al_pasar_el_tope_sin_leer_todo_el_flujo()
    {
        var clave = Clave();
        var flujo = new FlujoGrande(Epub()[..200], 60L * 1000 * 1000);

        var resultado = await _sut.Escribir(clave, FinalidadDeArchivo.Libro, flujo);

        Assert.Equal(MotivoDeRechazoDeArchivo.DemasiadoGrande, resultado.Rechazo);
        Assert.True(flujo.BytesEntregados <= PoliticaDeArchivos.TopeDeLibroEnBytes + S3AlmacenDeArchivos.TamanoDeParte, "Se siguió leyendo mucho después de pasar el tope.");
        Assert.Null(await Leer(Libros, clave));
        Assert.Equal(0, await SubidasSinTerminar(Libros));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task El_tamano_declarado_no_cuenta_solo_los_bytes_realmente_leidos()
    {
        var claveGrande = Clave();
        var clavePequena = Clave();
        var pequeno = Epub();

        // Declara 100 bytes pero entrega más de 50 MiB.
        var mentiraGrande = await _sut.Escribir(claveGrande, FinalidadDeArchivo.Libro, new FlujoGrande(pequeno[..200], 55L * 1024 * 1024, largoDeclarado: 100));

        // Declara 80 MiB pero entrega un EPUB pequeño y válido.
        var mentiraPequena = await _sut.Escribir(clavePequena, FinalidadDeArchivo.Libro, new FlujoGrande(pequeno, pequeno.Length, largoDeclarado: 80L * 1024 * 1024));

        Assert.Equal(MotivoDeRechazoDeArchivo.DemasiadoGrande, mentiraGrande.Rechazo);
        Assert.True(mentiraPequena.Escrito);
        Assert.Equal(pequeno.Length, mentiraPequena.Archivo!.TamanoEnBytes);
    }

    // ---------- Portadas ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_portada_valida_se_guarda_en_su_contenedor_y_se_puede_abrir_y_una_ausente_da_null()
    {
        var png = Png(800, 600, tamanoTotal: 20_000);
        var clave = Clave();

        var resultado = await _sut.Escribir(clave, FinalidadDeArchivo.Portada, new MemoryStream(png));

        Assert.Equal(TipoDeArchivo.Png, resultado.Archivo!.Tipo);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(png)), resultado.Archivo.Huella.Hex);
        Assert.Equal(png, await Leer(Portadas, clave));
        await using (var abierta = await _sut.AbrirPortada(clave))
        {
            using var memoria = new MemoryStream();
            await abierta!.CopyToAsync(memoria);
            Assert.Equal(png, memoria.ToArray());
        }

        Assert.Null(await _sut.AbrirPortada(Clave()));
        Assert.Null(await _sut.AbrirPortada("../no-es-una-clave"));
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-12")]
    [InlineData("jpeg")]
    [InlineData("png")]
    [InlineData("webp")]
    public async Task Las_tres_imagenes_admitidas_se_aceptan(string formato)
    {
        var datos = formato switch
        {
            "jpeg" => Jpeg(1200, 900, tamanoTotal: 10_000),
            "png" => Png(1200, 900, tamanoTotal: 10_000),
            _ => Webp(1200, 900),
        };

        var resultado = await _sut.Escribir(Clave(), FinalidadDeArchivo.Portada, new MemoryStream(datos));

        Assert.True(resultado.Escrito);
        Assert.Equal(formato, TiposDeArchivo.Nombre(resultado.Archivo!.Tipo));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_portada_de_3_MB_se_rechaza_y_una_de_exactamente_2_MiB_se_acepta()
    {
        var excedida = await _sut.Escribir(Clave(), FinalidadDeArchivo.Portada, new MemoryStream(Png(640, 480, tamanoTotal: 3 * 1024 * 1024)));
        var exacta = await _sut.Escribir(Clave(), FinalidadDeArchivo.Portada, new MemoryStream(Png(640, 480, tamanoTotal: 2 * 1024 * 1024)));
        var unByteMas = await _sut.Escribir(Clave(), FinalidadDeArchivo.Portada, new MemoryStream(Png(640, 480, tamanoTotal: 2 * 1024 * 1024 + 1)));

        Assert.Equal(MotivoDeRechazoDeArchivo.DemasiadoGrande, excedida.Rechazo);
        Assert.Equal(2 * 1024 * 1024, exacta.Archivo!.TamanoEnBytes);
        Assert.Equal(MotivoDeRechazoDeArchivo.DemasiadoGrande, unByteMas.Rechazo);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-12")]
    [InlineData(4097, 100)]
    [InlineData(4096, 4096)]
    [InlineData(0, 100)]
    public async Task Una_imagen_con_dimensiones_fuera_de_limite_se_rechaza_antes_de_escribir(int ancho, int alto)
    {
        var clave = Clave();

        var png = await _sut.Escribir(clave, FinalidadDeArchivo.Portada, new MemoryStream(Png(ancho, alto, tamanoTotal: 2000)));
        var jpeg = await _sut.Escribir(clave, FinalidadDeArchivo.Portada, new MemoryStream(Jpeg(ancho, alto, tamanoTotal: 2000)));

        Assert.Equal(MotivoDeRechazoDeArchivo.DimensionesInvalidas, png.Rechazo);
        Assert.Equal(MotivoDeRechazoDeArchivo.DimensionesInvalidas, jpeg.Rechazo);
        Assert.Null(await Leer(Portadas, clave));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_portada_que_es_un_pdf_o_un_libro_que_es_una_imagen_se_rechazan_por_la_finalidad()
    {
        var portadaPdf = await _sut.Escribir(Clave(), FinalidadDeArchivo.Portada, new MemoryStream(Pdf()));
        var libroPng = await _sut.Escribir(Clave(), FinalidadDeArchivo.Libro, new MemoryStream(Png(10, 10, 500)));
        var vacio = await _sut.Escribir(Clave(), FinalidadDeArchivo.Libro, new MemoryStream([]));
        var imagenTruncada = await _sut.Escribir(Clave(), FinalidadDeArchivo.Portada, new MemoryStream(Png(10, 10)[..16]));

        Assert.Equal(MotivoDeRechazoDeArchivo.TipoNoValido, portadaPdf.Rechazo);
        Assert.Equal(MotivoDeRechazoDeArchivo.TipoNoValido, libroPng.Rechazo);
        Assert.Equal(MotivoDeRechazoDeArchivo.TipoNoValido, vacio.Rechazo);
        Assert.Equal(MotivoDeRechazoDeArchivo.EstructuraInvalida, imagenTruncada.Rechazo);
    }

    // ---------- Borrar, caídas y cancelación ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Borrar_quita_el_objeto_de_cualquiera_de_los_dos_contenedores_y_borrar_uno_ausente_no_es_un_error()
    {
        var libro = Clave();
        var portada = Clave();
        await _sut.Escribir(libro, FinalidadDeArchivo.Libro, new MemoryStream(Epub()));
        await _sut.Escribir(portada, FinalidadDeArchivo.Portada, new MemoryStream(Png(10, 10, 500)));

        await _sut.Borrar(libro);
        await _sut.Borrar(portada);
        await _sut.Borrar(Clave());
        await _sut.Borrar("no-es-una-clave");

        Assert.Null(await Leer(Libros, libro));
        Assert.Null(await Leer(Portadas, portada));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_almacen_que_no_responde_es_almacen_no_disponible_en_escribir_abrir_y_borrar()
    {
        var caido = new AmazonS3Client(
            new BasicAWSCredentials("acceso-de-pruebas-0123456789", "secreta-de-pruebas-0123456789-abcdefghijkl"),
            new AmazonS3Config { ServiceURL = "http://127.0.0.1:1", ForcePathStyle = true, MaxErrorRetry = 0, Timeout = TimeSpan.FromSeconds(5), AuthenticationRegion = "us-east-1" });
        var sut = new S3AlmacenDeArchivos(caido, Libros, Portadas);

        await Assert.ThrowsAsync<AlmacenNoDisponibleException>(() => sut.Escribir(Clave(), FinalidadDeArchivo.Libro, new MemoryStream(Epub())));
        await Assert.ThrowsAsync<AlmacenNoDisponibleException>(() => sut.Escribir(Clave(), FinalidadDeArchivo.Portada, new MemoryStream(Png(10, 10, 500))));
        await Assert.ThrowsAsync<AlmacenNoDisponibleException>(() => sut.AbrirPortada(Clave()));
        await Assert.ThrowsAsync<AlmacenNoDisponibleException>(() => sut.Borrar(Clave()));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Un_contenedor_inexistente_es_almacen_no_disponible_y_no_un_exito()
    {
        var sut = new S3AlmacenDeArchivos(_almacen.Cliente, "no-existe-t17", Portadas);

        await Assert.ThrowsAsync<AlmacenNoDisponibleException>(() => sut.Escribir(Clave(), FinalidadDeArchivo.Libro, new MemoryStream(Epub())));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task La_cancelacion_del_llamador_se_propaga_y_no_se_confunde_con_un_almacen_caido()
    {
        using var cancelada = new CancellationTokenSource();
        await cancelada.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _sut.Escribir(Clave(), FinalidadDeArchivo.Libro, new MemoryStream(Epub()), cancelada.Token));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Una_clave_que_no_es_de_32_hexadecimales_se_rechaza()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.Escribir("../etc/passwd", FinalidadDeArchivo.Libro, new MemoryStream(Epub())));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.Escribir(new string('A', 32), FinalidadDeArchivo.Libro, new MemoryStream(Epub())));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public void Las_opciones_del_catalogo_se_cargan_con_el_cargador_generico_bajo_su_prefijo_y_no_revelan_la_clave_secreta()
    {
        static IConfiguration Configuracion(params (string Ruta, string Valor)[] valores) =>
            new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Ruta, v => (string?)v.Valor)).Build();

        var acceso = "acceso-" + Guid.NewGuid().ToString("N") + "-de-prueba";
        var secreta = "secreta-" + Guid.NewGuid().ToString("N") + "-de-prueba";
        (string, string)[] completa =
        [
            ("Almacen:Catalogo:Endpoint", "https://almacen.ejemplo.pe"),
            ("Almacen:Catalogo:Region", "sa-lima-1"),
            ("Almacen:Catalogo:ClaveDeAcceso", acceso),
            ("Almacen:Catalogo:ClaveSecreta", secreta),
        ];

        // La composición toma lo cargado y arma las opciones del catálogo (el módulo no usa la Infraestructura de Plataforma).
        var generica = OpcionesDelAlmacenDeObjetos.Cargar(Configuracion(completa), raiz: "Almacen:Catalogo:");
        var opciones = new OpcionesDelAlmacenDelCatalogo(generica.Endpoint, generica.Region, generica.ClaveDeAcceso, generica.ClaveSecreta);
        Assert.Equal("libros", opciones.ContenedorDeLibros);
        Assert.Equal("portadas", opciones.ContenedorDePortadas);
        Assert.DoesNotContain(secreta, opciones.ToString(), StringComparison.Ordinal);
        using var cliente = opciones.CrearCliente();
        Assert.NotNull(cliente);

        var sinRegion = completa.Where(v => !v.Item1.EndsWith("Region", StringComparison.Ordinal)).ToArray();
        var falta = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAlmacenDeObjetos.Cargar(Configuracion(sinRegion), raiz: "Almacen:Catalogo:"));
        Assert.Contains("BABELHUB__Almacen__Catalogo__Region", falta.Message, StringComparison.Ordinal);
    }
}
