using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

public sealed class RegistrarAvisoDeLaPasarelaTests
{
    private sealed class Entorno
    {
        public required EntornoDeConfirmacion Base { get; init; }

        public required RegistrarAvisoDeLaPasarela Registrar { get; init; }

        public EntornoDePagos Pagos => Base.Entorno;

        public IntentoDeCobro Intento => Base.Intento;

        public AvisoDeLaPasarelaEntrada Aviso(string cuerpo = "evento") => new(System.Text.Encoding.UTF8.GetBytes(cuerpo), "1763460000", "firma");
    }

    private static async Task<Entorno> Nuevo(string idDeEntrada = "evt_1")
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado();
        var p = b.Entorno;
        p.Pasarela.Interprete = _ => new AvisoDeLaPasarela(idDeEntrada, b.IntentoId);
        var registrar = new RegistrarAvisoDeLaPasarela(p.Procesador, p.Pasarela, p.Repositorio, p.Unidad, new BloqueoDeIntenciones(p.Repositorio, p.Cuenta), p.Auditoria, p.Reloj);
        return new Entorno { Base = b, Registrar = registrar };
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aviso_firmado_marca_la_consulta_inmediata_audita_y_no_llama_a_la_pasarela()
    {
        var e = await Nuevo();

        var resultado = await e.Registrar.Recibir(e.Aviso());

        Assert.True(resultado.EsExito);
        Assert.True(e.Intento.ConsultaInmediata);
        Assert.Equal("AvisoDeLaPasarelaRecibido", Assert.Single(e.Pagos.Auditoria.Solicitudes).Accion.Identificador);
        Assert.Empty(e.Pagos.Pasarela.Consultas);
        Assert.Empty(e.Pagos.Pasarela.Solicitudes);
        Assert.Equal(["evt_1"], e.Pagos.Procesador.Completadas);
        Assert.Equal(RegistrarAvisoDeLaPasarela.Proveedor, e.Pagos.Procesador.Recibidas.Single().Proveedor);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aviso_con_firma_falsa_se_rechaza_sin_ningun_efecto_ni_llamada_de_red()
    {
        var e = await Nuevo();
        e.Pagos.Procesador.FirmaValida = false;

        var resultado = await e.Registrar.Recibir(e.Aviso());

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.False(e.Intento.ConsultaInmediata);
        Assert.Empty(e.Pagos.Auditoria.Solicitudes);
        Assert.Empty(e.Pagos.Pasarela.Consultas);
        Assert.Empty(e.Pagos.Procesador.Completadas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_cuerpo_que_no_se_entiende_se_rechaza_de_forma_uniforme()
    {
        var e = await Nuevo();
        e.Pagos.Pasarela.Interprete = _ => null;

        var resultado = await e.Registrar.Recibir(e.Aviso("basura"));

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
        Assert.False(e.Intento.ConsultaInmediata);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_interprete_que_lanza_se_trata_como_un_cuerpo_que_no_se_entiende()
    {
        var e = await Nuevo();
        e.Pagos.Pasarela.Interprete = _ => throw new FormatException("json roto");

        var resultado = await e.Registrar.Recibir(e.Aviso("{"));

        Assert.Equal(CategoriaDeError.NoAutenticado, resultado.Error!.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Cincuenta_repeticiones_del_mismo_aviso_hacen_el_efecto_una_sola_vez()
    {
        var e = await Nuevo();

        for (var i = 0; i < 50; i++)
        {
            Assert.True((await e.Registrar.Recibir(e.Aviso())).EsExito);
        }

        Assert.Single(e.Pagos.Auditoria.Solicitudes);
        Assert.Empty(e.Pagos.Pasarela.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_cargo_desconocido_se_acepta_sin_efecto()
    {
        var e = await Nuevo();
        e.Pagos.Pasarela.Interprete = _ => new AvisoDeLaPasarela("evt_9", Guid.NewGuid());

        var resultado = await e.Registrar.Recibir(e.Aviso());

        Assert.True(resultado.EsExito);
        Assert.False(e.Intento.ConsultaInmediata);
        Assert.Empty(e.Pagos.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_intento_resuelto_hace_mas_de_24_horas_ya_no_se_marca()
    {
        var e = await Nuevo();
        var intento = e.Intento;
        intento.MarcarRechazado(MotivoDeRechazo.Otro, e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntento(intento);
        e.Pagos.Reloj.Ahora = e.Pagos.Reloj.Ahora.AddHours(25);

        var resultado = await e.Registrar.Recibir(e.Aviso());

        Assert.True(resultado.EsExito);
        Assert.False(e.Intento.ConsultaInmediata);
        Assert.Empty(e.Pagos.Auditoria.Solicitudes);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Si_la_auditoria_cae_responde_no_disponible_libera_el_reclamo_y_el_reenvio_funciona()
    {
        var e = await Nuevo();
        e.Pagos.Auditoria.Caida = true;

        var primero = await e.Registrar.Recibir(e.Aviso());

        Assert.Equal(CategoriaDeError.NoDisponible, primero.Error!.Categoria);
        Assert.False(e.Intento.ConsultaInmediata);
        Assert.Empty(e.Pagos.Procesador.Completadas);

        e.Pagos.Auditoria.Caida = false;
        var reenvio = await e.Registrar.Recibir(e.Aviso());

        Assert.True(reenvio.EsExito);
        Assert.True(e.Intento.ConsultaInmediata);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Otro_aviso_con_el_mismo_identificador_en_curso_responde_no_disponible()
    {
        var e = await Nuevo();
        e.Pagos.Procesador.EnCurso = true;

        var resultado = await e.Registrar.Recibir(e.Aviso());

        Assert.Equal(CategoriaDeError.NoDisponible, resultado.Error!.Categoria);
        Assert.False(e.Intento.ConsultaInmediata);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task La_marca_de_la_cuenta_se_lee_antes_de_bloquear_la_intencion()
    {
        var e = await Nuevo();
        e.Pagos.Orden.Clear();

        await e.Registrar.Recibir(e.Aviso());

        Assert.Equal(["marca-con-bloqueo", "intencion-con-bloqueo"], e.Pagos.Orden.Take(2).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task El_texto_del_aviso_no_repite_el_cuerpo_ni_la_firma()
    {
        var aviso = new AvisoDeLaPasarelaEntrada(System.Text.Encoding.UTF8.GetBytes("secreto-del-cuerpo"), "1763460000", "firma-secreta");

        var texto = aviso.ToString();

        Assert.DoesNotContain("secreto", texto);
        Assert.DoesNotContain("firma-secreta", texto);
        await Task.CompletedTask;
    }
}

public sealed class ConsultarPagosPendientesTests
{
    private sealed class Entorno
    {
        public required EntornoDeConfirmacion Base { get; init; }

        public required ConsultarPagosPendientes Consultar { get; init; }

        public EntornoDePagos Pagos => Base.Entorno;

        public IntentoDeCobro Intento => Base.Intento;

        public IntencionDePago Intencion => Base.Intencion;

        public Task<int> Correr() => Consultar.Ejecutar(50);

        public void Avanzar(TimeSpan tiempo) => Pagos.Reloj.Ahora = Pagos.Reloj.Ahora.Add(tiempo);

        public ResultadoDeCobro Aprobado(string referencia = "ch_1") => ResultadoDeCobro.Aprobado(referencia, Intento.Importe, "PEN", Intento.ReferenciaOpaca);
    }

    private static async Task<Entorno> Nuevo(bool pendiente, bool soloDigital = false)
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado(soloDigital);
        var p = b.Entorno;
        if (pendiente)
        {
            var intento = b.Intento;
            intento.MarcarPendiente(p.Reloj.Ahora);
            b.Intencion.PasarAPendiente();
            await p.Repositorio.GuardarIntento(intento);
            await p.Repositorio.GuardarIntencion(b.Intencion);
        }

        var bloqueo = new BloqueoDeIntenciones(p.Repositorio, p.Cuenta);
        var consultar = new ConsultarPagosPendientes(
            p.Repositorio, p.Unidad, bloqueo, p.Pasarela, p.Confirmador, new ExtenderReservaDelPago(p.Repositorio, p.Unidad, p.Cuenta, p.Reservas), p.Avisador, p.Auditoria, p.Alertas, p.Reloj, new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
        return new Entorno { Base = b, Consultar = consultar };
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Un_pendiente_se_consulta_y_si_la_pasarela_lo_aprobo_lo_aprueba_el_unico_punto_de_entrada()
    {
        var e = await Nuevo(pendiente: true);
        e.Pagos.Pasarela.RespuestaDeConsulta = _ => e.Aprobado();

        var revisados = await e.Correr();

        Assert.Equal(1, revisados);
        var llamada = Assert.Single(e.Pagos.Confirmador.Llamadas);
        Assert.Equal((InstantaneasDeCompraDePrueba.Pedido, e.Base.IntentoId, "ch_1"), llamada);
        Assert.Equal(1, e.Intento.Consultas);
        Assert.Equal(0, e.Pagos.Pasarela.ConsultasDentroDeUnaTransaccion);
        Assert.Equal(0, e.Pagos.Confirmador.LlamadasDentroDeUnaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Un_pendiente_se_consulta_cada_minuto_y_no_antes()
    {
        var e = await Nuevo(pendiente: true);

        await e.Correr();
        e.Avanzar(TimeSpan.FromSeconds(30));
        await e.Correr();
        e.Avanzar(TimeSpan.FromSeconds(31));
        await e.Correr();

        Assert.Equal(2, e.Pagos.Pasarela.Consultas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task El_aviso_entrante_adelanta_la_consulta_aunque_no_pasara_un_minuto()
    {
        var e = await Nuevo(pendiente: true);
        await e.Correr();
        e.Avanzar(TimeSpan.FromSeconds(5));
        var intento = e.Intento;
        intento.MarcarConsultaInmediata();
        await e.Pagos.Repositorio.GuardarIntento(intento);

        await e.Correr();

        Assert.Equal(2, e.Pagos.Pasarela.Consultas.Count);
        Assert.False(e.Intento.ConsultaInmediata);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Un_rechazo_en_la_consulta_cierra_el_intento_avisa_y_deja_la_intencion_abierta_con_la_reserva_viva()
    {
        var e = await Nuevo(pendiente: true);
        e.Pagos.Pasarela.RespuestaDeConsulta = _ => ResultadoDeCobro.Rechazado(MotivoDeRechazo.FondosInsuficientes);

        await e.Correr();

        Assert.Equal(EstadoDeIntento.Rechazado, e.Intento.Estado);
        Assert.Equal(EstadoDeIntencion.Abierta, e.Intencion.Estado);
        Assert.Contains(e.Pagos.Repositorio.AvisosACarrito, a => a.Transicion == TransicionDeAviso.Rechazado);
        Assert.Contains(e.Pagos.Repositorio.AvisosDePago, a => a.Evento == EventoDeAvisoDePago.Rechazado);
        Assert.Single(e.Pagos.Avisador.Enviados);
        Assert.Equal("fondos_insuficientes", e.Pagos.Auditoria.Solicitudes.Single().Detalle["motivo"]);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Sin_respuesta_de_la_pasarela_un_pendiente_sigue_pendiente()
    {
        var e = await Nuevo(pendiente: true);
        e.Pagos.Pasarela.ConsultaLanza = true;

        await e.Correr();

        Assert.Equal(EstadoDeIntento.PendienteDeConfirmacion, e.Intento.Estado);
        Assert.Empty(e.Pagos.Confirmador.Llamadas);
        Assert.Equal(1, e.Intento.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_iniciado_sin_resultado_a_los_15_segundos_pasa_a_pendiente_y_nunca_a_rechazado_por_no_hallar_el_cargo()
    {
        var e = await Nuevo(pendiente: false);
        e.Avanzar(TimeSpan.FromSeconds(16));

        await e.Correr();

        Assert.Equal(EstadoDeIntento.PendienteDeConfirmacion, e.Intento.Estado);
        Assert.NotEqual(EstadoDeIntento.Rechazado, e.Intento.Estado);
        Assert.Equal(EstadoDeIntencion.ConCobro, e.Intencion.Estado);
        Assert.Contains(e.Pagos.Repositorio.AvisosACarrito, a => a.Transicion == TransicionDeAviso.Pendiente && a.Estado == EstadoDeAvisoACarrito.Retenido);
        Assert.Contains(e.Pagos.Repositorio.AvisosDePago, a => a.Evento == EventoDeAvisoDePago.Pendiente);
        Assert.Equal("pendiente", e.Pagos.Auditoria.Solicitudes.Single().Detalle["resultado"]);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Al_convertir_un_iniciado_a_pendiente_se_extiende_la_reserva_y_se_libera_el_aviso_retenido()
    {
        var e = await Nuevo(pendiente: false);
        e.Avanzar(TimeSpan.FromSeconds(16));

        await e.Correr();

        Assert.Single(e.Pagos.Reservas.Extensiones);
        Assert.True(e.Intencion.ReservaExtendida);
        Assert.Equal(e.Base.IntentoId, Assert.Single(e.Pagos.Avisador.Liberados).IntentoId);
        Assert.Equal(0, e.Pagos.Reservas.LlamadasDentroDeUnaTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_iniciado_a_menos_de_15_segundos_aun_esta_en_vuelo_y_no_se_toca()
    {
        var e = await Nuevo(pendiente: false);
        e.Avanzar(TimeSpan.FromSeconds(5));

        await e.Correr();

        Assert.Equal(EstadoDeIntento.Iniciado, e.Intento.Estado);
        Assert.Empty(e.Pagos.Pasarela.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_iniciado_que_la_pasarela_dice_haber_rechazado_se_rechaza()
    {
        var e = await Nuevo(pendiente: false);
        e.Avanzar(TimeSpan.FromSeconds(16));
        e.Pagos.Pasarela.RespuestaDeConsulta = _ => ResultadoDeCobro.Rechazado(MotivoDeRechazo.TarjetaRechazada);

        await e.Correr();

        Assert.Equal(EstadoDeIntento.Rechazado, e.Intento.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Un_pago_solo_digital_pendiente_vence_a_los_30_minutos_tras_consultarlo_cada_minuto()
    {
        var e = await Nuevo(pendiente: true, soloDigital: true);

        for (var minuto = 0; minuto < 31; minuto++)
        {
            await e.Correr();
            e.Avanzar(TimeSpan.FromMinutes(1));
        }

        Assert.Equal(EstadoDeIntento.Vencido, e.Intento.Estado);
        Assert.True(e.Pagos.Pasarela.Consultas.Count >= 29);
        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, e.Intencion.Estado);
        Assert.Contains(e.Pagos.Repositorio.AvisosACarrito, a => a.Transicion == TransicionDeAviso.Vencido);
        Assert.Contains(e.Pagos.Repositorio.AvisosDePago, a => a.Evento == EventoDeAvisoDePago.Vencido);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Un_aprobado_que_llega_justo_en_el_limite_se_consulta_antes_de_vencer()
    {
        var e = await Nuevo(pendiente: true, soloDigital: true);
        e.Avanzar(TimeSpan.FromMinutes(31));
        e.Pagos.Pasarela.RespuestaDeConsulta = _ => e.Aprobado();

        await e.Correr();

        Assert.Single(e.Pagos.Confirmador.Llamadas);
        Assert.NotEqual(EstadoDeIntento.Vencido, e.Intento.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Si_la_primera_extension_fallo_el_pendiente_la_reextiende_y_libera_el_aviso_retenido()
    {
        var e = await Nuevo(pendiente: true);
        await e.Pagos.Repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(InstantaneasDeCompraDePrueba.Pedido, e.Base.IntentoId, TransicionDeAviso.Pendiente, retenido: true, e.Pagos.Reloj.Ahora));
        e.Avanzar(TimeSpan.FromMinutes(2));

        await e.Correr();

        Assert.Single(e.Pagos.Reservas.Extensiones);
        Assert.Single(e.Pagos.Avisador.Liberados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Un_aprobado_dudoso_publica_una_alerta_y_no_aprueba()
    {
        var e = await Nuevo(pendiente: true);
        e.Pagos.Pasarela.RespuestaDeConsulta = _ => ResultadoDeCobro.Aprobado("ch_1", Importe.De(1m), "PEN", e.Intento.ReferenciaOpaca);

        await e.Correr();

        Assert.Empty(e.Pagos.Confirmador.Llamadas);
        Assert.Equal(EstadoDeIntento.PendienteDeConfirmacion, e.Intento.Estado);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == ConsultarPagosPendientes.MotivoDeAlertaCobroDudoso);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Un_intento_rechazado_sin_aviso_no_se_consulta()
    {
        var e = await Nuevo(pendiente: true);
        var intento = e.Intento;
        intento.MarcarRechazado(MotivoDeRechazo.Otro, e.Pagos.Reloj.Ahora);
        await e.Pagos.Repositorio.GuardarIntento(intento);

        var revisados = await e.Correr();

        Assert.Equal(1, revisados);
        Assert.Empty(e.Pagos.Pasarela.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Una_falla_con_un_intento_publica_una_alerta_y_no_detiene_la_corrida()
    {
        var e = await Nuevo(pendiente: true);
        e.Pagos.Pasarela.RespuestaDeConsulta = _ => ResultadoDeCobro.Rechazado(MotivoDeRechazo.Otro);
        e.Pagos.Repositorio.BloqueoNoDisponible = true;

        var revisados = await e.Correr();

        Assert.Equal(1, revisados);
        Assert.Contains(e.Pagos.Alertas.Publicadas, a => a.Motivo == ConsultarPagosPendientes.MotivoDeAlertaConsultaFallida);
        Assert.Equal(EstadoDeIntento.PendienteDeConfirmacion, e.Intento.Estado);
    }
}

public sealed class CobrosAprobadosTardeTests
{
    private static async Task<(EntornoDeConfirmacion B, ConsultarPagosPendientes Consultar, RegistrarAvisoDeLaPasarela Aviso)> VencidoSoloDigital()
    {
        var b = await EntornoDeConfirmacion.ConIntentoIniciado(soloDigital: true);
        var p = b.Entorno;
        var intento = b.Intento;
        intento.MarcarPendiente(p.Reloj.Ahora);
        b.Intencion.PasarAPendiente();
        await p.Repositorio.GuardarIntento(intento);
        await p.Repositorio.GuardarIntencion(b.Intencion);
        p.Pasarela.Interprete = _ => new AvisoDeLaPasarela("evt_1", b.IntentoId);
        var bloqueo = new BloqueoDeIntenciones(p.Repositorio, p.Cuenta);
        var consultar = new ConsultarPagosPendientes(p.Repositorio, p.Unidad, bloqueo, p.Pasarela, p.Confirmador, new ExtenderReservaDelPago(p.Repositorio, p.Unidad, p.Cuenta, p.Reservas), p.Avisador, p.Auditoria, p.Alertas, p.Reloj, new OpcionesDelCobro(TimeSpan.FromMilliseconds(300)));
        var aviso = new RegistrarAvisoDeLaPasarela(p.Procesador, p.Pasarela, p.Repositorio, p.Unidad, bloqueo, p.Auditoria, p.Reloj);
        // El trabajo vence el intento a los 30 minutos.
        p.Reloj.Ahora = IntencionesDePrueba.Inicio.AddMinutes(31);
        await consultar.Ejecutar(10);
        Assert.Equal(EstadoDeIntento.Vencido, b.Intento.Estado);
        return (b, consultar, aviso);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_cobro_que_la_pasarela_aprueba_tras_vencer_el_intento_entra_como_sobrante_por_el_unico_punto_de_entrada()
    {
        var (b, consultar, aviso) = await VencidoSoloDigital();
        var p = b.Entorno;
        p.Reloj.Ahora = p.Reloj.Ahora.AddMinutes(2);
        p.Pasarela.RespuestaDeConsulta = _ => ResultadoDeCobro.Aprobado("ch_tarde", b.Intento.Importe, "PEN", b.Intento.ReferenciaOpaca);
        p.Confirmador.Llamadas.Clear();

        await aviso.Recibir(new AvisoDeLaPasarelaEntrada(System.Text.Encoding.UTF8.GetBytes("evento"), "1763460000", "firma"));
        await consultar.Ejecutar(10);

        Assert.Equal((InstantaneasDeCompraDePrueba.Pedido, b.IntentoId, "ch_tarde"), Assert.Single(p.Confirmador.Llamadas));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Tras_vencer_el_trabajo_sigue_consultando_cada_minuto_15_minutos_y_despues_solo_si_el_aviso_lo_adelanta()
    {
        var (b, consultar, _) = await VencidoSoloDigital();
        var p = b.Entorno;
        var consultasAlVencer = p.Pasarela.Consultas.Count;

        p.Reloj.Ahora = p.Reloj.Ahora.AddMinutes(2);
        await consultar.Ejecutar(10);
        Assert.Equal(consultasAlVencer + 1, p.Pasarela.Consultas.Count);

        p.Reloj.Ahora = p.Reloj.Ahora.AddMinutes(20);
        await consultar.Ejecutar(10);
        Assert.Equal(consultasAlVencer + 1, p.Pasarela.Consultas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task El_aviso_adelanta_la_consulta_de_un_intento_vencido_dentro_de_las_24_horas_y_no_despues()
    {
        var (b, consultar, aviso) = await VencidoSoloDigital();
        var p = b.Entorno;
        p.Reloj.Ahora = p.Reloj.Ahora.AddHours(3);

        await aviso.Recibir(new AvisoDeLaPasarelaEntrada(System.Text.Encoding.UTF8.GetBytes("evento"), "1763460000", "firma"));
        Assert.True(b.Intento.ConsultaInmediata);
        var antes = p.Pasarela.Consultas.Count;
        await consultar.Ejecutar(10);
        Assert.Equal(antes + 1, p.Pasarela.Consultas.Count);

        p.Reloj.Ahora = p.Reloj.Ahora.AddHours(25);
        p.Pasarela.Interprete = _ => new AvisoDeLaPasarela("evt_2", b.IntentoId);
        await aviso.Recibir(new AvisoDeLaPasarelaEntrada(System.Text.Encoding.UTF8.GetBytes("evento"), "1763460000", "firma"));
        Assert.False(b.Intento.ConsultaInmediata);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_rechazo_en_la_pasarela_nunca_se_vuelve_a_consultar_sin_aviso_ni_cambia_el_intento_resuelto()
    {
        var (b, consultar, _) = await VencidoSoloDigital();
        var p = b.Entorno;
        p.Reloj.Ahora = p.Reloj.Ahora.AddMinutes(2);
        p.Pasarela.RespuestaDeConsulta = _ => ResultadoDeCobro.Rechazado(MotivoDeRechazo.Otro);

        await consultar.Ejecutar(10);

        Assert.Equal(EstadoDeIntento.Vencido, b.Intento.Estado);
        Assert.Empty(p.Confirmador.Llamadas);
    }
}
