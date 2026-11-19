using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T31 (la parte de Pagos): la compra aprobada y su compensación sobre PostgreSQL real y el simulador (AC-PAG-04, AC-PAG-13, AC-PAG-17, AC-PAG-24): una falla de Pedidos que se retoma sin duplicar, dos ejecutores simultáneos, la compensación a los 30 minutos con un solo reembolso y el motivo de
/// liberación correcto, el pago tardío, el reembolso que se aplicó sin que llegara la respuesta y dos reembolsos simultáneos del mismo cobro. Carrito, Identidad y Pedidos son dobles (la parte con el Carrito real queda para la composición del anfitrión).
/// </summary>
[Collection(ColeccionPostgresDePagos.Nombre)]
public sealed class ComprasAprobadasConPostgresTests(PostgresDePagosFixture bd) : IAsyncLifetime
{
    private EntornoDePagosConPostgres _entorno = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _entorno = EntornoDePagosConPostgres.Crear(bd);
    }

    public async Task DisposeAsync() => await _entorno.DisposeAsync();

    private async Task<ResultadoDelCobroDelCliente> Cobrar(Guid pedido, string token, MedioDelContrato medio = MedioDelContrato.Tarjeta)
    {
        using var alcance = _entorno.Alcance();
        var pagos = alcance.ServiceProvider.GetRequiredService<IPagos>();
        var clave = await pagos.EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
        Assert.True(clave.EsExito);
        var cobro = await pagos.Cobrar(new CobrarEntrada(pedido, clave.Valor.Token, medio, TokenDelMedioDePago.Crear(token)));
        Assert.True(cobro.EsExito);
        return cobro.Valor;
    }

    private async Task<ResultadoDeReanudar> Reanudar()
    {
        using var alcance = _entorno.Alcance();
        return await alcance.ServiceProvider.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50);
    }

    private Task<long> Contar(string tabla, string? donde = null) => bd.Consultar<long>($"SELECT count(*) FROM pagos.{tabla}" + (donde is null ? string.Empty : $" WHERE {donde}"))!;

    private async Task<int> ReembolsosEnLaPasarela()
    {
        var referencia = await bd.Consultar<string>("SELECT referencia_pasarela FROM pagos.intentos WHERE referencia_pasarela IS NOT NULL LIMIT 1");
        return referencia is null ? 0 : (await _entorno.Pasarela.ConsultarReembolsos(referencia))!.Count;
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Una_falla_de_Pedidos_deja_la_compra_aprobada_y_la_retoma_la_completa_sin_duplicar_el_pedido_ni_el_cobro()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        _entorno.Pedidos.Falla = new InvalidOperationException("Pedidos no responde.");

        var primera = await Cobrar(pedido, EscenariosDelSimulador.Aprobado);

        Assert.NotEqual(EstadoDelCobro.Aprobado, primera.Estado);
        Assert.Equal(1, await Contar("intenciones", "estado = 'aprobada'"));
        Assert.Empty(_entorno.Pedidos.PedidosCreados);

        _entorno.Pedidos.Falla = null;
        await Reanudar();

        Assert.Single(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, await Contar("intenciones", "estado = 'completada'"));
        Assert.Equal(1, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(0, await ReembolsosEnLaPasarela());
        Assert.Empty(_entorno.Liberaciones);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Dos_ejecutores_a_la_vez_sobre_la_misma_compra_aprobada_crean_un_solo_pedido()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        _entorno.Pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        await Cobrar(pedido, EscenariosDelSimulador.Aprobado);
        _entorno.Pedidos.Falla = null;
        _entorno.Pedidos.Retraso = TimeSpan.FromMilliseconds(400);
        var llamadasAntes = _entorno.Pedidos.Llamadas;

        await Task.WhenAll(Task.Run(Reanudar), Task.Run(Reanudar));

        Assert.Single(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, _entorno.Pedidos.Llamadas - llamadasAntes);
        Assert.Equal(1, await Contar("intenciones", "estado = 'completada'"));
        Assert.Equal(0, await ReembolsosEnLaPasarela());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Si_Pedidos_sigue_fallando_a_los_treinta_minutos_se_compensa_con_un_solo_reembolso_y_el_motivo_de_liberacion_de_la_falla()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        _entorno.Pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        await Cobrar(pedido, EscenariosDelSimulador.Aprobado);

        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Reanudar();
        await Reanudar();
        await Reanudar();

        Assert.Equal(1, await Contar("intenciones", "estado = 'compensada'"));
        Assert.Equal(1, await Contar("reembolsos", "estado = 'reembolsado' AND origen = 'compensacion'"));
        Assert.Equal(1, await ReembolsosEnLaPasarela());
        Assert.Empty(_entorno.Pedidos.PedidosCreados);
        Assert.All(_entorno.Liberaciones, l => Assert.Equal(MotivoDeLiberacion.FallaPosteriorAlPago, l.Motivo));
        Assert.NotEmpty(_entorno.Liberaciones);
        Assert.Contains("compra-aprobada-sin-completar", _entorno.Alertas);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_pago_que_se_aprueba_despues_de_vencer_se_reembolsa_sin_crear_el_pedido()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var lento = await Cobrar(pedido, EscenariosDelSimulador.Lento);
        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, lento.Estado);

        // La consulta llega recién a los 31 minutos: el cargo existe y la intención ya venció (pago tardío).
        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        using (var alcance = _entorno.Alcance())
        {
            await alcance.ServiceProvider.GetRequiredService<ConsultarPagosPendientes>().Ejecutar(50);
        }

        await Reanudar();

        Assert.Empty(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, await Contar("reembolsos", "estado = 'reembolsado'"));
        Assert.Equal(1, await ReembolsosEnLaPasarela());
        Assert.Equal(0, await Contar("intenciones", "estado = 'completada'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_que_la_pasarela_aplico_sin_responder_se_reconcilia_al_reintentar_sin_reembolsar_dos_veces()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        _entorno.Pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        await Cobrar(pedido, EscenariosDelSimulador.ReembolsoSinRespuesta);

        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Reanudar();

        // La pasarela aplicó el reembolso y no contestó: queda solicitado, sin marcarse como hecho.
        Assert.Equal(1, await ReembolsosEnLaPasarela());
        Assert.Equal(0, await Contar("reembolsos", "estado = 'reembolsado'"));
        Assert.Equal(1, await Contar("reembolsos", "estado IN ('pendiente', 'solicitado')"));

        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(10));
        await Reanudar();

        Assert.Equal(1, await ReembolsosEnLaPasarela());
        Assert.Equal(1, await Contar("reembolsos", "estado = 'reembolsado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Dos_procesar_simultaneos_del_mismo_reembolso_lo_hacen_una_sola_vez()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        _entorno.Pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        await Cobrar(pedido, EscenariosDelSimulador.ReembolsoRechazado);
        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Reanudar();
        var reembolso = await bd.Consultar<Guid>("SELECT id FROM pagos.reembolsos LIMIT 1");

        // El cargo rechaza reembolsos: dos procesos a la vez lo intentan y ninguno lo cierra como hecho; sigue pendiente para la administración.
        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(async () =>
        {
            using var alcance = _entorno.Alcance();
            return await alcance.ServiceProvider.GetRequiredService<ProcesarReembolso>().Ejecutar(reembolso);
        })));

        Assert.All(resultados, r => Assert.True(r.EsExito));
        Assert.Equal(0, await ReembolsosEnLaPasarela());
        Assert.Equal(0, await Contar("reembolsos", "estado = 'reembolsado'"));
        Assert.Equal(1, await Contar("reembolsos"));
    }
}
