using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>Lo que dejaron las revisiones de seguridad y de arquitectura de ECO-2b: plazos que degradan, el almacén que no deja huérfanos, depuración revalidada, el Inventario nunca dentro de una transacción y el aviso de registro pendiente solo para la administración.</summary>
public sealed class CorreccionesDeLaRevisionDeEco2bTests
{
    private static readonly TimeSpan Plazo = TimeSpan.FromMilliseconds(80);

    private static HuellaDeArchivo Huella() => HuellaDeArchivo.Desde(SHA256.HashData(Guid.NewGuid().ToByteArray()));

    // ---------- Plazos ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Un_inventario_que_se_cuelga_no_tumba_la_busqueda_usa_el_respaldo_y_si_no_hay_dato_no_se_puede_confirmar()
    {
        var e = new EntornoDelCatalogo();
        var conDato = Sembrar(e, "Con dato");
        var sinDato = Sembrar(e, "Sin dato");
        e.Inventario.ConStock(conDato, 6);
        await e.Consulta.ObtenerObra(ObraDe(e, conDato));
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(1);
        e.Inventario.EsperaElToken = true;

        var resultado = await e.ConsultaConPlazo(Plazo).BuscarObras(new BusquedaDeObras());

        Assert.True(resultado.EsExito);
        var porTitulo = resultado.Valor.Obras.ToDictionary(o => o.Titulo, o => o.Ediciones.Single().Disponibilidad);
        Assert.Equal(FrescuraDeLaDisponibilidad.PuedeEstarDesactualizada, porTitulo["Con dato"].Frescura);
        Assert.Equal(6, porTitulo["Con dato"].VendibleEnLaRed);
        Assert.Equal(EstadoDeDisponibilidad.NoSePuedeConfirmar, porTitulo["Sin dato"].Estado);
        Assert.NotEqual(Guid.Empty, sinDato);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task La_cancelacion_del_llamador_si_se_propaga_y_no_se_confunde_con_el_plazo()
    {
        var e = new EntornoDelCatalogo();
        Sembrar(e, "Obra");
        e.Inventario.EsperaElToken = true;
        using var cancelacion = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => e.ConsultaConPlazo(TimeSpan.FromSeconds(30)).BuscarObras(new BusquedaDeObras(), cancelacion.Token));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_inventario_colgado_en_el_alta_deja_la_obra_creada_y_el_ingreso_fallido_porque_nada_se_envio()
    {
        var e = new EntornoDelCatalogo();
        e.Inventario.EsperaElToken = true;

        var alta = await e.Administracion(Plazo).RegistrarObra(e.Obra("Colgado", e.EdicionImpresa(stock: 12)));

        Assert.True(alta.EsExito);
        Assert.Single(e.Obras.Todas);
        Assert.Equal(EstadoDeIngresoSolicitado.Fallido, alta.Valor.Ingresos.Single().Estado);
        Assert.Empty(e.Inventario.Ingresos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_ingreso_que_se_cuelga_tras_confirmar_la_ficha_deja_el_ingreso_pendiente_sin_revertir_el_alta()
    {
        var e = new EntornoDelCatalogo();
        e.Inventario.EsperaElTokenEnIngresos = true;

        var alta = await e.Administracion(Plazo).RegistrarObra(e.Obra("Ingreso colgado", e.EdicionImpresa(stock: 12)));

        Assert.True(alta.EsExito);
        Assert.Equal(EstadoDeIngresoSolicitado.Pendiente, alta.Valor.Ingresos.Single().Estado);
        Assert.Single(e.Obras.Todas);
        Assert.Equal(EstadoDeIngreso.Pendiente, e.Ingresos.Todos.Single().Estado);
    }

    // ---------- Almacén y depuración ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task Si_el_almacen_no_puede_borrar_un_archivo_rechazado_la_fila_no_se_quita_pasa_a_descarte_y_la_depuracion_lo_borra_despues()
    {
        var e = new EntornoDelCatalogo();
        e.Almacen.BorrarFalla = true;
        var ejecutable = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 3, 0, 0, 0 };

        var rechazado = await e.Archivos().SubirArchivo(new SubirArchivoEntrada(FinalidadDeLaSubida.Libro, new MemoryStream(ejecutable)));

        Assert.Equal(ClavesDelCatalogo.ArchivoInvalido, rechazado.Error!.Clave);
        var fila = e.Referencias.Todas.Single();
        Assert.Equal(EstadoDeReferencia.Descarte, fila.Estado);

        e.Almacen.BorrarFalla = false;
        var depuracion = await e.Depurador().Ejecutar(default);
        Assert.Equal(new DepuracionDeArchivos(1, 1, 0), depuracion.Valor);
        Assert.Empty(e.Referencias.Todas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-12")]
    public async Task La_depuracion_revalida_cada_fila_bajo_bloqueo_y_no_borra_el_objeto_de_una_consumida_que_se_reemplazo_entre_la_lectura_y_el_borrado()
    {
        var e = new EntornoDelCatalogo();
        var id = Guid.NewGuid();
        var clave = ClaveEnAlmacen.Nueva();
        var creada = e.Reloj.Ahora.AddDays(-8);
        await e.Referencias.Agregar(ReferenciaDeArchivo.Rehidratar(id, e.UsuarioId, FinalidadDeArchivo.Libro, clave, EstadoDeReferencia.Consumida, TipoDeArchivo.Epub, 10, Huella(), creada, creada.AddHours(24), creada));
        await e.Almacen.Escribir(clave, FinalidadDeArchivo.Libro, new MemoryStream([0x50, 0x4B, 0x03, 0x04, 1, 2]));
        // Entre la lectura de las candidatas y el borrado, otro caso de uso reemplaza el archivo: la fila pasa a descarte.
        e.Referencias.AlLeerDepurables = () => e.Referencias.EncolarDescarte(clave, FinalidadDeArchivo.Libro, e.UsuarioId, e.Reloj.Ahora).GetAwaiter().GetResult();

        var resultado = await e.Depurador().Ejecutar(default);

        Assert.Equal(new DepuracionDeArchivos(0, 0, 0), resultado.Valor);
        Assert.Contains(clave, e.Almacen.Objetos.Keys);
        Assert.Equal(EstadoDeReferencia.Descarte, e.Referencias.Todas.Single().Estado);

        e.Referencias.AlLeerDepurables = null;
        var siguiente = await e.Depurador().Ejecutar(default);
        Assert.Equal(new DepuracionDeArchivos(1, 1, 0), siguiente.Valor);
        Assert.DoesNotContain(clave, e.Almacen.Objetos.Keys);
    }

    // ---------- El Inventario y el almacén nunca dentro de una transacción ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Llamar_al_inventario_o_al_almacen_dentro_de_una_transaccion_es_un_error_y_los_casos_de_uso_no_lo_hacen()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();

        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Unidad.Ejecutar(async token => await e.Inventario.ConsultarDisponibilidad([edicion], token), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => e.Unidad.Ejecutar(async token => await e.Almacen.Escribir(ClaveEnAlmacen.Nueva(), FinalidadDeArchivo.Libro, new MemoryStream([0x25, 0x50, 0x44, 0x46, 0x2D]), token), default));

        // Los casos de uso que llaman al Inventario (lista, conciliación, reintento, alta) lo hacen fuera de la transacción: no lanzan.
        var servicio = e.IngresosDeMercaderia();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var pedido = await servicio.Registrar(new RegistrarIngresoEntrada(edicion, 5, "GR", null, e.Token(edicion)));
        var lista = await servicio.Listar(new ListarIngresosEntrada(edicion));
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(3);
        var reintento = await servicio.Reintentar(new ReintentarIngresoEntrada(pedido.Valor.IngresoSolicitadoId));
        var alta = await e.Administracion().RegistrarObra(e.Obra("Alta", e.EdicionImpresa("0-306-40615-2", stock: 3)));

        Assert.True(lista.EsExito);
        Assert.True(reintento.EsExito);
        Assert.True(alta.EsExito);
    }

    // ---------- Aviso de registro pendiente (solo administración) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task El_alta_con_el_inventario_caido_avisa_que_la_ficha_de_la_impresa_quedo_sin_confirmar()
    {
        var e = new EntornoDelCatalogo();
        e.Inventario.Caido = true;

        var alta = await e.Administracion().RegistrarObra(e.Obra("Sin ficha", e.EdicionImpresa(), e.EdicionDigital()));

        var impresa = e.Obras.Todas.Single().Ediciones.Single(x => !x.EsDigital);
        Assert.Equal([impresa.Id], alta.Valor.FichasSinConfirmar);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-17")]
    public async Task La_administracion_ve_el_registro_pendiente_de_una_impresa_que_el_inventario_no_conoce_y_el_publico_la_ve_agotada()
    {
        var e = new EntornoDelCatalogo();
        var sinRegistrar = await e.ObraRegistrada("Sin registrar", e.EdicionImpresa(), e.EdicionDigital());
        var registrada = await e.ObraRegistrada("Registrada", e.EdicionImpresa("0-306-40615-2"));
        e.Inventario.ConStock(registrada.Ediciones[0].Id, 4);

        var a = await e.Administracion().ObtenerObra(sinRegistrar.Id);
        var b = await e.Administracion().ObtenerObra(registrada.Id);
        var publica = await e.Consulta.ObtenerObra(sinRegistrar.Id);

        Assert.True(a.Valor.Ediciones.Single(x => x.Formato == FormatoDeEdicion.Impresa).RegistroPendienteEnElInventario);
        Assert.Null(a.Valor.Ediciones.Single(x => x.Formato == FormatoDeEdicion.Digital).RegistroPendienteEnElInventario);
        Assert.False(b.Valor.Ediciones.Single().RegistroPendienteEnElInventario);
        Assert.Equal(EstadoDeDisponibilidad.Agotada, publica.Valor.Ediciones.Single(x => x.Formato == FormatoDeEdicion.Impresa).Disponibilidad.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-17")]
    public async Task Con_el_inventario_caido_la_lectura_de_administracion_no_falla_y_no_afirma_el_registro_pendiente()
    {
        var e = new EntornoDelCatalogo();
        var obra = await e.ObraRegistrada("Caído", e.EdicionImpresa());
        e.Inventario.Caido = true;

        var ficha = await e.Administracion().ObtenerObra(obra.Id);

        Assert.True(ficha.EsExito);
        Assert.Null(ficha.Valor.Ediciones.Single().RegistroPendienteEnElInventario);
    }

    // ---------- Conciliación e Inventario ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task No_se_puede_marcar_fallido_un_ingreso_cuyo_ultimo_intento_es_reciente_porque_aun_puede_aplicarse()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var servicio = e.IngresosDeMercaderia();
        var pedido = await servicio.Registrar(new RegistrarIngresoEntrada(edicion, 5, "GR", null, e.Token(edicion)));
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(1);

        var reciente = await servicio.Conciliar(new ConciliarIngresoEntrada(pedido.Valor.IngresoSolicitadoId, ResolucionDeIngreso.Fallido, true));
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(1);
        var pasado = await servicio.Conciliar(new ConciliarIngresoEntrada(pedido.Valor.IngresoSolicitadoId, ResolucionDeIngreso.Fallido, true));

        Assert.Contains(new ErrorDeCampo("resolucion", "intento-reciente"), reciente.Error!.Errores);
        Assert.Equal(EstadoDeIngresoSolicitado.Fallido, pasado.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-09")]
    public async Task Una_respuesta_del_inventario_con_demasiadas_fuentes_o_textos_largos_cuenta_como_sin_respuesta_y_no_se_guarda()
    {
        var e = new EntornoDelCatalogo();
        var obra = Sembrar(e, "Muchas fuentes");
        for (var i = 0; i < 101; i++)
        {
            e.Inventario.ConStock(obra, 1, $"F{i:D3}", "Fuente");
        }

        var resultado = await e.Consulta.ObtenerObra(ObraDe(e, obra));

        Assert.Equal(EstadoDeDisponibilidad.NoSePuedeConfirmar, resultado.Valor.Ediciones.Single().Disponibilidad.Estado);
        Assert.Null(e.Memoria.Obtener(obra));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task La_auditoria_de_una_edicion_no_lleva_el_isbn_que_el_detalle_seguro_podria_redactar_y_el_objeto_basta()
    {
        var e = new EntornoDelCatalogo();
        var obra = await e.ObraRegistrada("Sin isbn en el detalle", e.EdicionImpresa("9780000000088"), e.EdicionDigital());
        await e.EditarEdicion().Ejecutar(new EditarEdicionEntrada(obra.Id, obra.Ediciones[0].Id, obra.Version, 77m, null, null));

        Assert.DoesNotContain(e.Auditoria.Solicitudes, s => s.Detalle.ContainsKey("isbn"));
        Assert.All(e.Auditoria.Solicitudes.Where(s => s.ObjetoTipo == "edicion"), s => Assert.True(Guid.TryParse(s.ObjetoId, out _)));
    }

    // ---------- Apoyo ----------

    private static Guid Sembrar(EntornoDelCatalogo e, string titulo)
    {
        var isbn = TituloAIsbn(titulo);
        var impresa = Edicion.Impresa(Guid.NewGuid(), isbn, Precio.Crear(10m));
        var obra = Obra.Registrar(Guid.NewGuid(), new DatosDeObra(titulo, "A", "E", "C"), new PortadaDeObra(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Jpeg, Huella()), [impresa], e.Reloj.Ahora).Valor;
        e.Obras.Sembrar(obra);
        return impresa.Id;
    }

    private static Isbn TituloAIsbn(string titulo)
    {
        var n = Math.Abs(titulo.GetHashCode()) % 1_000_000_000;
        var doce = "978" + n.ToString("D9", System.Globalization.CultureInfo.InvariantCulture);
        var suma = doce.Select((c, i) => (c - '0') * (i % 2 == 0 ? 1 : 3)).Sum();
        return Isbn.Crear(doce + ((10 - (suma % 10)) % 10).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Guid ObraDe(EntornoDelCatalogo e, Guid edicionId) => e.Obras.Todas.Single(o => o.BuscarEdicion(edicionId) is not null).Id;
}
