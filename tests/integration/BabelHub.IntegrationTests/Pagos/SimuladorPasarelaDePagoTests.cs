using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using MedioDelDominio = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T25: la pasarela simulada y la nula (AC-PAG-03, AC-PAG-06, AC-PAG-15, AC-PAG-16), sin base de datos: el token elige el escenario (aprobado, rechazado con motivo, otro importe, más de 10 segundos, aplica y no responde, sin respuesta, falla de red),
/// los cargos y los reembolsos se recuerdan para consultarlos, el simulador no es idempotente a propósito, un aviso se lee de su cuerpo y la pasarela nula nunca aprueba.
/// </summary>
public sealed class SimuladorPasarelaDePagoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 10, 0, 0, TimeSpan.Zero);

    private readonly RelojManual _reloj = new(Inicio);

    private SimuladorPasarelaDePago Simulador(Func<TimeSpan, CancellationToken, Task>? espera = null) => new(_reloj, espera);

    private static SolicitudDeCobroEnPasarela Solicitud(string token, decimal importe = 140m, MedioDelDominio medio = MedioDelDominio.Tarjeta, Guid? referencia = null) =>
        new(referencia ?? Guid.NewGuid(), Importe.De(importe), medio, TokenDelMedioDePago.Crear(token), CorreoDelTitular: null);

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Un_token_aprobado_aprueba_por_el_importe_pedido_con_la_referencia_opaca_del_intento()
    {
        var simulador = Simulador();
        var solicitud = Solicitud(EscenariosDelSimulador.Aprobado);

        var resultado = await simulador.Cobrar(solicitud);

        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, resultado.Tipo);
        Assert.Equal(solicitud.Importe, resultado.Importe);
        Assert.Equal(solicitud.ReferenciaOpaca, resultado.ReferenciaOpaca);
        Assert.Equal("PEN", resultado.Moneda);
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, resultado.Validar(IntentoDeCobro.Iniciar(solicitud.ReferenciaOpaca, Guid.NewGuid(), MedioDelDominio.Tarjeta, solicitud.Importe, Inicio)).Tipo);
    }

    [Theory]
    [InlineData(EscenariosDelSimulador.FondosInsuficientes, MotivoDeRechazo.FondosInsuficientes)]
    [InlineData(EscenariosDelSimulador.TarjetaRechazada, MotivoDeRechazo.TarjetaRechazada)]
    [InlineData(EscenariosDelSimulador.YapeRechazado, MotivoDeRechazo.YapeRechazado)]
    [InlineData(EscenariosDelSimulador.AutenticacionRequerida, MotivoDeRechazo.AutenticacionRequerida)]
    [InlineData("tok_desconocido", MotivoDeRechazo.Otro)]
    public async Task Los_tokens_de_rechazo_y_los_desconocidos_rechazan_con_su_motivo_y_nunca_aprueban(string token, MotivoDeRechazo motivo)
    {
        var simulador = Simulador();
        var solicitud = Solicitud(token);

        var resultado = await simulador.Cobrar(solicitud);
        var consulta = await simulador.ConsultarEstado(solicitud.ReferenciaOpaca);

        Assert.Equal((TipoDeResultadoDeCobro.Rechazado, motivo), (resultado.Tipo, resultado.Motivo!.Value));
        Assert.Equal((TipoDeResultadoDeCobro.Rechazado, motivo), (consulta.Tipo, consulta.Motivo!.Value));
    }

    [Fact]
    public async Task El_token_de_otro_importe_aprueba_por_un_sol_mas_y_la_validacion_lo_trata_como_dudoso()
    {
        var simulador = Simulador();
        var solicitud = Solicitud(EscenariosDelSimulador.OtroImporte);

        var resultado = await simulador.Cobrar(solicitud);
        var validado = resultado.Validar(IntentoDeCobro.Iniciar(solicitud.ReferenciaOpaca, Guid.NewGuid(), MedioDelDominio.Tarjeta, solicitud.Importe, Inicio));

        Assert.Equal(Importe.De(141m), resultado.Importe);
        Assert.Equal(MotivoDeDuda.ImporteDistinto, validado.Duda);
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, validado.Tipo);
        Assert.True(validado.RequiereConciliacionManual);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task El_cobro_lento_tarda_mas_que_el_plazo_y_si_se_cancela_la_espera_el_cargo_queda_pendiente_hasta_que_pasa_el_tiempo()
    {
        TimeSpan? esperado = null;
        var simulador = Simulador(async (plazo, cancelacion) =>
        {
            esperado = plazo;
            await Task.Delay(Timeout.Infinite, cancelacion);
        });
        var solicitud = Solicitud(EscenariosDelSimulador.Lento);
        using var plazoDeLaLlamada = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => simulador.Cobrar(solicitud, plazoDeLaLlamada.Token));

        Assert.True(esperado > PlazosDelPago.TiempoDeRespuesta);
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await simulador.ConsultarEstado(solicitud.ReferenciaOpaca)).Tipo);
        _reloj.Avanzar(SimuladorPasarelaDePago.RetrasoDelCobroLento - TimeSpan.FromSeconds(1));
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await simulador.ConsultarEstado(solicitud.ReferenciaOpaca)).Tipo);
        _reloj.Avanzar(TimeSpan.FromSeconds(1));
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, (await simulador.ConsultarEstado(solicitud.ReferenciaOpaca)).Tipo);
    }

    [Fact]
    public async Task El_cobro_lento_sin_cancelar_responde_aprobado_al_terminar_la_espera()
    {
        var simulador = Simulador((_, _) => Task.CompletedTask);

        var resultado = await simulador.Cobrar(Solicitud(EscenariosDelSimulador.Lento));

        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, resultado.Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Un_cobro_que_se_aplica_sin_responder_responde_sin_respuesta_y_la_consulta_lo_encuentra_aprobado()
    {
        var simulador = Simulador();
        var solicitud = Solicitud(EscenariosDelSimulador.AplicaSinResponder);

        var resultado = await simulador.Cobrar(solicitud);
        var consulta = await simulador.ConsultarEstado(solicitud.ReferenciaOpaca);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, resultado.Tipo);
        Assert.Equal(TipoDeResultadoDeCobro.Aprobado, consulta.Tipo);
        Assert.Equal(1, simulador.CargosDe(solicitud.ReferenciaOpaca));
    }

    [Fact]
    public async Task Sin_respuesta_no_cobra_nada_y_la_consulta_de_un_cargo_desconocido_tampoco_responde()
    {
        var simulador = Simulador();
        var solicitud = Solicitud(EscenariosDelSimulador.SinRespuesta);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await simulador.Cobrar(solicitud)).Tipo);
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await simulador.ConsultarEstado(solicitud.ReferenciaOpaca)).Tipo);
        Assert.Equal(0, simulador.CargosDe(solicitud.ReferenciaOpaca));
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await simulador.ConsultarEstado(Guid.NewGuid())).Tipo);
    }

    [Fact]
    public async Task La_falla_de_red_lanza_y_no_deja_ningun_cargo()
    {
        var simulador = Simulador();
        var solicitud = Solicitud(EscenariosDelSimulador.FallaDeRed);

        await Assert.ThrowsAsync<HttpRequestException>(() => simulador.Cobrar(solicitud));

        Assert.Equal(0, simulador.CargosDe(solicitud.ReferenciaOpaca));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task El_simulador_no_es_idempotente_cobrar_dos_veces_con_la_misma_referencia_crea_dos_cargos()
    {
        var simulador = Simulador();
        var referencia = Guid.NewGuid();

        var primero = await simulador.Cobrar(Solicitud(EscenariosDelSimulador.Aprobado, referencia: referencia));
        var segundo = await simulador.Cobrar(Solicitud(EscenariosDelSimulador.Aprobado, referencia: referencia));

        Assert.NotEqual(primero.ReferenciaDePasarela, segundo.ReferenciaDePasarela);
        Assert.Equal(2, simulador.CargosDe(referencia));
        Assert.Equal(2, simulador.LlamadasDeCobrar);
    }

    [Fact]
    public async Task Un_reembolso_total_se_aplica_y_se_lista_y_uno_que_supera_el_cargo_se_rechaza()
    {
        var simulador = Simulador();
        var cobro = await simulador.Cobrar(Solicitud(EscenariosDelSimulador.Aprobado));
        var reembolsoId = Guid.NewGuid();

        var parcial = await simulador.Reembolsar(reembolsoId, cobro.ReferenciaDePasarela!, Importe.De(40m));
        var exceso = await simulador.Reembolsar(Guid.NewGuid(), cobro.ReferenciaDePasarela!, Importe.De(101m));
        var resto = await simulador.Reembolsar(Guid.NewGuid(), cobro.ReferenciaDePasarela!, Importe.De(100m));
        var lista = await simulador.ConsultarReembolsos(cobro.ReferenciaDePasarela!);

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, parcial.Tipo);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, exceso.Tipo);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, resto.Tipo);
        Assert.Equal(2, lista!.Count);
        Assert.Equal(reembolsoId, lista[0].ReembolsoId);
        Assert.Equal(Importe.De(40m), lista[0].Monto);
    }

    [Fact]
    public async Task Un_reembolso_de_un_cargo_desconocido_o_rechazado_se_rechaza()
    {
        var simulador = Simulador();
        var rechazado = Solicitud(EscenariosDelSimulador.TarjetaRechazada);
        await simulador.Cobrar(rechazado);

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, (await simulador.Reembolsar(Guid.NewGuid(), "ch_sim_1", Importe.De(10m))).Tipo);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, (await simulador.Reembolsar(Guid.NewGuid(), "ch_desconocido", Importe.De(10m))).Tipo);
        Assert.Empty((await simulador.ConsultarReembolsos("ch_desconocido"))!);
    }

    [Fact]
    public async Task El_token_de_reembolso_sin_respuesta_aplica_el_reembolso_y_no_responde_y_la_consulta_lo_lista()
    {
        var simulador = Simulador();
        var cobro = await simulador.Cobrar(Solicitud(EscenariosDelSimulador.ReembolsoSinRespuesta));
        var reembolsoId = Guid.NewGuid();

        var resultado = await simulador.Reembolsar(reembolsoId, cobro.ReferenciaDePasarela!, Importe.De(140m));
        var lista = await simulador.ConsultarReembolsos(cobro.ReferenciaDePasarela!);

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.SinRespuesta, resultado.Tipo);
        Assert.Equal(reembolsoId, Assert.Single(lista!).ReembolsoId);
    }

    [Fact]
    public async Task El_token_de_reembolso_rechazado_rechaza_cualquier_reembolso()
    {
        var simulador = Simulador();
        var cobro = await simulador.Cobrar(Solicitud(EscenariosDelSimulador.ReembolsoRechazado));

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Rechazado, (await simulador.Reembolsar(Guid.NewGuid(), cobro.ReferenciaDePasarela!, Importe.De(10m))).Tipo);
    }

    [Fact]
    public async Task Si_Yape_no_reembolsa_por_la_API_el_reembolso_de_un_cobro_con_Yape_es_no_soportado_y_el_de_tarjeta_se_aplica()
    {
        var simulador = Simulador();
        simulador.ReembolsaYape = false;
        var conYape = await simulador.Cobrar(Solicitud(EscenariosDelSimulador.Aprobado, medio: MedioDelDominio.Yape));
        var conTarjeta = await simulador.Cobrar(Solicitud(EscenariosDelSimulador.Aprobado, medio: MedioDelDominio.Tarjeta));

        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.NoSoportado, (await simulador.Reembolsar(Guid.NewGuid(), conYape.ReferenciaDePasarela!, Importe.De(10m))).Tipo);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.Aplicado, (await simulador.Reembolsar(Guid.NewGuid(), conTarjeta.ReferenciaDePasarela!, Importe.De(10m))).Tipo);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public void Un_aviso_se_lee_de_su_cuerpo_y_lo_que_no_se_entiende_es_nulo()
    {
        var simulador = Simulador();
        var referencia = Guid.NewGuid();

        var aviso = simulador.InterpretarAviso(SimuladorPasarelaDePago.CrearAviso("evt_1", referencia));

        Assert.Equal(("evt_1", referencia), (aviso!.IdDeEntrada, aviso.ReferenciaOpaca));
        Assert.Null(simulador.InterpretarAviso("no es json"u8.ToArray()));
        Assert.Null(simulador.InterpretarAviso("[]"u8.ToArray()));
        Assert.Null(simulador.InterpretarAviso("{\"id\":\"evt\"}"u8.ToArray()));
        Assert.Null(simulador.InterpretarAviso(SimuladorPasarelaDePago.CrearAviso(string.Empty, referencia)));
        Assert.Null(simulador.InterpretarAviso(SimuladorPasarelaDePago.CrearAviso("evt_2", Guid.Empty)));
        Assert.Null(simulador.InterpretarAviso(SimuladorPasarelaDePago.CrearAviso(new string('x', 101), referencia)));
    }

    [Fact]
    public void El_texto_de_la_solicitud_no_repite_el_token()
    {
        Assert.DoesNotContain(EscenariosDelSimulador.Aprobado, Solicitud(EscenariosDelSimulador.Aprobado).ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task La_pasarela_nula_nunca_aprueba_ni_reembolsa_ni_entiende_avisos()
    {
        var nula = new NuloPasarelaDePago();
        var solicitud = Solicitud(EscenariosDelSimulador.Aprobado);

        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await nula.Cobrar(solicitud)).Tipo);
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await nula.ConsultarEstado(solicitud.ReferenciaOpaca)).Tipo);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.SinRespuesta, (await nula.Reembolsar(Guid.NewGuid(), "ch_1", Importe.De(10m))).Tipo);
        Assert.Null(await nula.ConsultarReembolsos("ch_1"));
        Assert.Null(nula.InterpretarAviso(SimuladorPasarelaDePago.CrearAviso("evt_1", Guid.NewGuid())));
    }

    private sealed class RelojManual(DateTimeOffset inicio) : IReloj
    {
        public DateTimeOffset Ahora { get; private set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.UtcDateTime.AddHours(-5));

        public void Avanzar(TimeSpan tiempo) => Ahora += tiempo;
    }
}
