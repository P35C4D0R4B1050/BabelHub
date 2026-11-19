using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using AppOrigen = BabelHub.Ecommerce.Application.Pagos.OrigenDelReembolso;
using DomEstado = BabelHub.Ecommerce.Domain.Pagos.EstadoDeReembolso;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>Una compra mixta ya completada (cargo aprobado de S/ 140: 2 impresos de S/ 50, 1 digital de S/ 25 y envío S/ 15) con los casos de uso de reembolso cableados.</summary>
internal sealed class EntornoDeReembolsos
{
    private EntornoDeReembolsos(EntornoDeConfirmacion b)
    {
        Base = b;
        var p = b.Entorno;
        var bloqueo = new BloqueoDeIntenciones(p.Repositorio, p.Cuenta);
        Registrar = new RegistrarReembolso(p.Repositorio, p.Reembolsos, p.Unidad, p.Auditoria, p.Reloj);
        Procesar = new ProcesarReembolso(p.Repositorio, p.Reembolsos, p.Unidad, p.Pasarela, p.Auditoria, p.Alertas, p.Reloj, new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
        Reintentar = new ReintentarReembolso(p.Autorizador, p.Reembolsos, p.Unidad, Procesar, p.Reloj);
        Listar = new ListarReembolsosPendientes(p.Autorizador, p.Reembolsos, p.Unidad);
        Fachada = new ReembolsosDePagos(Registrar, Procesar, Reintentar, Listar, p.Reembolsos, p.Unidad);
    }

    public EntornoDeConfirmacion Base { get; }

    public EntornoDePagos Pagos => Base.Entorno;

    public RegistrarReembolso Registrar { get; }

    public ProcesarReembolso Procesar { get; }

    public ReintentarReembolso Reintentar { get; }

    public ListarReembolsosPendientes Listar { get; }

    public ReembolsosDePagos Fachada { get; }

    public static async Task<EntornoDeReembolsos> ConCompraCompletada()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var intencion = b.Intencion;
        var ahora = b.Entorno.Reloj.Ahora;
        intencion.AplicarAprobacion(b.Intento, "ch_1", ahora);
        intencion.MarcarConfirmacionIniciada(ahora);
        intencion.MarcarReservasConfirmadas(ahora, [new ReservaConfirmadaDeLinea(IntencionesDePrueba.LineaImpresa, "AYA-01", false, null, null)]);
        intencion.Completar(ahora);
        await b.Entorno.Repositorio.GuardarIntencion(intencion);
        await b.Entorno.Repositorio.GuardarIntento(b.Intento);
        return new EntornoDeReembolsos(b);
    }

    public static string ClaveDeAnulacion => ClaveDeReembolso.Anulacion(InstantaneasDeCompraDePrueba.Pedido);

    public static string ClaveDeDevolucion(Guid? devolucion = null, Guid? linea = null) => ClaveDeReembolso.Devolucion(devolucion ?? Guid.NewGuid(), linea ?? IntencionesDePrueba.LineaImpresa);

    public Task<Resultado<ReembolsoRegistrado>> Pedir(AppOrigen origen, string clave, decimal montoDeLaLinea = 100m, decimal envio = 0m) =>
        Registrar.Ejecutar(new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, origen, clave, montoDeLaLinea > 0m ? [new LineaAReembolsar(IntencionesDePrueba.LineaImpresa, montoDeLaLinea)] : [], envio));

    public async Task<Guid> UnReembolsoPendiente()
    {
        var registrado = await Pedir(AppOrigen.Anulacion, ClaveDeAnulacion);
        return registrado.Valor.ReembolsoId;
    }

    public Reembolso ReembolsoPorId(Guid id) => Pagos.Reembolsos.Todos.Single(r => r.Id == id);

    public void ComoAdministrador() => Pagos.Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central"));
}

public sealed class ReembolsosDePagosTests
{
    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Registrar_el_reembolso_de_una_linea_lo_deja_pendiente_y_lo_audita_sin_datos_de_pago()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();

