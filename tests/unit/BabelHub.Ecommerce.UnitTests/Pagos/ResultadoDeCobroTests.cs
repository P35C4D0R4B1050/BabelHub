using System.Text.Json;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Domain.Pagos;
using static BabelHub.Ecommerce.UnitTests.Pagos.IntencionesDePrueba;
using MedioDePagoDeLaApi = BabelHub.Ecommerce.Application.Pagos.MedioDePago;
using MedioDePagoDelDominio = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.UnitTests.Pagos;

/// <summary>
/// El resultado validado del cobro (común a <c>Cobrar</c> y <c>ConsultarEstado</c>) y el token redactado del medio de pago (T06; PAG-02, PAG-09, PAG-11, D13).
/// </summary>
public sealed class ResultadoDeCobroTests
{
    private static readonly Guid IntentoId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static IntentoDeCobro Intento() => IntentoDeCobro.Iniciar(IntentoId, PedidoId, MedioDePagoDelDominio.Yape, Importe.De(140m), Inicio);

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void Un_aprobado_con_el_importe_la_moneda_y_la_referencia_del_intento_se_acepta()
    {
        var resultado = ResultadoDeCobro.Aprobado("ch_1", Importe.De(140m), "PEN", IntentoId).Validar(Intento());

        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, resultado.Tipo);
        Assert.Equal("ch_1", resultado.ReferenciaDePasarela);
        Assert.Null(resultado.Duda);
        Assert.False(resultado.EsDudoso);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void Un_aprobado_con_otro_importe_es_dudoso_se_trata_como_sin_respuesta_y_pide_conciliacion_manual()
    {
        var resultado = ResultadoDeCobro.Aprobado("ch_1", Importe.De(139.99m), "PEN", IntentoId).Validar(Intento());

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
        Assert.Equal(MotivoDeDuda.ImporteDistinto, resultado.Duda);
        Assert.True(resultado.EsDudoso);
        Assert.True(resultado.RequiereConciliacionManual);
        Assert.Null(resultado.ReferenciaDePasarela);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void Un_aprobado_en_otra_moneda_es_dudoso_y_pide_conciliacion_manual()
    {
        var resultado = ResultadoDeCobro.Aprobado("ch_1", Importe.De(140m), "USD", IntentoId).Validar(Intento());

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
        Assert.Equal(MotivoDeDuda.MonedaDistinta, resultado.Duda);
        Assert.True(resultado.RequiereConciliacionManual);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void Un_cargo_sin_intento_coincidente_solo_alerta_y_nunca_pide_un_reembolso_automatico()
    {
        var resultado = ResultadoDeCobro.Aprobado("ch_ajeno", Importe.De(140m), "PEN", Guid.NewGuid()).Validar(Intento());

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
        Assert.Equal(MotivoDeDuda.ReferenciaDistinta, resultado.Duda);
        Assert.True(resultado.EsDudoso);
        Assert.False(resultado.RequiereConciliacionManual);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-09")]
    public void Un_rechazo_conserva_solo_el_motivo_de_la_lista_cerrada()
    {
        var resultado = ResultadoDeCobro.Rechazado(MotivoDeRechazo.TarjetaVencida).Validar(Intento());

        Assert.Equal(TipoDeResultadoDeCobro.Rechazado, resultado.Tipo);
        Assert.Equal(MotivoDeRechazo.TarjetaVencida, resultado.Motivo);
        Assert.Null(resultado.Duda);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void Sin_respuesta_sigue_siendo_sin_respuesta_y_no_es_dudoso()
    {
        var resultado = ResultadoDeCobro.SinRespuesta().Validar(Intento());

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
        Assert.False(resultado.EsDudoso);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void Un_aprobado_sin_referencia_de_la_pasarela_o_sin_referencia_opaca_no_se_construye()
    {
        Assert.Throws<ArgumentException>(() => ResultadoDeCobro.Aprobado(" ", Importe.De(1m), "PEN", IntentoId));
        Assert.Throws<ArgumentException>(() => ResultadoDeCobro.Aprobado("ch_1", Importe.De(1m), "PEN", Guid.Empty));
        Assert.Throws<ArgumentException>(() => ResultadoDeCobro.Aprobado("ch_1", Importe.De(1m), "", IntentoId));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void El_resultado_no_muestra_la_referencia_del_cargo_en_su_texto()
    {
        var texto = ResultadoDeCobro.Aprobado("ch_secreto", Importe.De(140m), "PEN", IntentoId).ToString();

        Assert.DoesNotContain("ch_secreto", texto);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void El_resultado_validado_no_muestra_la_referencia_del_cargo_en_su_texto()
    {
        var validado = ResultadoDeCobro.Aprobado("ch_secreto", Importe.De(140m), "PEN", IntentoId).Validar(Intento());

        Assert.Equal("ch_secreto", validado.ReferenciaDePasarela);
        Assert.DoesNotContain("ch_secreto", validado.ToString());
        Assert.DoesNotContain("ch_secreto", $"{validado}");
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-02")]
    [InlineData("4111-1111-1111-1111")]
    [InlineData("4111.1111.1111.1111")]
    [InlineData("4111_1111_1111_1111")]
    [InlineData("tkn_test_abc\n")]
    public void Un_numero_de_tarjeta_con_separadores_o_un_salto_de_linea_final_no_es_un_token(string texto)
    {
        Assert.False(TokenDelMedioDePago.TryCrear(texto, out _));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void Un_json_con_un_token_invalido_falla_con_una_excepcion_de_json_sin_repetir_el_valor()
    {
        var ex = Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<TokenDelMedioDePago>("\"4111111111111111\""));

        Assert.DoesNotContain("4111", ex.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void El_token_del_medio_de_pago_se_muestra_y_se_serializa_redactado()
    {
        var token = TokenDelMedioDePago.Crear("tkn_test_abc123XYZ");

        Assert.Equal("[redactado]", token.ToString());
        Assert.DoesNotContain("abc123XYZ", $"{token}");
        Assert.DoesNotContain("abc123XYZ", JsonSerializer.Serialize(token));
        Assert.DoesNotContain("abc123XYZ", JsonSerializer.Serialize(new { Medio = "yape", Token = token }));
        Assert.Equal("tkn_test_abc123XYZ", token.Revelar());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void El_token_redactado_tampoco_se_filtra_en_una_excepcion_ni_en_un_texto_compuesto()
    {
        var token = TokenDelMedioDePago.Crear("tkn_test_abc123XYZ");

        var mensaje = string.Concat("solicitud ", token, " fin");
        var excepcion = new InvalidOperationException($"No se pudo cobrar con {token}");

        Assert.DoesNotContain("abc123XYZ", mensaje);
        Assert.DoesNotContain("abc123XYZ", excepcion.ToString());
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-02")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("con espacio")]
    [InlineData("<script>")]
    public void Un_token_vacio_o_con_caracteres_raros_no_es_valido(string? texto)
    {
        Assert.False(TokenDelMedioDePago.TryCrear(texto, out var token));
        Assert.Null(token);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void Un_token_demasiado_largo_no_es_valido_y_un_numero_de_tarjeta_no_se_acepta_como_token()
    {
        Assert.False(TokenDelMedioDePago.TryCrear(new string('a', TokenDelMedioDePago.LongitudMaxima + 1), out _));
        Assert.True(TokenDelMedioDePago.TryCrear(new string('a', TokenDelMedioDePago.LongitudMaxima), out _));
        Assert.False(TokenDelMedioDePago.TryCrear("4111111111111111", out _));
        Assert.False(TokenDelMedioDePago.TryCrear("4111 1111 1111 1111", out _));
        Assert.Throws<ArgumentException>(() => TokenDelMedioDePago.Crear("4111111111111111"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public void El_token_se_lee_de_un_json_para_el_enlace_del_modelo_y_sigue_redactado_al_escribirlo_otra_vez()
    {
        var leido = JsonSerializer.Deserialize<TokenDelMedioDePago>("\"tkn_test_abc123XYZ\"");

        Assert.NotNull(leido);
        Assert.Equal("tkn_test_abc123XYZ", leido!.Revelar());
        Assert.Equal("\"[redactado]\"", JsonSerializer.Serialize(leido));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    public void El_medio_de_pago_de_la_raiz_de_la_aplicacion_se_traduce_al_del_dominio_y_de_vuelta()
    {
        Assert.Equal(MedioDePagoDelDominio.Yape, MedioDePagoDeLaApi.Yape.ADominio());
        Assert.Equal(MedioDePagoDelDominio.Tarjeta, MedioDePagoDeLaApi.Tarjeta.ADominio());
        Assert.Equal(MedioDePagoDeLaApi.Yape, MedioDePagoDelDominio.Yape.ALaAplicacion());
        Assert.Equal(MedioDePagoDeLaApi.Tarjeta, MedioDePagoDelDominio.Tarjeta.ALaAplicacion());
        Assert.Equal(Enum.GetNames<MedioDePagoDelDominio>(), Enum.GetNames<MedioDePagoDeLaApi>());
    }
}
