using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using DomMedio = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>La confirmación de una compra aprobada ya cableada con los pasos siguientes doblados (T14): el paso (c) y la compensación son de T15 y T16.</summary>
internal sealed class EntornoDeConfirmacion
{
    public EntornoDeConfirmacion(EntornoDePagos entorno)
    {
        Entorno = entorno;
        Completador = new CompletadorDeCompraDoble(entorno.Unidad);
        Compensador = new CompensadorDeCompraDoble(entorno.Unidad);
        var bloqueo = new BloqueoDeIntenciones(entorno.Repositorio, entorno.Cuenta);
        Marcar = new MarcarCompraAprobada(entorno.Repositorio, entorno.Unidad, bloqueo, entorno.Reloj);
        Reservas = new ConfirmarReservasDeLaCompra(entorno.Repositorio, entorno.Unidad, bloqueo, entorno.Reservas, entorno.Alertas, entorno.Reloj);
        Confirmar = new ConfirmarCompraAprobada(Marcar, Reservas, Completador, Compensador, entorno.Avisador, entorno.Alertas, entorno.Reloj);
    }

    public EntornoDePagos Entorno { get; }

    public CompletadorDeCompraDoble Completador { get; }

    public CompensadorDeCompraDoble Compensador { get; }

    public MarcarCompraAprobada Marcar { get; }

    public ConfirmarReservasDeLaCompra Reservas { get; }

    public ConfirmarCompraAprobada Confirmar { get; }

    public Guid IntentoId { get; private set; }

    /// <summary>La compra mixta con un intento <c>iniciado</c> (la intención con cobro), como la deja <c>IniciarCobro</c> antes de que la pasarela responda.</summary>
    public static async Task<EntornoDeConfirmacion> ConIntentoIniciado(bool soloDigital = false)
    {
        var entorno = new EntornoDePagos();
        if (soloDigital)
        {
            await entorno.Creador.Crear(InstantaneasDeCompraDePrueba.Cuenta, InstantaneasDeCompraDePrueba.SoloDigital(), "huella-d", entorno.ReservaExpiraEn);
        }
        else
        {
            await entorno.CrearMixta();
        }

        entorno.Reloj.Ahora = IntencionesDePrueba.Inicio.AddSeconds(30);
        var e = new EntornoDeConfirmacion(entorno);
        await e.Sembrar(i => i.IniciarCobro(entorno.Reloj.Ahora));
        return e;
    }

    private async Task Sembrar(Action<IntencionDePago> cambiar)
    {
        var intencion = (await Entorno.Repositorio.ObtenerIntencion(InstantaneasDeCompraDePrueba.Pedido, false))!;
        cambiar(intencion);
        await Entorno.Repositorio.GuardarIntencion(intencion);
        var intento = IntentoDeCobro.Iniciar(Guid.NewGuid(), intencion.Id, DomMedio.Yape, intencion.Total, Entorno.Reloj.Ahora);
        await Entorno.Repositorio.AgregarIntento(intento);
        IntentoId = intento.Id;
    }

    public Task<ResultadoDelCobroDelCliente> Aprobar(string referencia = "ch_1") => Confirmar.Confirmar(InstantaneasDeCompraDePrueba.Pedido, IntentoId, referencia);

    public IntencionDePago Intencion => Entorno.Repositorio.Intenciones.Single();

    public IntentoDeCobro Intento => Entorno.Repositorio.Intentos.Single(i => i.Id == IntentoId);

    /// <summary>Las dos reservas de la línea impresa tal como las devuelve el Inventario.</summary>
    public static RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>> Confirmadas(params LineaConfirmadaDeReserva[] lineas) => new(ResultadoDeLaLlamadaDeReserva.Correcto, lineas);

    public static LineaConfirmadaDeReserva LineaImpresaConfirmada(bool traslado = false) =>
        new(IntencionesDePrueba.LineaImpresa, "AYA-01", traslado, traslado ? Guid.Parse("55555555-5555-5555-5555-555555555551") : null, traslado ? new DateOnly(2026, 11, 22) : null);
}

