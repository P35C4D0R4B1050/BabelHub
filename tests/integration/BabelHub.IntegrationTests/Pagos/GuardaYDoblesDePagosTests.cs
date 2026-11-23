using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>La guarda de arranque de Pagos y sus dobles provisionales (T08; PAG-12, AC-PAG-10, D05): la lista blanca por entorno, los secretos y que nunca se publique con un cobro simulado.</summary>
public sealed class GuardaYDoblesDePagosTests
{
    private const string ClaveDeIntentos = "clave-de-intentos-de-prueba-de-32-caracteres-aprox";
    private const string Anterior = "clave-anterior-de-intentos-de-32-caracteres-aprox!";
    private const string LlaveDePruebas = "sk_test_llave-de-pruebas-1234567890";
    private const string LlaveDeProduccion = "sk_live_llave-de-produccion-1234567890";

    private static OpcionesDeArranqueDePagos Opciones(
        string? pasarela = "simulador",
        string entorno = "Development",
        bool sinLlamadasReales = false,
        bool nula = false,
        bool pedidos = true,
        string? llave = null,
        string? clave = ClaveDeIntentos,
        string? anterior = null) =>
        new(pasarela, entorno, sinLlamadasReales, nula, pedidos, llave, clave, anterior);

    private static ConfiguracionDePagosInvalidaException Rechaza(OpcionesDeArranqueDePagos opciones) =>
        Assert.Throws<ConfiguracionDePagosInvalidaException>(() => GuardaDeArranqueDePagos.Exigir(opciones));

