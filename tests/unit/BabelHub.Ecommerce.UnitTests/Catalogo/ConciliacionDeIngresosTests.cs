using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>ConciliarIngresoPendiente (T12; CAT-22, AC-CAT-18): solo la administración central, coincidencia única sobre los últimos 50 ingresos, ventana con margen por abajo y por arriba, «fallido» solo con confirmación y la fila intacta si el Inventario no responde.</summary>
public sealed class ConciliacionDeIngresosTests
{
    private sealed record Escenario(EntornoDelCatalogo Entorno, Guid Edicion, IngresoSolicitado Fila);

    /// <summary>Un ingreso de 30 con la referencia «GR-001» que quedó pendiente (el Inventario no respondió).</summary>
    private static async Task<Escenario> ConPendiente(EntornoDelCatalogo? entorno = null, string? referencia = "GR-001")
    {
        var e = entorno ?? new EntornoDelCatalogo();
        var edicion = e.ImpresaSembrada();
        e.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        await e.IngresosDeMercaderia().Registrar(new RegistrarIngresoEntrada(edicion, 30, referencia, "nota privada", e.Token(edicion)));
        // Pasados unos minutos: un reintento en vuelo reciente impediría marcar fallido (M-2).
        e.Reloj.Ahora = e.Reloj.Ahora.AddMinutes(3);
        return new Escenario(e, edicion, e.Ingresos.Todos.Single());
    }

    private static IngresoDelInventario IngresoRemoto(Escenario s, DateTimeOffset? instante = null, int cantidad = 30, string? referencia = "GR-001", Guid? usuario = null) =>
        new(Guid.NewGuid(), s.Edicion, "RECEPCION", cantidad, referencia, null, usuario ?? s.Fila.UsuarioId, instante ?? s.Fila.SolicitadoEn.AddMinutes(1));

