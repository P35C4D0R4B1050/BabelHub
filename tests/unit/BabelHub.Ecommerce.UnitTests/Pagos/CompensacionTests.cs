using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.IntencionesDePrueba;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>La compensación de un cobro que no se pudo completar: motivos, pasos que no se repiten y variante de un cobro sobrante (T04; PAG-15, PAG-20, PAG-29, PAG-32).</summary>
public sealed class CompensacionTests
{
    private static readonly Guid IntentoId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static Compensacion Completa(MotivoDeCompensacion motivo = MotivoDeCompensacion.FallaPosteriorAlPago, bool conReservas = true) =>
        Compensacion.Iniciar(PedidoId, IntentoId, motivo, VarianteDeCompensacion.Completa, conReservas, Inicio.AddMinutes(2));

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_pasados_los_30_minutos_o_con_la_reserva_expirada_se_clasifica_como_pago_tardio()
    {
        var pendiente = ConCobro();
        pendiente.PasarAPendiente();
        pendiente.MarcarAprobada(Inicio.AddMinutes(31));
        var sinReserva = ConCobro();
        sinReserva.PasarAPendiente();
        sinReserva.MarcarAprobada(Inicio.AddMinutes(16));

        Assert.Equal(MotivoDeCompensacion.PagoTardio, Compensacion.Clasificar(pendiente, cancelacionDeCuentaEnCurso: false));
        Assert.Equal(MotivoDeCompensacion.PagoTardio, Compensacion.Clasificar(sinReserva, cancelacionDeCuentaEnCurso: false));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void Un_aprobado_a_tiempo_no_se_compensa()
    {
        Assert.Null(Compensacion.Clasificar(Aprobada(), cancelacionDeCuentaEnCurso: false));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-25")]
    public void Un_aprobado_de_una_intencion_desvinculada_se_clasifica_como_pag_29_aunque_ademas_sea_tardio()
    {
        var intencion = ConCobro();
        intencion.PasarAPendiente();
        intencion.MarcarAprobada(Inicio.AddMinutes(31));
        intencion.Desvincular(Inicio.AddMinutes(20));

        Assert.Equal(MotivoDeCompensacion.IntencionDesvinculada, Compensacion.Clasificar(intencion, cancelacionDeCuentaEnCurso: false));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public void Un_aprobado_con_la_cancelacion_de_la_cuenta_en_ejecucion_se_clasifica_como_pag_32()
    {
        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, Compensacion.Clasificar(Aprobada(), cancelacionDeCuentaEnCurso: true));

        var tardia = ConCobro();
        tardia.PasarAPendiente();
        tardia.MarcarAprobada(Inicio.AddMinutes(31));
        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, Compensacion.Clasificar(tardia, cancelacionDeCuentaEnCurso: true));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Solo_se_clasifica_una_intencion_aprobada()
    {
        Assert.Throws<InvalidOperationException>(() => Compensacion.Clasificar(Abierta(), cancelacionDeCuentaEnCurso: false));
        Assert.Throws<InvalidOperationException>(() => Compensacion.Clasificar(ConCobro(), cancelacionDeCuentaEnCurso: false));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-17")]
    [InlineData(MotivoDeCompensacion.PagoTardio, true)]
    [InlineData(MotivoDeCompensacion.FallaPosteriorAlPago, true)]
    [InlineData(MotivoDeCompensacion.IntencionDesvinculada, false)]
    [InlineData(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, false)]
    public void Solo_la_cancelacion_de_la_cuenta_no_avisa_al_cliente_ni_lee_su_correo(MotivoDeCompensacion motivo, bool avisa)
    {
        var compensacion = Completa(motivo);

        Assert.Equal(avisa, compensacion.Avisar);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void La_compensacion_completa_sigue_sus_tres_pasos_en_orden_y_ninguno_se_repite()
    {
        var compensacion = Completa();
        var reembolso = Guid.Parse("66666666-6666-6666-6666-666666666666");

        Assert.Equal(PasoDeCompensacion.LiberarReservas, compensacion.SiguientePaso);
        Assert.True(compensacion.MarcarReservasLiberadas(MotivoDeLiberacion.FallaPosteriorAlPago, Inicio.AddMinutes(3)));
        Assert.False(compensacion.MarcarReservasLiberadas(MotivoDeLiberacion.PagoVencido, Inicio.AddMinutes(4)));
        Assert.Equal(MotivoDeLiberacion.FallaPosteriorAlPago, compensacion.MotivoDeLiberacion);
        Assert.Equal(Inicio.AddMinutes(3), compensacion.ReservasLiberadasEn);

        Assert.Equal(PasoDeCompensacion.RegistrarReembolso, compensacion.SiguientePaso);
        Assert.True(compensacion.MarcarReembolsoRegistrado(reembolso, Inicio.AddMinutes(5)));
        Assert.False(compensacion.MarcarReembolsoRegistrado(Guid.NewGuid(), Inicio.AddMinutes(6)));
        Assert.Equal(reembolso, compensacion.ReembolsoId);

        Assert.Equal(PasoDeCompensacion.CerrarCheckout, compensacion.SiguientePaso);
        Assert.False(compensacion.EstaTerminada);
        Assert.True(compensacion.MarcarCheckoutCerrado(Inicio.AddMinutes(7)));
        Assert.False(compensacion.MarcarCheckoutCerrado(Inicio.AddMinutes(8)));

        Assert.Equal(PasoDeCompensacion.Terminada, compensacion.SiguientePaso);
        Assert.True(compensacion.EstaTerminada);
        Assert.Equal(Inicio.AddMinutes(7), compensacion.CompletadaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Un_paso_no_se_puede_hacer_antes_que_el_anterior()
    {
        var compensacion = Completa();

        Assert.Throws<InvalidOperationException>(() => compensacion.MarcarReembolsoRegistrado(Guid.NewGuid(), Inicio.AddMinutes(3)));
        Assert.Throws<InvalidOperationException>(() => compensacion.MarcarCheckoutCerrado(Inicio.AddMinutes(3)));
        Assert.Throws<ArgumentException>(() => { compensacion.MarcarReservasLiberadas(MotivoDeLiberacion.PagoVencido, Inicio.AddMinutes(3)); compensacion.MarcarReembolsoRegistrado(Guid.Empty, Inicio.AddMinutes(4)); });
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Una_compra_solo_digital_no_tiene_reservas_que_liberar_y_empieza_por_el_reembolso()
    {
        var compensacion = Completa(conReservas: false);

        Assert.Equal(PasoDeCompensacion.RegistrarReembolso, compensacion.SiguientePaso);
        Assert.Null(compensacion.MotivoDeLiberacion);
        Assert.NotNull(compensacion.ReservasLiberadasEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void El_motivo_de_liberacion_es_falla_posterior_si_ya_se_intento_confirmar_y_pago_vencido_si_nunca()
    {
        var confirmando = Aprobada();
        Assert.True(confirmando.MarcarConfirmacionIniciada(Inicio.AddMinutes(2)));
        var nunca = ConCobro();
        nunca.PasarAPendiente();
        nunca.MarcarAprobada(Inicio.AddMinutes(31));

        Assert.Equal(MotivoDeLiberacion.FallaPosteriorAlPago, Compensacion.MotivoDeLiberacionPara(confirmando));
        Assert.Equal(MotivoDeLiberacion.PagoVencido, Compensacion.MotivoDeLiberacionPara(nunca));
        Assert.Equal("falla-posterior-al-pago", MotivoDeLiberacion.FallaPosteriorAlPago.ATexto());
        Assert.Equal("pago-vencido", MotivoDeLiberacion.PagoVencido.ATexto());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void El_motivo_de_liberacion_es_falla_posterior_aunque_no_conste_haber_confirmado_las_reservas()
    {
        var intencion = Aprobada();
        intencion.MarcarConfirmacionIniciada(Inicio.AddMinutes(2));

        Assert.Null(intencion.ReservasConfirmadasEn);
        Assert.Equal(MotivoDeLiberacion.FallaPosteriorAlPago, Compensacion.MotivoDeLiberacionPara(intencion));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void La_variante_solo_reembolso_de_un_cobro_sobrante_no_libera_reservas_ni_cierra_el_checkout_y_avisa()
    {
        var compensacion = Compensacion.Iniciar(PedidoId, IntentoId, MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.SoloReembolso, tieneReservas: true, Inicio.AddMinutes(40));

        Assert.Equal(VarianteDeCompensacion.SoloReembolso, compensacion.Variante);
        Assert.Equal(PasoDeCompensacion.RegistrarReembolso, compensacion.SiguientePaso);
        Assert.True(compensacion.Avisar);
        Assert.Null(compensacion.ReservasLiberadasEn);

        Assert.True(compensacion.MarcarReembolsoRegistrado(Guid.NewGuid(), Inicio.AddMinutes(41)));

        Assert.True(compensacion.EstaTerminada);
        Assert.Equal(Inicio.AddMinutes(41), compensacion.CompletadaEn);
        Assert.Null(compensacion.CheckoutCerradoEn);
        Assert.Throws<InvalidOperationException>(() => compensacion.MarcarCheckoutCerrado(Inicio.AddMinutes(42)));
        Assert.Throws<InvalidOperationException>(() => compensacion.MarcarReservasLiberadas(MotivoDeLiberacion.PagoVencido, Inicio.AddMinutes(42)));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public void La_clave_del_reembolso_de_una_compensacion_es_comp_y_el_intento()
    {
        Assert.Equal($"comp:{IntentoId}", Completa().ClaveDelReembolso);
        Assert.True(Completa().ClaveDelReembolso.Length <= 100);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public void Una_compensacion_necesita_su_intencion_y_su_intento()
    {
        Assert.Throws<ArgumentException>(() => Compensacion.Iniciar(Guid.Empty, IntentoId, MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.Completa, true, Inicio));
        Assert.Throws<ArgumentException>(() => Compensacion.Iniciar(PedidoId, Guid.Empty, MotivoDeCompensacion.PagoTardio, VarianteDeCompensacion.Completa, true, Inicio));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-28")]
    public void La_compensacion_guarda_el_motivo_con_que_se_inicio_y_sus_pasos_no_dependen_de_volver_a_clasificar()
    {
        var compensacion = Completa(MotivoDeCompensacion.CancelacionDeCuentaEnCurso);
        compensacion.MarcarReservasLiberadas(MotivoDeLiberacion.FallaPosteriorAlPago, Inicio.AddMinutes(3));

        // Los pasos siguientes avanzan sin consultar la intención ni la cuenta: el motivo ya consta en la fila y la Aplicación lo lee de ahí.
        Assert.Equal(MotivoDeCompensacion.CancelacionDeCuentaEnCurso, compensacion.Motivo);
        Assert.Equal(PasoDeCompensacion.RegistrarReembolso, compensacion.SiguientePaso);
        Assert.False(compensacion.Avisar);
    }
}
