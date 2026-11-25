using System.Net;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;
using MedioDelDominio = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T37: el adaptador de Culqi contra un servidor HTTP simulado con las respuestas y los errores de su especificación pública (<see cref="CulqiFalso"/>): ninguna llamada a Culqi real, ninguna llave real. Cubre el cobro (aprobado, rechazos con su motivo,
/// 3DS, errores del servicio, respuestas que no se entienden), la consulta del cargo por la referencia opaca, el reembolso y su reconocimiento, la lectura del aviso y la resiliencia del cliente HTTP (el cobro y el reembolso no se reintentan).
/// </summary>
public sealed class CulqiPasarelaDePagoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 25, 15, 0, 0, TimeSpan.Zero);
    private static readonly string LlaveDePrueba = "sk_" + "test_" + "llave-de-integracion-0123456789";
    private static readonly string TokenDeTarjeta = "tkn_" + "test_" + "0CjjdWhFpEAZlxlz";

    private readonly RelojDeCulqi _reloj = new(Inicio);

    private CulqiPasarelaDePago Adaptador(CulqiFalso falso) =>
        new(new HttpClient(falso) { BaseAddress = new Uri(OpcionesDeCulqi.UrlDeLaApi) }, new OpcionesDeCulqi(new ValorSecreto(LlaveDePrueba)), _reloj);

    private static SolicitudDeCobroEnPasarela Solicitud(Guid? referencia = null, MedioDelDominio medio = MedioDelDominio.Tarjeta, decimal importe = 140m, string? correo = "cliente@example.test") =>
        new(referencia ?? Guid.NewGuid(), Importe.De(importe), medio, TokenDelMedioDePago.Crear(TokenDeTarjeta), correo);

    // ---------- Cobrar ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-01")]
    [Trait("Criterio", "AC-PAG-26")]
    public async Task Un_cargo_creado_se_aprueba_por_el_importe_en_centimos_con_la_referencia_opaca_y_sin_datos_de_entrega()
    {
        var solicitud = Solicitud();
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.Created, RespuestasDeCulqi.Cargo(solicitud.ReferenciaOpaca));

        var resultado = await Adaptador(falso).Cobrar(solicitud);

        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, resultado.Tipo);
        Assert.Equal(RespuestasDeCulqi.IdDelCargo, resultado.ReferenciaDePasarela);
        Assert.Equal(solicitud.Importe, resultado.Importe);
        Assert.Equal("PEN", resultado.Moneda);
        Assert.Equal(solicitud.ReferenciaOpaca, resultado.ReferenciaOpaca);

        var enviada = Assert.Single(falso.Recibidas);
        Assert.Equal(HttpMethod.Post, enviada.Metodo);
        Assert.Equal("/v2/charges", enviada.Ruta);
        using var cuerpo = enviada.Json!;
        var raiz = cuerpo.RootElement;
        Assert.Equal(14000, raiz.GetProperty("amount").GetInt64());
        Assert.Equal("PEN", raiz.GetProperty("currency_code").GetString());
        Assert.Equal("cliente@example.test", raiz.GetProperty("email").GetString());
        Assert.Equal(TokenDeTarjeta, raiz.GetProperty("source_id").GetString());
        Assert.True(raiz.GetProperty("capture").GetBoolean());
        Assert.Equal(solicitud.ReferenciaOpaca.ToString("D"), raiz.GetProperty("metadata").GetProperty("referencia").GetString());

        // PAG-30: solo importe, moneda, correo, token, una descripción fija y la referencia opaca; nada de nombre, teléfono, dirección ni datos antifraude.
        var propiedades = raiz.EnumerateObject().Select(p => p.Name).Order().ToArray();
        Assert.Equal(["amount", "capture", "currency_code", "description", "email", "metadata", "source_id"], propiedades);
        Assert.Equal(["referencia"], raiz.GetProperty("metadata").EnumerateObject().Select(p => p.Name).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public async Task La_llave_privada_viaja_solo_en_la_cabecera_y_ni_el_resultado_ni_su_texto_repiten_el_token_ni_el_correo_ni_la_llave()
    {
        var solicitud = Solicitud();
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.Created, RespuestasDeCulqi.Cargo(solicitud.ReferenciaOpaca));

        var resultado = await Adaptador(falso).Cobrar(solicitud);

        var enviada = Assert.Single(falso.Recibidas);
        Assert.Equal("Bearer " + LlaveDePrueba, enviada.Autorizacion);
        Assert.Equal("test", enviada.Cabeceras["x-culqi-env"]);
        Assert.DoesNotContain(LlaveDePrueba, enviada.Cuerpo, StringComparison.Ordinal);
        foreach (var texto in new[] { resultado.ToString(), solicitud.ToString(), new OpcionesDeCulqi(new ValorSecreto(LlaveDePrueba)).ToString() })
        {
            Assert.DoesNotContain(TokenDeTarjeta, texto, StringComparison.Ordinal);
            Assert.DoesNotContain("cliente@example.test", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(LlaveDePrueba, texto, StringComparison.Ordinal);
            Assert.DoesNotContain(RespuestasDeCulqi.IdDelCargo, texto, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("insufficient_funds", MotivoDeRechazo.FondosInsuficientes)]
    [InlineData("expired_card", MotivoDeRechazo.TarjetaVencida)]
    [InlineData("invalid_cvv", MotivoDeRechazo.CodigoDeSeguridadInvalido)]
    [InlineData("incorrect_cvv", MotivoDeRechazo.CodigoDeSeguridadInvalido)]
    [InlineData("too_many_attempts_cvv", MotivoDeRechazo.CodigoDeSeguridadInvalido)]
    [InlineData("stolen_card", MotivoDeRechazo.TarjetaRechazada)]
    [InlineData("lost_card", MotivoDeRechazo.TarjetaRechazada)]
    [InlineData("contact_issuer", MotivoDeRechazo.TarjetaRechazada)]
    [InlineData("issuer_decline_operation", MotivoDeRechazo.TarjetaRechazada)]
    [InlineData("invalid_card", MotivoDeRechazo.TarjetaRechazada)]
    [InlineData("fraudulent", MotivoDeRechazo.TarjetaRechazada)]
    [InlineData("soft_block", MotivoDeRechazo.TarjetaRechazada)]
    [InlineData("culqi_card", MotivoDeRechazo.TarjetaRechazada)]
    [InlineData("issuer_not_available", MotivoDeRechazo.Otro)]
    [InlineData("processing_error", MotivoDeRechazo.Otro)]
    [InlineData("un_codigo_que_no_existe", MotivoDeRechazo.Otro)]
    [Trait("Criterio", "AC-PAG-05")]
    public async Task Una_tarjeta_denegada_402_es_un_rechazo_con_su_motivo_de_la_lista_cerrada(string declinacion, MotivoDeRechazo esperado)
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.PaymentRequired, RespuestasDeCulqi.ErrorDeTarjeta(declinacion));

        var resultado = await Adaptador(falso).Cobrar(Solicitud());

        Assert.Equal((TipoDeResultadoDeCobro.Rechazado, esperado), (resultado.Tipo, resultado.Motivo!.Value));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public async Task Una_denegacion_con_yape_es_yape_rechazado()
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.PaymentRequired, RespuestasDeCulqi.ErrorDeTarjeta("insufficient_funds"));

        var resultado = await Adaptador(falso).Cobrar(Solicitud(medio: MedioDelDominio.Yape));

        Assert.Equal(MotivoDeRechazo.YapeRechazado, resultado.Motivo);
    }

    [Fact]
    public async Task Culqi_pide_autenticar_al_titular_200_review_y_se_trata_como_un_rechazo_de_autenticacion()
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.OK, RespuestasDeCulqi.RevisionTresDs);

        var resultado = await Adaptador(falso).Cobrar(Solicitud());

        Assert.Equal((TipoDeResultadoDeCobro.Rechazado, MotivoDeRechazo.AutenticacionRequerida), (resultado.Tipo, resultado.Motivo!.Value));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.UnprocessableEntity)]
    public async Task Una_solicitud_que_Culqi_no_acepta_no_cobra_y_es_un_rechazo_otro(HttpStatusCode estado)
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", estado, RespuestasDeCulqi.ErrorDeParametro);

        var resultado = await Adaptador(falso).Cobrar(Solicitud());

        Assert.Equal((TipoDeResultadoDeCobro.Rechazado, MotivoDeRechazo.Otro), (resultado.Tipo, resultado.Motivo!.Value));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.BadGateway)]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Un_error_de_llave_de_limite_o_del_servicio_es_sin_respuesta_porque_no_se_sabe_si_se_cobro(HttpStatusCode estado)
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", estado, RespuestasDeCulqi.ErrorDeAutenticacion);

        var resultado = await Adaptador(falso).Cobrar(Solicitud());

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
    }

    [Theory]
    [MemberData(nameof(FallosDeLaLlamada))]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Un_fallo_de_red_un_tiempo_agotado_o_un_circuito_abierto_es_sin_respuesta(Exception excepcion)
    {
        var falso = new CulqiFalso().Falla(HttpMethod.Post, "/v2/charges", excepcion);

        var resultado = await Adaptador(falso).Cobrar(Solicitud());

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
    }

    public static IEnumerable<object[]> FallosDeLaLlamada() =>
    [
        [new HttpRequestException("sin red")],
        [new TaskCanceledException("tiempo agotado")],
        [new TimeoutException("tiempo agotado")],
        [new IOException("conexión cortada")],
    ];

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("{ no es json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"object":"charge","id":"otra-cosa","amount":14000,"currency_code":"PEN"}""")]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Una_respuesta_201_que_no_se_entiende_es_sin_respuesta_y_nunca_aprobada(string cuerpo)
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.Created, cuerpo);

        var resultado = await Adaptador(falso).Cobrar(Solicitud());

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
    }

    [Fact]
    public async Task Un_cargo_creado_sin_la_referencia_opaca_en_los_metadatos_es_sin_respuesta()
    {
        var solicitud = Solicitud();
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.Created, RespuestasDeCulqi.Cargo(solicitud.ReferenciaOpaca, conReferencia: false));

        var resultado = await Adaptador(falso).Cobrar(solicitud);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Un_cargo_cuyo_estado_no_es_de_exito_no_se_aprueba_aunque_la_respuesta_sea_201()
    {
        var solicitud = Solicitud();
        var cuerpo = RespuestasDeCulqi.Cargo(solicitud.ReferenciaOpaca).Replace("\"outcome\"", "\"state\":\"Denegada\",\"outcome\"", StringComparison.Ordinal).Replace("venta_exitosa", "otro", StringComparison.Ordinal);
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.Created, cuerpo);

        var resultado = await Adaptador(falso).Cobrar(solicitud);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
    }

    [Theory]
    [InlineData(14100, "PEN")]
    [InlineData(14000, "USD")]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Un_aprobado_de_otro_importe_o_de_otra_moneda_se_devuelve_tal_cual_y_la_validacion_lo_trata_como_dudoso(long centimos, string moneda)
    {
        var solicitud = Solicitud();
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.Created, RespuestasDeCulqi.Cargo(solicitud.ReferenciaOpaca, centimos, moneda));

        var resultado = await Adaptador(falso).Cobrar(solicitud);
        var validado = resultado.Validar(IntentoDeCobro.Iniciar(solicitud.ReferenciaOpaca, Guid.NewGuid(), MedioDelDominio.Tarjeta, solicitud.Importe, Inicio));

        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, resultado.Tipo);
        Assert.True(validado.EsDudoso);
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, validado.Tipo);
    }

    [Fact]
    public async Task Un_aprobado_con_otra_referencia_opaca_es_dudoso_por_referencia_distinta()
    {
        var solicitud = Solicitud();
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.Created, RespuestasDeCulqi.Cargo(Guid.NewGuid()));

        var validado = (await Adaptador(falso).Cobrar(solicitud)).Validar(IntentoDeCobro.Iniciar(solicitud.ReferenciaOpaca, Guid.NewGuid(), MedioDelDominio.Tarjeta, solicitud.Importe, Inicio));

        Assert.Equal(MotivoDeDuda.ReferenciaDistinta, validado.Duda);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Sin_correo_del_titular_no_hay_llamada_y_no_hay_cobro(string? correo)
    {
        var falso = new CulqiFalso();

        var resultado = await Adaptador(falso).Cobrar(Solicitud(correo: correo));

        Assert.Equal(TipoDeResultadoDeCobro.Rechazado, resultado.Tipo);
        Assert.Empty(falso.Recibidas);
    }

    // ---------- ConsultarEstado ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task La_consulta_reconoce_el_cargo_aprobado_por_la_referencia_opaca_dentro_de_la_ventana_del_intento()
    {
        var referencia = Guid.NewGuid();
        var falso = new CulqiFalso().Cuando(HttpMethod.Get, "/v2/charges?", HttpStatusCode.OK, RespuestasDeCulqi.Lista(RespuestasDeCulqi.Cargo(Guid.NewGuid(), id: "chr_" + "test_" + "OtroCargoDeOtro01"), RespuestasDeCulqi.Cargo(referencia)));

        var resultado = await Adaptador(falso).ConsultarEstado(referencia, iniciadoEn: Inicio);

        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, resultado.Tipo);
        Assert.Equal(RespuestasDeCulqi.IdDelCargo, resultado.ReferenciaDePasarela);
        Assert.Equal(referencia, resultado.ReferenciaOpaca);
        var consulta = Assert.Single(falso.Recibidas);
        Assert.Equal(HttpMethod.Get, consulta.Metodo);
        Assert.Contains($"creation_date_from={Inicio.AddMinutes(-1).ToUnixTimeSeconds()}", consulta.Ruta, StringComparison.Ordinal);
        Assert.Contains($"creation_date_to={Inicio.AddMinutes(10).ToUnixTimeSeconds()}", consulta.Ruta, StringComparison.Ordinal);
        Assert.Contains("limit=100", consulta.Ruta, StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_ausencia_del_cargo_nunca_es_un_rechazo_es_sin_respuesta()
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Get, "/v2/charges?", HttpStatusCode.OK, RespuestasDeCulqi.Lista(RespuestasDeCulqi.Cargo(Guid.NewGuid())));

        var resultado = await Adaptador(falso).ConsultarEstado(Guid.NewGuid(), iniciadoEn: Inicio);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
    }

    [Fact]
    public async Task Un_cargo_denegado_listado_con_la_referencia_del_intento_es_un_rechazo_con_su_motivo()
    {
        var referencia = Guid.NewGuid();
        var falso = new CulqiFalso().Cuando(HttpMethod.Get, "/v2/charges?", HttpStatusCode.OK, RespuestasDeCulqi.Lista(RespuestasDeCulqi.CargoDenegado(referencia, "insufficient_funds")));

        var resultado = await Adaptador(falso).ConsultarEstado(referencia, iniciadoEn: Inicio);

        Assert.Equal((TipoDeResultadoDeCobro.Rechazado, MotivoDeRechazo.FondosInsuficientes), (resultado.Tipo, resultado.Motivo!.Value));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"object":"error","type":"authentication_error"}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, null)]
    [InlineData(HttpStatusCode.OK, "<html></html>")]
    [InlineData(HttpStatusCode.OK, """{"data":"no es una lista"}""")]
    public async Task Una_consulta_que_falla_o_que_no_se_entiende_es_sin_respuesta(HttpStatusCode estado, string? cuerpo)
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Get, "/v2/charges?", estado, cuerpo);

        var resultado = await Adaptador(falso).ConsultarEstado(Guid.NewGuid(), iniciadoEn: Inicio);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
    }

    [Fact]
    public async Task Sin_el_inicio_del_intento_la_consulta_busca_en_las_ultimas_dos_horas()
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Get, "/v2/charges?", HttpStatusCode.OK, RespuestasDeCulqi.Lista());

        await Adaptador(falso).ConsultarEstado(Guid.NewGuid());

        Assert.Contains($"creation_date_from={Inicio.AddHours(-2).AddMinutes(-1).ToUnixTimeSeconds()}", Assert.Single(falso.Recibidas).Ruta, StringComparison.Ordinal);
    }

    // ---------- Reembolsar y ConsultarReembolsos ----------

    private static CulqiFalso ConElCargoYSusReembolsos(params string[] reembolsos) =>
        new CulqiFalso()
            .Cuando(HttpMethod.Get, $"/v2/charges/{RespuestasDeCulqi.IdDelCargo}", HttpStatusCode.OK, RespuestasDeCulqi.Cargo(Guid.NewGuid()))
            .Cuando(HttpMethod.Get, "/v2/refunds?", HttpStatusCode.OK, RespuestasDeCulqi.Lista(reembolsos));

    [Fact]
    [Trait("Criterio", "AC-PAG-08")]
    public async Task Un_reembolso_se_pide_por_el_cargo_en_centimos_y_se_marca_con_el_identificador_del_sistema()
    {
        var reembolsoId = Guid.NewGuid();
        var falso = ConElCargoYSusReembolsos()
            .Cuando(HttpMethod.Post, "/v2/refunds", HttpStatusCode.Created, RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, 1_700_000_100_000))
            .Cuando(HttpMethod.Patch, $"/v2/refunds/{RespuestasDeCulqi.IdDeLaDevolucion}", HttpStatusCode.OK, RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, 1_700_000_100_000, reembolsoId));

        var resultado = await Adaptador(falso).Reembolsar(reembolsoId, RespuestasDeCulqi.IdDelCargo, Importe.De(50m));

        Assert.Equal((TipoDeResultadoDeReembolsoEnPasarela.Aplicado, RespuestasDeCulqi.IdDeLaDevolucion), (resultado.Tipo, resultado.Referencia));
        using var pedido = falso.De(HttpMethod.Post).Single().Json!;
        Assert.Equal(["amount", "charge_id", "reason"], pedido.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
        Assert.Equal(5000, pedido.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal(RespuestasDeCulqi.IdDelCargo, pedido.RootElement.GetProperty("charge_id").GetString());
        Assert.Equal("solicitud_comprador", pedido.RootElement.GetProperty("reason").GetString());
        using var marca = falso.De(HttpMethod.Patch).Single().Json!;
        Assert.Equal(reembolsoId.ToString("D"), marca.RootElement.GetProperty("metadata").GetProperty("reembolso_id").GetString());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_ya_aplicado_y_marcado_se_reconoce_y_no_se_pide_otro()
    {
        var reembolsoId = Guid.NewGuid();
        var falso = ConElCargoYSusReembolsos(RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, 1_700_000_100_000, reembolsoId));

        var resultado = await Adaptador(falso).Reembolsar(reembolsoId, RespuestasDeCulqi.IdDelCargo, Importe.De(50m));

        Assert.Equal((TipoDeResultadoDeReembolsoEnPasarela.Aplicado, RespuestasDeCulqi.IdDeLaDevolucion), (resultado.Tipo, resultado.Referencia));
        Assert.Empty(falso.De(HttpMethod.Post));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_reciente_de_la_misma_cuantia_sin_marca_se_toma_por_posible_duplicado_y_no_se_pide_otro()
    {
        var creadoHaceCincoMinutos = Inicio.AddMinutes(-5).ToUnixTimeMilliseconds();
        var falso = ConElCargoYSusReembolsos(RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, creadoHaceCincoMinutos));

        var resultado = await Adaptador(falso).Reembolsar(Guid.NewGuid(), RespuestasDeCulqi.IdDelCargo, Importe.De(50m));

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.SinRespuesta, resultado.Tipo);
        Assert.Empty(falso.De(HttpMethod.Post));
    }

    [Fact]
    public async Task Un_reembolso_de_otra_cuantia_de_otro_cargo_o_con_marca_ajena_no_impide_pedir_el_nuevo()
    {
        var antiguo = Inicio.AddDays(-3).ToUnixTimeMilliseconds();
        var reciente = Inicio.AddMinutes(-1).ToUnixTimeMilliseconds();
        var otroCargo = "chr_" + "test_" + "OtroCargoDeOtro01";
        var falso = ConElCargoYSusReembolsos(
                RespuestasDeCulqi.Devolucion("ref_" + "test_" + "Antiguo0000000001", RespuestasDeCulqi.IdDelCargo, 3000, antiguo, Guid.NewGuid()),
                RespuestasDeCulqi.Devolucion("ref_" + "test_" + "OtraCuantia000001", RespuestasDeCulqi.IdDelCargo, 2500, reciente),
                RespuestasDeCulqi.Devolucion("ref_" + "test_" + "OtroCargo0000001", otroCargo, 5000, reciente))
            .Cuando(HttpMethod.Post, "/v2/refunds", HttpStatusCode.Created, RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, reciente))
            .Cuando(HttpMethod.Patch, "/v2/refunds/", HttpStatusCode.OK, "{}");

        var resultado = await Adaptador(falso).Reembolsar(Guid.NewGuid(), RespuestasDeCulqi.IdDelCargo, Importe.De(50m));

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, resultado.Tipo);
        Assert.Single(falso.De(HttpMethod.Post));
    }


    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_sin_marca_de_la_misma_cuantia_bloquea_otro_sin_importar_cuanto_hace_que_se_creo()
    {
        var hacetresDias = Inicio.AddDays(-3).ToUnixTimeMilliseconds();
        var falso = ConElCargoYSusReembolsos(RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, hacetresDias));

        var resultado = await Adaptador(falso).Reembolsar(Guid.NewGuid(), RespuestasDeCulqi.IdDelCargo, Importe.De(50m));

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.SinRespuesta, resultado.Tipo);
        Assert.Empty(falso.De(HttpMethod.Post));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Un_201_con_un_resultado_de_denegacion_no_se_aprueba_aunque_el_importe_y_la_referencia_coincidan()
    {
        var solicitud = Solicitud();
        var denegado = RespuestasDeCulqi.CargoDenegado(solicitud.ReferenciaOpaca, "insufficient_funds");
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.Created, denegado);

        var resultado = await Adaptador(falso).Cobrar(solicitud);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
    }

    [Theory]
    [InlineData("""{"type":"codigo_intermedio","code":"AUT0000"}""")]
    [InlineData("""{"code":"AUT0000"}""")]
    [InlineData("""{"type":"succesfull_request"}""")]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task La_consulta_nunca_rechaza_por_un_codigo_cualquiera_solo_por_una_denegacion_positiva(string resultadoDelCargo)
    {
        var referencia = Guid.NewGuid();
        var cargo = "{\"object\":\"charge\",\"id\":\"" + RespuestasDeCulqi.IdDelCargo + "\",\"creation_date\":1700000000000,\"amount\":14000,\"currency_code\":\"PEN\",\"outcome\":" + resultadoDelCargo + ",\"metadata\":{\"referencia\":\"" + referencia.ToString("D") + "\"}}";
        var falso = new CulqiFalso().Cuando(HttpMethod.Get, "/v2/charges?", HttpStatusCode.OK, RespuestasDeCulqi.Lista(cargo));

        var consulta = await Adaptador(falso).ConsultarEstado(referencia, iniciadoEn: Inicio);

        Assert.NotEqual(TipoDeResultadoDeCobro.Rechazado, consulta.Tipo);
    }
    [Fact]
    public async Task Si_marcar_el_reembolso_falla_el_reembolso_sigue_aplicado()
    {
        var falso = ConElCargoYSusReembolsos()
            .Cuando(HttpMethod.Post, "/v2/refunds", HttpStatusCode.Created, RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, 1_700_000_100_000))
            .Cuando(HttpMethod.Patch, "/v2/refunds/", HttpStatusCode.InternalServerError, null);

        var resultado = await Adaptador(falso).Reembolsar(Guid.NewGuid(), RespuestasDeCulqi.IdDelCargo, Importe.De(50m));

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, resultado.Tipo);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Rechazado")]
    [InlineData(HttpStatusCode.PaymentRequired, "Rechazado")]
    [InlineData(HttpStatusCode.NotFound, "Rechazado")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "Rechazado")]
    [InlineData(HttpStatusCode.Unauthorized, "SinRespuesta")]
    [InlineData(HttpStatusCode.TooManyRequests, "SinRespuesta")]
    [InlineData(HttpStatusCode.InternalServerError, "SinRespuesta")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "SinRespuesta")]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_que_Culqi_rechaza_queda_rechazado_y_un_error_del_servicio_queda_sin_respuesta(HttpStatusCode estado, string esperado)
    {
        var falso = ConElCargoYSusReembolsos().Cuando(HttpMethod.Post, "/v2/refunds", estado, RespuestasDeCulqi.ErrorDeParametro);

        var resultado = await Adaptador(falso).Reembolsar(Guid.NewGuid(), RespuestasDeCulqi.IdDelCargo, Importe.De(50m));

        Assert.Equal(Enum.Parse<TipoDeResultadoDeReembolsoEnPasarela>(esperado), resultado.Tipo);
        Assert.Empty(falso.De(HttpMethod.Patch));
    }

    [Fact]
    public async Task Un_reembolso_con_estado_rechazado_en_la_respuesta_es_rechazado()
    {
        var falso = ConElCargoYSusReembolsos().Cuando(HttpMethod.Post, "/v2/refunds", HttpStatusCode.Created, RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, 1_700_000_100_000, estado: "rechazado"));

        var resultado = await Adaptador(falso).Reembolsar(Guid.NewGuid(), RespuestasDeCulqi.IdDelCargo, Importe.De(50m));

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, resultado.Tipo);
    }

    [Theory]
    [InlineData("chr_test_x/../../refunds")]
    [InlineData("chr_test_abc?x=1")]
    [InlineData("../charges")]
    [InlineData("")]
    [InlineData("ref_test_HuVDSH10IoLKtYMa")]
    public async Task La_referencia_del_cargo_solo_entra_en_la_ruta_con_la_forma_de_un_id_de_cargo(string referencia)
    {
        var falso = new CulqiFalso();
        var pasarela = Adaptador(falso);

        var reembolso = await pasarela.Reembolsar(Guid.NewGuid(), referencia, Importe.De(10m));
        var existentes = await pasarela.ConsultarReembolsos(referencia);

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, reembolso.Tipo);
        Assert.Null(existentes);
        Assert.Empty(falso.Recibidas);
    }

    [Fact]
    public async Task Consultar_los_reembolsos_lista_los_del_cargo_con_su_marca_y_omite_los_rechazados_y_los_de_otros_cargos()
    {
        var marca = Guid.NewGuid();
        var otroCargo = "chr_" + "test_" + "OtroCargoDeOtro01";
        var falso = ConElCargoYSusReembolsos(
            RespuestasDeCulqi.Devolucion(RespuestasDeCulqi.IdDeLaDevolucion, RespuestasDeCulqi.IdDelCargo, 5000, 1_700_000_100_000, marca),
            RespuestasDeCulqi.Devolucion("ref_" + "test_" + "SinMarca000000001", RespuestasDeCulqi.IdDelCargo, 1000, 1_700_000_200_000),
            RespuestasDeCulqi.Devolucion("ref_" + "test_" + "Rechazada00000001", RespuestasDeCulqi.IdDelCargo, 1000, 1_700_000_200_000, estado: "rechazado"),
            RespuestasDeCulqi.Devolucion("ref_" + "test_" + "OtroCargo0000001", otroCargo, 1000, 1_700_000_200_000));

        var existentes = await Adaptador(falso).ConsultarReembolsos(RespuestasDeCulqi.IdDelCargo);

        Assert.NotNull(existentes);
        Assert.Equal(2, existentes.Count);
        Assert.Equal((marca, RespuestasDeCulqi.IdDeLaDevolucion, Importe.De(50m)), (existentes[0].ReembolsoId, existentes[0].Referencia, existentes[0].Monto));
        Assert.Null(existentes[1].ReembolsoId);
        Assert.Contains("creation_date_from=1699999940", falso.De(HttpMethod.Get).Last().Ruta, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Un_cargo_inexistente_no_tiene_reembolsos_y_una_pagina_llena_o_un_error_es_no_se_sabe()
    {
        var sinCargo = new CulqiFalso().Cuando(HttpMethod.Get, "/v2/charges/", HttpStatusCode.NotFound, RespuestasDeCulqi.ErrorDeParametro);
        Assert.Empty((await Adaptador(sinCargo).ConsultarReembolsos(RespuestasDeCulqi.IdDelCargo))!);

        var llena = ConElCargoYSusReembolsos(Enumerable.Range(0, 100).Select(i => RespuestasDeCulqi.Devolucion("ref_" + "test_" + $"Relleno{i:D10}", RespuestasDeCulqi.IdDelCargo, 100, 1_700_000_100_000)).ToArray());
        Assert.Null(await Adaptador(llena).ConsultarReembolsos(RespuestasDeCulqi.IdDelCargo));
        var reintento = await Adaptador(llena).Reembolsar(Guid.NewGuid(), RespuestasDeCulqi.IdDelCargo, Importe.De(50m));
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.SinRespuesta, reintento.Tipo);
        Assert.Empty(llena.De(HttpMethod.Post));

        var cae = new CulqiFalso().Cuando(HttpMethod.Get, "/v2/charges/", HttpStatusCode.ServiceUnavailable, null);
        Assert.Null(await Adaptador(cae).ConsultarReembolsos(RespuestasDeCulqi.IdDelCargo));
    }

    // ---------- InterpretarAviso ----------

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void Un_aviso_de_cargo_con_el_recurso_como_texto_o_como_objeto_da_el_evento_y_la_referencia_opaca()
    {
        var referencia = Guid.NewGuid();
        var cargo = RespuestasDeCulqi.Cargo(referencia);
        var comoTexto = JsonSerializer.Serialize(new { @object = "event", id = "evt_" + "test_" + "bOoowQYioJrxqBlr", type = "charge.creation.succeeded", creation_date = 1547246714217, data = cargo });
        var comoObjeto = $$"""{"object":"event","id":"evt_test_otro","type":"charge.creation.failed","data":{{cargo}}}""";

        var uno = Adaptador(new CulqiFalso()).InterpretarAviso(System.Text.Encoding.UTF8.GetBytes(comoTexto));
        var dos = Adaptador(new CulqiFalso()).InterpretarAviso(System.Text.Encoding.UTF8.GetBytes(comoObjeto));

        Assert.Equal(new AvisoDeLaPasarela("evt_" + "test_" + "bOoowQYioJrxqBlr", referencia), uno);
        Assert.Equal(new AvisoDeLaPasarela("evt_test_otro", referencia), dos);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no es json")]
    [InlineData("[]")]
    [InlineData("""{"object":"event","id":"evt_test_x","type":"order.status.changed","data":"{}"}""")]
    [InlineData("""{"object":"event","id":"sin-prefijo","type":"charge.creation.succeeded","data":"{}"}""")]
    [InlineData("""{"object":"event","id":"evt_test_x","type":"charge.creation.succeeded","data":"{ roto"}""")]
    [InlineData("""{"object":"event","id":"evt_test_x","type":"charge.creation.succeeded","data":"{\"metadata\":{\"referencia\":\"no-es-guid\"}}"}""")]
    [InlineData("""{"object":"event","id":"evt_test_x","type":"charge.creation.succeeded","data":"{\"metadata\":{}}"}""")]
    public void Un_aviso_que_no_se_entiende_o_que_no_es_de_un_cargo_es_null(string cuerpo)
    {
        Assert.Null(Adaptador(new CulqiFalso()).InterpretarAviso(System.Text.Encoding.UTF8.GetBytes(cuerpo)));
    }

    // ---------- Resiliencia del cliente HTTP (PAG-07, SEG-14) ----------

    private static HttpClient ClienteResiliente(CulqiFalso falso)
    {
        var servicios = new ServiceCollection();
        servicios.AgregarClienteDeCulqi().ConfigurePrimaryHttpMessageHandler(() => falso);
        return servicios.BuildServiceProvider().GetRequiredService<IHttpClientFactory>().CreateClient(OpcionesDeCulqi.NombreDelCliente);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task El_cobro_y_el_reembolso_nunca_se_reintentan_y_una_lectura_se_reintenta_una_sola_vez()
    {
        var falso = new CulqiFalso()
            .Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.ServiceUnavailable, null)
            .Cuando(HttpMethod.Get, "/v2/charges?", HttpStatusCode.ServiceUnavailable, null);
        var pasarela = new CulqiPasarelaDePago(ClienteResiliente(falso), new OpcionesDeCulqi(new ValorSecreto(LlaveDePrueba)), _reloj);

        var cobro = await pasarela.Cobrar(Solicitud());
        var consulta = await pasarela.ConsultarEstado(Guid.NewGuid(), iniciadoEn: Inicio);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, cobro.Tipo);
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, consulta.Tipo);
        Assert.Single(falso.De(HttpMethod.Post));
        Assert.Equal(1 + ClienteHttpDeCulqi.ReintentosDeLecturas, falso.De(HttpMethod.Get).Count());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Con_fallos_repetidos_el_circuito_se_abre_y_el_cobro_falla_al_instante_sin_llamar_a_Culqi_y_sin_aprobar()
    {
        var falso = new CulqiFalso().Cuando(HttpMethod.Post, "/v2/charges", HttpStatusCode.InternalServerError, null);
        var pasarela = new CulqiPasarelaDePago(ClienteResiliente(falso), new OpcionesDeCulqi(new ValorSecreto(LlaveDePrueba)), _reloj);

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await pasarela.Cobrar(Solicitud())).Tipo);
        }

        Assert.InRange(falso.De(HttpMethod.Post).Count(), 5, 9);
    }

    // ---------- Texto del módulo ----------

    [Fact]
    public void Las_opciones_nunca_repiten_la_llave_privada()
    {
        var opciones = new OpcionesDeCulqi(new ValorSecreto(LlaveDePrueba));

        Assert.DoesNotContain(LlaveDePrueba, opciones.ToString(), StringComparison.Ordinal);
        Assert.Equal("https://api.culqi.com/v2/", OpcionesDeCulqi.UrlDeLaApi);
    }

    private sealed class RelojDeCulqi(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }
}