    private static Task<Resultado<ResultadoDeIngreso>> Conciliar(Escenario s, ResolucionDeIngreso resolucion, bool confirma = false) =>
        s.Entorno.IngresosDeMercaderia().Conciliar(new ConciliarIngresoEntrada(s.Fila.Id, resolucion, confirma));

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Una_coincidencia_unica_marca_registrado_guarda_el_ingreso_id_audita_sin_nota_ni_referencia_e_invalida_la_memoria()
    {
        var s = await ConPendiente();
        var remoto = IngresoRemoto(s);
        s.Entorno.Inventario.Ingresos.Add(remoto);
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s, cantidad: 31));
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s, usuario: Guid.NewGuid()));
        s.Entorno.Memoria.Guardar(new EntradaDeDisponibilidad(s.Edicion, 0, [], s.Entorno.Reloj.Ahora));

        var resultado = await Conciliar(s, ResolucionDeIngreso.Registrado);

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, resultado.Valor.Estado);
        var fila = s.Entorno.Ingresos.Todos.Single();
        Assert.Equal(EstadoDeIngreso.Registrado, fila.Estado);
        Assert.Equal(remoto.IngresoId, fila.IngresoId);
        var auditoria = Assert.Single(s.Entorno.Auditoria.Solicitudes);
        Assert.Equal("IngresoPendienteConciliado", auditoria.Accion.Identificador);
        Assert.Equal("ingreso", auditoria.ObjetoTipo);
        Assert.Equal("registrado", auditoria.Detalle["resolucion"]);
        Assert.DoesNotContain(auditoria.Detalle.Keys, k => k is "nota" or "referencia");
        Assert.DoesNotContain(auditoria.Detalle.Values, v => v is not null && (v.Contains("GR-001", StringComparison.Ordinal) || v.Contains("privada", StringComparison.Ordinal)));
        Assert.True(s.Entorno.Memoria.Obtener(s.Edicion)!.Invalidada);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Sin_coincidencia_o_con_varias_no_se_puede_marcar_registrado_y_la_fila_sigue_pendiente()
    {
        var s = await ConPendiente();

        var ninguna = await Conciliar(s, ResolucionDeIngreso.Registrado);
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s));
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s));
        var varias = await Conciliar(s, ResolucionDeIngreso.Registrado);

        Assert.Equal(CategoriaDeError.ReglaDeNegocio, ninguna.Error!.Categoria);
        Assert.Contains(new ErrorDeCampo("resolucion", "sin-coincidencia-unica"), ninguna.Error.Errores);
        Assert.Contains(new ErrorDeCampo("resolucion", "sin-coincidencia-unica"), varias.Error!.Errores);
        Assert.Equal(EstadoDeIngreso.Pendiente, s.Entorno.Ingresos.Todos.Single().Estado);
        Assert.Empty(s.Entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Fallido_sin_confirmacion_se_rechaza_y_con_confirmacion_se_acepta_sin_coincidencia_o_con_varias_y_audita()
    {
        var s = await ConPendiente();
        var t = await ConPendiente();
        t.Entorno.Inventario.Ingresos.Add(IngresoRemoto(t));
        t.Entorno.Inventario.Ingresos.Add(IngresoRemoto(t));

        var sinConfirmar = await Conciliar(s, ResolucionDeIngreso.Fallido);
        var confirmado = await Conciliar(s, ResolucionDeIngreso.Fallido, confirma: true);
        var variasConfirmado = await Conciliar(t, ResolucionDeIngreso.Fallido, confirma: true);

        Assert.Contains(new ErrorDeCampo("confirmaFallido", "obligatorio"), sinConfirmar.Error!.Errores);
        Assert.Equal(EstadoDeIngresoSolicitado.Fallido, confirmado.Valor.Estado);
        Assert.Equal(EstadoDeIngresoSolicitado.Fallido, variasConfirmado.Valor.Estado);
        Assert.Equal("fallido", s.Entorno.Auditoria.Solicitudes.Single().Detalle["resolucion"]);
        Assert.Equal(EstadoDeIngreso.Fallido, s.Entorno.Ingresos.Todos.Single().Estado);
        Assert.Null(s.Entorno.Ingresos.Todos.Single().IngresoId);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Fallido_con_una_coincidencia_unica_se_rechaza_porque_el_ingreso_consta_como_aplicado_aunque_se_confirme()
    {
        var s = await ConPendiente();
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s));

        var resultado = await Conciliar(s, ResolucionDeIngreso.Fallido, confirma: true);

        Assert.Contains(new ErrorDeCampo("resolucion", "consta-como-aplicado"), resultado.Error!.Errores);
        Assert.Equal(EstadoDeIngreso.Pendiente, s.Entorno.Ingresos.Todos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Despues_de_fallido_se_puede_emitir_una_clave_nueva_y_pedir_otro_ingreso()
    {
        var s = await ConPendiente();
        await Conciliar(s, ResolucionDeIngreso.Fallido, confirma: true);

        var nuevo = await s.Entorno.IngresosDeMercaderia().Registrar(new RegistrarIngresoEntrada(s.Edicion, 30, "GR-002", null, s.Entorno.Token(s.Edicion)));

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, nuevo.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Con_ingresos_listar_caido_el_pendiente_queda_intacto_y_se_responde_servicio_no_disponible()
    {
        var s = await ConPendiente();
        s.Entorno.Inventario.ListarIngresosCaido = true;

        var resultado = await Conciliar(s, ResolucionDeIngreso.Fallido, confirma: true);

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.Equal(EstadoDeIngreso.Pendiente, s.Entorno.Ingresos.Todos.Single().Estado);
        Assert.Empty(s.Entorno.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Un_ingreso_ya_constatado_por_otra_fila_no_cuenta_como_coincidencia()
    {
        var s = await ConPendiente();
        var remoto = IngresoRemoto(s);
        s.Entorno.Inventario.Ingresos.Add(remoto);
        await s.Entorno.Ingresos.Agregar(IngresoSolicitado.Rehidratar(Guid.NewGuid(), s.Edicion, Guid.NewGuid(), s.Fila.UsuarioId, EstadoDeIngreso.Registrado, remoto.IngresoId, 30, "GR-001", null, s.Fila.SolicitadoEn, 1, s.Fila.SolicitadoEn, s.Fila.SolicitadoEn));

        var resultado = await Conciliar(s, ResolucionDeIngreso.Registrado);

        Assert.Contains(new ErrorDeCampo("resolucion", "sin-coincidencia-unica"), resultado.Error!.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task La_ventana_acepta_5_minutos_antes_de_solicitado_y_60_despues_del_ultimo_intento_y_rechaza_un_instante_mas_alla()
    {
        var s = await ConPendiente();
        var inicio = s.Fila.SolicitadoEn;
        var ultimo = s.Fila.UltimoIntentoEn;

        Assert.True(ConciliarIngresoPendiente.Coincide(s.Fila, IngresoRemoto(s, inicio.AddMinutes(-5))));
        Assert.False(ConciliarIngresoPendiente.Coincide(s.Fila, IngresoRemoto(s, inicio.AddMinutes(-5).AddTicks(-1))));
        Assert.True(ConciliarIngresoPendiente.Coincide(s.Fila, IngresoRemoto(s, ultimo.AddMinutes(60))));
        Assert.False(ConciliarIngresoPendiente.Coincide(s.Fila, IngresoRemoto(s, ultimo.AddMinutes(60).AddTicks(1))));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Con_dos_intentos_inciertos_el_ingreso_aplicado_en_el_primero_se_encuentra_porque_la_ventana_llega_al_ultimo_intento()
    {
        var s = await ConPendiente();
        var primerEnvio = s.Fila.SolicitadoEn.AddSeconds(2);
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s, primerEnvio));
        s.Entorno.Reloj.Ahora = s.Entorno.Reloj.Ahora.AddHours(2);
        s.Entorno.Inventario.RespuestasDeIngreso.Enqueue(RespuestaDeIngreso.SinRespuesta);
        await s.Entorno.IngresosDeMercaderia().Reintentar(new ReintentarIngresoEntrada(s.Fila.Id));
        var tras = s.Entorno.Ingresos.Todos.Single();
        Assert.Equal(2, tras.Intentos);

        var resultado = await s.Entorno.IngresosDeMercaderia().Conciliar(new ConciliarIngresoEntrada(s.Fila.Id, ResolucionDeIngreso.Registrado, false));

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, resultado.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Una_fila_anonimizada_no_coincide_con_ningun_ingreso_y_solo_se_cierra_como_fallida_con_confirmacion()
    {
        var s = await ConPendiente();
        var fila = RepositorioDeIngresosEnMemoria.Clonar(s.Fila);
        fila.Anonimizar();
        await s.Entorno.Ingresos.GuardarTransicion(fila);
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s, usuario: Guid.Empty));
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s));

        var registrado = await Conciliar(s, ResolucionDeIngreso.Registrado);
        var fallido = await Conciliar(s, ResolucionDeIngreso.Fallido, confirma: true);

        Assert.NotNull(registrado.Error);
        Assert.Equal(EstadoDeIngresoSolicitado.Fallido, fallido.Valor.Estado);
        Assert.Null(s.Entorno.Ingresos.Todos.Single().Referencia);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Sin_referencia_en_la_fila_la_coincidencia_no_la_exige()
    {
        var s = await ConPendiente(referencia: null);
        s.Entorno.Inventario.Ingresos.Add(IngresoRemoto(s, referencia: "CUALQUIERA"));

        var resultado = await Conciliar(s, ResolucionDeIngreso.Registrado);

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, resultado.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Dos_conciliaciones_simultaneas_dejan_una_sola_transicion_y_la_otra_audita_la_discrepancia_sin_reescribir_la_fila()
    {
        var s = await ConPendiente();
        var remoto = IngresoRemoto(s);
        s.Entorno.Inventario.Ingresos.Add(remoto);
        var otraResolucion = false;
        s.Entorno.Inventario.AlListarIngresos = () =>
        {
            if (otraResolucion)
            {
                return;
            }

            otraResolucion = true;
            var fila = RepositorioDeIngresosEnMemoria.Clonar(s.Fila);
            fila.MarcarFallido(s.Entorno.Reloj.Ahora);
            s.Entorno.Ingresos.GuardarTransicion(fila).GetAwaiter().GetResult();
        };

        var resultado = await Conciliar(s, ResolucionDeIngreso.Registrado);

        Assert.Equal(ClavesDelCatalogo.IngresoYaResuelto, resultado.Error!.Clave);
        var auditoria = Assert.Single(s.Entorno.Auditoria.Solicitudes);
        Assert.Equal("discrepancia", auditoria.Detalle["resolucion"]);
        Assert.Equal(EstadoDeIngreso.Fallido, s.Entorno.Ingresos.Todos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Una_fila_ya_resuelta_inexistente_o_de_otra_edicion_se_rechaza_antes_de_llamar_al_inventario()
    {
        var s = await ConPendiente();
        var otraEdicion = s.Entorno.ImpresaSembrada("0-306-40615-2");
        var servicio = s.Entorno.IngresosDeMercaderia();
        await Conciliar(s, ResolucionDeIngreso.Fallido, confirma: true);
        var llamadas = s.Entorno.Inventario.LlamadasAListarIngresos;

        var yaResuelta = await Conciliar(s, ResolucionDeIngreso.Fallido, confirma: true);
        var inexistente = await servicio.Conciliar(new ConciliarIngresoEntrada(Guid.NewGuid(), ResolucionDeIngreso.Registrado, false));
        var deOtra = await servicio.Conciliar(new ConciliarIngresoEntrada(s.Fila.Id, ResolucionDeIngreso.Registrado, false, otraEdicion));

        Assert.Equal(ClavesDelCatalogo.IngresoYaResuelto, yaResuelta.Error!.Clave);
        Assert.Equal(ClavesDelCatalogo.IngresoNoEncontrado, inexistente.Error!.Clave);
        Assert.Equal(ClavesDelCatalogo.IngresoNoEncontrado, deOtra.Error!.Clave);
        Assert.Equal(llamadas, s.Entorno.Inventario.LlamadasAListarIngresos);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-19")]
    [InlineData("encargado")]
    [InlineData("cliente")]
    [InlineData("anonimo")]
    public async Task Quien_no_es_la_administracion_central_no_concilia_y_queda_el_acceso_denegado_sin_llamar_al_inventario(string quien)
    {
        var admin = await ConPendiente();
        var otro = new EntornoDelCatalogo(quien switch
        {
            "encargado" => ActorDePrueba.DeEncargadoDe(Guid.NewGuid()),
            "cliente" => ActorDePrueba.DeCliente(),
            _ => ActorDePrueba.Anonimo(),
        });
        await otro.Ingresos.Agregar(RepositorioDeIngresosEnMemoria.Clonar(admin.Fila));

        var resultado = await otro.IngresosDeMercaderia().Conciliar(new ConciliarIngresoEntrada(admin.Fila.Id, ResolucionDeIngreso.Fallido, true));

        Assert.NotNull(resultado.Error);
        Assert.Single(otro.Denegaciones.Eventos);
        Assert.Equal(0, otro.Inventario.LlamadasAListarIngresos);
        Assert.Equal(EstadoDeIngreso.Pendiente, otro.Ingresos.Todos.Single().Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-18")]
    public async Task Otro_administrador_central_si_puede_conciliar_el_pendiente_de_un_autor_que_no_esta_disponible()
    {
        var s = await ConPendiente();
        var remoto = IngresoRemoto(s);
        var otro = new EntornoDelCatalogo(ActorDePrueba.DeAdministradorCentral());
        otro.Inventario.Ingresos.Add(remoto);
        await otro.Ingresos.Agregar(RepositorioDeIngresosEnMemoria.Clonar(s.Fila));

        var resultado = await otro.IngresosDeMercaderia().Conciliar(new ConciliarIngresoEntrada(s.Fila.Id, ResolucionDeIngreso.Registrado, false));

        Assert.Equal(EstadoDeIngresoSolicitado.Registrado, resultado.Valor.Estado);
        Assert.Empty(otro.Denegaciones.Eventos);
    }
}
