using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>Las solicitudes sobre datos personales y el calendario hábil (T07; AC-IDE-11, AC-IDE-29; IDE-12, IDE-32).</summary>
public sealed class SolicitudesDeDatosTests
{
    // 2026-11-13 es viernes.
    private static readonly DateOnly Viernes = new(2026, 11, 13);
    private static readonly DateTimeOffset InstanteViernes = new(2026, 11, 13, 15, 0, 0, TimeSpan.FromHours(-5));

    [Fact]
    [Trait("Criterio", "AC-IDE-11")]
    public void Los_dias_habiles_son_de_lunes_a_viernes_y_se_cuentan_desde_el_dia_siguiente()
    {
        Assert.Equal(DayOfWeek.Friday, Viernes.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 11, 16), CalendarioHabil.SumarDiasHabiles(Viernes, 1));
        Assert.Equal(new DateOnly(2026, 11, 17), CalendarioHabil.SumarDiasHabiles(Viernes, 2));
        Assert.Equal(new DateOnly(2026, 11, 20), CalendarioHabil.SumarDiasHabiles(Viernes, 5));
        Assert.Equal(Viernes, CalendarioHabil.SumarDiasHabiles(Viernes, 0));
        Assert.Equal(new DateOnly(2026, 11, 17), CalendarioHabil.SumarDiasHabiles(new DateOnly(2026, 11, 14), 1).AddDays(1));
        Assert.True(CalendarioHabil.EsHabil(new DateOnly(2026, 11, 13)));
        Assert.False(CalendarioHabil.EsHabil(new DateOnly(2026, 11, 14)));
        Assert.False(CalendarioHabil.EsHabil(new DateOnly(2026, 11, 15)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-11")]
    public void Una_solicitud_hecha_en_fin_de_semana_cuenta_desde_el_lunes_siguiente_como_primer_dia()
    {
        var sabado = new DateOnly(2026, 11, 14);

        Assert.Equal(new DateOnly(2026, 11, 16), CalendarioHabil.SumarDiasHabiles(sabado, 1));
        Assert.Equal(new DateOnly(2026, 11, 20), CalendarioHabil.SumarDiasHabiles(sabado, 5));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-11")]
    public void El_acceso_tiene_veinte_dias_habiles_y_los_demas_tipos_diez()
    {
        var acceso = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Acceso, InstanteViernes, Viernes);
        var rectificacion = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Rectificacion, InstanteViernes, Viernes);
        var cancelacion = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Cancelacion, InstanteViernes, Viernes);
        var oposicion = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Oposicion, InstanteViernes, Viernes);

        Assert.Equal(EstadoDeSolicitudDeDatos.Recibida, acceso.Estado);
        Assert.Equal(new DateOnly(2026, 12, 11), acceso.FechaLimite);
        Assert.Equal(20, CalendarioHabil.DiasHabilesEntre(Viernes, acceso.FechaLimite));
        Assert.Equal(new DateOnly(2026, 11, 27), rectificacion.FechaLimite);
        Assert.Equal(10, CalendarioHabil.DiasHabilesEntre(Viernes, rectificacion.FechaLimite));
        Assert.Equal(rectificacion.FechaLimite, cancelacion.FechaLimite);
        Assert.Equal(rectificacion.FechaLimite, oposicion.FechaLimite);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-29")]
    public void Una_solicitud_se_vence_solo_despues_de_su_fecha_limite_y_una_en_atencion_cuenta_como_sin_atender()
    {
        var solicitud = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Rectificacion, InstanteViernes, Viernes);

        Assert.False(solicitud.EstaVencida(solicitud.FechaLimite));
        Assert.True(solicitud.EstaVencida(solicitud.FechaLimite.AddDays(1)));

        solicitud.PonerEnAtencion();
        Assert.Equal(EstadoDeSolicitudDeDatos.EnAtencion, solicitud.Estado);
        Assert.True(solicitud.EstaVencida(solicitud.FechaLimite.AddDays(1)));