public sealed class ConfirmarCompraAprobadaTests
{
    private static readonly Guid Pedido = InstantaneasDeCompraDePrueba.Pedido;

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Una_compra_aprobada_a_tiempo_marca_la_aprobacion_confirma_las_reservas_con_su_clave_y_completa()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada(traslado: true));

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.Aprobado, resultado.Estado);
        Assert.Equal(EstadoDeIntento.Aprobado, e.Intento.Estado);
        Assert.Equal("ch_1", e.Intento.ReferenciaDePasarela);
        Assert.Equal(EstadoDeIntencion.Aprobada, e.Intencion.Estado);
        Assert.NotNull(e.Intencion.AprobadoEn);
        Assert.Equal($"conf-{e.IntentoId:D}", Assert.Single(e.Entorno.Reservas.Confirmaciones));
        Assert.Single(e.Completador.Llamadas);
        Assert.Empty(e.Compensador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task La_fuente_y_el_traslado_que_devuelve_el_inventario_quedan_guardados_en_la_intencion()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada(traslado: true));

        await e.Aprobar();

        var linea = Assert.Single(e.Intencion.LineasConfirmadas);
        Assert.Equal(IntencionesDePrueba.LineaImpresa, linea.LineaId);
        Assert.Equal("AYA-01", linea.CodigoDeFuente);
        Assert.True(linea.RequiereTraslado);
        Assert.Equal(Guid.Parse("55555555-5555-5555-5555-555555555551"), linea.TrasladoId);
        Assert.Equal(new DateOnly(2026, 11, 22), linea.FechaEstimadaDeLlegada);
        Assert.NotNull(e.Intencion.ReservasConfirmadasEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Ninguna_llamada_al_inventario_ni_a_los_pasos_siguientes_ocurre_dentro_de_una_transaccion()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());

        await e.Aprobar();

        Assert.Equal(0, e.Entorno.Reservas.LlamadasDentroDeUnaTransaccion);
        Assert.Equal(0, e.Completador.LlamadasDentroDeUnaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task La_confirmacion_iniciada_consta_antes_de_llamar_al_inventario()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        DateTimeOffset? anotadaAlLlamar = null;
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());
        e.Entorno.Reservas.AlConfirmar = () => anotadaAlLlamar = e.Intencion.ConfirmacionIniciadaEn;

        await e.Aprobar();

        Assert.NotNull(anotadaAlLlamar);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Una_compra_solo_digital_no_llama_al_inventario_ni_anota_la_confirmacion_iniciada()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado(soloDigital: true);

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.Aprobado, resultado.Estado);
        Assert.Empty(e.Entorno.Reservas.Confirmaciones);
        Assert.Null(e.Intencion.ConfirmacionIniciadaEn);
        Assert.Single(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_cobro_aprobado_pasados_los_30_minutos_se_compensa_como_pago_tardio_sin_confirmar_reservas_ni_completar()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reloj.Ahora = IntencionesDePrueba.Inicio.AddMinutes(31);

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Estado);
        var llamada = Assert.Single(e.Compensador.Llamadas);
        Assert.Equal(MotivoDeCompensacion.PagoTardio, llamada.Motivo);
        Assert.Equal(VarianteDeCompensacion.Completa, llamada.Variante);
        Assert.Empty(e.Entorno.Reservas.Confirmaciones);
        Assert.Empty(e.Completador.Llamadas);
        Assert.Null(e.Intencion.ConfirmacionIniciadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_cobro_aprobado_con_la_reserva_ya_expirada_es_tardio_aunque_no_pasaran_30_minutos()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reloj.Ahora = e.Entorno.ReservaExpiraEn.AddSeconds(1);

        await e.Aprobar();

        Assert.Equal(MotivoDeCompensacion.PagoTardio, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Una_compra_solo_digital_aprobada_despues_de_los_30_minutos_tambien_se_compensa()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado(soloDigital: true);
        e.Entorno.Reloj.Ahora = IntencionesDePrueba.Inicio.AddMinutes(31);

        await e.Aprobar();

        Assert.Equal(MotivoDeCompensacion.PagoTardio, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task Una_intencion_desvinculada_se_compensa_por_PAG_29_sin_confirmar_reservas()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        await Desvincular(e);

        await e.Aprobar();

        Assert.Equal(MotivoDeCompensacion.IntencionDesvinculada, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Entorno.Reservas.Confirmaciones);
        Assert.Empty(e.Completador.Llamadas);
        Assert.Equal(0, e.Entorno.Cuenta.MarcasLeidasConBloqueo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task La_cancelacion_de_la_cuenta_en_ejecucion_prevalece_por_PAG_32_y_no_confirma_reservas()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Cuenta.CancelacionEnCurso = true;

        await e.Aprobar();

        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Entorno.Reservas.Confirmaciones);
        Assert.Empty(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task La_desvinculacion_entre_la_lectura_de_la_cuenta_y_el_bloqueo_va_a_PAG_29_sin_leer_nada_mas()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Cuenta.CancelacionEnCurso = true;
        e.Entorno.Cuenta.AlLeerLaMarca = () => DesvincularSincrono(e);

        await e.Aprobar();

        // La cancelación en ejecución y la desvinculación coinciden: prevalece la desvinculación, que no avisa ni lee el correo.
        Assert.Equal(MotivoDeCompensacion.IntencionDesvinculada, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Entorno.Reservas.Confirmaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task La_marca_de_la_cuenta_se_lee_con_bloqueo_antes_de_bloquear_la_intencion()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Orden.Clear();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());

        await e.Aprobar();

        var primeras = e.Entorno.Orden.Take(2).ToArray();
        Assert.Equal(["marca-con-bloqueo", "intencion-con-bloqueo"], primeras);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_estado_invalido_de_la_reserva_equivale_a_que_vencio_y_se_compensa_como_pago_tardio()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, null);

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Estado);
        Assert.Equal(MotivoDeCompensacion.PagoTardio, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Completador.Llamadas);
        Assert.NotNull(e.Intencion.ConfirmacionIniciadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Sin_respuesta_del_inventario_no_se_compensa_se_suelta_la_reclamacion_y_se_reintenta_con_la_misma_clave()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null);

        var primero = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, primero.Estado);
        Assert.Empty(e.Compensador.Llamadas);
        Assert.Empty(e.Completador.Llamadas);
        Assert.Null(e.Intencion.ReclamadaHasta);
        Assert.Equal(EstadoDeIntencion.Aprobada, e.Intencion.Estado);

        // La retoma: el inventario ya responde y la misma clave confirma.
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());
        var segundo = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.Aprobado, segundo.Estado);
        Assert.Equal(2, e.Entorno.Reservas.Confirmaciones.Count);
        Assert.Single(e.Entorno.Reservas.Confirmaciones.Distinct());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Una_excepcion_del_inventario_se_trata_como_sin_respuesta()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Falla = true;

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Estado);
        Assert.Empty(e.Compensador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task Si_la_intencion_se_desvincula_mientras_se_confirmaban_las_reservas_se_anotan_y_se_compensa_por_PAG_29()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());
        e.Entorno.Reservas.AlConfirmar = () => DesvincularSincrono(e);

        await e.Aprobar();

        Assert.NotNull(e.Intencion.ReservasConfirmadasEn);
        Assert.Equal(MotivoDeCompensacion.IntencionDesvinculada, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public async Task Si_la_cancelacion_de_la_cuenta_empieza_mientras_se_confirmaban_las_reservas_prevalece_y_se_compensa()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());
        e.Entorno.Reservas.AlConfirmar = () => e.Entorno.Cuenta.CancelacionEnCurso = true;

        await e.Aprobar();

        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Completador.Llamadas);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-13")]
    [InlineData("sin-lineas")]
    [InlineData("linea-ajena")]
    [InlineData("sin-fuente")]
    [InlineData("traslado-sin-id")]
    [InlineData("linea-repetida")]
    public async Task Una_respuesta_del_inventario_que_no_coincide_con_las_lineas_es_una_falla_posterior_al_pago_con_alerta(string caso)
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        var buena = EntornoDeConfirmacion.LineaImpresaConfirmada();
        e.Entorno.Reservas.Confirmacion = caso switch
        {
            "sin-lineas" => EntornoDeConfirmacion.Confirmadas(),
            "linea-ajena" => EntornoDeConfirmacion.Confirmadas(buena with { LineaId = Guid.NewGuid() }),
            "sin-fuente" => EntornoDeConfirmacion.Confirmadas(buena with { CodigoDeFuente = " " }),
            "traslado-sin-id" => EntornoDeConfirmacion.Confirmadas(buena with { RequiereTraslado = true, TrasladoId = null }),
            _ => EntornoDeConfirmacion.Confirmadas(buena, buena),
        };

        await e.Aprobar();

        Assert.Equal(MotivoDeCompensacion.FallaPosteriorAlPago, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Empty(e.Completador.Llamadas);
        Assert.Null(e.Intencion.ReservasConfirmadasEn);
        Assert.Contains(e.Entorno.Alertas.Publicadas, a => a.Motivo == ConfirmarReservasDeLaCompra.MotivoDeAlertaReservasInconsistentes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Una_reserva_que_el_inventario_no_conoce_es_una_falla_posterior_al_pago_con_alerta()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.NoExiste, null);

        await e.Aprobar();

        Assert.Equal(MotivoDeCompensacion.FallaPosteriorAlPago, Assert.Single(e.Compensador.Llamadas).Motivo);
        Assert.Contains(e.Entorno.Alertas.Publicadas, a => a.Motivo == ConfirmarReservasDeLaCompra.MotivoDeAlertaReservaInexistente);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_cobro_aprobado_de_un_intento_rechazado_es_un_sobrante_que_solo_se_reembolsa_y_no_toca_la_intencion()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        var intento = e.Intento;
        intento.MarcarRechazado(MotivoDeRechazo.TarjetaRechazada, e.Entorno.Reloj.Ahora);
        await e.Entorno.Repositorio.GuardarIntento(intento);
        var intencion = e.Intencion;
        intencion.ResolverIntentoSinCobro(e.Entorno.Reloj.Ahora);
        await e.Entorno.Repositorio.GuardarIntencion(intencion);

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Estado);
        var llamada = Assert.Single(e.Compensador.Llamadas);
        Assert.Equal(VarianteDeCompensacion.SoloReembolso, llamada.Variante);
        Assert.Equal(EstadoDeIntento.AprobadoSobrante, e.Intento.Estado);
        Assert.Equal(EstadoDeIntencion.Abierta, e.Intencion.Estado);
        Assert.Empty(e.Entorno.Reservas.Confirmaciones);
        Assert.Empty(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Repetir_el_aviso_de_un_sobrante_vuelve_a_pedir_solo_el_reembolso()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        var intento = e.Intento;
        intento.MarcarRechazado(MotivoDeRechazo.Otro, e.Entorno.Reloj.Ahora);
        await e.Entorno.Repositorio.GuardarIntento(intento);
        e.Intencion.ResolverIntentoSinCobro(e.Entorno.Reloj.Ahora);

        await e.Aprobar();
        await e.Aprobar();

        Assert.Equal(2, e.Compensador.Llamadas.Count);
        Assert.All(e.Compensador.Llamadas, l => Assert.Equal(VarianteDeCompensacion.SoloReembolso, l.Variante));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Otra_referencia_de_la_pasarela_para_el_mismo_intento_alerta_y_no_compensa_ni_completa_de_nuevo()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());
        await e.Aprobar("ch_1");

        var resultado = await e.Aprobar("ch_2");

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Estado);
        Assert.Contains(e.Entorno.Alertas.Publicadas, a => a.Motivo == ConfirmarCompraAprobada.MotivoDeAlertaCargoDuplicado);
        Assert.Empty(e.Compensador.Llamadas);
        Assert.Single(e.Completador.Llamadas);
        Assert.Equal("ch_1", e.Intento.ReferenciaDePasarela);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Repetir_la_aprobacion_de_una_compra_ya_completada_responde_aprobado_sin_llamar_a_nadie()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());
        await e.Aprobar();
        var intencion = e.Intencion;
        intencion.Completar(e.Entorno.Reloj.Ahora);
        await e.Entorno.Repositorio.GuardarIntencion(intencion);

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.Aprobado, resultado.Estado);
        Assert.Single(e.Entorno.Reservas.Confirmaciones);
        Assert.Single(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compra_reclamada_por_otro_ejecutor_no_confirma_ni_completa()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        var aprobada = e.Intencion;
        aprobada.AplicarAprobacion(e.Intento, "ch_1", e.Entorno.Reloj.Ahora);
        aprobada.Reclamar(e.Entorno.Reloj.Ahora);
        await e.Entorno.Repositorio.GuardarIntencion(aprobada);
        var intento = e.Intento;
        await e.Entorno.Repositorio.GuardarIntento(intento);

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Estado);
        Assert.Empty(e.Entorno.Reservas.Confirmaciones);
        Assert.Empty(e.Completador.Llamadas);
        Assert.Empty(e.Compensador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_compensacion_ya_iniciada_se_termina_con_su_motivo_aunque_la_cancelacion_de_la_cuenta_se_revierta()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Cuenta.CancelacionEnCurso = true;
        await e.Aprobar();
        // La compensación quedó iniciada por PAG-32 y la cancelación se revierte.
        await e.Entorno.Repositorio.AgregarCompensacion(Compensacion.Iniciar(Pedido, e.IntentoId, MotivoDeCompensacion.CancelacionDeCuentaEnCurso, VarianteDeCompensacion.Completa, true, e.Entorno.Reloj.Ahora));
        e.Entorno.Cuenta.CancelacionEnCurso = false;
        e.Compensador.Llamadas.Clear();

        await e.Aprobar();

        var llamada = Assert.Single(e.Compensador.Llamadas);
        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, llamada.Motivo);
        Assert.Empty(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Cada_resultado_del_paso_de_completar_se_traduce_a_lo_que_ve_el_cliente_y_a_lo_que_se_hace()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Reservas.Confirmacion = EntornoDeConfirmacion.Confirmadas(EntornoDeConfirmacion.LineaImpresaConfirmada());
        e.Completador.Resultado = new ResultadoDeCompletarCompra(TipoDeCompletar.Compensar, MotivoDeCompensacion.FallaPosteriorAlPago);

        var compensada = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, compensada.Estado);
        Assert.Equal(MotivoDeCompensacion.FallaPosteriorAlPago, Assert.Single(e.Compensador.Llamadas).Motivo);

        e.Compensador.Llamadas.Clear();
        e.Intencion.SoltarReclamacion(); // la compensación real cierra la intención; aquí el doble no la toca.
        e.Completador.Resultado = new ResultadoDeCompletarCompra(TipoDeCompletar.Reintentar);
        var reintento = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, reintento.Estado);
        Assert.Empty(e.Compensador.Llamadas);
        Assert.Null(e.Intencion.ReclamadaHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_falla_inesperada_no_interrumpe_al_llamador_publica_una_alerta_y_deja_la_compra_para_la_retoma()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();
        e.Entorno.Repositorio.BloqueoNoDisponible = true;

        var resultado = await e.Aprobar();

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Estado);
        Assert.Contains(e.Entorno.Alertas.Publicadas, a => a.Motivo == ConfirmarCompraAprobada.MotivoDeAlertaConfirmacionFallida);
        Assert.Empty(e.Completador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Un_intento_que_no_es_de_la_intencion_o_no_existe_no_cambia_nada()
    {
        var e = await EntornoDeConfirmacion.ConIntentoIniciado();

        var resultado = await e.Confirmar.Confirmar(Pedido, Guid.NewGuid(), "ch_1");

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Estado);
        Assert.Equal(EstadoDeIntencion.ConCobro, e.Intencion.Estado);
        Assert.Empty(e.Completador.Llamadas);
    }

    private static async Task Desvincular(EntornoDeConfirmacion e)
    {
        var intencion = (await e.Entorno.Repositorio.ObtenerIntencion(Pedido, false))!;
        intencion.Desvincular(e.Entorno.Reloj.Ahora);
        await e.Entorno.Repositorio.GuardarIntencion(intencion);
    }

    private static void DesvincularSincrono(EntornoDeConfirmacion e)
    {
        var intencion = e.Entorno.Repositorio.ObtenerIntencion(Pedido, false).GetAwaiter().GetResult()!;
        intencion.Desvincular(e.Entorno.Reloj.Ahora);
        e.Entorno.Repositorio.GuardarIntencion(intencion).GetAwaiter().GetResult();
    }
}