    [Theory]
    [Trait("Criterio", "AC-PAG-10")]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Staging")]
    [InlineData("staging")]
    public void El_simulador_se_acepta_en_desarrollo_en_pruebas_y_en_staging(string entorno)
    {
        GuardaDeArranqueDePagos.Exigir(Opciones(entorno: entorno));
        Assert.True(GuardaDeArranqueDePagos.AdmiteElSimulador(entorno));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-10")]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("Production")]
    [InlineData("production")]
    [InlineData("")]
    [InlineData("Qa")]
    [InlineData("Staging2")]
    public void El_simulador_se_rechaza_en_produccion_y_en_cualquier_otro_entorno(string entorno)
    {
        var error = Rechaza(Opciones(entorno: entorno));

        Assert.Contains("simulador", error.Message, StringComparison.Ordinal);
        Assert.False(GuardaDeArranqueDePagos.AdmiteElSimulador(entorno));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void Staging_nunca_cobra_a_ciegas_sin_pasarela_ni_con_culqi_ni_con_una_llave()
    {
        // Sin pasarela declarada o con una desconocida, también en Staging: no hay valor por defecto que cobre.
        Assert.Contains(GuardaDeArranqueDePagos.VariablePasarela, Rechaza(Opciones(pasarela: null, entorno: "Staging")).Message, StringComparison.Ordinal);
        Assert.Contains(GuardaDeArranqueDePagos.VariablePasarela, Rechaza(Opciones(pasarela: "stripe", entorno: "Staging")).Message, StringComparison.Ordinal);
        // Con «culqi», Staging no tiene adaptador real en revisión: rechaza la llave de pruebas y la de producción.
        Assert.Contains(GuardaDeArranqueDePagos.VariableLlavePrivada, Rechaza(Opciones(pasarela: "culqi", entorno: "Staging", llave: LlaveDePruebas)).Message, StringComparison.Ordinal);
        Assert.Contains(GuardaDeArranqueDePagos.VariableLlavePrivada, Rechaza(Opciones(pasarela: "culqi", entorno: "Staging", llave: LlaveDeProduccion)).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-30")]
    public void Produccion_solo_acepta_la_pasarela_real_con_su_llave_de_produccion()
    {
        Assert.Contains("simulador", Rechaza(Opciones(entorno: "Production")).Message, StringComparison.Ordinal);
        Assert.Contains(GuardaDeArranqueDePagos.VariableLlavePrivada, Rechaza(Opciones(pasarela: "culqi", entorno: "Production", llave: LlaveDePruebas)).Message, StringComparison.Ordinal);
        Assert.Contains(GuardaDeArranqueDePagos.VariableLlavePrivada, Rechaza(Opciones(pasarela: "culqi", entorno: "Production", llave: null)).Message, StringComparison.Ordinal);
        GuardaDeArranqueDePagos.Exigir(Opciones(pasarela: "culqi", entorno: "Production", llave: LlaveDeProduccion));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-10")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("stripe")]
    [InlineData("Culqi")]
    [InlineData("SIMULADOR")]
    public void Una_pasarela_ausente_o_desconocida_falla_el_arranque(string? pasarela)
    {
        var error = Rechaza(Opciones(pasarela: pasarela));

        Assert.Contains(GuardaDeArranqueDePagos.VariablePasarela, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-10")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("corta")]
    public void La_clave_de_intentos_ausente_o_corta_falla_el_arranque_tambien_con_el_simulador(string? clave)
    {
        var error = Rechaza(Opciones(clave: clave));

        Assert.Contains(GuardaDeArranqueDePagos.VariableClaveDeIntentos, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void La_clave_anterior_de_intentos_corta_o_repetida_falla_el_arranque()
    {
        Assert.Contains(GuardaDeArranqueDePagos.VariableClaveDeIntentosAnterior, Rechaza(Opciones(anterior: "corta")).Message, StringComparison.Ordinal);
        Assert.Contains(GuardaDeArranqueDePagos.VariableClaveDeIntentosAnterior, Rechaza(Opciones(anterior: ClaveDeIntentos)).Message, StringComparison.Ordinal);
        GuardaDeArranqueDePagos.Exigir(Opciones(anterior: Anterior));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-10")]
    [InlineData("Development", LlaveDePruebas)]
    [InlineData("Test", LlaveDePruebas)]
    [InlineData("Production", LlaveDeProduccion)]
    public void Culqi_acepta_la_llave_que_corresponde_al_entorno(string entorno, string llave)
    {
        GuardaDeArranqueDePagos.Exigir(Opciones(pasarela: "culqi", entorno: entorno, llave: llave));
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-10")]
    [InlineData("Production", LlaveDePruebas)]
    [InlineData("Development", LlaveDeProduccion)]
    [InlineData("Test", LlaveDeProduccion)]
    [InlineData("Staging", LlaveDePruebas)]
    [InlineData("Staging", LlaveDeProduccion)]
    public void Culqi_rechaza_una_llave_de_otro_entorno_y_cualquier_llave_en_un_entorno_distinto_de_los_tres(string entorno, string llave)
    {
        var error = Rechaza(Opciones(pasarela: "culqi", entorno: entorno, llave: llave));

        Assert.Contains(GuardaDeArranqueDePagos.VariableLlavePrivada, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-10")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sk_test_x")]
    [InlineData("llave-sin-prefijo-pero-larga-de-sobra")]
    public void Culqi_rechaza_una_llave_ausente_corta_o_sin_prefijo(string? llave)
    {
        var error = Rechaza(Opciones(pasarela: "culqi", llave: llave));

        Assert.Contains(GuardaDeArranqueDePagos.VariableLlavePrivada, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void Culqi_exige_el_modulo_pedidos_y_una_llave_distinta_de_las_claves_de_intentos()
    {
        Assert.Contains("Pedidos", Rechaza(Opciones(pasarela: "culqi", llave: LlaveDePruebas, pedidos: false)).Message, StringComparison.Ordinal);
        Assert.Contains("repetir", Rechaza(Opciones(pasarela: "culqi", llave: LlaveDePruebas, clave: LlaveDePruebas)).Message, StringComparison.Ordinal);
        Assert.Contains("repetir", Rechaza(Opciones(pasarela: "culqi", llave: LlaveDePruebas, anterior: LlaveDePruebas)).Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void En_restauracion_y_ensayo_solo_se_acepta_el_adaptador_nulo_de_la_pasarela()
    {
        var conSimulador = Rechaza(Opciones(sinLlamadasReales: true, nula: false));
        var conCulqi = Rechaza(Opciones(pasarela: "culqi", llave: LlaveDePruebas, sinLlamadasReales: true, nula: false));

        Assert.Contains("nulo", conSimulador.Message, StringComparison.Ordinal);
        Assert.Contains("nulo", conCulqi.Message, StringComparison.Ordinal);
        GuardaDeArranqueDePagos.Exigir(Opciones(sinLlamadasReales: true, nula: true));
        GuardaDeArranqueDePagos.Exigir(Opciones(pasarela: "culqi", llave: LlaveDePruebas, sinLlamadasReales: true, nula: true));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void El_mensaje_de_rechazo_nombra_la_variable_y_nunca_repite_un_secreto()
    {
        var errores = new[]
        {
            Rechaza(Opciones(pasarela: "culqi", entorno: "Production", llave: LlaveDePruebas)),
            Rechaza(Opciones(pasarela: "culqi", llave: LlaveDePruebas, clave: LlaveDePruebas)),
            Rechaza(Opciones(clave: "corta-secreta")),
        };

        foreach (var error in errores)
        {
            Assert.DoesNotContain("sk_", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("secreta", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("llave-de-pruebas", error.Message, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("sk_test", Opciones(pasarela: "culqi", llave: LlaveDePruebas).ToString(), StringComparison.Ordinal);
    }

    // ---------- Dobles provisionales ----------

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class CuentaReal : ICuentaDelPagador
    {
        public Task<EstadoDeLaCuentaParaPagar?> Consultar(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult<EstadoDeLaCuentaParaPagar?>(new(true, null));

        public Task<bool> CancelacionEnEjecucion(Guid cuentaId, bool bloquear, CancellationToken cancelacion = default) => Task.FromResult(false);

        public Task<string?> CorreoDelTitular(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult<string?>(null);
    }

    private sealed class ConfirmacionReal : IConfirmacionDeCompra
    {
        public Task<ResultadoDeConfirmarCompra> Confirmar(CompraAprobada compra, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeConfirmarCompra.Creado);
    }

    private sealed class PasarelaReal : IPasarelaDePago
    {
        public Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeCobro> ConsultarEstado(Guid referenciaOpaca, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<ResultadoDeReembolsoEnPasarela> Reembolsar(Guid reembolsoId, string referenciaDelCargo, Importe monto, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<ReembolsoEnPasarela>?> ConsultarReembolsos(string referenciaDelCargo, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public AvisoDeLaPasarela? InterpretarAviso(ReadOnlyMemory<byte> cuerpo) => throw new NotSupportedException();
    }

    private sealed class ReservasReales : IReservasDelPago
    {
        public Task<RespuestaDeReserva<EstadoDeLaReserva>> Consultar(Guid pedidoId, Guid cuentaId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<RespuestaDeReserva<ResultadoDeExtension>> Extender(Guid pedidoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>> Confirmar(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<RespuestaDeReserva<bool>> Liberar(Guid pedidoId, MotivoDeLiberacion motivo, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    private static ServiceCollection ConReloj()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IReloj>(new RelojDePrueba(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero)));
        return servicios;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void En_desarrollo_los_dobles_se_registran_sin_pisar_a_los_reales()
    {
        var servicios = ConReloj();
        servicios.AddSingleton<ICuentaDelPagador, CuentaReal>();
        servicios.AgregarDoblesProvisionalesDePagos(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();

        Assert.IsType<CuentaReal>(proveedor.GetRequiredService<ICuentaDelPagador>());
        Assert.NotNull(proveedor.GetRequiredService<IConfirmacionDeCompra>());
        Assert.NotNull(proveedor.GetRequiredService<IPasarelaDePago>());
        Assert.NotNull(proveedor.GetRequiredService<IReservasDelPago>());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void En_produccion_el_arranque_falla_nombrando_cada_puerto_que_queda_con_un_doble()
    {
        var servicios = ConReloj();

        var error = Assert.Throws<InvalidOperationException>(() => servicios.AgregarDoblesProvisionalesDePagos(esProduccion: true));

        foreach (var puerto in new[] { nameof(ICuentaDelPagador), nameof(IConfirmacionDeCompra), nameof(IPasarelaDePago), nameof(IReservasDelPago) })
        {
            Assert.Contains(puerto, error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void En_produccion_el_arranque_pasa_cuando_los_cuatro_puertos_tienen_su_implementacion_real()
    {
        var servicios = ConReloj();
        servicios.AddSingleton<ICuentaDelPagador, CuentaReal>();
        servicios.AddSingleton<IConfirmacionDeCompra, ConfirmacionReal>();
        servicios.AddSingleton<IPasarelaDePago, PasarelaReal>();
        servicios.AddSingleton<IReservasDelPago, ReservasReales>();

        Assert.Same(servicios, servicios.AgregarDoblesProvisionalesDePagos(esProduccion: true));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public async Task Identidad_y_pedidos_sin_modulo_real_fallan_cerrado_y_la_pasarela_sin_proveedor_nunca_aprueba()
    {
        var servicios = ConReloj();
        servicios.AgregarDoblesProvisionalesDePagos(esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();

        await Assert.ThrowsAsync<DependenciaProvisionalDePagosNoDisponibleException>(() => proveedor.GetRequiredService<ICuentaDelPagador>().Consultar(Guid.NewGuid()));
        await Assert.ThrowsAsync<DependenciaProvisionalDePagosNoDisponibleException>(() => proveedor.GetRequiredService<ICuentaDelPagador>().CancelacionEnEjecucion(Guid.NewGuid(), bloquear: true));
        await Assert.ThrowsAsync<DependenciaProvisionalDePagosNoDisponibleException>(() => proveedor.GetRequiredService<IConfirmacionDeCompra>().Confirmar(
            new CompraAprobada(Guid.NewGuid(), Guid.NewGuid(), BabelHub.Ecommerce.Application.Carrito.TipoDeEntregaDeCompra.Digital, null, null, 0m, 10m, null, [], [])));
        var pasarela = proveedor.GetRequiredService<IPasarelaDePago>();
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await pasarela.Cobrar(new SolicitudDeCobroEnPasarela(Guid.NewGuid(), Importe.De(10m), BabelHub.Ecommerce.Domain.Pagos.MedioDePago.Yape, TokenDelMedioDePago.Crear("tkn_test_abc123XYZ"), null))).Tipo);
        Assert.Equal(TipoDeResultadoDeCobro.SinRespuesta, (await pasarela.ConsultarEstado(Guid.NewGuid())).Tipo);
        Assert.Equal(TipoDeResultadoDeReembolsoEnPasarela.SinRespuesta, (await pasarela.Reembolsar(Guid.NewGuid(), "ch_1", Importe.De(5m))).Tipo);
        Assert.Null(await pasarela.ConsultarReembolsos("ch_1"));
    }

    // ---------- El Inventario simulado ----------

    private static readonly Guid Pedido = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static (ReservasSimuladasDelPago Inventario, RelojDePrueba Reloj) Inventario()
    {
        var reloj = new RelojDePrueba(new DateTimeOffset(2026, 11, 18, 15, 0, 0, TimeSpan.Zero));
        var inventario = new ReservasSimuladasDelPago(reloj);
        inventario.Sembrar(Pedido, reloj.Ahora.AddMinutes(15), [new LineaConfirmadaDeReserva(Guid.NewGuid(), "AYA-01", false, null, null)]);
        return (inventario, reloj);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-11")]
    public async Task Una_reserva_pendiente_se_consulta_y_se_extiende_una_sola_vez()
    {
        var (inventario, reloj) = Inventario();

        var consulta = await inventario.Consultar(Pedido, Cuenta);
        var primera = await inventario.Extender(Pedido);
        var segunda = await inventario.Extender(Pedido);

        Assert.True(consulta.EsCorrecta);
        Assert.True(consulta.Valor!.Vigente);
        // Sin cuerpo: el Inventario calcula el instante (inicio de la reserva más 30 minutos) y la segunda llamada es un éxito sin cambios.
        Assert.Equal(new ResultadoDeExtension(reloj.Ahora.AddMinutes(30), true), primera.Valor);
        Assert.Equal(new ResultadoDeExtension(reloj.Ahora.AddMinutes(30), false), segunda.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-07")]
    public async Task Una_reserva_expirada_no_esta_vigente_ni_se_extiende_ni_se_confirma()
    {
        var (inventario, reloj) = Inventario();
        reloj.Ahora = reloj.Ahora.AddMinutes(16);

        Assert.False((await inventario.Consultar(Pedido, Cuenta)).Valor!.Vigente);
        // Extender una reserva vencida no es un error: es un éxito sin cambios (contrato v1).
        var extension = await inventario.Extender(Pedido);
        Assert.True(extension.EsCorrecta);
        Assert.False(extension.Valor!.Extendida);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, (await inventario.Confirmar(Pedido, "conf-1")).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Confirmar_con_la_misma_clave_devuelve_lo_mismo_y_con_otra_clave_es_un_estado_invalido()
    {
        var (inventario, _) = Inventario();

        var primera = await inventario.Confirmar(Pedido, "conf-1");
        var repetida = await inventario.Confirmar(Pedido, "conf-1");
        var otra = await inventario.Confirmar(Pedido, "conf-2");

        Assert.Equal("AYA-01", primera.Valor!.Single().CodigoDeFuente);
        Assert.Equal(primera.Valor, repetida.Valor);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, otra.Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Liberar_una_reserva_confirmada_con_pago_vencido_es_un_estado_invalido_y_con_falla_posterior_la_libera()
    {
        var (inventario, _) = Inventario();
        await inventario.Confirmar(Pedido, "conf-1");

        var conVencido = await inventario.Liberar(Pedido, MotivoDeLiberacion.PagoVencido);
        var conFalla = await inventario.Liberar(Pedido, MotivoDeLiberacion.FallaPosteriorAlPago);
        var repetida = await inventario.Liberar(Pedido, MotivoDeLiberacion.FallaPosteriorAlPago);

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.EstadoInvalido, conVencido.Resultado);
        Assert.True(conFalla.EsCorrecta);
        Assert.True(repetida.EsCorrecta);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Liberar_una_reserva_expirada_o_ya_liberada_es_un_exito_sin_cambios_y_una_desconocida_no_existe()
    {
        var (inventario, reloj) = Inventario();
        reloj.Ahora = reloj.Ahora.AddMinutes(16);

        Assert.True((await inventario.Liberar(Pedido, MotivoDeLiberacion.PagoVencido)).EsCorrecta);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.NoExiste, (await inventario.Liberar(Guid.NewGuid(), MotivoDeLiberacion.PagoVencido)).Resultado);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-04")]
    public async Task Un_inventario_caido_responde_sin_respuesta_y_una_reserva_desconocida_no_existe()
    {
        var (inventario, _) = Inventario();

        Assert.Equal(ResultadoDeLaLlamadaDeReserva.NoExiste, (await inventario.Consultar(Guid.NewGuid(), Cuenta)).Resultado);
        inventario.Caido = true;
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await inventario.Consultar(Pedido, Cuenta)).Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await inventario.Confirmar(Pedido, "conf-1")).Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await inventario.Extender(Pedido)).Resultado);
        Assert.Equal(ResultadoDeLaLlamadaDeReserva.SinRespuesta, (await inventario.Liberar(Pedido, MotivoDeLiberacion.PagoVencido)).Resultado);
    }
}
