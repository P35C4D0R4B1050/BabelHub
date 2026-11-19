using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using DomMedio = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>Una compra mixta aprobada (cargo de S/ 140) con el orquestador de compensación real y la pasarela, el Inventario y los repositorios doblados.</summary>
internal sealed class EntornoDeCompensacion
{
    private EntornoDeCompensacion(EntornoDeConfirmacion b)
    {
        Base = b;
        var p = b.Entorno;
        var bloqueo = new BloqueoDeIntenciones(p.Repositorio, p.Cuenta);
        var registrar = new RegistrarReembolso(p.Repositorio, p.Reembolsos, p.Unidad, p.Auditoria, p.Reloj);
        var procesar = new ProcesarReembolso(p.Repositorio, p.Reembolsos, p.Unidad, p.Pasarela, p.Auditoria, p.Alertas, p.Reloj, new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
        Compensar = new CompensarCompra(p.Repositorio, p.Unidad, bloqueo, p.Reservas, registrar, procesar, p.Avisador, p.Auditoria, p.Alertas, p.Reloj);
    }

    public EntornoDeConfirmacion Base { get; }

    public EntornoDePagos Pagos => Base.Entorno;

    public CompensarCompra Compensar { get; }

    public Guid IntentoId => Base.IntentoId;

    public IntencionDePago Intencion => Base.Intencion;

    public Compensacion Compensacion => Pagos.Repositorio.Compensaciones.Single();

    /// <summary>La compra aprobada (a tiempo) lista para compensarse; con <paramref name="confirmacionIniciada"/> consta que se intentó confirmar las reservas.</summary>
    public static async Task<EntornoDeCompensacion> Aprobada(bool confirmacionIniciada = false, bool soloDigital = false)
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado(soloDigital);
        var intencion = b.Intencion;
        var ahora = b.Entorno.Reloj.Ahora;
        intencion.AplicarAprobacion(b.Intento, "ch_1", ahora);
        if (confirmacionIniciada)
        {
            intencion.MarcarConfirmacionIniciada(ahora);
        }

        await b.Entorno.Repositorio.GuardarIntencion(intencion);
        await b.Entorno.Repositorio.GuardarIntento(b.Intento);
        return new EntornoDeCompensacion(b);
    }

    /// <summary>Un cobro que la pasarela aprobó cuando su intento ya estaba rechazado (la intención quedó abierta): un sobrante que solo se reembolsa.</summary>
    public static async Task<EntornoDeCompensacion> ConSobrante()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var ahora = b.Entorno.Reloj.Ahora;
        var intento = b.Intento;
        var intencion = b.Intencion;
        intento.MarcarRechazado(MotivoDeRechazo.Otro, ahora);
        intencion.ResolverIntentoSinCobro(ahora);
        intencion.AplicarAprobacion(intento, "ch_9", ahora);
        await b.Entorno.Repositorio.GuardarIntento(intento);
        await b.Entorno.Repositorio.GuardarIntencion(intencion);
        return new EntornoDeCompensacion(b);
    }

    public Task Ejecutar(MotivoDeCompensacion motivo = MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion variante = VarianteDeCompensacion.Completa) =>
        Compensar.Compensar(InstantaneasDeCompraDePrueba.Pedido, IntentoId, motivo, variante);
}

