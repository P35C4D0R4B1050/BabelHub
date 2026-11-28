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
