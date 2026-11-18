using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>SubirArchivo, ObtenerPortada y DepurarArchivosSinUso (T13; CAT-16, AC-CAT-12): fila subiendo antes de escribir, tipo por contenido, tope por bytes reales, almacén caído que rechaza y depuración desde las filas.</summary>
public sealed class ArchivosDelCatalogoUsoTests
{
    private static byte[] Epub(int total = 200)
    {
        var datos = new byte[total];
        datos[0] = 0x50;
        datos[1] = 0x4B;
        datos[2] = 0x03;
        datos[3] = 0x04;
        return datos;
    }

    private static byte[] Png(int total = 100)
    {
        var datos = new byte[total];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(datos, 0);
        return datos;
    }

    private static SubirArchivoEntrada Subida(FinalidadDeLaSubida finalidad, byte[] contenido) => new(finalidad, new MemoryStream(contenido));

    // ---------- Subir ----------

    [Fact]
    public async Task Un_epub_valido_se_acepta_queda_lista_con_su_huella_y_tamano_reales_ligada_al_usuario_con_vencimiento_a_24_horas()
    {
        var e = new EntornoDelCatalogo();
        var epub = Epub(500);

        var resultado = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, epub));

        Assert.True(resultado.EsExito);
        Assert.Equal("epub", resultado.Valor.Tipo);
        Assert.Equal(500, resultado.Valor.TamanoEnBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(epub)), resultado.Valor.HuellaSha256);
        Assert.Equal(e.Reloj.Ahora.AddHours(24), resultado.Valor.VenceEn);
        var fila = e.Referencias.Todas.Single();
        Assert.Equal(resultado.Valor.ReferenciaId, fila.Id);
        Assert.Equal(EstadoDeReferencia.Lista, fila.Estado);
        Assert.Equal(e.UsuarioId, fila.UsuarioId);
        Assert.Equal(FinalidadDeArchivo.Libro, fila.Finalidad);
        Assert.Equal(epub, e.Almacen.Objetos[fila.ClaveDelAlmacen]);
        Assert.True(ClaveEnAlmacen.EsValida(fila.ClaveDelAlmacen));
    }

    [Fact]
    public async Task Un_pdf_de_exactamente_50_mib_se_acepta_con_su_huella_guardada()
    {
        var e = new EntornoDelCatalogo();
        var pdf = new byte[(int)PoliticaDeArchivos.TopeDeLibroEnBytes];
        "%PDF-1.7\n"u8.CopyTo(pdf);

        var resultado = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, pdf));

        Assert.True(resultado.EsExito);
        Assert.Equal(52_428_800, resultado.Valor.TamanoEnBytes);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(pdf)), resultado.Valor.HuellaSha256);
    }

    [Fact]
    public async Task Las_portadas_jpeg_png_y_webp_validas_se_aceptan_y_una_de_3_mb_se_rechaza_con_su_motivo()
    {
        var e = new EntornoDelCatalogo();
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3 };
        var webp = "RIFF\0\0\0\0WEBPVP8 abc"u8.ToArray();
        var grande = Png((int)(3 * 1024 * 1024));

        var a = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, jpeg));
        var b = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, Png()));
        var c = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, webp));
        var d = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, grande));

        Assert.Equal(["jpeg", "png", "webp"], new[] { a, b, c }.Select(r => r.Valor.Tipo).ToList());
        Assert.Equal(ClavesDelCatalogo.ArchivoDemasiadoGrande, d.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("archivo", "demasiado-grande"), d.Error.Errores);
    }

    [Fact]
    public async Task Un_ejecutable_renombrado_como_epub_y_un_libro_de_60_mb_se_rechazan_sin_dejar_fila_ni_objeto()
    {
        var e = new EntornoDelCatalogo();
        var ejecutable = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 3, 0, 0, 0 };
        var libro = Epub(60 * 1024 * 1024);

        var exe = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, ejecutable));
        var grande = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, libro));

        Assert.Equal(ClavesDelCatalogo.ArchivoInvalido, exe.Error!.Clave);
        Assert.Contains(new ErrorDeCampo("archivo", "tipo-no-valido"), exe.Error.Errores);
        Assert.Equal(ClavesDelCatalogo.ArchivoDemasiadoGrande, grande.Error!.Clave);
        Assert.Empty(e.Referencias.Todas);
        Assert.Empty(e.Almacen.Objetos);
        Assert.Equal(2, e.Almacen.Borrados.Count);
    }

    [Fact]
    public async Task Una_portada_que_es_un_pdf_o_un_libro_que_es_una_imagen_se_rechazan_por_su_finalidad_y_la_estructura_invalida_tambien()
    {
        var e = new EntornoDelCatalogo();

        var portadaPdf = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, "%PDF-1.4 x"u8.ToArray()));
        var libroPng = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, Png()));
        e.Almacen.EstructuraInvalidaEnLaProxima = true;
        var truncado = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, Epub()));
        e.Almacen.DimensionesDeLaProxima = (5000, 100);
        var enorme = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, Png()));

        Assert.Contains(new ErrorDeCampo("archivo", "tipo-no-valido"), portadaPdf.Error!.Errores);
        Assert.Contains(new ErrorDeCampo("archivo", "tipo-no-valido"), libroPng.Error!.Errores);
        Assert.Contains(new ErrorDeCampo("archivo", "estructura-invalida"), truncado.Error!.Errores);
        Assert.Contains(new ErrorDeCampo("archivo", "dimensiones-invalidas"), enorme.Error!.Errores);
        Assert.Empty(e.Referencias.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Si_el_almacen_no_responde_la_subida_se_rechaza_y_la_fila_subiendo_queda_para_el_trabajo_de_fondo()
    {
        var e = new EntornoDelCatalogo();
        e.Almacen.NoResponde = true;

        var resultado = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, Epub()));

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(ClavesDelCatalogo.AlmacenNoDisponible, resultado.Error.Clave);
        Assert.Equal(EstadoDeReferencia.Subiendo, e.Referencias.Todas.Single().Estado);
    }

    [Fact]
    public async Task La_fila_subiendo_nace_antes_de_escribir_el_objeto_y_la_subida_son_dos_transacciones_cortas()
    {
        var e = new EntornoDelCatalogo();
        Guid? filaAlEscribir = null;
        var almacen = new AlmacenQueMiraLasFilas(e, id => filaAlEscribir = id);
        var subir = new SubirArchivo(e.Autorizador, new ValidadorDeSubirArchivo(), e.Referencias, almacen, e.Unidad, e.Reloj, e.Limitador, e.Eventos, e.Origen);

        var resultado = await subir.Ejecutar(Subida(FinalidadDeLaSubida.Libro, Epub()));

        Assert.True(resultado.EsExito);
        Assert.Equal(resultado.Valor.ReferenciaId, filaAlEscribir);
        Assert.Equal(2, e.Unidad.Ejecuciones);
    }

    private sealed class AlmacenQueMiraLasFilas(EntornoDelCatalogo entorno, Action<Guid> alEscribir) : IAlmacenDeArchivos
    {
        public Task<ResultadoDeEscrituraEnAlmacen> Escribir(string claveEnAlmacen, FinalidadDeArchivo finalidad, Stream contenido, CancellationToken cancelacion = default)
        {
            // En este instante la fila ya debe existir, en estado subiendo, con esta clave.
            var fila = entorno.Referencias.Todas.Single(r => r.ClaveDelAlmacen == claveEnAlmacen);
            Assert.Equal(EstadoDeReferencia.Subiendo, fila.Estado);
            alEscribir(fila.Id);
            return entorno.Almacen.Escribir(claveEnAlmacen, finalidad, contenido, cancelacion);
        }

        public Task<Stream?> AbrirPortada(string claveEnAlmacen, CancellationToken cancelacion = default) => entorno.Almacen.AbrirPortada(claveEnAlmacen, cancelacion);

        public Task Borrar(string claveEnAlmacen, CancellationToken cancelacion = default) => entorno.Almacen.Borrar(claveEnAlmacen, cancelacion);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-11")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_la_administracion_central_no_sube_y_el_cuerpo_no_se_lee(string quien)
    {
        var e = new EntornoDelCatalogo(quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.Anonimo(),
        });
        var flujo = new FlujoQueCuentaLecturas(Epub());

        var resultado = await e.Archivos().SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, flujo));

        Assert.NotNull(resultado.Error);
        Assert.Equal(0, flujo.Lecturas);
        Assert.Single(e.Denegaciones.Eventos);
        Assert.Empty(e.Referencias.Todas);
        Assert.Equal(0, e.Almacen.Escrituras);
    }

    private sealed class FlujoQueCuentaLecturas(byte[] datos) : MemoryStream(datos)
    {
        public int Lecturas { get; private set; }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Lecturas++;
            return base.Read(buffer, offset, count);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Lecturas++;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    [Fact]
    public async Task La_referencia_subida_se_puede_consumir_al_registrar_una_obra()
    {
        var e = new EntornoDelCatalogo();
        var portada = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, Png()));
        var libro = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, Epub()));

        var alta = await e.Administracion().RegistrarObra(new RegistrarObraEntrada("Subida", "A", "E", "C", portada.Valor.ReferenciaId, [e.EdicionDigital(archivoRef: libro.Valor.ReferenciaId)]));

        Assert.True(alta.EsExito, alta.Error?.Clave);
        Assert.All(e.Referencias.Todas, r => Assert.Equal(EstadoDeReferencia.Consumida, r.Estado));
    }

    // ---------- Portada ----------

    [Fact]
    public async Task Una_portada_se_sirve_por_su_huella_con_el_tipo_detectado_tambien_la_de_una_obra_dada_de_baja()
    {
        var e = new EntornoDelCatalogo();
        var subida = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, Png()));
        var obra = (await e.Administracion().RegistrarObra(new RegistrarObraEntrada("Con portada", "A", "E", "C", subida.Valor.ReferenciaId, [e.EdicionImpresa()]))).Valor;
        await e.DarDeBajaObra().Ejecutar(new DarDeBajaObraEntrada(obra.ObraId, obra.Version));

        var resultado = await e.Archivos().ObtenerPortada(subida.Valor.HuellaSha256);

        Assert.True(resultado.EsExito);
        await using var flujo = resultado.Valor.Contenido;
        Assert.Equal("image/png", resultado.Valor.TipoDeMedio);
        Assert.Equal(100, flujo.Length);
    }

    [Fact]
    public async Task La_huella_de_un_libro_da_404_en_portadas_y_tambien_una_huella_mal_formada_inexistente_o_con_el_almacen_sin_objeto()
    {
        var e = new EntornoDelCatalogo();
        var libro = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, Epub()));
        await e.Administracion().RegistrarObra(new RegistrarObraEntrada("Con libro", "A", "E", "C", (await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, Png()))).Valor.ReferenciaId, [e.EdicionDigital(archivoRef: libro.Valor.ReferenciaId)]));

        var deLibro = await e.Archivos().ObtenerPortada(libro.Valor.HuellaSha256);
        var mayusculas = await e.Archivos().ObtenerPortada(libro.Valor.HuellaSha256.ToUpperInvariant());
        var malFormada = await e.Archivos().ObtenerPortada("../../etc/passwd");
        var inexistente = await e.Archivos().ObtenerPortada(new string('a', 64));
        var vacia = await e.Archivos().ObtenerPortada(string.Empty);

        Assert.All(new[] { deLibro, mayusculas, malFormada, inexistente, vacia }, r =>
        {
            Assert.Equal(CategoriaDeError.NoEncontrado, r.Error!.Categoria);
            Assert.Equal(ClavesDelCatalogo.ObraNoEncontrada, r.Error.Clave);
        });
    }

    [Fact]
    public async Task Con_el_almacen_caido_la_portada_responde_no_disponible_y_si_falta_el_objeto_no_encontrada()
    {
        var e = new EntornoDelCatalogo();
        var subida = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Portada, Png()));
        await e.Administracion().RegistrarObra(new RegistrarObraEntrada("P", "A", "E", "C", subida.Valor.ReferenciaId, [e.EdicionImpresa()]));
        e.Almacen.NoResponde = true;

        var caido = await e.Archivos().ObtenerPortada(subida.Valor.HuellaSha256);
        e.Almacen.NoResponde = false;
        e.Almacen.Objetos.Keys.ToList().ForEach(k => e.Almacen.Borrar(k).GetAwaiter().GetResult());
        var sinObjeto = await e.Archivos().ObtenerPortada(subida.Valor.HuellaSha256);

        Assert.Equal(CategoriaDeError.NoDisponible, caido.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoEncontrado, sinObjeto.Error!.Categoria);
    }

    // ---------- Depuración ----------

    private static void Sembrar(EntornoDelCatalogo e, EstadoDeReferencia estado, TimeSpan edad, out string clave)
    {
        var creada = e.Reloj.Ahora - edad;
        var id = Guid.NewGuid();
        clave = ClaveEnAlmacen.Nueva();
        var huella = HuellaDeArchivo.Desde(SHA256.HashData(id.ToByteArray()));
        DateTimeOffset? consumida = estado == EstadoDeReferencia.Consumida ? creada : null;
        var referencia = ReferenciaDeArchivo.Rehidratar(
            id,
            e.UsuarioId,
            FinalidadDeArchivo.Libro,
            clave,
            estado,
            estado is EstadoDeReferencia.Subiendo ? null : TipoDeArchivo.Epub,
            estado is EstadoDeReferencia.Subiendo ? null : 10,
            estado is EstadoDeReferencia.Subiendo ? null : huella,
            creada,
            creada + PoliticaDeArchivos.VigenciaDeReferencia,
            consumida);
        e.Referencias.Agregar(referencia).GetAwaiter().GetResult();
        e.Almacen.Escribir(clave, FinalidadDeArchivo.Libro, new MemoryStream(Epub())).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task La_depuracion_parte_de_las_filas_borra_el_objeto_y_luego_la_fila_de_una_subiendo_vieja_una_lista_vencida_y_una_descarte()
    {
        var e = new EntornoDelCatalogo();
        Sembrar(e, EstadoDeReferencia.Subiendo, TimeSpan.FromHours(2), out var vieja);
        Sembrar(e, EstadoDeReferencia.Subiendo, TimeSpan.FromMinutes(10), out var enCurso);
        Sembrar(e, EstadoDeReferencia.Lista, TimeSpan.FromHours(25), out var vencida);
        Sembrar(e, EstadoDeReferencia.Lista, TimeSpan.FromHours(3), out var vigente);
        Sembrar(e, EstadoDeReferencia.Descarte, TimeSpan.FromMinutes(1), out var descarte);

        var resultado = await e.Depurador().Ejecutar(default);

        Assert.True(resultado.EsExito);
        Assert.Equal(new DepuracionDeArchivos(3, 3, 0), resultado.Valor);
        Assert.DoesNotContain(vieja, e.Almacen.Objetos.Keys);
        Assert.DoesNotContain(vencida, e.Almacen.Objetos.Keys);
        Assert.DoesNotContain(descarte, e.Almacen.Objetos.Keys);
        Assert.Contains(enCurso, e.Almacen.Objetos.Keys);
        Assert.Contains(vigente, e.Almacen.Objetos.Keys);
        Assert.Equal(2, e.Referencias.Todas.Count);
    }

    [Fact]
    public async Task Purgar_una_fila_consumida_nunca_borra_el_objeto_que_ya_vive_en_la_edicion_o_la_obra()
    {
        var e = new EntornoDelCatalogo();
        Sembrar(e, EstadoDeReferencia.Consumida, TimeSpan.FromDays(8), out var vieja);
        Sembrar(e, EstadoDeReferencia.Consumida, TimeSpan.FromDays(6), out var reciente);

        var resultado = await e.Depurador().Ejecutar(default);

        Assert.Equal(new DepuracionDeArchivos(0, 1, 0), resultado.Valor);
        Assert.Contains(vieja, e.Almacen.Objetos.Keys);
        Assert.Contains(reciente, e.Almacen.Objetos.Keys);
        Assert.Single(e.Referencias.Todas);
        Assert.Empty(e.Almacen.Borrados);
    }

    [Fact]
    public async Task Un_libro_reemplazado_a_los_30_dias_deja_su_objeto_borrado_aunque_su_fila_consumida_ya_se_purgo()
    {
        var e = new EntornoDelCatalogo();
        var obra = await e.ObraRegistrada("Reemplazo", e.EdicionDigital());
        var digital = obra.Ediciones[0];
        var claveAnterior = digital.Archivo!.ClaveDelAlmacen;
        await e.Almacen.Escribir(claveAnterior, FinalidadDeArchivo.Libro, new MemoryStream(Epub()));
        e.Reloj.Ahora = e.Reloj.Ahora.AddDays(30);
        await e.Depurador().Ejecutar(default);
        Assert.DoesNotContain(e.Referencias.Todas, r => r.Estado == EstadoDeReferencia.Consumida);
        var nuevo = await e.Archivos().SubirArchivo(Subida(FinalidadDeLaSubida.Libro, Epub(300)));

        await e.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, digital.Id, obra.Version, null, null, nuevo.Valor.ReferenciaId));
        var depuracion = await e.Depurador().Ejecutar(default);

        Assert.DoesNotContain(claveAnterior, e.Almacen.Objetos.Keys);
        Assert.True(depuracion.Valor.ObjetosBorrados >= 1);
        Assert.Contains((await e.Obras.Obtener(obra.Id, false))!.Ediciones[0].Archivo!.ClaveDelAlmacen, e.Almacen.Objetos.Keys);
    }

    [Fact]
    public async Task Un_objeto_que_no_se_pudo_borrar_deja_su_fila_para_la_corrida_siguiente_y_solo_el_sistema_depura()
    {
        var e = new EntornoDelCatalogo();
        Sembrar(e, EstadoDeReferencia.Descarte, TimeSpan.FromMinutes(1), out _);
        e.Almacen.NoResponde = true;

        var fallida = await e.Depurador().Ejecutar(default);
        var deUsuario = await e.Depurador(ActorDePrueba.DeAdministradorCentral()).Ejecutar(default);

        Assert.Equal(new DepuracionDeArchivos(0, 0, 1), fallida.Valor);
        Assert.Single(e.Referencias.Todas);
        Assert.Equal(CategoriaDeError.Prohibido, deUsuario.Error!.Categoria);

        e.Almacen.NoResponde = false;
        var siguiente = await e.Depurador().Ejecutar(default);
        Assert.Equal(new DepuracionDeArchivos(1, 1, 0), siguiente.Valor);
        Assert.Empty(e.Referencias.Todas);
    }
}
