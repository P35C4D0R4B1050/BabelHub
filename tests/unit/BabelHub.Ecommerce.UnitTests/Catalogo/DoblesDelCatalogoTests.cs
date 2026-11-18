using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>
/// Los dobles de los puertos del Catálogo (T05) respetan la semántica del puerto, para que las pruebas de los casos de uso (T07 a T13) no prueben contra un doble que miente:
/// la idempotencia del Inventario, el respaldo de la memoria, el vencimiento del token a los 30 minutos y la verificación del almacén (AC-CAT-12, AC-CAT-16, AC-CAT-18).
/// </summary>
public sealed class DoblesDelCatalogoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly Guid Ana = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Beto = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    [Fact]
    public async Task El_inventario_doble_aplica_una_vez_la_misma_clave_con_la_misma_carga_y_rechaza_otra_carga()
    {
        var inventario = new InventarioDelCatalogoDoble();
        var edicion = Guid.NewGuid();
        var clave = Guid.NewGuid();

        var primero = await inventario.RegistrarIngreso(new SolicitudDeIngresoAlInventario(edicion, clave, 30, "GR-001", null));
        var repetido = await inventario.RegistrarIngreso(new SolicitudDeIngresoAlInventario(edicion, clave, 30, "GR-001", null));
        var otraCarga = await inventario.RegistrarIngreso(new SolicitudDeIngresoAlInventario(edicion, clave, 31, "GR-001", null));

        Assert.Equal(TipoDeRespuestaDeIngreso.Creado, primero.Tipo);
        Assert.Equal(primero.IngresoId, repetido.IngresoId);
        Assert.Equal("INV-IDEMPOTENCIA-EN-CONFLICTO", otraCarga.CodigoDeError);
        Assert.Single(inventario.Ingresos);
        Assert.Equal(30, inventario.Ediciones[edicion].Sum(f => f.Vendible));
    }

    [Fact]
    public async Task El_inventario_doble_puede_aplicar_el_ingreso_y_no_responder_o_estar_caido()
    {
        var inventario = new InventarioDelCatalogoDoble { AplicaYNoResponde = true };
        var edicion = Guid.NewGuid();

        var agotado = await inventario.RegistrarIngreso(new SolicitudDeIngresoAlInventario(edicion, Guid.NewGuid(), 12, null, null));
        inventario.Caido = true;
        var caido = await inventario.RegistrarIngreso(new SolicitudDeIngresoAlInventario(edicion, Guid.NewGuid(), 12, null, null));

        Assert.Equal(TipoDeRespuestaDeIngreso.SinRespuesta, agotado.Tipo);
        Assert.Single(inventario.Ingresos);
        Assert.Equal(TipoDeRespuestaDeIngreso.SinRespuesta, caido.Tipo);
        Assert.Null(await inventario.ConsultarDisponibilidad([edicion]));
        Assert.False(await inventario.RegistrarEdicionImpresa(edicion));
        Assert.Null(await inventario.ListarIngresos(edicion, 50));
    }

    [Fact]
    public async Task El_inventario_doble_separa_las_ediciones_registradas_de_las_no_registradas()
    {
        var inventario = new InventarioDelCatalogoDoble();
        var conStock = Guid.NewGuid();
        var agotada = Guid.NewGuid();
        var desconocida = Guid.NewGuid();
        inventario.ConStock(conStock, 4);
        inventario.ConStock(agotada, 0);

        var respuesta = (await inventario.ConsultarDisponibilidad([conStock, agotada, desconocida]))!;

        Assert.Equal(4, respuesta.Ediciones.Single(e => e.EdicionId == conStock).VendibleEnLaRed);
        Assert.Equal(0, respuesta.Ediciones.Single(e => e.EdicionId == agotada).VendibleEnLaRed);
        Assert.Equal([desconocida], respuesta.NoRegistradas);
    }

    [Fact]
    public void La_memoria_invalida_para_la_cache_pero_conserva_el_dato_como_respaldo()
    {
        var memoria = new MemoriaDeDisponibilidadDoble();
        var edicion = Guid.NewGuid();
        memoria.Guardar(new EntradaDeDisponibilidad(edicion, 5, [], Ahora));

        memoria.Invalidar(edicion);

        var entrada = memoria.Obtener(edicion)!;
        Assert.True(entrada.Invalidada);
        Assert.Equal(5, entrada.VendibleEnLaRed);
        memoria.Guardar(new EntradaDeDisponibilidad(edicion, 35, [], Ahora.AddMinutes(1)));
        Assert.False(memoria.Obtener(edicion)!.Invalidada);
        Assert.Null(memoria.Obtener(Guid.NewGuid()));
    }

    [Fact]
    public void El_emisor_de_claves_emite_un_token_valido_solo_para_su_usuario_y_edicion_y_hasta_los_30_minutos()
    {
        var emisor = new EmisorDeClavesDeIngresoDoble();
        var edicion = Guid.NewGuid();
        var token = emisor.Emitir(Ana, edicion, Ahora);

        Assert.Equal(token.Nonce, emisor.Verificar(token.Token, Ana, edicion, Ahora.AddMinutes(29)));
        Assert.Null(emisor.Verificar(token.Token, Beto, edicion, Ahora.AddMinutes(1)));
        Assert.Null(emisor.Verificar(token.Token, Ana, Guid.NewGuid(), Ahora.AddMinutes(1)));
        Assert.Null(emisor.Verificar(token.Token, Ana, edicion, Ahora.AddMinutes(30)));
        Assert.Null(emisor.Verificar(token.Token + "0", Ana, edicion, Ahora.AddMinutes(1)));
        Assert.Null(emisor.Verificar("manipulado", Ana, edicion, Ahora));
        Assert.Null(emisor.Verificar(null, Ana, edicion, Ahora));
        Assert.Equal(Ahora.AddMinutes(30), token.VenceEn);
    }

    [Fact]
    public async Task El_almacen_doble_rechaza_un_ejecutable_renombrado_y_acepta_un_pdf_con_su_huella_y_tamano_reales()
    {
        var almacen = new AlmacenDeArchivosDoble();
        var ejecutable = await almacen.Escribir(ClaveEnAlmacen.Nueva(), FinalidadDeArchivo.Libro, new MemoryStream([0x4D, 0x5A, 0x90, 0x00, 0x03]));
        var pdf = "%PDF-1.7\nhola\n%%EOF"u8.ToArray();

        var clave = ClaveEnAlmacen.Nueva();
        var correcto = await almacen.Escribir(clave, FinalidadDeArchivo.Libro, new MemoryStream(pdf));

        Assert.Equal(MotivoDeRechazoDeArchivo.TipoNoValido, ejecutable.Rechazo);
        Assert.True(correcto.Escrito);
        Assert.Equal(TipoDeArchivo.Pdf, correcto.Archivo!.Tipo);
        Assert.Equal(pdf.Length, correcto.Archivo.TamanoEnBytes);
        Assert.Equal(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(pdf)), correcto.Archivo.Huella.Hex);
        Assert.Equal(pdf, almacen.Objetos[clave]);
    }

    [Fact]
    public async Task El_almacen_doble_cuenta_los_bytes_leidos_acepta_el_tope_exacto_y_falla_si_no_responde()
    {
        var almacen = new AlmacenDeArchivosDoble();
        var exacto = new byte[(int)PoliticaDeArchivos.TopeDeLibroEnBytes];
        "%PDF-1.7\n"u8.CopyTo(exacto);
        var unoMas = new byte[exacto.Length + 1];
        exacto.CopyTo(unoMas, 0);

        var aceptado = await almacen.Escribir(ClaveEnAlmacen.Nueva(), FinalidadDeArchivo.Libro, new MemoryStream(exacto));
        var rechazado = await almacen.Escribir(ClaveEnAlmacen.Nueva(), FinalidadDeArchivo.Libro, new MemoryStream(unoMas));
        almacen.NoResponde = true;

        Assert.True(aceptado.Escrito);
        Assert.Equal(PoliticaDeArchivos.TopeDeLibroEnBytes, aceptado.Archivo!.TamanoEnBytes);
        Assert.Equal(MotivoDeRechazoDeArchivo.DemasiadoGrande, rechazado.Rechazo);
        await Assert.ThrowsAsync<AlmacenNoDisponibleException>(() => almacen.Escribir(ClaveEnAlmacen.Nueva(), FinalidadDeArchivo.Libro, new MemoryStream(exacto)));
    }

    [Fact]
    public async Task El_almacen_doble_abre_portadas_y_borra()
    {
        var almacen = new AlmacenDeArchivosDoble();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4 };
        var clave = ClaveEnAlmacen.Nueva();
        await almacen.Escribir(clave, FinalidadDeArchivo.Portada, new MemoryStream(png));
        await using var portada = (await almacen.AbrirPortada(clave))!;
        await almacen.Borrar(clave);

        Assert.Equal(png.Length, portada.Length);
        Assert.Null(await almacen.AbrirPortada(clave));
        Assert.Equal([clave], almacen.Borrados);
    }
}
