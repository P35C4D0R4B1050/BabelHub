using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>El alta con stock inicial y los ingresos de mercadería (T12; CAT-20, CAT-22, CAT-23, AC-CAT-16, AC-CAT-18, AC-CAT-19): fila pendiente antes de llamar, clave del token, reintento con la misma clave y carga, y el Inventario caído sin revertir nada.</summary>
public sealed class IngresosDeMercaderiaTests
{
    private static RegistrarIngresoEntrada Ingreso(EntornoDelCatalogo e, Guid edicion, int cantidad = 30, string? referencia = "GR-001", string? token = null, Guid? usuario = null) =>
        new(edicion, cantidad, referencia, null, token ?? e.Token(edicion, usuario));

    // ---------- Alta con stock inicial (AC-CAT-16) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Un_alta_con_stock_inicial_de_12_crea_la_edicion_registra_la_ficha_y_el_ingreso_en_la_fuente_de_recepcion_y_lo_marca_registrado()
    {
        var e = new EntornoDelCatalogo();

        var alta = await e.Administracion().RegistrarObra(e.Obra("Con stock", e.EdicionImpresa(stock: 12)));

        Assert.True(alta.EsExito);
        var ingreso = Assert.Single(alta.Valor.Ingresos);
        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, ingreso.Estado);
        Assert.Equal(12, ingreso.Cantidad);
        Assert.Equal([alta.Valor.EdicionIds[0]], e.Inventario.FichasRegistradas);
        var enviado = Assert.Single(e.Inventario.SolicitudesDeIngreso);
        Assert.Equal(12, enviado.Cantidad);
        Assert.Equal(alta.Valor.EdicionIds[0], enviado.EdicionId);
        Assert.Equal(12, e.Inventario.Ediciones[enviado.EdicionId].Sum(f => f.Vendible));
        var fila = e.Ingresos.Todos.Single();
        Assert.Equal(EstadoDeIngreso.Registrado, fila.Estado);
        Assert.NotNull(fila.IngresoId);
        Assert.Equal(1, fila.Intentos);
        Assert.Equal(fila.ClaveDeIdempotencia, enviado.ClaveDeIdempotencia);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Si_el_inventario_no_responde_la_edicion_se_crea_sin_stock_y_el_ingreso_queda_fallido_porque_nada_se_envio()
    {
        var e = new EntornoDelCatalogo();
        e.Inventario.Caido = true;

        var alta = await e.Administracion().RegistrarObra(e.Obra("Sin inventario", e.EdicionImpresa(stock: 12)));

        Assert.True(alta.EsExito);
        Assert.Single(e.Obras.Todas);
        Assert.Equal(EstadoDeIngresoSolicitado.Fallido, alta.Valor.Ingresos.Single().Estado);
        Assert.Empty(e.Inventario.Ingresos);
        Assert.Equal(EstadoDeIngreso.Fallido, e.Ingresos.Todos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Con_tiempo_agotado_tras_aplicarse_el_ingreso_el_alta_no_se_revierte_y_el_reintento_no_duplica_el_lote()
    {
        var e = new EntornoDelCatalogo();
        e.Inventario.AplicaYNoResponde = true;
        var alta = await e.Administracion().RegistrarObra(e.Obra("Respuesta perdida", e.EdicionImpresa(stock: 12)));
        var ingresoId = alta.Valor.Ingresos.Single().IngresoSolicitadoId;

        Assert.True(alta.EsExito);
        Assert.Equal(EstadoDeIngresoSolicitado.Pendiente, alta.Valor.Ingresos.Single().Estado);
        Assert.Single(e.Obras.Todas);
        Assert.Single(e.Inventario.Ingresos);

        e.Inventario.AplicaYNoResponde = false;
        var reintento = await e.IngresosDeMercaderia().Reintentar(new ReintentarIngresoEntrada(ingresoId));

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, reintento.Valor.Estado);
        Assert.Single(e.Inventario.Ingresos);
        Assert.Equal(12, e.Inventario.Ediciones[alta.Valor.EdicionIds[0]].Sum(f => f.Vendible));
        Assert.Equal(2, e.Ingresos.Todos.Single().Intentos);
        Assert.Equal(2, e.Inventario.SolicitudesDeIngreso.Count);
        Assert.Equal(e.Inventario.SolicitudesDeIngreso[0].ClaveDeIdempotencia, e.Inventario.SolicitudesDeIngreso[1].ClaveDeIdempotencia);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Una_impresa_sin_stock_inicial_igual_registra_su_ficha_y_una_digital_nunca()
    {
        var e = new EntornoDelCatalogo();

        var alta = await e.Administracion().RegistrarObra(e.Obra("Ambas", e.EdicionImpresa(), e.EdicionDigital()));

        Assert.Empty(alta.Valor.Ingresos);
        var impresa = e.Obras.Todas.Single().Ediciones.Single(x => !x.EsDigital);
        Assert.Equal([impresa.Id], e.Inventario.FichasRegistradas);
        Assert.Empty(e.Inventario.SolicitudesDeIngreso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-16")]
    public async Task Agregar_una_edicion_impresa_con_stock_a_una_obra_existente_tambien_ingresa_el_lote()
    {
        var e = new EntornoDelCatalogo();
        var obra = await e.ObraRegistrada("Solo digital", e.EdicionDigital());

        var alta = await e.Administracion().RegistrarEdicion(new RegistrarEdicionEntrada(obra.Id, obra.Version, e.EdicionImpresa(stock: 40)));

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, alta.Valor.Ingresos.Single().Estado);
        Assert.Equal(40, e.Inventario.SolicitudesDeIngreso.Single().Cantidad);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task Un_alta_rechazada_no_toca_el_inventario()
    {
        var e = new EntornoDelCatalogo();

        var alta = await e.Administracion().RegistrarObra(e.Obra("Mal", e.EdicionImpresa(stock: 6000)));

        Assert.False(alta.EsExito);
        Assert.Empty(e.Inventario.FichasRegistradas);
        Assert.Empty(e.Inventario.SolicitudesDeIngreso);
    }

    // ---------- Ingreso suelto (AC-CAT-18) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_ingreso_de_30_con_la_referencia_GR_001_suma_30_se_marca_registrado_y_figura_en_la_lista_con_fecha_y_usuario()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.ConStock(edicion, 0);
        e.Inventario.UsuarioDelIngreso = e.UsuarioId;
        var servicio = e.IngresosDeMercaderia();

        var resultado = await servicio.Registrar(Ingreso(e, edicion));
        var lista = await servicio.Listar(new ListarIngresosEntrada(edicion));

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, resultado.Valor.Estado);
        Assert.Equal(30, e.Inventario.Ediciones[edicion].Sum(f => f.Vendible));
        var fila = e.Ingresos.Todos.Single();
        Assert.Equal(EstadoDeIngreso.Registrado, fila.Estado);
        Assert.Equal("GR-001", fila.Referencia);
        Assert.Equal([edicion], e.Inventario.FichasRegistradas);
        var ingreso = Assert.Single(lista.Valor.Ingresos);
        Assert.Equal(30, ingreso.Cantidad);
        Assert.Equal("GR-001", ingreso.Referencia);
        Assert.Equal(e.UsuarioId, ingreso.UsuarioId);
        Assert.Equal(fila.IngresoId, ingreso.IngresoId);
        Assert.False(lista.Valor.InventarioNoRespondio);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task El_ingreso_invalida_la_disponibilidad_guardada_de_la_edicion_y_deja_de_mostrarla_agotada()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.ConStock(edicion, 0);
        var antes = await e.Consulta.Consultar([edicion]);

        await e.IngresosDeMercaderia().Registrar(Ingreso(e, edicion));
        var despues = await e.Consulta.Consultar([edicion]);

        Assert.Equal(EstadoDeDisponibilidad.Agotada, antes.Valor.Single().Estado);
        Assert.Equal(EstadoDeDisponibilidad.Disponible, despues.Valor.Single().Estado);
        Assert.Equal(30, despues.Valor.Single().VendibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Repetir_la_misma_clave_con_la_misma_carga_devuelve_el_mismo_resultado_sin_enviar_otra_vez()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        var token = e.Token(edicion);
        var servicio = e.IngresosDeMercaderia();

        var primero = await servicio.Registrar(Ingreso(e, edicion, token: token));
        var repetido = await servicio.Registrar(Ingreso(e, edicion, token: token));

        Assert.Equal(primero.Valor.IngresoSolicitadoId, repetido.Valor.IngresoSolicitadoId);
        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, repetido.Valor.Estado);
        Assert.Single(e.Inventario.SolicitudesDeIngreso);
        Assert.Single(e.Ingresos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task La_misma_clave_con_otra_carga_o_la_de_un_fallido_responde_clave_invalida()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        var token = e.Token(edicion);
        var servicio = e.IngresosDeMercaderia();
        await servicio.Registrar(Ingreso(e, edicion, 30, token: token));

        var otraCantidad = await servicio.Registrar(Ingreso(e, edicion, 31, token: token));
        var otraReferencia = await servicio.Registrar(Ingreso(e, edicion, 30, "OTRA", token));

        Assert.Equal(ClavesDelCatalogo.ClaveDeIngresoInvalida, otraCantidad.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, otraCantidad.Error.Categoria);
        Assert.Equal(ClavesDelCatalogo.ClaveDeIngresoInvalida, otraReferencia.Error!.Clave);
        Assert.Single(e.Inventario.SolicitudesDeIngreso);

        // Un fallido: el token ya usado no sirve y el formulario ofrece uno nuevo.
        var f = new EntornoDelCatalogo();
        var otra = f.ImpresaSembrada();
        f.Inventario.NoConfirmaLaFicha = true;
        var tokenFallido = f.Token(otra);
        var fallido = await f.IngresosDeMercaderia().Registrar(Ingreso(f, otra, token: tokenFallido));
        Assert.Equal(EstadoDeIngresoSolicitado.Fallido, fallido.Valor.Estado);
        var reenviado = await f.IngresosDeMercaderia().Registrar(Ingreso(f, otra, token: tokenFallido));
        Assert.Equal(ClavesDelCatalogo.ClaveDeIngresoInvalida, reenviado.Error!.Clave);

        f.Inventario.NoConfirmaLaFicha = false;
        var nuevo = await f.IngresosDeMercaderia().Registrar(Ingreso(f, otra));
        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, nuevo.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_token_manipulado_de_otro_usuario_de_otra_edicion_o_vencido_se_rechaza_sin_crear_nada()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        var otraEdicion = e.ImpresaSembrada("0-306-40615-2");
        var servicio = e.IngresosDeMercaderia();
        var valido = e.Token(edicion);
        var vencido = e.Token(edicion);
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(30);

        var casos = new[]
        {
            Ingreso(e, edicion, token: valido + "x"),
            Ingreso(e, edicion, token: e.Token(edicion, Guid.NewGuid())),
            Ingreso(e, edicion, token: e.Token(otraEdicion)),
            Ingreso(e, edicion, token: vencido),
        };
        var errores = new List<string>();
        foreach (var caso in casos)
        {
            errores.Add((await servicio.Registrar(caso)).Error!.Clave);
        }

        Assert.All(errores, c => Assert.Equal(ClavesDelCatalogo.ClaveDeIngresoInvalida, c));
        Assert.Empty(e.Ingresos.Todos);
        Assert.Empty(e.Inventario.FichasRegistradas);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-19")]
    [InlineData(6000)]
    [InlineData(-3)]
    [InlineData(0)]
    public async Task Una_cantidad_fuera_de_1_a_5000_se_rechaza_sin_crear_ni_enviar_nada(int cantidad)
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();

        var resultado = await e.IngresosDeMercaderia().Registrar(Ingreso(e, edicion, cantidad));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(e.Ingresos.Todos);
        Assert.Empty(e.Inventario.SolicitudesDeIngreso);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-19")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_la_administracion_central_no_registra_un_ingreso_y_queda_el_acceso_denegado(string quien)
    {
        var admin = new EntornoDelCatalogo();
        var edicion = admin.ImpresaSembrada();
        var e = new EntornoDelCatalogo(quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.Anonimo(),
        });
        e.Obras.Sembrar((await admin.Obras.ObtenerPorEdicion(edicion, false))!);

        var ingreso = await e.IngresosDeMercaderia().Registrar(Ingreso(e, edicion, token: "cualquiera"));
        var clave = await e.IngresosDeMercaderia().EmitirClave(new EmitirClaveDeIngresoEntrada(edicion));
        var lista = await e.IngresosDeMercaderia().Listar(new ListarIngresosEntrada(edicion));

        Assert.NotNull(ingreso.Error);
        Assert.NotNull(clave.Error);
        Assert.NotNull(lista.Error);
        Assert.Equal(3, e.Denegaciones.Eventos.Count);
        Assert.Empty(e.Ingresos.Todos);
        Assert.Empty(e.Inventario.SolicitudesDeIngreso);
        Assert.Equal(0, e.Emisor.Emisiones);
    }

    [Fact]
    public async Task Una_edicion_digital_dada_de_baja_o_inexistente_no_recibe_ingresos_ni_clave()
    {
        var e = new EntornoDelCatalogo();
        var obra = await e.ObraRegistrada("Mixta", e.EdicionImpresa(), e.EdicionDigital());
        var digital = obra.Ediciones.Single(x => x.EsDigital).Id;
        var impresa = obra.Ediciones.Single(x => !x.EsDigital).Id;
        var copia = RepositorioDeObrasEnMemoria.Clonar(obra);
        copia.DarDeBajaEdicion(impresa, e.Reloj.Ahora);
        e.Obras.Sembrar(copia);
        var servicio = e.IngresosDeMercaderia();

        var deDigital = await servicio.Registrar(Ingreso(e, digital, token: e.Token(digital)));
        var deBaja = await servicio.Registrar(Ingreso(e, impresa, token: e.Token(impresa)));
        var inexistente = await servicio.Registrar(Ingreso(e, Guid.NewGuid(), token: "x"));
        var claveDeDigital = await servicio.EmitirClave(new EmitirClaveDeIngresoEntrada(digital));

        Assert.Contains(new ErrorDeCampo("edicionId", "solo-edicion-impresa-activa"), deDigital.Error!.Errores);
        Assert.Contains(new ErrorDeCampo("edicionId", "solo-edicion-impresa-activa"), deBaja.Error!.Errores);
        Assert.Equal(ClavesDelCatalogo.EdicionNoEncontrada, inexistente.Error!.Clave);
        Assert.NotNull(claveDeDigital.Error);
        Assert.Empty(e.Inventario.SolicitudesDeIngreso);
    }

    // ---------- Pendiente y reintento ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_tiempo_agotado_deja_el_ingreso_pendiente_y_otro_administrador_no_puede_iniciar_uno_para_esa_edicion()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var servicio = e.IngresosDeMercaderia();

        var primero = await servicio.Registrar(Ingreso(e, edicion));
        var otroAdmin = new EntornoDelCatalogo(ActorDePrueba.DeAdministradorCentral());
        otroAdmin.Obras.Sembrar((await e.Obras.ObtenerPorEdicion(edicion, false))!);
        foreach (var fila in e.Ingresos.Todos)
        {
            await otroAdmin.Ingresos.Agregar(RepositorioDeIngresosEnMemoria.Clonar(fila));
        }

        var segundo = await otroAdmin.IngresosDeMercaderia().Registrar(Ingreso(otroAdmin, edicion));

        Assert.Equal(EstadoDeIngresoSolicitado.Pendiente, primero.Valor.Estado);
        Assert.Equal(ClavesDelCatalogo.IngresoPendiente, segundo.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, segundo.Error.Categoria);
        Assert.Empty(otroAdmin.Inventario.SolicitudesDeIngreso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task El_mismo_usuario_con_una_clave_nueva_tampoco_inicia_otro_mientras_haya_un_pendiente()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var servicio = e.IngresosDeMercaderia();
        await servicio.Registrar(Ingreso(e, edicion));

        var otro = await servicio.Registrar(Ingreso(e, edicion, 5, "OTRO"));

        Assert.Equal(ClavesDelCatalogo.IngresoPendiente, otro.Error!.Clave);
        Assert.Single(e.Ingresos.Todos);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-18")]
    [InlineData(401, "INV-NO-AUTENTICADO", true)]
    [InlineData(403, "INV-PROHIBIDO", true)]
    [InlineData(404, "INV-NO-ENCONTRADO", true)]
    [InlineData(422, "INV-EDICION-NO-IMPRESA", true)]
    [InlineData(422, "INV-FUENTE-NO-ES-DE-RECEPCION", true)]
    [InlineData(400, "INV-VALIDACION-INVALIDA", true)]
    [InlineData(409, "INV-IDEMPOTENCIA-EN-CONFLICTO", false)]
    [InlineData(409, "INV-IDEMPOTENCIA-EN-CURSO", false)]
    [InlineData(503, "INV-SERVICIO-NO-DISPONIBLE", false)]
    [InlineData(401, null, false)]
    [InlineData(403, "INV-NO-ENCONTRADO", false)]
    [InlineData(200, "INV-PROHIBIDO", false)]
    public async Task Solo_una_respuesta_de_la_lista_cerrada_con_su_estado_y_un_problema_bien_formado_marca_fallido_en_el_primer_envio(int estado, string? codigo, bool fallido)
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.Rechazado(estado, codigo));

        var resultado = await e.IngresosDeMercaderia().Registrar(Ingreso(e, edicion));

        Assert.Equal(fallido ? EstadoDeIngresoSolicitado.Fallido : EstadoDeIngresoSolicitado.Pendiente, resultado.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_201_mal_formado_o_un_corte_dejan_el_ingreso_pendiente()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(new RespuestaDeIngreso(TipoDeRespuestaDeIngreso.Creado, Guid.Empty));

        var resultado = await e.IngresosDeMercaderia().Registrar(Ingreso(e, edicion));

        Assert.Equal(EstadoDeIngresoSolicitado.Pendiente, resultado.Valor.Estado);
        Assert.Null(e.Ingresos.Todos.Single().IngresoId);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task En_un_reintento_todo_resultado_distinto_de_201_deja_pendiente_aunque_sea_un_rechazo_de_la_lista_cerrada()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var servicio = e.IngresosDeMercaderia();
        var primero = await servicio.Registrar(Ingreso(e, edicion));
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.Rechazado(401, "INV-NO-AUTENTICADO"));

        var reintento = await servicio.Reintentar(new ReintentarIngresoEntrada(primero.Valor.IngresoSolicitadoId));

        Assert.True(reintento.EsExito);
        Assert.Equal(EstadoDeIngresoSolicitado.Pendiente, reintento.Valor.Estado);
        Assert.Equal(EstadoDeIngreso.Pendiente, e.Ingresos.Todos.Single().Estado);
        Assert.Equal(2, e.Ingresos.Todos.Single().Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Si_la_ficha_falla_en_un_reintento_la_fila_sigue_pendiente_y_no_se_envia_nada()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var servicio = e.IngresosDeMercaderia();
        var primero = await servicio.Registrar(Ingreso(e, edicion));
        e.Inventario.NoConfirmaLaFicha = true;

        var reintento = await servicio.Reintentar(new ReintentarIngresoEntrada(primero.Valor.IngresoSolicitadoId));

        Assert.Equal(EstadoDeIngresoSolicitado.Pendiente, reintento.Valor.Estado);
        Assert.Single(e.Inventario.SolicitudesDeIngreso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-19")]
    public async Task Solo_el_usuario_de_la_fila_reintenta_otro_administrador_recibe_prohibido_con_el_acceso_denegado_y_el_rol_se_comprueba_antes_de_buscar_la_fila()
    {
        var autor = new EntornoDelCatalogo();
        var edicion = autor.ImpresaSembrada();
        autor.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var primero = await autor.IngresosDeMercaderia().Registrar(Ingreso(autor, edicion));
        var otro = new EntornoDelCatalogo(ActorDePrueba.DeAdministradorCentral());
        otro.Obras.Sembrar((await autor.Obras.ObtenerPorEdicion(edicion, false))!);
        await otro.Ingresos.Agregar(RepositorioDeIngresosEnMemoria.Clonar(autor.Ingresos.Todos.Single()));
        var cliente = new EntornoDelCatalogo(ActorDePrueba.DeCliente());
        await cliente.Ingresos.Agregar(RepositorioDeIngresosEnMemoria.Clonar(autor.Ingresos.Todos.Single()));

        var ajeno = await otro.IngresosDeMercaderia().Reintentar(new ReintentarIngresoEntrada(primero.Valor.IngresoSolicitadoId));
        var deCliente = await cliente.IngresosDeMercaderia().Reintentar(new ReintentarIngresoEntrada(Guid.NewGuid()));

        Assert.Equal(CategoriaDeError.Prohibido, ajeno.Error!.Categoria);
        Assert.Equal("recurso-ajeno", Assert.Single(otro.Denegaciones.Eventos).Motivo);
        Assert.Empty(otro.Inventario.SolicitudesDeIngreso);
        Assert.Equal(CategoriaDeError.Prohibido, deCliente.Error!.Categoria);
        Assert.Equal("fuera-de-rol", Assert.Single(cliente.Denegaciones.Eventos).Motivo);
    }

    [Fact]
    public async Task Reintentar_una_fila_de_otra_edicion_o_inexistente_responde_no_encontrado_y_una_ya_resuelta_conflicto()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        var otraEdicion = e.ImpresaSembrada("0-306-40615-2");
        var servicio = e.IngresosDeMercaderia();
        var resuelto = await servicio.Registrar(Ingreso(e, edicion));

        var deOtra = await servicio.Reintentar(new ReintentarIngresoEntrada(resuelto.Valor.IngresoSolicitadoId, otraEdicion));
        var inexistente = await servicio.Reintentar(new ReintentarIngresoEntrada(Guid.NewGuid()));
        var yaResuelto = await servicio.Reintentar(new ReintentarIngresoEntrada(resuelto.Valor.IngresoSolicitadoId, edicion));

        Assert.Equal(ClavesDelCatalogo.IngresoNoEncontrado, deOtra.Error!.Clave);
        Assert.Equal(CategoriaDeError.NoEncontrado, inexistente.Error!.Categoria);
        Assert.Equal(ClavesDelCatalogo.IngresoYaResuelto, yaResuelto.Error!.Clave);
        Assert.Equal(CategoriaDeError.Conflicto, yaResuelto.Error.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_pendiente_de_mas_de_24_horas_ya_no_se_reintenta_se_concilia()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var servicio = e.IngresosDeMercaderia();
        var primero = await servicio.Registrar(Ingreso(e, edicion));

        e.Reloj.Ahora = e.Reloj.Ahora.AddHours(24);
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var enElBorde = await servicio.Reintentar(new ReintentarIngresoEntrada(primero.Valor.IngresoSolicitadoId));
        e.Reloj.Ahora = e.Reloj.Ahora.AddSeconds(1);
        var vencido = await servicio.Reintentar(new ReintentarIngresoEntrada(primero.Valor.IngresoSolicitadoId));

        Assert.True(enElBorde.EsExito);
        Assert.Equal(CategoriaDeError.ReglaDeNegocio, vencido.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("ingresoId", "pendiente-vencido"), vencido.Error.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Dos_reintentos_seguidos_no_duplican_el_lote_porque_comparten_la_clave()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.AplicaYNoResponde = true;
        var servicio = e.IngresosDeMercaderia();
        var primero = await servicio.Registrar(Ingreso(e, edicion));
        e.Inventario.AplicaYNoResponde = false;

        var a = await servicio.Reintentar(new ReintentarIngresoEntrada(primero.Valor.IngresoSolicitadoId));
        var b = await servicio.Reintentar(new ReintentarIngresoEntrada(primero.Valor.IngresoSolicitadoId));

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, a.Valor.Estado);
        Assert.Equal(ClavesDelCatalogo.IngresoYaResuelto, b.Error!.Clave);
        Assert.Single(e.Inventario.Ingresos);
        Assert.Equal(30, e.Inventario.Ediciones[edicion].Sum(f => f.Vendible));
    }

    // ---------- Lista (CAT-22) ----------

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Con_el_inventario_caido_la_lista_trae_lo_local_con_el_aviso_pendiente_y_lo_avisa()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        var servicio = e.IngresosDeMercaderia();
        var primero = await servicio.Registrar(Ingreso(e, edicion));
        e.Inventario.ListarIngresosCaido = true;

        var lista = await servicio.Listar(new ListarIngresosEntrada(edicion));

        Assert.True(lista.EsExito);
        Assert.True(lista.Valor.InventarioNoRespondio);
        var pendiente = Assert.Single(lista.Valor.Ingresos);
        Assert.Equal(EstadoDeIngresoSolicitado.Pendiente, pendiente.Estado);
        Assert.Equal(primero.Valor.IngresoSolicitadoId, pendiente.IngresoSolicitadoId);
        Assert.Equal(e.UsuarioId, pendiente.UsuarioId);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task La_lista_ignora_un_inventario_que_devuelve_ingresos_de_otra_edicion()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        var otra = e.ImpresaSembrada("0-306-40615-2");
        await e.IngresosDeMercaderia().Registrar(Ingreso(e, otra));
        e.Inventario.Ingresos.Add(new IngresoDelInventario(Guid.NewGuid(), otra, "RECEPCION", 9, null, null, null, e.Reloj.Ahora));

        var lista = await e.IngresosDeMercaderia().Listar(new ListarIngresosEntrada(edicion));

        Assert.Empty(lista.Valor.Ingresos);
        Assert.False(lista.Valor.InventarioNoRespondio);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Emitir_la_clave_no_escribe_nada_y_un_inventario_caido_nunca_la_impide()
    {
        var e = new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.Caido = true;

        var clave = await e.IngresosDeMercaderia().EmitirClave(new EmitirClaveDeIngresoEntrada(edicion));

        Assert.True(clave.EsExito);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(30), clave.Valor.VenceEn);
        Assert.Empty(e.Ingresos.Todos);
        Assert.Empty(e.Auditoria.Solicitudes);
        Assert.Empty(e.Inventario.FichasRegistradas);
    }
}