        var resultado = await e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion);

        Assert.True(resultado.EsExito);
        Assert.Equal(100m, resultado.Valor.Monto);
        Assert.Equal(EstadoDelReembolso.Pendiente, resultado.Valor.Estado);
        Assert.False(resultado.Valor.EraRepetido);
        var auditoria = Assert.Single(e.Pagos.Auditoria.Solicitudes);
        Assert.Equal("ReembolsoRegistrado", auditoria.Accion.Identificador);
        Assert.Equal("pedido", auditoria.ObjetoTipo);
        Assert.Equal("100.00", auditoria.Detalle["monto"]);
        Assert.Equal("PEN", auditoria.Detalle["moneda"]);
        Assert.Equal("manual", auditoria.Detalle["modalidad"]);
        Assert.DoesNotContain("ch_1", string.Join(' ', auditoria.Detalle.Values));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Registrar_solo_bloquea_la_intencion_sin_tomar_la_marca_porque_quien_llama_ya_tiene_su_orden()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        e.Pagos.Orden.Clear();

        await e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion);

        Assert.Equal(["intencion-con-bloqueo"], e.Pagos.Orden.ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Registrar_otra_vez_el_mismo_origen_y_clave_con_el_mismo_monto_devuelve_el_original_sin_otra_fila_ni_auditoria()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var primero = await e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion);

        var segundo = await e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion);

        Assert.Equal(primero.Valor.ReembolsoId, segundo.Valor.ReembolsoId);
        Assert.True(segundo.Valor.EraRepetido);
        Assert.Single(e.Pagos.Reembolsos.Todos);
        Assert.Single(e.Pagos.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Registrar_la_misma_clave_con_otro_monto_se_rechaza()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        await e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, 100m);

        var resultado = await e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, 50m);

        Assert.Equal(ClavesDePagos.ReembolsoDistinto, resultado.Error!.Clave);
        Assert.Single(e.Pagos.Reembolsos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task El_tope_por_linea_impide_reembolsar_mas_que_el_importe_de_la_linea_entre_varios_reembolsos()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        await e.Pedir(AppOrigen.Devolucion, EntornoDeReembolsos.ClaveDeDevolucion(), 60m);

        var segundo = await e.Pedir(AppOrigen.Devolucion, EntornoDeReembolsos.ClaveDeDevolucion(), 60m);

        Assert.Equal(ClavesDePagos.ReembolsoSuperaElTope, segundo.Error!.Clave);
        Assert.Single(e.Pagos.Reembolsos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task El_envio_se_reembolsa_a_lo_mas_una_vez()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        await e.Pedir(AppOrigen.Devolucion, EntornoDeReembolsos.ClaveDeDevolucion(), 10m, envio: 15m);

        var segundo = await e.Pedir(AppOrigen.Devolucion, EntornoDeReembolsos.ClaveDeDevolucion(), 10m, envio: 15m);

        Assert.Equal(ClavesDePagos.ReembolsoSuperaElTope, segundo.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_cobro_sobrante_junto_a_una_devolucion_parcial_no_comparten_el_tope()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        // Un segundo cobro del mismo pedido que la pasarela aprobó cuando ya estaba completado: es un sobrante.
        var sobrante = IntentoDeCobro.Iniciar(Guid.NewGuid(), InstantaneasDeCompraDePrueba.Pedido, Domain.Pagos.MedioDePago.Tarjeta, Importe.De(140m), e.Pagos.Reloj.Ahora);
        sobrante.MarcarRechazado(MotivoDeRechazo.Otro, e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.AgregarIntento(sobrante);
        var intencion = e.Pagos.Repositorio.Intenciones.Single();
        intencion.AplicarAprobacion(sobrante, "ch_2", e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntento(sobrante);

        await e.Pagos.Unidad.Ejecutar(token => e.Registrar.RegistrarDeCompensacion(sobrante, token));
        var devolucion = await e.Pedir(AppOrigen.Devolucion, EntornoDeReembolsos.ClaveDeDevolucion(), 100m);

        Assert.True(devolucion.EsExito);
        Assert.Equal(2, e.Pagos.Reembolsos.Todos.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task El_reembolso_de_una_compensacion_es_idempotente_por_su_clave_y_reembolsa_el_cargo_completo()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();

        var primero = await e.Pagos.Unidad.Ejecutar(token => e.Registrar.RegistrarDeCompensacion(e.Base.Intento, token));
        var segundo = await e.Pagos.Unidad.Ejecutar(token => e.Registrar.RegistrarDeCompensacion(e.Base.Intento, token));

        Assert.Equal(primero.Id, segundo.Id);
        Assert.Equal(Importe.De(140m), primero.Monto);
        Assert.Equal($"comp:{e.Base.IntentoId:D}", primero.ClaveNatural);
        Assert.Single(e.Pagos.Reembolsos.Todos);
        Assert.Equal("automatico", Assert.Single(e.Pagos.Auditoria.Solicitudes).Detalle["modalidad"]);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Sin_una_compra_completada_no_hay_cargo_que_reembolsar()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var p = b.Entorno;
        var registrar = new RegistrarReembolso(p.Repositorio, p.Reembolsos, p.Unidad, p.Auditoria, p.Reloj);

        var resultado = await registrar.Ejecutar(new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, [new LineaAReembolsar(IntencionesDePrueba.LineaImpresa, 10m)], 0m));

        Assert.Equal(ClavesDePagos.SinCargoQueReembolsar, resultado.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Un_pedido_desconocido_no_se_encuentra()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();

        var resultado = await e.Registrar.Ejecutar(new RegistrarReembolsoEntrada(Guid.NewGuid(), AppOrigen.Anulacion, ClaveDeReembolso.Anulacion(Guid.NewGuid()), [new LineaAReembolsar(IntencionesDePrueba.LineaImpresa, 10m)], 0m));

        Assert.Equal(ClavesDePagos.NoEncontrado, resultado.Error!.Clave);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-08")]
    [InlineData("compensacion")]
    [InlineData("clave-mal-formada")]
    [InlineData("monto-cero")]
    [InlineData("monto-con-tres-decimales")]
    [InlineData("linea-repetida")]
    [InlineData("envio-negativo")]
    [InlineData("vacio")]
    public async Task Una_solicitud_mal_formada_se_rechaza_antes_de_tocar_nada(string caso)
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var linea = IntencionesDePrueba.LineaImpresa;
        var entrada = caso switch
        {
            "compensacion" => new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Compensacion, "comp:x", [new LineaAReembolsar(linea, 10m)], 0m),
            "clave-mal-formada" => new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Anulacion, "anul:no-es-un-guid", [new LineaAReembolsar(linea, 10m)], 0m),
            "monto-cero" => new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, [new LineaAReembolsar(linea, 0m)], 0m),
            "monto-con-tres-decimales" => new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, [new LineaAReembolsar(linea, 10.123m)], 0m),
            "linea-repetida" => new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, [new LineaAReembolsar(linea, 10m), new LineaAReembolsar(linea, 5m)], 0m),
            "envio-negativo" => new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, [new LineaAReembolsar(linea, 10m)], -1m),
            _ => new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, [], 0m),
        };

        var resultado = await e.Registrar.Ejecutar(entrada);

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(e.Pagos.Reembolsos.Todos);
        Assert.Equal(0, e.Pagos.Cuenta.MarcasLeidasConBloqueo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Una_linea_que_no_es_de_la_compra_se_rechaza()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();

        var resultado = await e.Registrar.Ejecutar(new RegistrarReembolsoEntrada(InstantaneasDeCompraDePrueba.Pedido, AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, [new LineaAReembolsar(Guid.NewGuid(), 10m)], 0m));

        Assert.Equal(CategoriaDeError.Validacion, resultado.Error!.Categoria);
        Assert.Empty(e.Pagos.Reembolsos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Si_la_auditoria_cae_no_queda_el_reembolso()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        e.Pagos.Auditoria.Caida = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion));

        Assert.Empty(e.Pagos.Reembolsos.Todos);
    }

    // ---------- Procesar ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Procesar_pide_a_la_pasarela_el_monto_con_el_id_del_reembolso_fuera_de_transaccion_y_lo_marca_reembolsado()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();

        var resultado = await e.Procesar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Reembolsado, resultado.Valor);
        var pedido = Assert.Single(e.Pagos.Pasarela.Reembolsos);
        Assert.Equal(id, pedido.ReembolsoId);
        Assert.Equal("ch_1", pedido.Cargo);
        Assert.Equal(Importe.De(100m), pedido.Monto);
        Assert.Equal(0, e.Pagos.Pasarela.ReembolsosDentroDeUnaTransaccion);
        Assert.Equal(0, e.Pagos.Pasarela.ConsultasDeReembolsos);
        Assert.Equal("re_1", e.ReembolsoPorId(id).ReferenciaDePasarela);
        Assert.Equal(1, e.ReembolsoPorId(id).Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Procesar_un_reembolso_ya_concluido_no_vuelve_a_pedirlo()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        await e.Procesar.Ejecutar(id);

        var segundo = await e.Procesar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Reembolsado, segundo.Valor);
        Assert.Single(e.Pagos.Pasarela.Reembolsos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Sin_respuesta_de_la_pasarela_queda_pendiente_y_no_se_pide_de_nuevo_antes_de_la_espera()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.SinRespuesta;

        var primero = await e.Procesar.Ejecutar(id);
        var segundo = await e.Procesar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Pendiente, primero.Valor);
        Assert.Equal(MotivoPendienteDeReembolso.SinRespuesta, e.ReembolsoPorId(id).MotivoPendiente);
        Assert.Equal(EstadoDelReembolso.Pendiente, segundo.Valor);
        Assert.Single(e.Pagos.Pasarela.Reembolsos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Tras_la_espera_un_reintento_consulta_antes_y_si_la_pasarela_ya_lo_aplico_no_pide_otro()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.SinRespuesta;
        await e.Procesar.Ejecutar(id);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddMinutes(1);
        e.Pagos.Pasarela.ReembolsosDelCargo = [new ReembolsoEnPasarela(id, "re_7", Importe.De(100m))];

        var resultado = await e.Procesar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Reembolsado, resultado.Valor);
        Assert.Equal(1, e.Pagos.Pasarela.ConsultasDeReembolsos);
        Assert.Single(e.Pagos.Pasarela.Reembolsos);
        Assert.Equal("re_7", e.ReembolsoPorId(id).ReferenciaDePasarela);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Tras_la_espera_un_reintento_sin_el_reembolso_en_la_pasarela_lo_pide_una_vez_mas()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.SinRespuesta;
        await e.Procesar.Ejecutar(id);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddMinutes(1);
        e.Pagos.Pasarela.RespuestaDeReembolso = null;

        var resultado = await e.Procesar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Reembolsado, resultado.Valor);
        Assert.Equal(2, e.Pagos.Pasarela.Reembolsos.Count);
        Assert.Equal(2, e.ReembolsoPorId(id).Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_no_se_pueden_consultar_los_reembolsos_del_cargo_un_reintento_no_pide_a_ciegas()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.SinRespuesta;
        await e.Procesar.Ejecutar(id);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddMinutes(1);
        e.Pagos.Pasarela.ReembolsosDelCargo = null;

        var resultado = await e.Procesar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Pendiente, resultado.Valor);
        Assert.Single(e.Pagos.Pasarela.Reembolsos);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-17")]
    [InlineData("rechazado", MotivoPendienteDeReembolso.RechazadoPorPasarela)]
    [InlineData("no-soportado", MotivoPendienteDeReembolso.NoSoportado)]
    [InlineData("lanza", MotivoPendienteDeReembolso.SinRespuesta)]
    public async Task Un_rechazo_un_medio_no_soportado_o_una_falla_dejan_el_reembolso_pendiente_con_su_motivo(string caso, MotivoPendienteDeReembolso motivo)
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        switch (caso)
        {
            case "rechazado":
                e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.Rechazado;
                break;
            case "no-soportado":
                e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.NoSoportado;
                break;
            default:
                e.Pagos.Pasarela.ReembolsarLanza = true;
                break;
        }

        var resultado = await e.Procesar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Pendiente, resultado.Valor);
        Assert.Equal(motivo, e.ReembolsoPorId(id).MotivoPendiente);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_la_base_cae_tras_pedirlo_a_la_pasarela_queda_solicitado_con_una_alerta()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ =>
        {
            e.Pagos.Reembolsos.GuardarLanza = true;
            return ResultadoDeReembolsoEnPasarela.Aplicado("re_1");
        };

        var resultado = await e.Procesar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Solicitado, resultado.Valor);
        Assert.Equal(DomEstado.Solicitado, e.ReembolsoPorId(id).Estado);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == ProcesarReembolso.MotivoDeAlertaReembolsoSinRegistrar);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Procesar_un_reembolso_que_no_existe_no_se_encuentra()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();

        var resultado = await e.Procesar.Ejecutar(Guid.NewGuid());

        Assert.Equal(ClavesDePagos.NoEncontrado, resultado.Error!.Clave);
    }

    // ---------- Administración central ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Reintentar_y_listar_son_solo_de_la_administracion_central()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();

        var reintento = await e.Reintentar.Ejecutar(id);
        var lista = await e.Listar.Ejecutar(new EntradaDeListarReembolsos(1, 10));

        Assert.Equal(CategoriaDeError.Prohibido, reintento.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Prohibido, lista.Error!.Categoria);
        Assert.Equal(2, e.Pagos.Denegaciones.Eventos.Count);
        Assert.Empty(e.Pagos.Pasarela.Reembolsos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Un_visitante_sin_sesion_recibe_no_autenticado()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        e.Pagos.Actor.Establecer(ActorActual.Anonimo());

        var lista = await e.Listar.Ejecutar(new EntradaDeListarReembolsos(1, 10));

        Assert.Equal(CategoriaDeError.NoAutenticado, lista.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task La_administracion_central_reintenta_un_reembolso_pendiente_y_lo_concluye()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.Rechazado;
        await e.Procesar.Ejecutar(id);
        e.Pagos.Pasarela.RespuestaDeReembolso = null;
        e.ComoAdministrador();

        var resultado = await e.Reintentar.Ejecutar(id);

        Assert.Equal(EstadoDelReembolso.Reembolsado, resultado.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Reintentar_antes_de_la_espera_tras_un_sin_respuesta_se_rechaza_sin_pedir_nada()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        var id = await e.UnReembolsoPendiente();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.SinRespuesta;
        await e.Procesar.Ejecutar(id);
        e.ComoAdministrador();

        var resultado = await e.Reintentar.Ejecutar(id);

        Assert.Equal(ClavesDePagos.ReembolsoNoReintentable, resultado.Error!.Clave);
        Assert.Single(e.Pagos.Pasarela.Reembolsos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Reintentar_un_reembolso_que_no_existe_no_se_encuentra()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        e.ComoAdministrador();

        var resultado = await e.Reintentar.Ejecutar(Guid.NewGuid());

        Assert.Equal(ClavesDePagos.NoEncontrado, resultado.Error!.Clave);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Listar_pagina_los_pendientes_sin_datos_de_pago_y_valida_la_pagina()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        await e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion, 50m);
        await e.Pedir(AppOrigen.Devolucion, EntornoDeReembolsos.ClaveDeDevolucion(), 10m);
        e.ComoAdministrador();

        var pagina = await e.Listar.Ejecutar(new EntradaDeListarReembolsos(1, 1));
        var invalida = await e.Listar.Ejecutar(new EntradaDeListarReembolsos(0, 10));
        var grande = await e.Listar.Ejecutar(new EntradaDeListarReembolsos(1, 101));

        Assert.Equal(2, pagina.Valor.Total);
        Assert.Single(pagina.Valor.Reembolsos);
        Assert.Equal(CategoriaDeError.Validacion, invalida.Error!.Categoria);
        Assert.Equal(CategoriaDeError.Validacion, grande.Error!.Categoria);
        Assert.DoesNotContain("ch_1", pagina.Valor.Reembolsos.Single().ToString());
    }

    // ---------- La fachada del contrato público ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public async Task La_fachada_cuenta_los_reembolsos_pendientes_de_una_cuenta_por_origen_y_consulta_por_clave()
    {
        var e = await EntornoDeReembolsos.ConCompraCompletada();
        await e.Pedir(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion);

        var anulaciones = await e.Fachada.ContarPendientes(InstantaneasDeCompraDePrueba.Cuenta, AppOrigen.Anulacion);
        var devoluciones = await e.Fachada.ContarPendientes(InstantaneasDeCompraDePrueba.Cuenta, AppOrigen.Devolucion);
        var otraCuenta = await e.Fachada.ContarPendientes(Guid.NewGuid(), AppOrigen.Anulacion);
        var consultado = await e.Fachada.ConsultarPorOrigen(AppOrigen.Anulacion, EntornoDeReembolsos.ClaveDeAnulacion);
        var ninguno = await e.Fachada.ConsultarPorOrigen(AppOrigen.Anulacion, ClaveDeReembolso.Anulacion(Guid.NewGuid()));
        var invalida = await e.Fachada.ConsultarPorOrigen(AppOrigen.Anulacion, " ");

        Assert.Equal(1, anulaciones);
        Assert.Equal(0, devoluciones);
        Assert.Equal(0, otraCuenta);
        Assert.Equal(100m, consultado.Valor!.Monto);
        Assert.Null(ninguno.Valor);
        Assert.Equal(CategoriaDeError.Validacion, invalida.Error!.Categoria);
    }
}
