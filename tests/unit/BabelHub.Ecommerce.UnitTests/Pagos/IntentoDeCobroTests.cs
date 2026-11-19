using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.IntencionesDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>El intento de cobro: su clave, sus estados y sus transiciones condicionales (T03; PAG-06, PAG-07, PAG-11, PAG-16, PAG-17, PAG-18).</summary>
public sealed class IntentoDeCobroTests
{
    private static readonly Guid IntentoId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static IntentoDeCobro Nuevo(MedioDePago medio = MedioDePago.Yape) =>
        IntentoDeCobro.Iniciar(IntentoId, PedidoId, medio, Importe.De(140m), Inicio.AddSeconds(30));

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public void Un_intento_nace_iniciado_con_su_clave_como_referencia_opaca_y_sin_datos_del_medio()
    {
        var intento = Nuevo(MedioDePago.Tarjeta);

        Assert.Equal(IntentoId, intento.Id);
        Assert.Equal(IntentoId, intento.ReferenciaOpaca);
        Assert.Equal(PedidoId, intento.IntencionId);
        Assert.Equal(MedioDePago.Tarjeta, intento.Medio);
        Assert.Equal(EstadoDeIntento.Iniciado, intento.Estado);
        Assert.Equal(Importe.De(140m), intento.Importe);
        Assert.Equal(Inicio.AddSeconds(30), intento.IniciadoEn);
        Assert.True(intento.EsActivo);
        Assert.Null(intento.ReferenciaDePasarela);
        Assert.Null(intento.MotivoDeRechazo);
        Assert.Equal(0, intento.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public void Un_intento_necesita_su_clave_su_intencion_y_un_importe_mayor_que_cero()
    {
        Assert.Throws<ArgumentException>(() => IntentoDeCobro.Iniciar(Guid.Empty, PedidoId, MedioDePago.Yape, Importe.De(1m), Inicio));
        Assert.Throws<ArgumentException>(() => IntentoDeCobro.Iniciar(IntentoId, Guid.Empty, MedioDePago.Yape, Importe.De(1m), Inicio));
        Assert.Throws<ArgumentException>(() => IntentoDeCobro.Iniciar(IntentoId, PedidoId, MedioDePago.Yape, Importe.Cero, Inicio));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-09")]
    public void Un_rechazo_guarda_solo_el_motivo_de_la_lista_cerrada_y_deja_de_estar_activo()
    {
        var intento = Nuevo();

        Assert.True(intento.MarcarRechazado(MotivoDeRechazo.FondosInsuficientes, Inicio.AddSeconds(35)));

        Assert.Equal(EstadoDeIntento.Rechazado, intento.Estado);
        Assert.Equal(MotivoDeRechazo.FondosInsuficientes, intento.MotivoDeRechazo);
        Assert.Equal(Inicio.AddSeconds(35), intento.ResueltoEn);
        Assert.False(intento.EsActivo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public void Sin_respuesta_en_15_segundos_el_intento_iniciado_pasa_a_pendiente_nunca_a_rechazado()
    {
        var intento = Nuevo();

        Assert.False(intento.DebeResolverseSinRespuesta(Inicio.AddSeconds(30).AddSeconds(14)));
        Assert.True(intento.DebeResolverseSinRespuesta(Inicio.AddSeconds(30).AddSeconds(15)));

        Assert.True(intento.MarcarPendiente(Inicio.AddSeconds(45)));

        Assert.Equal(EstadoDeIntento.PendienteDeConfirmacion, intento.Estado);
        Assert.True(intento.EsActivo);
        Assert.False(intento.DebeResolverseSinRespuesta(Inicio.AddMinutes(5)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public void El_instante_de_envio_a_la_pasarela_se_anota_una_sola_vez_y_el_intento_puede_quedar_pendiente()
    {
        var intento = Nuevo();

        intento.MarcarEnviado(Inicio.AddSeconds(31));
        intento.MarcarEnviado(Inicio.AddSeconds(40));

        Assert.Equal(Inicio.AddSeconds(31), intento.EnviadoEn);
        Assert.True(intento.MarcarPendiente(Inicio.AddSeconds(41)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public void Un_resultado_tardio_no_deshace_un_aprobado()
    {
        var intento = Nuevo();
        Assert.Equal(ResultadoDeAprobacion.Aprobado, intento.AplicarAprobacion("ch_1", Inicio.AddMinutes(1), laIntencionLaAcepta: true));

        Assert.False(intento.MarcarPendiente(Inicio.AddMinutes(2)));
        Assert.False(intento.MarcarRechazado(MotivoDeRechazo.Otro, Inicio.AddMinutes(2)));
        Assert.False(intento.Vencer(Inicio.AddMinutes(40)));

        Assert.Equal(EstadoDeIntento.Aprobado, intento.Estado);
        Assert.Equal("ch_1", intento.ReferenciaDePasarela);
        Assert.Null(intento.MotivoDeRechazo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public void Un_pendiente_se_aprueba_se_rechaza_o_vence_y_cada_transicion_es_condicional()
    {
        var aprobado = Nuevo();
        aprobado.MarcarPendiente(Inicio.AddSeconds(45));
        Assert.Equal(ResultadoDeAprobacion.Aprobado, aprobado.AplicarAprobacion("ch_2", Inicio.AddMinutes(3), laIntencionLaAcepta: true));

        var rechazado = Nuevo();
        rechazado.MarcarPendiente(Inicio.AddSeconds(45));
        Assert.True(rechazado.MarcarRechazado(MotivoDeRechazo.TarjetaRechazada, Inicio.AddMinutes(3)));
        Assert.False(rechazado.Vencer(Inicio.AddMinutes(31)));

        var vencido = Nuevo();
        vencido.MarcarPendiente(Inicio.AddSeconds(45));
        Assert.True(vencido.Vencer(Inicio.AddMinutes(30)));
        Assert.Equal(EstadoDeIntento.Vencido, vencido.Estado);
        Assert.False(vencido.EsActivo);
        Assert.False(vencido.MarcarRechazado(MotivoDeRechazo.Otro, Inicio.AddMinutes(31)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public void Un_pendiente_vence_a_los_30_minutos_del_inicio_de_la_intencion_y_no_antes()
    {
        var intencion = Abierta();
        Assert.Equal(ValidacionDelCobro.Permitido, intencion.IniciarCobro(Inicio.AddSeconds(30)));
        var intento = Nuevo();
        intento.MarcarPendiente(Inicio.AddSeconds(45));
        intencion.PasarAPendiente();

        Assert.False(intento.DebeVencer(intencion.LimitePendienteEn, Inicio.AddMinutes(30).AddTicks(-1)));
        Assert.True(intento.DebeVencer(intencion.LimitePendienteEn, Inicio.AddMinutes(30)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_de_un_intento_rechazado_o_vencido_es_un_cobro_sobrante_fuera_del_intento_activo()
    {
        var rechazado = Nuevo();
        rechazado.MarcarRechazado(MotivoDeRechazo.Otro, Inicio.AddSeconds(35));
        var vencido = Nuevo();
        vencido.Vencer(Inicio.AddMinutes(31));

        Assert.Equal(ResultadoDeAprobacion.Sobrante, rechazado.AplicarAprobacion("ch_3", Inicio.AddMinutes(32), laIntencionLaAcepta: true));
        Assert.Equal(ResultadoDeAprobacion.Sobrante, vencido.AplicarAprobacion("ch_4", Inicio.AddMinutes(32), laIntencionLaAcepta: true));

        Assert.Equal(EstadoDeIntento.AprobadoSobrante, rechazado.Estado);
        Assert.Equal(EstadoDeIntento.AprobadoSobrante, vencido.Estado);
        Assert.False(rechazado.EsActivo);
        Assert.False(vencido.EsActivo);
        Assert.Equal("ch_3", rechazado.ReferenciaDePasarela);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_de_un_intento_vivo_cuya_intencion_ya_no_lo_acepta_es_sobrante()
    {
        var intento = Nuevo();

        Assert.Equal(ResultadoDeAprobacion.Sobrante, intento.AplicarAprobacion("ch_5", Inicio.AddMinutes(20), laIntencionLaAcepta: false));

        Assert.Equal(EstadoDeIntento.AprobadoSobrante, intento.Estado);
        Assert.False(intento.EsActivo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Repetir_la_aprobacion_es_idempotente_y_no_cambia_la_referencia()
    {
        var intento = Nuevo();
        intento.AplicarAprobacion("ch_6", Inicio.AddMinutes(1), laIntencionLaAcepta: true);

        Assert.Equal(ResultadoDeAprobacion.YaAprobado, intento.AplicarAprobacion("ch_6", Inicio.AddMinutes(2), laIntencionLaAcepta: true));
        Assert.Equal("ch_6", intento.ReferenciaDePasarela);
        Assert.Equal(Inicio.AddMinutes(1), intento.ResueltoEn);

        var sobrante = Nuevo();
        sobrante.Vencer(Inicio.AddMinutes(31));
        sobrante.AplicarAprobacion("ch_7", Inicio.AddMinutes(32), laIntencionLaAcepta: true);
        Assert.Equal(ResultadoDeAprobacion.YaAprobado, sobrante.AplicarAprobacion("ch_7", Inicio.AddMinutes(33), laIntencionLaAcepta: true));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void Un_segundo_cargo_aprobado_con_otra_referencia_para_el_mismo_intento_se_senala_como_duplicado_y_no_cambia_el_primero()
    {
        var intento = Nuevo();
        intento.AplicarAprobacion("ch_1", Inicio.AddMinutes(1), laIntencionLaAcepta: true);

        Assert.Equal(ResultadoDeAprobacion.CargoDuplicado, intento.AplicarAprobacion("ch_2", Inicio.AddMinutes(2), laIntencionLaAcepta: true));

        Assert.Equal("ch_1", intento.ReferenciaDePasarela);
        Assert.Equal(EstadoDeIntento.Aprobado, intento.Estado);

        var sobrante = Nuevo();
        sobrante.Vencer(Inicio.AddMinutes(31));
        sobrante.AplicarAprobacion("ch_3", Inicio.AddMinutes(32), laIntencionLaAcepta: true);
        Assert.Equal(ResultadoDeAprobacion.CargoDuplicado, sobrante.AplicarAprobacion("ch_4", Inicio.AddMinutes(33), laIntencionLaAcepta: true));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void La_consulta_de_un_pendiente_corre_cada_minuto_y_el_aviso_la_adelanta()
    {
        var intento = Nuevo();
        intento.MarcarPendiente(Inicio.AddSeconds(45));

        Assert.True(intento.DebeConsultar(Inicio.AddSeconds(45)));
        intento.RegistrarConsulta(Inicio.AddSeconds(45));
        Assert.Equal(1, intento.Consultas);
        Assert.False(intento.DebeConsultar(Inicio.AddSeconds(45).AddSeconds(59)));
        Assert.True(intento.DebeConsultar(Inicio.AddSeconds(45).AddMinutes(1)));

        Assert.True(intento.MarcarConsultaInmediata());
        Assert.True(intento.ConsultaInmediata);
        Assert.True(intento.DebeConsultar(Inicio.AddSeconds(46)));
        intento.RegistrarConsulta(Inicio.AddSeconds(46));
        Assert.False(intento.ConsultaInmediata);
        Assert.Equal(2, intento.Consultas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public void Un_intento_en_vuelo_no_se_consulta_antes_de_los_15_segundos_salvo_que_el_aviso_lo_adelante()
    {
        var intento = Nuevo();

        Assert.False(intento.DebeConsultar(Inicio.AddSeconds(30).AddSeconds(5)));
        Assert.True(intento.DebeConsultar(Inicio.AddSeconds(30).AddSeconds(15)));

        var conAviso = Nuevo();
        conAviso.MarcarConsultaInmediata();
        Assert.True(conAviso.DebeConsultar(Inicio.AddSeconds(31)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void Un_aviso_sobre_un_intento_resuelto_no_adelanta_ninguna_consulta()
    {
        var intento = Nuevo();
        intento.MarcarRechazado(MotivoDeRechazo.Otro, Inicio.AddSeconds(35));

        Assert.False(intento.MarcarConsultaInmediata());
        Assert.False(intento.ConsultaInmediata);
        Assert.False(intento.DebeConsultar(Inicio.AddHours(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public void Un_pendiente_rechazado_con_la_reserva_viva_deja_la_intencion_abierta_y_el_cliente_puede_reintentar()
    {
        var intencion = Abierta();
        intencion.IniciarCobro(Inicio.AddSeconds(30));
        var intento = Nuevo();
        intento.MarcarPendiente(Inicio.AddSeconds(45));
        intencion.PasarAPendiente();
        intencion.ExtenderReserva(Inicio.AddMinutes(30));

        Assert.True(intento.MarcarRechazado(MotivoDeRechazo.TarjetaRechazada, Inicio.AddMinutes(20)));
        Assert.Equal(EstadoDeIntencion.Abierta, intencion.ResolverIntentoSinCobro(Inicio.AddMinutes(20)));

        Assert.Equal(ValidacionDelCobro.Permitido, intencion.ValidarCobro(Inicio.AddMinutes(20)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public void Un_pendiente_solo_digital_vencido_a_los_30_minutos_cierra_la_intencion_sin_cobro()
    {
        var intencion = AbiertaSoloDigital();
        intencion.IniciarCobro(Inicio.AddSeconds(30));
        var intento = Nuevo();
        intento.MarcarPendiente(Inicio.AddSeconds(45));
        intencion.PasarAPendiente();

        Assert.True(intento.Vencer(Inicio.AddMinutes(30)));
        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, intencion.ResolverIntentoSinCobro(Inicio.AddMinutes(30)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-09")]
    public void El_texto_de_un_intento_no_muestra_el_motivo_ni_datos_del_medio()
    {
        var intento = Nuevo(MedioDePago.Tarjeta);
        intento.MarcarRechazado(MotivoDeRechazo.TarjetaRechazada, Inicio.AddSeconds(35));

        var texto = intento.ToString();

        Assert.Equal($"IntentoDeCobro {{ Id = {IntentoId}, Estado = Rechazado }}", texto);
        Assert.DoesNotContain("TarjetaRechazada", texto);
    }
}
