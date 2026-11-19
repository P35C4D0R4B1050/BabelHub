using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Carrito;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T26c: los adaptadores reales de Carrito sobre lo que entregó el carril de plataforma (CAR-17, CAR-18, CAR-31, AC-CAR-14, AC-CAR-15): los límites con la política de cada operación y el paso al pago que falla cerrado, la auditoría solo con conteos y motivos, la marca
/// de cancelación, el consentimiento por huella y las alertas.
/// </summary>
public sealed class AdaptadoresRealesDelCarritoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private sealed class LimitesFalsos : IControlDeLimites
    {
        public List<(ClaveDePolitica Politica, string? Sujeto, string? Clave)> Llamadas { get; } = [];

        public Func<ClaveDePolitica, ResultadoDeLimite> Respuesta { get; set; } = _ => ResultadoDeLimite.PermitidoSinContar();

        public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
        {
            Llamadas.Add((politica, sujeto, claveDelLlamador));
            return Task.FromResult(Respuesta(politica));
        }

        public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class LimitesQueLanzan : IControlDeLimites
    {
        public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default) =>
            throw new InvalidOperationException("sin almacén");

        public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Cada_operacion_usa_su_politica_con_la_cuenta_como_sujeto_o_el_origen_si_no_hay_cuenta()
    {
        var limites = new LimitesFalsos();
        var limitador = new LimitadorDelCarritoSobreLimites(limites);

        await limitador.CrearCarritoDeVisitante();
        await limitador.EditarLineas(null);
        await limitador.EditarLineas(Cuenta);
        await limitador.RevisarCheckout(Cuenta);
        await limitador.PasarAlPago(Cuenta, "pedido-1");
        await limitador.VolverOReiniciar(Cuenta);

        var cuenta = Cuenta.ToString("N");
        Assert.Equal(
            [
                (PoliticasDeLimite.CarritosDeVisitantePorOrigen, null, null),
                (PoliticasDeLimite.EdicionDeLineasPorOrigen, null, null),
                (PoliticasDeLimite.EdicionDeLineasPorCuenta, cuenta, null),
                (PoliticasDeLimite.RevisionDelCheckoutPorCuenta, cuenta, null),
                (PoliticasDeLimite.PasosAlPagoPorCuenta, cuenta, "pedido-1"),
                (PoliticasDeLimite.VolverOReiniciarPorCuenta, cuenta, null),
            ],
            limites.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-14")]
    public async Task Un_exceso_trae_el_instante_de_reintento_y_un_almacen_caido_en_el_paso_al_pago_es_no_disponible()
    {
        var desde = Ahora.AddMinutes(20);
        var limites = new LimitesFalsos { Respuesta = _ => ResultadoDeLimite.Excedido("pasos-al-pago-por-cuenta", 1200, desde) };
        var excedido = await new LimitadorDelCarritoSobreLimites(limites).PasarAlPago(Cuenta, "pedido-1");
        limites.Respuesta = _ => ResultadoDeLimite.NoDisponible();
        var cerrado = await new LimitadorDelCarritoSobreLimites(limites).PasarAlPago(Cuenta, "pedido-1");
        var lanza = await new LimitadorDelCarritoSobreLimites(new LimitesQueLanzan()).PasarAlPago(Cuenta, "pedido-1");

        Assert.Equal(new ResultadoDeLimiteDelCarrito(DecisionDeLimiteDelCarrito.Excedido, desde), excedido);
        Assert.Equal(DecisionDeLimiteDelCarrito.NoDisponible, cerrado.Decision);
        Assert.Equal(DecisionDeLimiteDelCarrito.NoDisponible, lanza.Decision);
    }

    [Fact]
    public async Task Un_fallo_del_contador_en_una_edicion_no_bloquea_pero_en_el_paso_al_pago_si()
    {
        var limitador = new LimitadorDelCarritoSobreLimites(new LimitesQueLanzan());

        Assert.Equal(DecisionDeLimiteDelCarrito.Permitido, (await limitador.EditarLineas(Cuenta)).Decision);
        Assert.Equal(DecisionDeLimiteDelCarrito.Permitido, (await limitador.VolverOReiniciar(Cuenta)).Decision);
        Assert.Equal(DecisionDeLimiteDelCarrito.NoDisponible, (await limitador.PasarAlPago(Cuenta, "pedido-1")).Decision);
    }

    private sealed class AuditoriaEspia : IAuditoria
    {
        public List<SolicitudDeRegistro> Solicitudes { get; } = [];

        public bool Falla { get; set; }

        public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new AuditoriaNoDisponibleException();
            }

            Solicitudes.Add(solicitud);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task La_auditoria_registra_cada_operacion_con_su_accion_y_solo_conteos_y_motivos()
    {
        var espia = new AuditoriaEspia();
        IAuditoriaDelCarrito auditoria = new AuditoriaDelCarritoSobreAuditoria(espia);

        await auditoria.FusionAplicada(Cuenta, 3, [MotivoDeAjusteDeAviso.SinStock, MotivoDeAjusteDeAviso.YaPoseida]);
        await auditoria.EdicionesRetiradas(2, 5, 4, 1);
        await auditoria.CheckoutTerminado("volver");
        await auditoria.CarritoEliminadoPorIdentidad("cancelacion");
        await auditoria.PurgaRealizada("purga-de-carritos", 7);
        await auditoria.PasoAlPagoAbortado("reserva-expirada");
        await auditoria.PasoAlPagoIniciado(true, false);

        Assert.Equal(
            [
                AccionesAuditables.FusionDeCarritoAplicada, AccionesAuditables.EdicionesRetiradasDeCarritos, AccionesAuditables.CheckoutTerminado, AccionesAuditables.CarritoEliminadoPorIdentidad,
                AccionesAuditables.PurgaDeCarritosRealizada, AccionesAuditables.PasoAlPagoAbortado, AccionesAuditables.PasoAlPagoIniciado,
            ],
            espia.Solicitudes.Select(s => s.Accion));
        Assert.Equal(("3", "SinStock,YaPoseida"), (espia.Solicitudes[0].Detalle["lineasDelVisitante"], espia.Solicitudes[0].Detalle["ajustes"]));
        Assert.Equal(new[] { "2", "5", "4", "1" }, new[] { "ediciones", "carritosAfectados", "lineasQuitadas", "lineasAnotadas" }.Select(k => espia.Solicitudes[1].Detalle[k]));
        Assert.Equal(("true", "false"), (espia.Solicitudes[6].Detalle["conImpresos"], espia.Solicitudes[6].Detalle["aDomicilio"]));
        // Nada identifica a la cuenta, al pedido ni al visitante.
        Assert.All(espia.Solicitudes, s =>
        {
            Assert.Null(s.ObjetoId);
            Assert.Null(s.ObjetoTipo);
            Assert.DoesNotContain(Cuenta.ToString(), string.Join('|', s.Detalle.Values), StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public async Task Si_no_se_puede_auditar_la_excepcion_sube_y_la_operacion_no_se_aplica()
    {
        var espia = new AuditoriaEspia { Falla = true };

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => new AuditoriaDelCarritoSobreAuditoria(espia).PasoAlPagoIniciado(false, false));
    }

    private sealed class MarcaFalsa(EstadoDeLaMarca estado) : IMarcaDeCancelacionDeCuenta
    {
        public Task<EstadoDeLaMarca> LeerConBloqueo(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(estado);
    }

    [Theory]
    [Trait("Criterio", "AC-CAR-15")]
    [InlineData(EstadoDeLaMarca.Libre, EstadoDeLaMarcaDeCancelacion.Libre)]
    [InlineData(EstadoDeLaMarca.Diferida, EstadoDeLaMarcaDeCancelacion.Diferida)]
    [InlineData(EstadoDeLaMarca.EnEjecucion, EstadoDeLaMarcaDeCancelacion.EnEjecucion)]
    [InlineData(EstadoDeLaMarca.Ejecutada, EstadoDeLaMarcaDeCancelacion.Ejecutada)]
    public async Task La_marca_de_cancelacion_se_traduce_estado_por_estado(EstadoDeLaMarca de, EstadoDeLaMarcaDeCancelacion a)
    {
        Assert.Equal(a, await new MarcaDeCancelacionSobreIdentidad(new MarcaFalsa(de)).LeerConBloqueo(Cuenta));
    }

    private sealed class CuentaFalsa : ICuentaDelCliente
    {
        public Func<Resultado<OfertaDeConsentimientoDeAvisos>>? Oferta { get; set; }

        public Func<string, Resultado<RespuestaUniforme>>? Guardado { get; set; }

        public Task<Resultado<OfertaDeConsentimientoDeAvisos>> ConsultarLaOfertaDeConsentimiento(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(Oferta!());

        public Task<Resultado<RespuestaUniforme>> GuardarElConsentimientoPorHuella(Guid usuarioId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default) => Task.FromResult(Guardado!(huellaEnmascaradaDelNumero));

        public Task<Resultado<CuentaDelCliente>> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<CuentaDelCliente>> CorregirElNombre(Guid usuarioId, string nombre, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<RespuestaUniforme>> CambiarLaContrasena(Guid usuarioId, ValorSecreto actual, ValorSecreto nueva, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<CuentaDelCliente>> GuardarElNumeroDeWhatsApp(Guid usuarioId, string? numero, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<CuentaDelCliente>> CambiarElConsentimientoDeWhatsApp(Guid usuarioId, bool consiente, string? numeroConfirmado, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<SecretoDelSegundoFactor>> IniciarElSegundoFactor(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<IReadOnlyList<ValorSecreto>>> ConfirmarElSegundoFactor(Guid usuarioId, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<RespuestaUniforme>> DesactivarElSegundoFactor(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto codigo, ValorSecreto? renovacionDeLaSesion = null, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Reconfirmar(Guid usuarioId, ValorSecreto contrasena, ValorSecreto? codigoDelSegundoFactor, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<EstadoDeLaCancelacionDeCuenta>> Desistir(Guid usuarioId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<EstadoDeLaCancelacionDeCuenta>> MostrarElAviso(Guid usuarioId, bool desdeUnaSesionReconocida, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<IReadOnlyList<ValorSecreto>>> RegenerarLosCodigosDeRespaldo(Guid usuarioId, ValorSecreto contrasenaActual, ValorSecreto codigo, ValorSecreto? marcaDeDispositivo = null, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<IReadOnlyList<AvisoDeCuenta>>> ListarLosAvisos(Guid usuarioId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<RespuestaUniforme>> MarcarElAvisoComoVisto(Guid usuarioId, Guid avisoId, bool desdeUnaSesionReconocida, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-16")]
    public async Task La_oferta_de_consentimiento_lleva_solo_la_huella_y_cualquier_falla_es_no_se_ofrece()
    {
        var cuenta = new CuentaFalsa { Oferta = () => Resultado<OfertaDeConsentimientoDeAvisos>.Exito(new OfertaDeConsentimientoDeAvisos(true, false, "***321")) };
        var adaptador = new ConsentimientoDeAvisosSobreIdentidad(cuenta);

        var ofrece = await adaptador.Consultar(Cuenta);
        cuenta.Oferta = () => Resultado<OfertaDeConsentimientoDeAvisos>.Exito(new OfertaDeConsentimientoDeAvisos(false, true, null));
        var consiente = await adaptador.Consultar(Cuenta);
        cuenta.Oferta = () => Resultado<OfertaDeConsentimientoDeAvisos>.Exito(new OfertaDeConsentimientoDeAvisos(false, false, null));
        var sinNumero = await adaptador.Consultar(Cuenta);
        cuenta.Oferta = () => Resultado<OfertaDeConsentimientoDeAvisos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"));
        var falla = await adaptador.Consultar(Cuenta);
        cuenta.Oferta = () => throw new InvalidOperationException();
        var lanza = await adaptador.Consultar(Cuenta);

        Assert.Equal(new OfertaDeConsentimiento(EstadoDeLaOfertaDeConsentimiento.SeOfrece, "***321"), ofrece);
        Assert.Equal(EstadoDeLaOfertaDeConsentimiento.YaConsiente, consiente.Estado);
        Assert.All(new[] { sinNumero, falla, lanza }, o => Assert.Equal(OfertaDeConsentimiento.NoSeOfrece, o));
    }

    [Fact]
    public async Task Guardar_el_consentimiento_es_verdadero_solo_si_Identidad_lo_confirma_y_falso_ante_un_cambio_de_numero_o_una_falla()
    {
        var cuenta = new CuentaFalsa { Guardado = _ => Resultado<RespuestaUniforme>.Exito(default!) };
        var adaptador = new ConsentimientoDeAvisosSobreIdentidad(cuenta);

        var guardado = await adaptador.Guardar(Cuenta, "***321");
        cuenta.Guardado = _ => Resultado<RespuestaUniforme>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, "huella-no-coincide"));
        var cambio = await adaptador.Guardar(Cuenta, "***321");
        cuenta.Guardado = _ => throw new TimeoutException();
        var lanza = await adaptador.Guardar(Cuenta, "***321");

        Assert.True(guardado);
        Assert.False(cambio);
        Assert.False(lanza);
    }

    private sealed class AlertasFalsas : IPublicadorDeAlertasTecnicas
    {
        public List<AlertaTecnica> Publicadas { get; } = [];

        public bool Falla { get; set; }

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException();
            }

            Publicadas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora => AdaptadoresRealesDelCarritoTests.Ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    [Fact]
    public async Task Cada_alerta_sale_con_un_motivo_cerrado_y_sin_datos_y_una_falla_al_publicar_no_interrumpe()
    {
        var alertas = new AlertasFalsas();
        var adaptador = new AlertasDelCarritoSobrePublicador(alertas, new RelojFijo());

        foreach (var tipo in Enum.GetValues<TipoDeAlertaDelCarrito>())
        {
            await adaptador.Publicar(tipo, 3);
        }

        alertas.Falla = true;
        await adaptador.Publicar(TipoDeAlertaDelCarrito.TrabajoSinCorrer, 1);

        Assert.Equal(Enum.GetValues<TipoDeAlertaDelCarrito>().Length, alertas.Publicadas.Count);
        Assert.Equal(alertas.Publicadas.Count, alertas.Publicadas.Select(a => a.Motivo).Distinct().Count());
        Assert.All(alertas.Publicadas, a =>
        {
            Assert.StartsWith("carrito-", a.Motivo, StringComparison.Ordinal);
            Assert.Equal(Ahora, a.Instante);
        });
    }
}
