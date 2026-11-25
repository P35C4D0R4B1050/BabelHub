using System.Net;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T37: el flujo de pago completo con <see cref="CulqiPasarelaDePago"/> sobre PostgreSQL real y un Culqi simulado con estado (<see cref="CulqiSimulado"/>): ninguna llamada a Culqi real. Aprobado con tarjeta y con Yape, rechazado y reintento con otro
/// medio, 3DS, un cobro cuya respuesta se pierde y se descubre por consulta (un solo cargo), una red caída, otro importe, veinte cobros simultáneos con la misma clave (un solo cargo), el reembolso por pago tardío, el reembolso cuya respuesta se pierde
/// (no se pide otro), el rechazado por Culqi y un aviso duplicado. <b>Lo que no prueba:</b> que la API verdadera responda como la especificación (reserva del spike T38).
/// </summary>
[Collection(ColeccionPostgresDePagos.Nombre)]
public sealed class PagosConCulqiSimuladoTests(PostgresDePagosFixture bd) : IAsyncLifetime
{
    private static readonly string LlaveDePrueba = "sk_" + "test_" + "llave-de-integracion-0123456789";

    private EntornoDePagosConPostgres _entorno = null!;

    private CulqiSimulado Culqi => _entorno.Proveedor.GetRequiredService<CulqiSimulado>();

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _entorno = EntornoDePagosConPostgres.Crear(bd, servicios =>
        {
            // Culqi exige el correo del titular: la composición real lo activa con «culqi»; aquí se repite porque el entorno de prueba compone con «simulador».
            servicios.AddSingleton(new OpcionesDelCobro(TimeSpan.FromMilliseconds(300), EnviarElCorreoDelTitular: true));
            servicios.AddSingleton(sp => new CulqiSimulado(sp.GetRequiredService<IReloj>()));
            servicios.AddSingleton<IPasarelaDePago>(sp => new CulqiPasarelaDePago(
                new HttpClient(sp.GetRequiredService<CulqiSimulado>()) { BaseAddress = new Uri(OpcionesDeCulqi.UrlDeLaApi) },
                new OpcionesDeCulqi(new ValorSecreto(LlaveDePrueba)),
                sp.GetRequiredService<IReloj>()));
        });
    }

    public async Task DisposeAsync() => await _entorno.DisposeAsync();

    private async Task<string> Clave(Guid pedido)
    {
        using var alcance = _entorno.Alcance();
        var emitida = await alcance.ServiceProvider.GetRequiredService<IPagos>().EmitirClaveDeIntento(new EmitirClaveDeIntentoEntrada(pedido));
        Assert.True(emitida.EsExito);
        return emitida.Valor.Token;
    }

    private async Task<Resultado<ResultadoDelCobroDelCliente>> Cobrar(Guid pedido, string clave, MedioDelContrato medio, string token)
    {
        using var alcance = _entorno.Alcance();
        return await alcance.ServiceProvider.GetRequiredService<IPagos>().Cobrar(new CobrarEntrada(pedido, clave, medio, TokenDelMedioDePago.Crear(token)));
    }

    private async Task<ResultadoDelCobroDelCliente> CobrarUnaVez(Guid pedido, MedioDelContrato medio, string token)
    {
        var respuesta = await Cobrar(pedido, await Clave(pedido), medio, token);
        Assert.True(respuesta.EsExito);
        return respuesta.Valor;
    }

    private async Task Correr(bool consultar, bool reanudar)
    {
        using var alcance = _entorno.Alcance();
        if (consultar)
        {
            await alcance.ServiceProvider.GetRequiredService<ConsultarPagosPendientes>().Ejecutar(50);
        }

        if (reanudar)
        {
            await alcance.ServiceProvider.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50);
        }
    }

    private Task<long> Contar(string tabla, string? donde = null) => bd.Consultar<long>($"SELECT count(*) FROM pagos.{tabla}" + (donde is null ? string.Empty : $" WHERE {donde}"))!;

    [Theory]
    [InlineData(MedioDelContrato.Tarjeta)]
    [InlineData(MedioDelContrato.Yape)]
    [Trait("Criterio", "AC-PAG-01")]
    public async Task Un_pago_aprobado_con_tarjeta_o_con_yape_cobra_una_sola_vez_el_total_en_centimos_y_crea_el_pedido(MedioDelContrato medio)
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        var cobro = await CobrarUnaVez(pedido, medio, CulqiSimulado.Aprobado);

        Assert.Equal(EstadoDelCobro.Aprobado, cobro.Estado);
        Assert.Equal(1, Culqi.CargosCreados);
        Assert.Equal(1, Culqi.Llamadas(HttpMethod.Post, "charges"));
        Assert.Single(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, await Contar("intenciones", "estado = 'completada'"));
        var referencia = await bd.Consultar<string>("SELECT referencia_pasarela FROM pagos.intentos WHERE estado = 'aprobado'");
        Assert.StartsWith("chr_test_", referencia, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-02")]
    public async Task Ni_el_token_ni_la_llave_ni_el_correo_quedan_en_las_tablas_de_pagos_ni_en_la_auditoria()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        await CobrarUnaVez(pedido, MedioDelContrato.Tarjeta, CulqiSimulado.Aprobado);

        var volcado = await bd.Consultar<string>("SELECT coalesce(string_agg(row_to_json(i)::text, ' '), '') || coalesce((SELECT string_agg(row_to_json(n)::text, ' ') FROM pagos.intenciones n), '') FROM pagos.intentos i");
        var todo = (volcado ?? string.Empty) + JsonSerializer.Serialize(_entorno.Auditoria.Solicitudes.Select(s => s.ToString()));
        Assert.DoesNotContain(CulqiSimulado.Aprobado, todo, StringComparison.Ordinal);
        Assert.DoesNotContain(LlaveDePrueba, todo, StringComparison.Ordinal);
        Assert.DoesNotContain("ana.perez@correo.example", todo, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public async Task Una_tarjeta_sin_fondos_se_rechaza_sin_cobro_y_se_reintenta_con_yape_en_un_intento_nuevo()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        var rechazo = await CobrarUnaVez(pedido, MedioDelContrato.Tarjeta, CulqiSimulado.FondosInsuficientes);
        var aprobado = await CobrarUnaVez(pedido, MedioDelContrato.Yape, CulqiSimulado.Aprobado);

        Assert.Equal((EstadoDelCobro.Rechazado, MotivoDeRechazoDePago.FondosInsuficientes), (rechazo.Estado, rechazo.Motivo!.Value));
        Assert.Equal(EstadoDelCobro.Aprobado, aprobado.Estado);
        Assert.NotEqual(rechazo.IntentoId, aprobado.IntentoId);
        Assert.Equal(1, Culqi.CargosCreados);
        Assert.Equal(2, Culqi.Llamadas(HttpMethod.Post, "charges"));
        Assert.Single(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    public async Task Una_autenticacion_3ds_sin_resolver_se_trata_como_un_rechazo_sin_cobro()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        var cobro = await CobrarUnaVez(pedido, MedioDelContrato.Tarjeta, CulqiSimulado.Revision3Ds);

        Assert.Equal((EstadoDelCobro.Rechazado, MotivoDeRechazoDePago.AutenticacionRequerida), (cobro.Estado, cobro.Motivo!.Value));
        Assert.Equal(0, Culqi.CargosCreados);
        Assert.Empty(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Un_cargo_creado_cuya_respuesta_se_pierde_queda_pendiente_y_se_descubre_por_consulta_sin_cobrar_otra_vez()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        var cobro = await CobrarUnaVez(pedido, MedioDelContrato.Yape, CulqiSimulado.RespuestaPerdida);

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, cobro.Estado);
        Assert.Equal(1, await Contar("intentos", "estado = 'pendiente_de_confirmacion'"));

        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await Correr(consultar: true, reanudar: true);

        Assert.Equal(1, Culqi.Llamadas(HttpMethod.Post, "charges"));
        Assert.Equal(1, Culqi.CargosCreados);
        Assert.Equal(1, await Contar("intentos", "estado = 'aprobado'"));
        Assert.Single(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, await Contar("intenciones", "estado = 'completada'"));
    }

    [Theory]
    [InlineData(CulqiSimulado.RedCaida)]
    [InlineData(CulqiSimulado.ErrorDelServidor)]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Una_red_caida_o_un_error_del_servicio_deja_el_pago_pendiente_se_consulta_cada_minuto_y_vence_sin_pedido(string token)
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        var cobro = await CobrarUnaVez(pedido, MedioDelContrato.Tarjeta, token);

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, cobro.Estado);
        for (var minuto = 0; minuto < 31; minuto++)
        {
            _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(1));
            await Correr(consultar: true, reanudar: true);
        }

        Assert.Equal(0, Culqi.CargosCreados);
        Assert.Equal(1, Culqi.Llamadas(HttpMethod.Post, "charges"));
        Assert.True(Culqi.Llamadas(HttpMethod.Get, "charges") >= 25);
        Assert.Equal(1, await Contar("intentos", "estado = 'vencido'"));
        Assert.Empty(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aprobado_por_otro_importe_no_se_acepta_deja_una_alerta_y_no_crea_el_pedido()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        var cobro = await CobrarUnaVez(pedido, MedioDelContrato.Tarjeta, CulqiSimulado.OtroImporte);
        await Correr(consultar: true, reanudar: true);

        Assert.NotEqual(EstadoDelCobro.Aprobado, cobro.Estado);
        Assert.Contains(IniciarCobro.MotivoDeAlertaCobroDudoso, _entorno.Alertas);
        Assert.Empty(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(0, await Contar("intenciones", "estado = 'completada'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Veinte_cobros_simultaneos_con_la_misma_clave_llegan_a_Culqi_una_sola_vez()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var clave = await Clave(pedido);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Cobrar(pedido, clave, MedioDelContrato.Yape, CulqiSimulado.Aprobado))));

        Assert.All(respuestas, r => Assert.True(r.EsExito));
        Assert.Equal(1, Culqi.Llamadas(HttpMethod.Post, "charges"));
        Assert.Equal(1, Culqi.CargosCreados);
        Assert.Single(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Veinte_claves_distintas_a_la_vez_dejan_un_solo_cargo_en_Culqi()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var claves = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Clave(pedido)));

        await Task.WhenAll(claves.Select(clave => Task.Run(() => Cobrar(pedido, clave, MedioDelContrato.Tarjeta, CulqiSimulado.Aprobado))));

        Assert.Equal(1, Culqi.CargosCreados);
        Assert.Single(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-13")]
    public async Task Un_pago_que_Culqi_aprueba_despues_de_vencer_se_reembolsa_una_sola_vez_sin_crear_el_pedido()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var cobro = await CobrarUnaVez(pedido, MedioDelContrato.Tarjeta, CulqiSimulado.RespuestaPerdida);
        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, cobro.Estado);

        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Correr(consultar: true, reanudar: false);
        await Correr(consultar: false, reanudar: true);
        await Correr(consultar: false, reanudar: true);

        Assert.Empty(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, Culqi.DevolucionesCreadas);
        Assert.Equal(1, Culqi.Llamadas(HttpMethod.Patch, "refunds/"));
        Assert.Equal(1, await Contar("reembolsos", "estado = 'reembolsado'"));
        Assert.Equal(0, await Contar("intenciones", "estado = 'completada'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_que_Culqi_creo_sin_que_llegara_la_respuesta_no_se_pide_otra_vez_y_queda_pendiente_para_la_administracion()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        _entorno.Pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        Culqi.PerderLaRespuestaDeLasDevoluciones = true;
        await CobrarUnaVez(pedido, MedioDelContrato.Tarjeta, CulqiSimulado.Aprobado);

        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Correr(consultar: false, reanudar: true);

        Assert.Equal(1, Culqi.DevolucionesCreadas);
        Assert.Equal(0, await Contar("reembolsos", "estado = 'reembolsado'"));

        // La administración reintenta pasado el tiempo de espera: el adaptador ve la devolución sin marca y de la misma cuantía y no pide otra.
        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(30));
        _entorno.Actor.Actual = BabelHub.Ecommerce.Application.Plataforma.ActorActual.DeUsuario(Guid.NewGuid(), BabelHub.Shared.Contratos.RolesDeUsuario.AdministradorCentral, null);
        var reembolso = await bd.Consultar<Guid>("SELECT id FROM pagos.reembolsos LIMIT 1");
        using (var alcance = _entorno.Alcance())
        {
            Assert.True((await alcance.ServiceProvider.GetRequiredService<IReembolsos>().Reintentar(reembolso)).EsExito);
        }

        Assert.Equal(1, Culqi.DevolucionesCreadas);
        Assert.Equal(0, await Contar("reembolsos", "estado = 'reembolsado'"));
        Assert.Equal(1, await Contar("reembolsos", "estado IN ('pendiente', 'solicitado')"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-17")]
    public async Task Un_reembolso_que_Culqi_rechaza_queda_pendiente_y_visible_y_no_se_marca_como_hecho()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        _entorno.Pedidos.Falla = new InvalidOperationException("Pedidos no responde.");
        Culqi.RechazarDevoluciones = true;
        await CobrarUnaVez(pedido, MedioDelContrato.Yape, CulqiSimulado.Aprobado);

        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(31));
        await Correr(consultar: false, reanudar: true);

        Assert.Equal(0, Culqi.DevolucionesCreadas);
        Assert.Equal(1, await Contar("reembolsos", "estado = 'pendiente'"));
        Assert.Equal(0, await Contar("reembolsos", "estado = 'reembolsado'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aviso_de_Culqi_solo_adelanta_la_consulta_un_duplicado_no_repite_nada_y_el_cobro_se_confirma_por_consulta()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var cobro = await CobrarUnaVez(pedido, MedioDelContrato.Yape, CulqiSimulado.RespuestaPerdida);

        var primero = await Aviso("evt_" + "test_" + "uno", cobro.IntentoId);
        var duplicado = await Aviso("evt_" + "test_" + "uno", cobro.IntentoId);

        Assert.True(primero.EsExito);
        Assert.True(duplicado.EsExito);
        Assert.Equal(1, await Contar("intentos", "consulta_inmediata"));
        Assert.Empty(_entorno.Pedidos.PedidosCreados);

        _entorno.Reloj.Avanzar(TimeSpan.FromSeconds(30));
        await Correr(consultar: true, reanudar: true);

        Assert.Single(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, Culqi.CargosCreados);
    }

    private Task<Resultado<bool>> Aviso(string idDelEvento, Guid referencia)
    {
        var cargo = "{\"object\":\"charge\",\"id\":\"chr_test_Sim0000000000001\",\"metadata\":{\"referencia\":\"" + referencia.ToString("D") + "\"}}";
        var cuerpo = JsonSerializer.SerializeToUtf8Bytes(new { @object = "event", id = idDelEvento, type = "charge.creation.succeeded", creation_date = 1763550000000L, data = cargo });
        using var alcance = _entorno.Alcance();
        return alcance.ServiceProvider.GetRequiredService<IAvisosDeLaPasarela>().Recibir(new AvisoDeLaPasarelaEntrada(cuerpo, "1763550000", "firma-valida"));
    }
}
