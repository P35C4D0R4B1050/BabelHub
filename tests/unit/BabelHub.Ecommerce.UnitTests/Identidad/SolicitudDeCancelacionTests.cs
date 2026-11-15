using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>La solicitud de cancelación diferida (T08; AC-IDE-24, AC-IDE-61; IDE-27, IDE-68, IDE-71, IDE-76, IDE-81).</summary>
public sealed class SolicitudDeCancelacionTests
{
    private static readonly DateTimeOffset Dia0 = new(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(-5));

    private static SolicitudDeCancelacion Nueva() => SolicitudDeCancelacion.Diferir(Guid.NewGuid(), Guid.NewGuid(), Dia0);

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public void Una_solicitud_diferida_impide_comprar_y_no_se_ejecuta_hasta_cerrarse_los_pendientes()
    {
        var solicitud = Nueva();

        Assert.Equal(EstadoDeCancelacion.Diferida, solicitud.Estado);
        Assert.True(solicitud.ImpideComprar);
        Assert.Equal(ResultadoDeReconfirmacion.NoEstaEsperando, solicitud.Reconfirmar(Dia0.AddDays(1), hayPendientes: false));
        Assert.Equal(EstadoDeCancelacion.Diferida, solicitud.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public void Al_cerrarse_los_pendientes_espera_la_reconfirmacion_siete_dias_y_se_ejecuta_si_reconfirma_sin_pendientes()
    {
        var solicitud = Nueva();
        var cierre = Dia0.AddDays(3);

        Assert.True(solicitud.PendientesCerrados(cierre, avisoPorCorreo: true));
        Assert.Equal(EstadoDeCancelacion.EsperandoReconfirmacion, solicitud.Estado);
        Assert.Equal(cierre.AddDays(7), solicitud.ReconfirmarHasta);
        Assert.True(solicitud.ImpideComprar);

        Assert.Equal(ResultadoDeReconfirmacion.Ejecutar, solicitud.Reconfirmar(cierre.AddDays(7), hayPendientes: false));
        Assert.True(solicitud.EjecucionEnCurso);
        solicitud.Ejecutada(cierre.AddDays(7));

        Assert.Equal(EstadoDeCancelacion.Ejecutada, solicitud.Estado);
        Assert.False(solicitud.ImpideComprar);
        Assert.Equal(cierre.AddDays(7), solicitud.CerradaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public void Si_no_reconfirma_en_siete_dias_se_descarta_y_recupera_la_posibilidad_de_comprar()
    {
        var solicitud = Nueva();
        solicitud.PendientesCerrados(Dia0.AddDays(2), true);

        Assert.False(solicitud.DescartarSiVencio(Dia0.AddDays(9)));
        Assert.True(solicitud.DescartarSiVencio(Dia0.AddDays(9).AddSeconds(1)));

        Assert.Equal(EstadoDeCancelacion.Descartada, solicitud.Estado);
        Assert.False(solicitud.ImpideComprar);
        Assert.Equal(ResultadoDeReconfirmacion.NoEstaEsperando, solicitud.Reconfirmar(Dia0.AddDays(10), false));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public void Una_reconfirmacion_tardia_se_descarta()
    {
        var solicitud = Nueva();
        solicitud.PendientesCerrados(Dia0.AddDays(2), true);

        var resultado = solicitud.Reconfirmar(Dia0.AddDays(9).AddMinutes(1), false);

        Assert.Equal(ResultadoDeReconfirmacion.FueraDePlazo, resultado);
        Assert.Equal(EstadoDeCancelacion.Descartada, solicitud.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public void Desistir_antes_de_la_ejecucion_descarta_la_solicitud()
    {
        var diferida = Nueva();
        var esperando = Nueva();
        esperando.PendientesCerrados(Dia0.AddDays(1), true);
        var ejecutada = Nueva();
        ejecutada.PendientesCerrados(Dia0.AddDays(1), true);
        ejecutada.Reconfirmar(Dia0.AddDays(2), false);
        ejecutada.Ejecutada(Dia0.AddDays(2));

        Assert.True(diferida.Desistir(Dia0.AddDays(1)));
        Assert.True(esperando.Desistir(Dia0.AddDays(2)));
        Assert.False(ejecutada.Desistir(Dia0.AddDays(3)));
        Assert.Equal(EstadoDeCancelacion.Descartada, diferida.Estado);
        Assert.Equal(EstadoDeCancelacion.Descartada, esperando.Estado);
        Assert.Equal(EstadoDeCancelacion.Ejecutada, ejecutada.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public void Si_al_reconfirmar_aparecio_un_pendiente_no_se_ejecuta_sigue_diferida_y_al_cerrarse_el_plazo_de_siete_dias_se_reinicia_sin_pasar_de_treinta()
    {
        var solicitud = Nueva();
        solicitud.PendientesCerrados(Dia0.AddDays(5), true);

        Assert.Equal(ResultadoDeReconfirmacion.MantenerDiferida, solicitud.Reconfirmar(Dia0.AddDays(6), hayPendientes: true));
        Assert.Equal(EstadoDeCancelacion.Diferida, solicitud.Estado);
        Assert.False(solicitud.EjecucionEnCurso);
        Assert.Null(solicitud.ReconfirmarHasta);

        // Se cierra el nuevo pendiente el día 12: nuevo plazo de 7 días (hasta el 19).
        solicitud.PendientesCerrados(Dia0.AddDays(12), true);
        Assert.Equal(Dia0.AddDays(19), solicitud.ReconfirmarHasta);

        // Otro cierre el día 27 tendría plazo hasta el 34: se corta en el 30.
        Assert.Equal(ResultadoDeReconfirmacion.MantenerDiferida, solicitud.Reconfirmar(Dia0.AddDays(13), true));
        solicitud.PendientesCerrados(Dia0.AddDays(27), true);
        Assert.Equal(Dia0.AddDays(30), solicitud.ReconfirmarHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public void Una_cancelacion_cuyo_aviso_nunca_se_mostro_se_descarta_a_los_treinta_dias()
    {
        var solicitud = Nueva();
        solicitud.PendientesCerrados(Dia0.AddDays(4), avisoPorCorreo: false);

        Assert.Equal(EstadoDeCancelacion.EsperandoReconfirmacion, solicitud.Estado);
        Assert.Null(solicitud.ReconfirmarHasta);
        Assert.False(solicitud.DescartarSiVencio(Dia0.AddDays(29)));
        Assert.True(solicitud.DescartarSiVencio(Dia0.AddDays(30)));

        Assert.Equal(EstadoDeCancelacion.Descartada, solicitud.Estado);
        Assert.Equal(Dia0.AddDays(30), solicitud.CerradaEn);
        Assert.False(solicitud.ImpideComprar);
    }

[Fact]    [Trait("Criterio", "AC-IDE-61")]    public void No_se_puede_reconfirmar_antes_de_que_el_aviso_se_muestre()    {        var solicitud = Nueva();        solicitud.PendientesCerrados(Dia0.AddDays(4), avisoPorCorreo: false);        Assert.Equal(ResultadoDeReconfirmacion.NoEstaEsperando, solicitud.Reconfirmar(Dia0.AddDays(5), hayPendientes: false));        Assert.False(solicitud.EjecucionEnCurso);        solicitud.AvisoMostrado(Dia0.AddDays(5));        Assert.Equal(ResultadoDeReconfirmacion.Ejecutar, solicitud.Reconfirmar(Dia0.AddDays(6), hayPendientes: false));    }
    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public void El_plazo_cuenta_desde_que_el_aviso_se_muestra_en_una_sesion_reconocida_con_el_tope_de_treinta_dias()
    {
        var solicitud = Nueva();
        solicitud.PendientesCerrados(Dia0.AddDays(10), avisoPorCorreo: false);

        solicitud.AvisoMostrado(Dia0.AddDays(12));
        Assert.Equal(Dia0.AddDays(12), solicitud.AvisoVistoEn);
        Assert.Equal(Dia0.AddDays(19), solicitud.ReconfirmarHasta);

        // Mostrarlo otra vez no reinicia el plazo.
        solicitud.AvisoMostrado(Dia0.AddDays(15));
        Assert.Equal(Dia0.AddDays(19), solicitud.ReconfirmarHasta);

        var tardia = Nueva();
        tardia.PendientesCerrados(Dia0.AddDays(10), false);
        tardia.AvisoMostrado(Dia0.AddDays(28));
        Assert.Equal(Dia0.AddDays(30), tardia.ReconfirmarHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public void Si_los_pendientes_se_cierran_el_dia_29_el_plazo_llega_hasta_el_30_y_la_reconfirmacion_posterior_no_se_acepta()
    {
        var solicitud = Nueva();
        solicitud.PendientesCerrados(Dia0.AddDays(29), true);

        Assert.Equal(Dia0.AddDays(30), solicitud.ReconfirmarHasta);
        Assert.Equal(ResultadoDeReconfirmacion.FueraDePlazo, solicitud.Reconfirmar(Dia0.AddDays(30).AddSeconds(1), false));
        Assert.Equal(EstadoDeCancelacion.Descartada, solicitud.Estado);
        Assert.Equal(Dia0.AddDays(30).AddSeconds(1), solicitud.CerradaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public void Cerrar_los_pendientes_despues_del_dia_30_descarta_la_solicitud()
    {
        var solicitud = Nueva();

        solicitud.PendientesCerrados(Dia0.AddDays(31), true);

        Assert.Equal(EstadoDeCancelacion.Descartada, solicitud.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public void Una_ejecucion_que_fallo_cerrada_se_reintenta_y_si_cruza_el_dia_30_se_descarta()
    {
        var reintentable = Nueva();
        reintentable.PendientesCerrados(Dia0.AddDays(20), true);
        reintentable.Reconfirmar(Dia0.AddDays(21), false);
        reintentable.EjecucionFallo(Dia0.AddDays(21));

        Assert.Equal(EstadoDeCancelacion.EsperandoReconfirmacion, reintentable.Estado);
        Assert.False(reintentable.EjecucionEnCurso);
        Assert.Equal(ResultadoDeReconfirmacion.Ejecutar, reintentable.Reconfirmar(Dia0.AddDays(22), false));

        var cruzada = Nueva();
        cruzada.PendientesCerrados(Dia0.AddDays(28), true);
        cruzada.Reconfirmar(Dia0.AddDays(29), false);
        cruzada.EjecucionFallo(Dia0.AddDays(30));

        Assert.Equal(EstadoDeCancelacion.Descartada, cruzada.Estado);
        Assert.False(cruzada.ImpideComprar);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public void Una_solicitud_diferida_pasa_a_descartada_a_los_treinta_dias_aunque_los_pendientes_no_se_cierren()
    {
        var solicitud = Nueva();

        Assert.False(solicitud.DescartarSiVencio(Dia0.AddDays(29)));
        Assert.True(solicitud.DescartarSiVencio(Dia0.AddDays(30)));
        Assert.False(solicitud.DescartarSiVencio(Dia0.AddDays(31)));
        Assert.Equal(Dia0.AddDays(30), solicitud.CerradaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-24")]
    public void Los_datos_invalidos_se_rechazan_y_la_restauracion_conserva_el_estado()
    {
        Assert.Throws<ArgumentException>(() => SolicitudDeCancelacion.Diferir(Guid.Empty, Guid.NewGuid(), Dia0));
        Assert.Throws<ArgumentException>(() => SolicitudDeCancelacion.Diferir(Guid.NewGuid(), Guid.Empty, Dia0));
        var restaurada = SolicitudDeCancelacion.Restaurar(Guid.NewGuid(), Guid.NewGuid(), EstadoDeCancelacion.EsperandoReconfirmacion, Dia0, Dia0.AddDays(8), null, null, ejecucionEnCurso: true);

        Assert.True(restaurada.EjecucionEnCurso);
        Assert.Equal(Dia0.AddDays(30), restaurada.FechaTope);
        Assert.False(restaurada.PendientesCerrados(Dia0.AddDays(2), true));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-61")]
    public void Una_ejecucion_en_curso_no_se_descarta_ni_por_desistir_ni_por_vencimiento_y_solo_termina_si_empezo()
    {
        var solicitud = Nueva();
        solicitud.PendientesCerrados(Dia0.AddDays(2), true);
        Assert.False(solicitud.Ejecutada(Dia0.AddDays(3)));
        Assert.Equal(ResultadoDeReconfirmacion.Ejecutar, solicitud.Reconfirmar(Dia0.AddDays(3), false));

        Assert.False(solicitud.Desistir(Dia0.AddDays(3)));
        Assert.False(solicitud.DescartarSiVencio(Dia0.AddDays(40)));
        Assert.Equal(EstadoDeCancelacion.EsperandoReconfirmacion, solicitud.Estado);

        Assert.True(solicitud.Ejecutada(Dia0.AddDays(3)));
        Assert.Equal(EstadoDeCancelacion.Ejecutada, solicitud.Estado);
        Assert.False(solicitud.Ejecutada(Dia0.AddDays(4)));
    }
}
