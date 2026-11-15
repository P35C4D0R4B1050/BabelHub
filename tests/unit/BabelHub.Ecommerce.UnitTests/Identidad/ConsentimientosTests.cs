using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>La aceptación de textos, la mayoría de edad y el consentimiento de WhatsApp (T06; AC-IDE-10, AC-IDE-28, AC-IDE-31, AC-IDE-36, AC-IDE-39; IDE-11, IDE-31, IDE-34, IDE-39, IDE-43, IDE-57, IDE-58).</summary>
public sealed class ConsentimientosTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));
    private static readonly VersionesDeTextos V1 = new("t1", "p1", "r1");
    private static readonly VersionesDeTextos V2 = new("t2", "p2", "r1");

    [Fact]
    [Trait("Criterio", "AC-IDE-31")]
    public void El_registro_sin_aceptar_los_terminos_o_la_privacidad_se_rechaza()
    {
        Assert.Equal(ResultadoDeAceptacion.Incompleta, AceptacionDeTextos.EnElRegistro(V1, null, "p1", true, Ahora).Resultado);
        Assert.Equal(ResultadoDeAceptacion.Incompleta, AceptacionDeTextos.EnElRegistro(V1, "t1", "", true, Ahora).Resultado);
        Assert.Equal(ResultadoDeAceptacion.Incompleta, AceptacionDeTextos.EnElRegistro(V1, "  ", "p1", true, Ahora).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-31")]
    public void Al_aceptarlos_la_cuenta_guarda_la_version_y_la_fecha_de_ambos_textos()
    {
        var (aceptacion, resultado) = AceptacionDeTextos.EnElRegistro(V1, "t1", "p1", true, Ahora);

        Assert.Equal(ResultadoDeAceptacion.Aceptada, resultado);
        Assert.Equal("t1", aceptacion!.VersionAceptada(TextoLegal.Terminos));
        Assert.Equal("p1", aceptacion.VersionAceptada(TextoLegal.Privacidad));
        Assert.Equal(Ahora, aceptacion.Aceptadas[TextoLegal.Terminos].Fecha);
        Assert.Equal(Ahora, aceptacion.Aceptadas[TextoLegal.Privacidad].Fecha);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-36")]
    public void El_registro_sin_declarar_la_mayoria_de_edad_se_rechaza_y_con_la_declaracion_guarda_su_fecha()
    {
        Assert.Equal(ResultadoDeAceptacion.Incompleta, AceptacionDeTextos.EnElRegistro(V1, "t1", "p1", false, Ahora).Resultado);

        var (aceptacion, _) = AceptacionDeTextos.EnElRegistro(V1, "t1", "p1", true, Ahora);

        Assert.Equal(Ahora, aceptacion!.MayorDeEdadDeclaradaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-39")]
    public void Una_aceptacion_que_cita_otra_version_se_rechaza_tambien_en_el_registro()
    {
        Assert.Equal(ResultadoDeAceptacion.VersionNoVigente, AceptacionDeTextos.EnElRegistro(V2, "t1", "p2", true, Ahora).Resultado);
        Assert.Equal(ResultadoDeAceptacion.VersionNoVigente, AceptacionDeTextos.EnElRegistro(V2, "t2", "p1", true, Ahora).Resultado);

        var (aceptacion, _) = AceptacionDeTextos.EnElRegistro(V1, "t1", "p1", true, Ahora);
        Assert.Equal(ResultadoDeAceptacion.VersionNoVigente, aceptacion!.Aceptar(V2, TextoLegal.Terminos, "t1", Ahora));
        Assert.Equal(ResultadoDeAceptacion.VersionNoVigente, aceptacion.Aceptar(V2, TextoLegal.Terminos, null, Ahora));
        Assert.Equal("t1", aceptacion.VersionAceptada(TextoLegal.Terminos));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-39")]
    public void Con_una_version_nueva_vigente_se_pide_aceptarla_y_hasta_entonces_no_hay_pagos_ni_cambios_sensibles_pero_si_cuenta_y_pedidos()
    {
        var (aceptacion, _) = AceptacionDeTextos.EnElRegistro(V1, "t1", "p1", true, Ahora);
        aceptacion!.Aceptar(V1, TextoLegal.Reembolso, "r1", Ahora);
        Assert.True(aceptacion.EstaAlDia(V1));
        Assert.True(aceptacion.PuedeIniciarPagosOHacerCambiosSensibles(V1));

        Assert.Equal([TextoLegal.Terminos, TextoLegal.Privacidad], aceptacion.Pendientes(V2));
        Assert.False(aceptacion.PuedeIniciarPagosOHacerCambiosSensibles(V2));
        Assert.True(aceptacion.PuedeVerSuCuentaYSusPedidos);

        var despues = Ahora.AddDays(3);
        Assert.Equal(ResultadoDeAceptacion.Aceptada, aceptacion.Aceptar(V2, TextoLegal.Terminos, "t2", despues));
        Assert.Equal(ResultadoDeAceptacion.Aceptada, aceptacion.Aceptar(V2, TextoLegal.Privacidad, "p2", despues));
        Assert.Equal("t2", aceptacion.VersionAceptada(TextoLegal.Terminos));
        Assert.Equal(despues, aceptacion.Aceptadas[TextoLegal.Terminos].Fecha);
        Assert.True(aceptacion.PuedeIniciarPagosOHacerCambiosSensibles(V2));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-39")]
    public void Sin_aceptar_el_reembolso_no_se_inician_pagos()
    {
        var (aceptacion, _) = AceptacionDeTextos.EnElRegistro(V1, "t1", "p1", true, Ahora);

        Assert.Equal([TextoLegal.Reembolso], aceptacion!.Pendientes(V1));
        Assert.False(aceptacion.PuedeIniciarPagosOHacerCambiosSensibles(V1));
        Assert.Equal(ResultadoDeAceptacion.VersionNoVigente, aceptacion.Aceptar(V1, TextoLegal.Reembolso, "r0", Ahora));
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-39")]
    [InlineData("", "p", "r")]
    [InlineData("t", " ", "r")]
    [InlineData("t", "p", null)]
    [InlineData("0123456789012345678901234567890123", "p", "r")]
    public void Una_version_vacia_o_demasiado_larga_no_es_una_version(string terminos, string privacidad, string? reembolso) =>
        Assert.Throws<ArgumentException>(() => new VersionesDeTextos(terminos, privacidad, reembolso!));

    // ---------- WhatsApp ----------

    [Theory]
    [Trait("Criterio", "AC-IDE-28")]
    [InlineData("+51987654321", true)]
    [InlineData(" +51987654321 ", true)]
    [InlineData("+14155552671", true)]
    [InlineData("51987654321", false)]
    [InlineData("+0987654321", false)]
    [InlineData("+5198765", false)]
    [InlineData("+5198765432109876", false)]
    [InlineData("+51 987 654 321", false)]
    [InlineData("+51abc654321", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void El_numero_se_valida_en_formato_e164(string? texto, bool valido) => Assert.Equal(valido, NumeroDeWhatsApp.TryCrear(texto, out _));

    [Fact]
    [Trait("Criterio", "AC-IDE-28")]
    public void Un_numero_invalido_se_rechaza_sin_tocar_el_guardado_y_uno_valido_se_guarda_sin_consentimiento()
    {
        var consentimiento = ConsentimientoDeWhatsApp.SinNumero();

        Assert.False(consentimiento.GuardarNumero("12345", Ahora));
        Assert.Null(consentimiento.Numero);

        Assert.True(consentimiento.GuardarNumero("+51987654321", Ahora));
        Assert.Equal("+51987654321", consentimiento.Numero!.Value.Valor);
        Assert.False(consentimiento.Consentido);
        Assert.False(consentimiento.PuedeRecibirAvisos);
        Assert.DoesNotContain("987", consentimiento.Numero.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-28")]
    public void Al_cambiar_de_numero_no_recibe_avisos_hasta_confirmar_el_consentimiento_para_el_nuevo()
    {
        var consentimiento = ConsentimientoDeWhatsApp.SinNumero();
        consentimiento.GuardarNumero("+51987654321", Ahora);
        Assert.True(consentimiento.Consentir("+51987654321", Ahora.AddMinutes(1)));
        Assert.True(consentimiento.PuedeRecibirAvisos);

        consentimiento.GuardarNumero("+51911111111", Ahora.AddDays(1));

        Assert.False(consentimiento.Consentido);
        Assert.False(consentimiento.PuedeRecibirAvisos);
        Assert.False(consentimiento.Consentir("+51987654321", Ahora.AddDays(1)));
        Assert.True(consentimiento.Consentir("+51911111111", Ahora.AddDays(1).AddMinutes(1)));
        Assert.True(consentimiento.PuedeRecibirAvisos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-28")]
    public void Guardar_el_mismo_numero_no_pierde_el_consentimiento()
    {
        var consentimiento = ConsentimientoDeWhatsApp.SinNumero();
        consentimiento.GuardarNumero("+51987654321", Ahora);
        consentimiento.Consentir("+51987654321", Ahora);

        Assert.True(consentimiento.GuardarNumero(" +51987654321", Ahora.AddDays(1)));

        Assert.True(consentimiento.Consentido);
        Assert.Equal(Ahora, consentimiento.Fecha);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-10")]
    public void Revocar_guarda_la_fecha_y_los_avisos_siguientes_no_se_envian()
    {
        var consentimiento = ConsentimientoDeWhatsApp.SinNumero();
        consentimiento.GuardarNumero("+51987654321", Ahora);
        consentimiento.Consentir("+51987654321", Ahora);

        consentimiento.Revocar(Ahora.AddDays(2));

        Assert.False(consentimiento.PuedeRecibirAvisos);
        Assert.Equal(Ahora.AddDays(2), consentimiento.Fecha);
        Assert.NotNull(consentimiento.Numero);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-10")]
    public void Eliminar_quita_el_numero_y_el_consentimiento_y_no_se_consiente_sin_numero()
    {
        var consentimiento = ConsentimientoDeWhatsApp.SinNumero();
        Assert.False(consentimiento.Consentir("+51987654321", Ahora));
        consentimiento.GuardarNumero("+51987654321", Ahora);
        consentimiento.Consentir("+51987654321", Ahora);

        consentimiento.Eliminar();

        Assert.Null(consentimiento.Numero);
        Assert.False(consentimiento.PuedeRecibirAvisos);
        Assert.Null(consentimiento.Fecha);
        Assert.False(ConsentimientoDeWhatsApp.Restaurar(null, true, Ahora).Consentido);
    }
}
