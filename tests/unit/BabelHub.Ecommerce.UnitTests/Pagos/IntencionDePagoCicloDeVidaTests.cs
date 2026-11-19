using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.IntencionesDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>
/// El ciclo de vida de la intención tras iniciar el cobro: pendiente, rechazo, aprobación, reclamación, completarse o compensarse, la desvinculación de la cuenta y la
/// liberación del contacto (T02; PAG-07, PAG-18, PAG-20, PAG-26, PAG-27, D23, D24).
/// </summary>
public sealed class IntencionDePagoCicloDeVidaTests
{
    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public void Un_pago_pendiente_lleva_la_vigencia_de_la_intencion_al_limite_de_30_minutos()
    {
        var intencion = ConCobro();
        Assert.Equal(Inicio.AddMinutes(15), intencion.VenceEn);

        intencion.PasarAPendiente();

        Assert.Equal(Inicio.AddMinutes(30), intencion.VenceEn);
        Assert.Equal(EstadoDeIntencion.ConCobro, intencion.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public void La_reserva_se_extiende_una_sola_vez_hasta_30_minutos_desde_el_inicio()
    {
        var intencion = ConCobro();
        intencion.PasarAPendiente();

        Assert.True(intencion.ExtenderReserva(Inicio.AddMinutes(30)));

        Assert.Equal(Inicio.AddMinutes(30), intencion.ReservaExpiraEn);
        Assert.True(intencion.ReservaExtendida);
        Assert.False(intencion.ExtenderReserva(Inicio.AddMinutes(45)));
        Assert.Equal(Inicio.AddMinutes(30), intencion.ReservaExpiraEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public void La_reserva_no_se_extiende_en_una_compra_solo_digital_ni_mas_alla_del_limite()
    {
        var digital = AbiertaSoloDigital();
        digital.IniciarCobro(Inicio.AddSeconds(5));
        digital.PasarAPendiente();
        Assert.False(digital.ExtenderReserva(Inicio.AddMinutes(30)));
        Assert.Null(digital.ReservaExpiraEn);

        var mixta = ConCobro();
        mixta.PasarAPendiente();
        Assert.Throws<ArgumentOutOfRangeException>(() => mixta.ExtenderReserva(Inicio.AddMinutes(31)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public void Un_rechazo_con_la_reserva_viva_deja_la_intencion_abierta_y_reintentable()
    {
        var intencion = ConCobro();

        Assert.Equal(EstadoDeIntencion.Abierta, intencion.ResolverIntentoSinCobro(Inicio.AddMinutes(2)));

        Assert.Equal(EstadoDeIntencion.Abierta, intencion.Estado);
        Assert.Equal(ValidacionDelCobro.Permitido, intencion.ValidarCobro(Inicio.AddMinutes(2)));
        Assert.Null(intencion.MotivoDeCierre);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public void Un_rechazo_con_la_reserva_expirada_cierra_la_intencion_sin_cobro_y_elimina_el_contacto()
    {
        var intencion = ConCobro();

        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, intencion.ResolverIntentoSinCobro(Inicio.AddMinutes(16)));

        Assert.Equal(MotivoDeCierre.RechazadaSinReintento, intencion.MotivoDeCierre);
        Assert.Equal(Inicio.AddMinutes(16), intencion.CerradaEn);
        Assert.Null(intencion.Contacto);
        Assert.Equal(Inicio.AddMinutes(16), intencion.ContactoLiberadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public void Un_pendiente_rechazado_antes_de_los_30_minutos_con_la_reserva_extendida_deja_la_intencion_abierta()
    {
        var intencion = ConCobro();
        intencion.PasarAPendiente();
        intencion.ExtenderReserva(Inicio.AddMinutes(30));

        Assert.Equal(EstadoDeIntencion.Abierta, intencion.ResolverIntentoSinCobro(Inicio.AddMinutes(20)));
        Assert.Equal(ValidacionDelCobro.Permitido, intencion.ValidarCobro(Inicio.AddMinutes(20)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public void Un_pendiente_solo_digital_rechazado_antes_de_los_30_minutos_queda_reintentable_y_despues_se_cierra()
    {
        var antes = AbiertaSoloDigital();
        antes.IniciarCobro(Inicio.AddSeconds(5));
        antes.PasarAPendiente();
        Assert.Equal(EstadoDeIntencion.Abierta, antes.ResolverIntentoSinCobro(Inicio.AddMinutes(29)));

        var despues = AbiertaSoloDigital();
        despues.IniciarCobro(Inicio.AddSeconds(5));
        despues.PasarAPendiente();
        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, despues.ResolverIntentoSinCobro(Inicio.AddMinutes(30)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public void Resolver_un_intento_sin_cobro_solo_aplica_a_una_intencion_con_cobro()
    {
        Assert.Throws<InvalidOperationException>(() => Abierta().ResolverIntentoSinCobro(Inicio.AddMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => Aprobada().ResolverIntentoSinCobro(Inicio.AddMinutes(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public void Aprobar_marca_el_instante_y_pasa_a_aprobada_solo_desde_con_cobro()
    {
        var intencion = ConCobro();

        Assert.True(intencion.MarcarAprobada(Inicio.AddMinutes(1)));

        Assert.Equal(EstadoDeIntencion.Aprobada, intencion.Estado);
        Assert.Equal(Inicio.AddMinutes(1), intencion.AprobadoEn);
        Assert.False(intencion.EsTardia);
        // Repetir es idempotente (el aviso y la consulta pueden coincidir).
        Assert.True(intencion.MarcarAprobada(Inicio.AddMinutes(2)));
        Assert.Equal(Inicio.AddMinutes(1), intencion.AprobadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_cobro_aprobado_despues_de_cerrada_la_intencion_nunca_la_reabre_ni_la_completa()
    {
        var abierta = Abierta();
        var cerrada = Abierta();
        cerrada.Cancelar(Inicio.AddMinutes(1));

        Assert.False(abierta.MarcarAprobada(Inicio.AddMinutes(1)));
        Assert.False(cerrada.MarcarAprobada(Inicio.AddMinutes(20)));
        Assert.Equal(EstadoDeIntencion.Abierta, abierta.Estado);
        Assert.Equal(EstadoDeIntencion.CerradaSinCobro, cerrada.Estado);
        Assert.Null(cerrada.AprobadoEn);
    }

    private static IntentoDeCobro Intento(Guid? id = null) =>
        IntentoDeCobro.Iniciar(id ?? Guid.NewGuid(), PedidoId, MedioDePago.Yape, Importe.De(140m), Inicio.AddSeconds(30));

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public void Aprobar_el_intento_activo_pasa_la_intencion_a_aprobada_en_una_sola_operacion()
    {
        var intencion = Abierta();
        intencion.IniciarCobro(Inicio.AddSeconds(30));
        var intento = Intento();

        Assert.Equal(ResultadoDeAprobacion.Aprobado, intencion.AplicarAprobacion(intento, "ch_1", Inicio.AddMinutes(1)));

        Assert.Equal(EstadoDeIntencion.Aprobada, intencion.Estado);
        Assert.Equal(EstadoDeIntento.Aprobado, intento.Estado);
        Assert.Equal(Inicio.AddMinutes(1), intencion.AprobadoEn);
        Assert.Equal(ResultadoDeAprobacion.YaAprobado, intencion.AplicarAprobacion(intento, "ch_1", Inicio.AddMinutes(2)));
        Assert.Equal(ResultadoDeAprobacion.CargoDuplicado, intencion.AplicarAprobacion(intento, "ch_2", Inicio.AddMinutes(2)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_tardio_de_un_intento_rechazado_con_otro_intento_activo_es_sobrante_y_no_toca_la_intencion()
    {
        var intencion = Abierta();
        intencion.IniciarCobro(Inicio.AddSeconds(30));
        var primero = Intento();
        primero.MarcarRechazado(MotivoDeRechazo.TarjetaRechazada, Inicio.AddSeconds(40));
        intencion.ResolverIntentoSinCobro(Inicio.AddSeconds(40));
        intencion.IniciarCobro(Inicio.AddSeconds(50));
        var segundo = Intento();

        Assert.Equal(ResultadoDeAprobacion.Sobrante, intencion.AplicarAprobacion(primero, "ch_viejo", Inicio.AddMinutes(2)));

        Assert.Equal(EstadoDeIntencion.ConCobro, intencion.Estado);
        Assert.Null(intencion.AprobadoEn);
        Assert.Equal(EstadoDeIntento.AprobadoSobrante, primero.Estado);
        Assert.Equal(EstadoDeIntento.Iniciado, segundo.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_tardio_de_un_intento_vencido_con_la_intencion_completada_es_sobrante()
    {
        var intencion = Abierta();
        intencion.IniciarCobro(Inicio.AddSeconds(30));
        var vencido = Intento();
        vencido.Vencer(Inicio.AddMinutes(31));
        // La intención se completó por otro intento (aquí se simula llevándola a completada).
        var activo = Aprobada();
        activo.Completar(Inicio.AddMinutes(2));
        var delActivo = IntentoDeCobro.Iniciar(Guid.NewGuid(), PedidoId, MedioDePago.Tarjeta, Importe.De(140m), Inicio.AddSeconds(30));
        delActivo.Vencer(Inicio.AddMinutes(31));

        Assert.Equal(ResultadoDeAprobacion.Sobrante, activo.AplicarAprobacion(delActivo, "ch_tardio", Inicio.AddMinutes(32)));

        Assert.Equal(EstadoDeIntencion.Completada, activo.Estado);
        Assert.Equal(Inicio.AddMinutes(1), activo.AprobadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Aprobar_el_intento_de_otra_intencion_se_rechaza()
    {
        var intencion = ConCobro();
        var ajeno = IntentoDeCobro.Iniciar(Guid.NewGuid(), Guid.NewGuid(), MedioDePago.Yape, Importe.De(10m), Inicio);

        Assert.Throws<InvalidOperationException>(() => intencion.AplicarAprobacion(ajeno, "ch_1", Inicio.AddMinutes(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public void Una_intencion_abierta_con_impresos_tambien_se_cierra_cuando_expira_su_reserva_aunque_la_vigencia_siga()
    {
        var intencion = ConCobro();
        intencion.PasarAPendiente();
        Assert.Equal(EstadoDeIntencion.Abierta, intencion.ResolverIntentoSinCobro(Inicio.AddMinutes(3)));

        Assert.False(intencion.CerrarPorVencimiento(Inicio.AddMinutes(14)));
        Assert.True(intencion.CerrarPorVencimiento(Inicio.AddMinutes(15)));
        Assert.Null(intencion.HuellaDeInstantanea);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_pasados_30_minutos_o_con_la_reserva_expirada_es_tardio()
    {
        var pendiente = ConCobro();
        pendiente.PasarAPendiente();
        Assert.True(pendiente.MarcarAprobada(Inicio.AddMinutes(31)));
        Assert.True(pendiente.EsTardia);

        var conReservaExpirada = ConCobro();
        conReservaExpirada.PasarAPendiente();
        Assert.True(conReservaExpirada.MarcarAprobada(Inicio.AddMinutes(16)));
        Assert.True(conReservaExpirada.EsTardia);

        var digital = AbiertaSoloDigital();
        digital.IniciarCobro(Inicio.AddSeconds(5));
        digital.PasarAPendiente();
        Assert.True(digital.MarcarAprobada(Inicio.AddMinutes(29)));
        Assert.False(digital.EsTardia);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public void La_confirmacion_de_reservas_se_marca_antes_de_llamar_y_solo_con_impresos_y_no_tardia()
    {
        var intencion = Aprobada();

        Assert.True(intencion.DebeConfirmarReservas);
        Assert.True(intencion.MarcarConfirmacionIniciada(Inicio.AddMinutes(2)));
        Assert.Equal(Inicio.AddMinutes(2), intencion.ConfirmacionIniciadaEn);
        Assert.True(intencion.MarcarConfirmacionIniciada(Inicio.AddMinutes(3)));
        Assert.Equal(Inicio.AddMinutes(2), intencion.ConfirmacionIniciadaEn);
        intencion.MarcarReservasConfirmadas(Inicio.AddMinutes(3));
        Assert.Equal(Inicio.AddMinutes(3), intencion.ReservasConfirmadasEn);

        var digital = AbiertaSoloDigital();
        digital.IniciarCobro(Inicio.AddSeconds(5));
        digital.MarcarAprobada(Inicio.AddMinutes(1));
        Assert.False(digital.DebeConfirmarReservas);
        Assert.False(digital.MarcarConfirmacionIniciada(Inicio.AddMinutes(2)));
        Assert.Null(digital.ConfirmacionIniciadaEn);

        var tardia = ConCobro();
        tardia.PasarAPendiente();
        tardia.MarcarAprobada(Inicio.AddMinutes(31));
        Assert.False(tardia.DebeConfirmarReservas);
        Assert.False(tardia.MarcarConfirmacionIniciada(Inicio.AddMinutes(32)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void La_reclamacion_dura_dos_minutos_y_otro_ejecutor_no_compite_con_ella()
    {
        var intencion = Aprobada();

        Assert.True(intencion.Reclamar(Inicio.AddMinutes(2)));
        Assert.Equal(Inicio.AddMinutes(4), intencion.ReclamadaHasta);
        Assert.False(intencion.Reclamar(Inicio.AddMinutes(3)));
        Assert.True(intencion.EstaReclamada(Inicio.AddMinutes(3)));
        Assert.True(intencion.Reclamar(Inicio.AddMinutes(4)));
        Assert.Equal(Inicio.AddMinutes(6), intencion.ReclamadaHasta);

        intencion.SoltarReclamacion();
        Assert.False(intencion.EstaReclamada(Inicio.AddMinutes(5)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public void Completar_pasa_de_aprobada_a_completada_y_solo_si_no_se_desvinculo()
    {
        var intencion = Aprobada();

        Assert.Equal(ResultadoDeCompletar.Completada, intencion.Completar(Inicio.AddMinutes(2)));
        Assert.Equal(EstadoDeIntencion.Completada, intencion.Estado);
        Assert.Equal(ResultadoDeCompletar.Completada, intencion.Completar(Inicio.AddMinutes(3)));

        var desvinculada = Aprobada();
        desvinculada.Desvincular(Inicio.AddMinutes(2));
        Assert.Equal(ResultadoDeCompletar.Desvinculada, desvinculada.Completar(Inicio.AddMinutes(3)));
        Assert.Equal(EstadoDeIntencion.Aprobada, desvinculada.Estado);

        Assert.Throws<InvalidOperationException>(() => Abierta().Completar(Inicio.AddMinutes(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Compensar_pasa_de_aprobada_a_compensada_elimina_el_contacto_y_no_se_hace_sobre_una_completada()
    {
        var intencion = Aprobada();

        intencion.Compensar(Inicio.AddMinutes(2));

        Assert.Equal(EstadoDeIntencion.Compensada, intencion.Estado);
        Assert.Equal(Inicio.AddMinutes(2), intencion.CerradaEn);
        Assert.Null(intencion.Contacto);
        Assert.Equal(Inicio.AddMinutes(2), intencion.ContactoLiberadoEn);
        intencion.Compensar(Inicio.AddMinutes(3));
        Assert.Equal(Inicio.AddMinutes(2), intencion.CerradaEn);

        var completada = Aprobada();
        completada.Completar(Inicio.AddMinutes(2));
        Assert.Throws<InvalidOperationException>(() => completada.Compensar(Inicio.AddMinutes(3)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public void El_contacto_se_libera_a_pedido_de_pedidos_de_forma_idempotente()
    {
        var intencion = Aprobada();
        intencion.Completar(Inicio.AddMinutes(2));
        Assert.NotNull(intencion.Contacto);

        Assert.True(intencion.LiberarContacto(Inicio.AddMinutes(3)));

        Assert.Null(intencion.Contacto);
        Assert.Equal(Inicio.AddMinutes(3), intencion.ContactoLiberadoEn);
        Assert.Null(intencion.HuellaDeInstantanea);
        Assert.False(intencion.LiberarContacto(Inicio.AddMinutes(4)));
        Assert.Equal(Inicio.AddMinutes(3), intencion.ContactoLiberadoEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public void El_contacto_vence_a_los_30_dias_de_creada_y_a_los_29_dias_y_23_horas_sigue_en_la_intencion()
    {
        var intencion = Abierta();

        Assert.False(intencion.ContactoVencido(Inicio.AddDays(29).AddHours(23)));
        Assert.True(intencion.ContactoVencido(Inicio.AddDays(30)));
        Assert.NotNull(intencion.Contacto);

        intencion.LiberarContacto(Inicio.AddDays(30));
        Assert.False(intencion.ContactoVencido(Inicio.AddDays(31)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-23")]
    public void Una_intencion_sin_contacto_de_entrega_no_tiene_nada_que_liberar()
    {
        var intencion = Abierta(ConRecojo());

        Assert.Null(intencion.Contacto);
        Assert.False(intencion.ContactoVencido(Inicio.AddDays(40)));
        Assert.False(intencion.LiberarContacto(Inicio.AddDays(40)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public void Desvincular_anula_la_cuenta_los_textos_la_entrega_el_titulo_el_isbn_y_el_contacto_y_conserva_los_importes()
    {
        var intencion = Aprobada();
        intencion.Completar(Inicio.AddMinutes(2));

        Assert.True(intencion.Desvincular(Inicio.AddDays(1)));

        Assert.True(intencion.EstaDesvinculada);
        Assert.Null(intencion.CuentaId);
        Assert.Equal(Inicio.AddDays(1), intencion.DesvinculadaEn);
        Assert.Null(intencion.Terminos);
        Assert.Null(intencion.Privacidad);
        Assert.Null(intencion.ReembolsoDigital);
        Assert.Null(intencion.TipoDeEntrega);
        Assert.Null(intencion.RegionDeEntrega);
        Assert.Null(intencion.CodigoDeSucursalDeRecojo);
        Assert.Null(intencion.Contacto);
        Assert.Null(intencion.HuellaDeInstantanea);
        Assert.All(intencion.Lineas, l =>
        {
            Assert.Null(l.EdicionId);
            Assert.Null(l.Titulo);
            Assert.Null(l.Isbn);
            Assert.True(l.Importe.Valor > 0m);
        });
        Assert.Equal(Importe.De(140m), intencion.Total);
        Assert.Equal(Importe.De(15m), intencion.Envio);
        Assert.Equal(Inicio.AddMinutes(1), intencion.AprobadoEn);
        Assert.False(intencion.EsDeLaCuenta(Cuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-22")]
    public void Desvincular_es_idempotente_y_conserva_el_primer_instante()
    {
        var intencion = Aprobada();
        Assert.True(intencion.Desvincular(Inicio.AddDays(1)));
        Assert.False(intencion.Desvincular(Inicio.AddDays(2)));
        Assert.Equal(Inicio.AddDays(1), intencion.DesvinculadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-21")]
    public void Solo_la_cuenta_duena_es_la_propietaria()
    {
        var intencion = Abierta();

        Assert.True(intencion.EsDeLaCuenta(Cuenta));
        Assert.False(intencion.EsDeLaCuenta(Guid.NewGuid()));
        Assert.False(intencion.EsDeLaCuenta(Guid.Empty));
    }
}