public sealed class CompensarCompraTests
{
    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_pago_tardio_libera_las_reservas_reembolsa_el_cargo_completo_y_deja_el_aviso_compensado()
    {
        var e = await EntornoDeCompensacion.Aprobada();

        await e.Ejecutar();

        Assert.Equal(EstadoDeIntencion.Compensada, e.Intencion.Estado);
        Assert.Null(e.Intencion.Contacto);
        Assert.Equal(MotivoDeLiberacion.PagoVencido, Assert.Single(e.Pagos.Reservas.Liberaciones));
        var reembolso = Assert.Single(e.Pagos.Reembolsos.Todos);
        Assert.Equal(Importe.De(140m), reembolso.Monto);
        Assert.Equal(OrigenDeReembolso.Compensacion, reembolso.Origen);
        Assert.Equal(EstadoDeReembolso.Reembolsado, reembolso.Estado);
        Assert.Equal(reembolso.Id, Assert.Single(e.Pagos.Pasarela.Reembolsos).ReembolsoId);
        Assert.True(e.Compensacion.EstaTerminada);
        var aviso = Assert.Single(e.Pagos.Repositorio.AvisosACarrito);
        Assert.Equal(TransicionDeAviso.Compensado, aviso.Transicion);
        Assert.Equal(EventoDeAvisoDePago.ReembolsoAutomatico, Assert.Single(e.Pagos.Repositorio.AvisosDePago).Evento);
        Assert.DoesNotContain(e.Pagos.Repositorio.Intentos, i => i.Estado != EstadoDeIntento.Aprobado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_consta_que_se_intento_confirmar_las_reservas_se_liberan_con_falla_posterior_al_pago()
    {
        var e = await EntornoDeCompensacion.Aprobada(confirmacionIniciada: true);

        await e.Ejecutar(MotivoDeCompensacion.FallaPosteriorAlPago);

        Assert.Equal(MotivoDeLiberacion.FallaPosteriorAlPago, Assert.Single(e.Pagos.Reservas.Liberaciones));
        Assert.Equal(MotivoDeLiberacion.FallaPosteriorAlPago, e.Compensacion.MotivoDeLiberacion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compra_solo_digital_no_llama_al_inventario_para_liberar()
    {
        var e = await EntornoDeCompensacion.Aprobada(soloDigital: true);

        await e.Ejecutar();

        Assert.Empty(e.Pagos.Reservas.Liberaciones);
        Assert.True(e.Compensacion.EstaTerminada);
        Assert.Single(e.Pagos.Reembolsos.Todos);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-25")]
    [InlineData(MotivoDeCompensacion.IntencionDesvinculada)]
    [InlineData(MotivoDeCompensacion.CancelacionDeCuentaEnCurso)]
    public async Task En_la_desvinculacion_o_la_cancelacion_de_la_cuenta_se_reembolsa_sin_avisar_al_cliente(MotivoDeCompensacion motivo)
    {
        var e = await EntornoDeCompensacion.Aprobada();
        if (motivo == MotivoDeCompensacion.IntencionDesvinculada)
        {
            e.Intencion.Desvincular(e.Pagos.Reloj.Ahora);
        }

        await e.Ejecutar(motivo);

        Assert.Empty(e.Pagos.Repositorio.AvisosDePago);
        Assert.Single(e.Pagos.Reembolsos.Todos);
        Assert.Equal(EstadoDeReembolso.Reembolsado, e.Pagos.Reembolsos.Todos.Single().Estado);
        Assert.Equal(TransicionDeAviso.Compensado, Assert.Single(e.Pagos.Repositorio.AvisosACarrito).Transicion);
        Assert.Equal(EstadoDeIntencion.Compensada, e.Intencion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-25")]
    public async Task Compensar_no_lee_el_correo_de_la_cuenta()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Cuenta.Falla = true;
        e.Intencion.Desvincular(e.Pagos.Reloj.Ahora);

        await e.Ejecutar(MotivoDeCompensacion.IntencionDesvinculada);

        Assert.Equal(0, e.Pagos.Cuenta.Consultas);
        Assert.True(e.Compensacion.EstaTerminada);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_cobro_sobrante_solo_se_reembolsa_sin_liberar_reservas_ni_cerrar_el_checkout_ni_tocar_la_intencion()
    {
        var e = await EntornoDeCompensacion.ConSobrante();
        var estadoAntes = e.Intencion.Estado;

        await e.Ejecutar(MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.SoloReembolso);

        Assert.Equal(estadoAntes, e.Intencion.Estado);
        Assert.Empty(e.Pagos.Reservas.Liberaciones);
        Assert.Empty(e.Pagos.Repositorio.AvisosACarrito);
        Assert.Single(e.Pagos.Reembolsos.Todos);
        Assert.Equal("ch_9", Assert.Single(e.Pagos.Pasarela.Reembolsos).Cargo);
        Assert.Equal(EventoDeAvisoDePago.ReembolsoAutomatico, Assert.Single(e.Pagos.Repositorio.AvisosDePago).Evento);
        Assert.True(e.Compensacion.EstaTerminada);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Compensar_como_sobrante_un_cobro_que_no_lo_es_no_hace_nada_y_alerta()
    {
        var e = await EntornoDeCompensacion.Aprobada();

        await e.Ejecutar(MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.SoloReembolso);

        Assert.Empty(e.Pagos.Reembolsos.Todos);
        Assert.Empty(e.Pagos.Repositorio.Compensaciones);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == CompensarCompra.MotivoDeAlertaCompensacionNoAplicable);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compra_ya_completada_no_se_compensa_y_se_alerta()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Intencion.Completar(e.Pagos.Reloj.Ahora);

        await e.Ejecutar();

        Assert.Equal(EstadoDeIntencion.Completada, e.Intencion.Estado);
        Assert.Empty(e.Pagos.Reembolsos.Todos);
        Assert.Empty(e.Pagos.Reservas.Liberaciones);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == CompensarCompra.MotivoDeAlertaCompensacionNoAplicable);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Sin_respuesta_del_inventario_se_detiene_sin_reembolsar_y_la_retoma_sigue_sin_repetir_pasos()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Reservas.Liberacion = new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, false);

        await e.Ejecutar();

        Assert.Empty(e.Pagos.Reembolsos.Todos);
        Assert.False(e.Compensacion.EstaTerminada);
        Assert.Null(e.Compensacion.ReservasLiberadasEn);

        e.Pagos.Reservas.Liberacion = null;
        await e.Ejecutar();
        await e.Ejecutar();

        Assert.True(e.Compensacion.EstaTerminada);
        Assert.Single(e.Pagos.Reembolsos.Todos);
        Assert.Single(e.Pagos.Pasarela.Reembolsos);
        Assert.Single(e.Pagos.Repositorio.AvisosACarrito);
        Assert.Single(e.Pagos.Repositorio.AvisosDePago);
        Assert.Equal(2, e.Pagos.Reservas.Liberaciones.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_excepcion_del_inventario_se_trata_como_sin_respuesta()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Reservas.Falla = true;

        await e.Ejecutar();

        Assert.Empty(e.Pagos.Reembolsos.Todos);
        Assert.False(e.Compensacion.EstaTerminada);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_estado_invalido_al_liberar_con_pago_vencido_es_una_reserva_ya_confirmada_sin_reintento_y_con_alerta()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Reservas.Liberacion = new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, false);

        await e.Ejecutar();

        Assert.Single(e.Pagos.Reservas.Liberaciones);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == CompensarCompra.MotivoDeAlertaReservaConfirmadaSinLiberar);
        Assert.True(e.Compensacion.EstaTerminada);
        Assert.Single(e.Pagos.Reembolsos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Liberar_una_reserva_expirada_o_que_no_existe_es_un_exito_sin_cambios_y_sin_alerta()
    {
        var e = await EntornoDeCompensacion.Aprobada(confirmacionIniciada: true);
        e.Pagos.Reservas.Liberacion = new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.NoExiste, false);

        await e.Ejecutar(MotivoDeCompensacion.FallaPosteriorAlPago);

        Assert.Empty(e.Pagos.Alertas.Publicadas);
        Assert.True(e.Compensacion.EstaTerminada);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_la_pasarela_no_responde_al_reembolsar_la_compensacion_termina_y_el_reembolso_queda_pendiente()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Pasarela.RespuestaDeReembolso = _ => ResultadoDeReembolsoEnPasarela.SinRespuesta;

        await e.Ejecutar();

        Assert.True(e.Compensacion.EstaTerminada);
        Assert.Equal(EstadoDeReembolso.Pendiente, e.Pagos.Reembolsos.Todos.Single().Estado);
        Assert.Single(e.Pagos.Repositorio.AvisosACarrito);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_falla_a_mitad_deja_los_pasos_hechos_marcados_y_la_retoma_no_duplica_el_reembolso()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Repositorio.FallaAlAgregarAvisos = true;

        await e.Ejecutar();

        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == CompensarCompra.MotivoDeAlertaCompensacionFallida);
        Assert.NotNull(e.Compensacion.ReservasLiberadasEn);
        Assert.Null(e.Compensacion.ReembolsoId);

        e.Pagos.Repositorio.FallaAlAgregarAvisos = false;
        await e.Ejecutar();

        Assert.True(e.Compensacion.EstaTerminada);
        Assert.Single(e.Pagos.Reembolsos.Todos);
        Assert.Single(e.Pagos.Reservas.Liberaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compensacion_ya_iniciada_conserva_su_motivo_aunque_la_pidan_con_otro()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Reservas.Liberacion = new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, false);
        await e.Ejecutar(MotivoDeCompensacion.CancelacionDeCuentaEnCurso);

        await e.Ejecutar(MotivoDeCompensacion.PagoTardio);

        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, e.Compensacion.Motivo);
        Assert.False(e.Compensacion.Avisar);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-24")]
    public async Task Los_avisos_de_pendiente_que_ya_no_tienen_sentido_pasan_a_omitido()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        await e.Pagos.Repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(InstantaneasDeCompraDePrueba.Pedido, e.IntentoId, TransicionDeAviso.Pendiente, retenido: true, e.Pagos.Reloj.Ahora));
        await e.Pagos.Repositorio.AgregarAvisoDePago(AvisoDePago.Crear(InstantaneasDeCompraDePrueba.Pedido, e.IntentoId, EventoDeAvisoDePago.Pendiente, e.Pagos.Reloj.Ahora));

        await e.Ejecutar();

        Assert.Equal(EstadoDeAvisoACarrito.Omitido, e.Pagos.Repositorio.AvisosACarrito.Single(a => a.Transicion == TransicionDeAviso.Pendiente).Estado);
        Assert.Equal(EstadoDeAvisoDePago.Omitido, e.Pagos.Repositorio.AvisosDePago.Single(a => a.Evento == EventoDeAvisoDePago.Pendiente).Estado);
        Assert.Equal(EstadoDeAvisoACarrito.Pendiente, e.Pagos.Repositorio.AvisosACarrito.Single(a => a.Transicion == TransicionDeAviso.Compensado).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Ninguna_llamada_externa_ocurre_dentro_de_una_transaccion_y_la_marca_se_lee_antes_que_la_intencion()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Orden.Clear();

        await e.Ejecutar();

        Assert.Equal(0, e.Pagos.Reservas.LlamadasDentroDeUnaTransaccion);
        Assert.Equal(0, e.Pagos.Pasarela.ReembolsosDentroDeUnaTransaccion);
        Assert.Equal("marca-con-bloqueo", e.Pagos.Orden.First());
        Assert.Equal("intencion-con-bloqueo", e.Pagos.Orden.Skip(1).First());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Compensar_un_intento_que_no_es_de_la_intencion_no_hace_nada()
    {
        var e = await EntornoDeCompensacion.Aprobada();

        await e.Compensar.Compensar(InstantaneasDeCompraDePrueba.Pedido, Guid.NewGuid(), MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.Completa);

        Assert.Empty(e.Pagos.Repositorio.Compensaciones);
        Assert.Equal(EstadoDeIntencion.Aprobada, e.Intencion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_falla_inesperada_no_interrumpe_al_llamador_y_publica_una_alerta()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Repositorio.BloqueoNoDisponible = true;

        await e.Ejecutar();

        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == CompensarCompra.MotivoDeAlertaCompensacionFallida);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-25")]
    [InlineData(MotivoDeCompensacion.CancelacionDeCuentaEnCurso)]
    [InlineData(MotivoDeCompensacion.IntencionDesvinculada)]
    public async Task Un_cobro_sobrante_con_la_cuenta_en_cancelacion_o_desvinculada_no_avisa_ni_deja_avisos_que_lean_el_correo(MotivoDeCompensacion motivo)
    {
        var e = await EntornoDeCompensacion.ConSobrante();
        await e.Pagos.Repositorio.AgregarAvisoDePago(AvisoDePago.Crear(InstantaneasDeCompraDePrueba.Pedido, e.IntentoId, EventoDeAvisoDePago.Rechazado, e.Pagos.Reloj.Ahora));
        await e.Pagos.Repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(InstantaneasDeCompraDePrueba.Pedido, e.IntentoId, TransicionDeAviso.Rechazado, false, e.Pagos.Reloj.Ahora));

        await e.Ejecutar(motivo, VarianteDeCompensacion.SoloReembolso);

        Assert.False(e.Compensacion.Avisar);
        Assert.All(e.Pagos.Repositorio.AvisosDePago, a => Assert.Equal(EstadoDeAvisoDePago.Omitido, a.Estado));
        Assert.DoesNotContain(e.Pagos.Repositorio.AvisosDePago, a => a.Evento == EventoDeAvisoDePago.ReembolsoAutomatico);
        Assert.All(e.Pagos.Repositorio.AvisosACarrito, a => Assert.Equal(EstadoDeAvisoACarrito.Omitido, a.Estado));
        Assert.Single(e.Pagos.Reembolsos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Cada_paso_de_la_compensacion_se_audita_con_su_motivo_y_sin_datos_de_pago()
    {
        var e = await EntornoDeCompensacion.Aprobada();

        await e.Ejecutar(MotivoDeCompensacion.PagoTardio);

        var pasos = e.Pagos.Auditoria.Solicitudes.Where(s => s.Accion.Identificador == "PasoDeCompensacionHecho").Select(s => s.Detalle["paso"]!).ToArray();
        Assert.Equal(["abrir", "liberar-reservas", "registrar-reembolso", "cerrar-checkout"], pasos);
        Assert.All(e.Pagos.Auditoria.Solicitudes.Where(s => s.Accion.Identificador == "PasoDeCompensacionHecho"), s =>
        {
            Assert.Equal("pago-tardio", s.Detalle["motivo"]);
            Assert.Equal("pedido", s.ObjetoTipo);
            Assert.DoesNotContain("ch_", string.Join(' ', s.Detalle.Values));
        });
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_la_auditoria_cae_el_paso_se_revierte_y_la_retoma_lo_repite_sin_duplicar()
    {
        var e = await EntornoDeCompensacion.Aprobada();
        e.Pagos.Auditoria.Caida = true;

        await e.Ejecutar();

        Assert.Empty(e.Pagos.Repositorio.Compensaciones);
        Assert.Equal(EstadoDeIntencion.Aprobada, e.Intencion.Estado);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == CompensarCompra.MotivoDeAlertaCompensacionFallida);

        e.Pagos.Auditoria.Caida = false;
        await e.Ejecutar();

        Assert.True(e.Compensacion.EstaTerminada);
        Assert.Single(e.Pagos.Reembolsos.Todos);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task El_medio_no_cambia_lo_que_se_reembolsa_el_cargo_completo_en_pesos()
    {
        var e = await EntornoDeCompensacion.Aprobada();

        await e.Ejecutar();

        var pedido = Assert.Single(e.Pagos.Pasarela.Reembolsos);
        Assert.Equal(Importe.De(140m), pedido.Monto);
        Assert.Equal(DomMedio.Yape, e.Pagos.Repositorio.Intentos.Single().Medio);
    }
}
