using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T30 (segunda parte): un cobro aplicado sin respuesta, un importe distinto, los avisos repetidos, forjados y fuera de orden, un total que cambió en otra pestaña y los límites del cobro (429 con la espera y el almacén de contadores caído), sobre PostgreSQL real y el simulador
/// (AC-PAG-06, AC-PAG-14, AC-PAG-16).
/// </summary>
[Collection(ColeccionPostgresDePagos.Nombre)]
public sealed class PagosConPostgresAvisosYLimitesTests(PostgresDePagosFixture bd) : IAsyncLifetime
{
    private EntornoDePagosConPostgres _entorno = null!;

    public async Task InitializeAsync()
    {
        await bd.Limpiar();
        _entorno = EntornoDePagosConPostgres.Crear(bd);
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

    private Task<Resultado<bool>> Aviso(string id, Guid referencia, string firma = "firma-valida")
    {
        using var alcance = _entorno.Alcance();
        return alcance.ServiceProvider.GetRequiredService<IAvisosDeLaPasarela>().Recibir(new AvisoDeLaPasarelaEntrada(SimuladorPasarelaDePago.CrearAviso(id, referencia), "1763550000", firma));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Un_cobro_aplicado_sin_respuesta_se_descubre_por_consulta_y_se_cobra_una_sola_vez()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var respuesta = await Cobrar(pedido, await Clave(pedido), MedioDelContrato.Yape, EscenariosDelSimulador.AplicaSinResponder);

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, respuesta.Valor.Estado);
        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        await Correr(consultar: true, reanudar: true);

        Assert.Equal(1, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(1, _entorno.Pasarela.CargosDe(respuesta.Valor.IntentoId));
        Assert.Equal(1, await Contar("intentos", "estado = 'aprobado'"));
        Assert.Single(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aprobado_por_otro_importe_no_se_acepta_deja_una_alerta_y_no_crea_el_pedido()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        var respuesta = await Cobrar(pedido, await Clave(pedido), MedioDelContrato.Tarjeta, EscenariosDelSimulador.OtroImporte);
        await Correr(consultar: true, reanudar: true);

        Assert.NotEqual(EstadoDelCobro.Aprobado, respuesta.Valor.Estado);
        Assert.Contains(IniciarCobro.MotivoDeAlertaCobroDudoso, _entorno.Alertas);
        Assert.Empty(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(0, await Contar("intenciones", "estado = 'completada'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aviso_solo_adelanta_la_consulta_un_duplicado_no_repite_nada_y_uno_forjado_se_rechaza()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var respuesta = await Cobrar(pedido, await Clave(pedido), MedioDelContrato.Yape, EscenariosDelSimulador.SinRespuesta);
        var intento = respuesta.Valor.IntentoId;

        var primero = await Aviso("evt-1", intento);
        var duplicado = await Aviso("evt-1", intento);
        var forjado = await Aviso("evt-2", intento, firma: "forjada");

        Assert.True(primero.EsExito);
        Assert.True(duplicado.EsExito);
        Assert.Equal(CategoriaDeError.NoAutenticado, forjado.Error!.Categoria);
        Assert.Equal(1, await Contar("intentos", "consulta_inmediata"));
        Assert.Single(_entorno.Auditoria.Solicitudes, s => s.Accion == AccionesAuditables.AvisoDeLaPasarelaRecibido);
        // El aviso por sí solo no aprueba nada: el cobro sigue pendiente y no hay pedido.
        Assert.Equal(1, await Contar("intentos", "estado = 'pendiente_de_confirmacion'"));
        Assert.Empty(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-16")]
    public async Task Un_aviso_fuera_de_orden_de_un_cargo_sin_intento_se_acepta_sin_efecto_y_no_cobra_ni_consulta()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);

        var respuesta = await Aviso("evt-9", Guid.NewGuid());

        Assert.True(respuesta.EsExito);
        Assert.Equal(0, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(0, await Contar("intentos"));
        Assert.Equal(1, await Contar("intenciones", $"id = '{pedido:D}' AND estado = 'abierta'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Un_total_que_cambio_en_otra_pestana_invalida_la_clave_vieja_y_no_se_cobra()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var vieja = await Clave(pedido);

        // La otra pestaña cambió el carrito: Carrito reemplaza la intención con otro total y otra huella.
        using (var alcance = _entorno.Alcance())
        {
            var creador = alcance.ServiceProvider.GetRequiredService<ICreadorDeIntencionesDePago>();
            var texto = new AceptacionDeCompra(TextoLegalDeCompra.Terminos, "2026-11", _entorno.Reloj.Ahora.AddMinutes(-5));
            var lineas = new[]
            {
                new LineaDeInstantanea(Guid.Parse("33333333-3333-3333-3333-333333333331"), Guid.Parse("44444444-4444-4444-4444-444444444441"), false, "La ciudad y los perros", "9780306406157", 3, 50m, 150m, "AYA-01", false, null),
            };
            var instantanea = new InstantaneaDeCompra(
                pedido,
                ComprasDePrueba.Cuenta,
                TipoDeEntregaDeCompra.Domicilio,
                null,
                "AYA",
                150m,
                15m,
                165m,
                new ContactoDeCompra("Ana Pérez Quispe", "987654321", "Av. Los Olivos 123, Ayacucho", "AYA"),
                [texto, texto with { Texto = TextoLegalDeCompra.Privacidad }, texto with { Texto = TextoLegalDeCompra.ReembolsoDigital }],
                lineas,
                "huella-b");
            var creada = await creador.Crear(ComprasDePrueba.Cuenta, instantanea, "huella-b", _entorno.Reloj.Ahora + BabelHub.Ecommerce.Domain.Pagos.PlazosDelPago.VigenciaConImpresos);
            Assert.Equal(ResultadoDeCrearIntencion.Creada, creada);
        }

        var respuesta = await Cobrar(pedido, vieja, MedioDelContrato.Yape, EscenariosDelSimulador.Aprobado);

        Assert.False(respuesta.EsExito);
        Assert.Equal(CategoriaDeError.Conflicto, respuesta.Error!.Categoria);
        Assert.Equal(0, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(0, await Contar("intentos"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task El_sexto_intento_de_una_intencion_en_una_hora_se_rechaza_con_429_y_la_espera_y_no_llama_a_la_pasarela()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        for (var i = 0; i < 5; i++)
        {
            var rechazo = await Cobrar(pedido, await Clave(pedido), MedioDelContrato.Tarjeta, EscenariosDelSimulador.TarjetaRechazada);
            Assert.Equal(EstadoDelCobro.Rechazado, rechazo.Valor.Estado);
        }

        var sexto = await Cobrar(pedido, await Clave(pedido), MedioDelContrato.Tarjeta, EscenariosDelSimulador.Aprobado);

        Assert.Equal(CategoriaDeError.Limite, sexto.Error!.Categoria);
        Assert.Contains(sexto.Error.Errores, e => e.Campo == ClavesDePagos.EsperaEnSegundos);
        Assert.Equal(5, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(5, await Contar("intentos"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Con_el_almacen_de_contadores_caido_el_cobro_falla_cerrado_sin_intento_ni_llamada_a_la_pasarela()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var clave = await Clave(pedido);
        _entorno.Limites.Caido = true;

        var respuesta = await Cobrar(pedido, clave, MedioDelContrato.Yape, EscenariosDelSimulador.Aprobado);

        Assert.Equal(CategoriaDeError.NoDisponible, respuesta.Error!.Categoria);
        Assert.Equal(0, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(0, await Contar("intentos"));

        // Al volver el almacén, la misma clave sí cobra.
        _entorno.Limites.Caido = false;
        Assert.Equal(EstadoDelCobro.Aprobado, (await Cobrar(pedido, clave, MedioDelContrato.Yape, EscenariosDelSimulador.Aprobado)).Valor.Estado);
    }
}