public sealed class ResultadoOriginalDelCobroTests
{
    private static async Task<(EntornoDePagos Entorno, CobrarEntrada Entrada)> UnCobroHecho()
    {
        var e = await EntornoDePagosExtensiones.ConIntencionLista();
        var clave = (await e.EmitirClave.Ejecutar(new EmitirClaveDeIntentoEntrada(InstantaneasDeCompraDePrueba.Pedido))).Valor;
        var entrada = new CobrarEntrada(InstantaneasDeCompraDePrueba.Pedido, clave.Token, Application.Pagos.MedioDePago.Yape, TokenDelMedioDePago.Crear("tkn_test_abc123XYZ"));
        await e.Cobrar.Ejecutar(entrada);
        return (e, entrada);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Repetir_la_clave_de_un_cobro_aprobado_cuya_compra_no_se_completo_responde_pendiente_y_no_aprobado()
    {
        var (e, entrada) = await UnCobroHecho();
        var intento = e.Repositorio.Intentos.Single();
        var intencion = e.Repositorio.Intenciones.Single();
        intencion.AplicarAprobacion(intento, "ch_1", e.Reloj.Ahora);
        await e.Repositorio.GuardarIntento(intento);
        await e.Repositorio.GuardarIntencion(intencion);

        var resultado = await e.Cobrar.Ejecutar(entrada);

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Repetir_la_clave_de_un_cobro_aprobado_con_la_compra_completada_responde_aprobado()
    {
        var (e, entrada) = await UnCobroHecho();
        var intento = e.Repositorio.Intentos.Single();
        var intencion = e.Repositorio.Intenciones.Single();
        intencion.AplicarAprobacion(intento, "ch_1", e.Reloj.Ahora);
        intencion.Completar(e.Reloj.Ahora);
        await e.Repositorio.GuardarIntento(intento);
        await e.Repositorio.GuardarIntencion(intencion);

        var resultado = await e.Cobrar.Ejecutar(entrada);

        Assert.Equal(EstadoDelCobro.Aprobado, resultado.Valor.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public async Task Repetir_la_clave_de_un_cobro_sobrante_responde_pendiente_porque_se_reembolsa()
    {
        var (e, entrada) = await UnCobroHecho();
        var intento = e.Repositorio.Intentos.Single();
        var intencion = e.Repositorio.Intenciones.Single();
        intento.MarcarRechazado(MotivoDeRechazo.Otro, e.Reloj.Ahora);
        intencion.ResolverIntentoSinCobro(e.Reloj.Ahora);
        intencion.AplicarAprobacion(intento, "ch_1", e.Reloj.Ahora);
        await e.Repositorio.GuardarIntento(intento);
        await e.Repositorio.GuardarIntencion(intencion);

        var resultado = await e.Cobrar.Ejecutar(entrada);

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, resultado.Valor.Estado);
    }
}