        solicitud.Atender(InstanteViernes.AddDays(20));
        Assert.False(solicitud.EstaVencida(solicitud.FechaLimite.AddDays(30)));
        Assert.Equal(InstanteViernes.AddDays(20), solicitud.AtendidaEn);
        solicitud.Atender(InstanteViernes.AddDays(40));
        Assert.Equal(InstanteViernes.AddDays(20), solicitud.AtendidaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-29")]
    public void La_lista_va_por_fecha_limite_y_la_de_acceso_con_21_dias_habiles_sin_atender_figura_vencida_y_la_de_3_no()
    {
        // Acceso recibida hace 21 días hábiles; rectificación recibida hace 3.
        var hoy = CalendarioHabil.SumarDiasHabiles(Viernes, 21);
        var recibidaAcceso = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Acceso, InstanteViernes, Viernes);
        var haceTres = CalendarioHabil.SumarDiasHabiles(Viernes, 18);
        var rectificacion = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Rectificacion, InstanteViernes.AddDays(25), haceTres);

        var lista = SolicitudDeDatos.Ordenar([rectificacion, recibidaAcceso], hoy);

        Assert.Equal(recibidaAcceso.Id, lista[0].Solicitud.Id);
        Assert.True(lista[0].Vencida);
        Assert.Equal(rectificacion.Id, lista[1].Solicitud.Id);
        Assert.False(lista[1].Vencida);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-29")]
    public void A_igual_fecha_limite_se_ordena_por_la_recepcion_y_el_identificador()
    {
        var temprano = SolicitudDeDatos.Recibir(Guid.Parse("00000000-0000-4000-8000-000000000002"), Guid.NewGuid(), TipoDeSolicitudDeDatos.Oposicion, InstanteViernes, Viernes);
        var tarde = SolicitudDeDatos.Recibir(Guid.Parse("00000000-0000-4000-8000-000000000001"), Guid.NewGuid(), TipoDeSolicitudDeDatos.Oposicion, InstanteViernes.AddHours(2), Viernes);

        var lista = SolicitudDeDatos.Ordenar([tarde, temprano], Viernes);

        Assert.Equal([temprano.Id, tarde.Id], lista.Select(l => l.Solicitud.Id));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-11")]
    public void Los_datos_invalidos_se_rechazan()
    {
        Assert.Throws<ArgumentException>(() => SolicitudDeDatos.Recibir(Guid.Empty, Guid.NewGuid(), TipoDeSolicitudDeDatos.Acceso, InstanteViernes, Viernes));
        Assert.Throws<ArgumentException>(() => SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.Empty, TipoDeSolicitudDeDatos.Acceso, InstanteViernes, Viernes));
        Assert.Throws<ArgumentOutOfRangeException>(() => CalendarioHabil.SumarDiasHabiles(Viernes, -1));
        Assert.Equal(0, CalendarioHabil.DiasHabilesEntre(Viernes, Viernes));
        Assert.Equal(0, CalendarioHabil.DiasHabilesEntre(Viernes, Viernes.AddDays(-3)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-23")]
    public void Atender_guarda_la_respuesta_y_cierra_la_solicitud_y_una_cerrada_no_cambia()
    {
        var solicitud = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Acceso, InstanteViernes, Viernes);

        Assert.True(solicitud.Atender(InstanteViernes.AddDays(1), "Se envió el resumen de sus datos."));

        Assert.Equal(EstadoDeSolicitudDeDatos.Atendida, solicitud.Estado);
        Assert.Equal("Se envió el resumen de sus datos.", solicitud.Respuesta);
        Assert.True(solicitud.EstaCerrada);
        Assert.False(solicitud.Atender(InstanteViernes.AddDays(2), "otra"));
        Assert.False(solicitud.Rechazar(InstanteViernes.AddDays(2), "no"));
        Assert.Equal("Se envió el resumen de sus datos.", solicitud.Respuesta);
        Assert.Null(solicitud.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-23")]
    public void Rechazar_guarda_el_motivo_y_cierra_la_solicitud_y_una_rechazada_ya_no_figura_vencida()
    {
        var solicitud = SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Rectificacion, InstanteViernes, Viernes);

        Assert.True(solicitud.Rechazar(InstanteViernes.AddDays(1), "No se pudo verificar la titularidad."));

        Assert.Equal(EstadoDeSolicitudDeDatos.Rechazada, solicitud.Estado);
        Assert.Equal("No se pudo verificar la titularidad.", solicitud.Motivo);
        Assert.Null(solicitud.Respuesta);
        Assert.False(solicitud.EstaVencida(Viernes.AddDays(60)));
        Assert.False(solicitud.Atender(InstanteViernes.AddDays(2)));
        Assert.Throws<ArgumentException>(() => SolicitudDeDatos.Recibir(Guid.NewGuid(), Guid.NewGuid(), TipoDeSolicitudDeDatos.Oposicion, InstanteViernes, Viernes).Rechazar(InstanteViernes, " "));
    }
}
