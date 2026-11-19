using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;
using MedioDelContrato = BabelHub.Ecommerce.Application.Pagos.MedioDePago;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>Compras de prueba sembradas en la base real, con su reserva del Inventario simulado y el checkout vivo en pago.</summary>
internal static class ComprasDePrueba
{
    public static readonly Guid Cuenta = ActorDePagos.CuentaPorOmision;

    private static readonly Guid LineaImpresa = Guid.Parse("33333333-3333-3333-3333-333333333331");
    private static readonly Guid LineaDigital = Guid.Parse("33333333-3333-3333-3333-333333333332");

    public static InstantaneaDeIntencion Mixta(Guid cuenta, DateTimeOffset ahora)
    {
        var impresa = new LineaDeIntencion(LineaImpresa, Guid.Parse("44444444-4444-4444-4444-444444444441"), EsDigital: false, "La ciudad y los perros", "9780306406157", 2, Importe.De(50m), Importe.De(100m));
        var digital = new LineaDeIntencion(LineaDigital, Guid.Parse("44444444-4444-4444-4444-444444444442"), EsDigital: true, "Rayuela", "9780306406164", 1, Importe.De(25m), Importe.De(25m));
        var texto = new TextoAceptado("2026-11", ahora.AddMinutes(-5));
        return InstantaneaDeIntencion.Crear(cuenta, TipoDeEntrega.Domicilio, null, "AYA", [impresa, digital], Importe.De(15m), Importe.De(140m), "huella-a", texto, texto, texto, new ContactoDeEntrega("Ana Pérez Quispe", "987654321", "Av. Los Olivos 123, Ayacucho"));
    }

    /// <summary>Una compra mixta de S/ 140 con la intención abierta, la reserva pendiente en el Inventario y Carrito en «en pago».</summary>
    public static async Task<Guid> Sembrar(EntornoDePagosConPostgres entorno, Guid? pedido = null, Guid? cuenta = null)
    {
        var id = pedido ?? Guid.NewGuid();
        var ahora = entorno.Reloj.Ahora;
        var intencion = IntencionDePago.Crear(id, Mixta(cuenta ?? Cuenta, ahora), ahora + PlazosDelPago.VigenciaConImpresos, ahora);
        await entorno.Bd.Unidad.Ejecutar(async token => { await entorno.Bd.Pagos.AgregarIntencion(intencion, token); return 0; });
        entorno.Reservas.Sembrar(id, ahora + PlazosDelPago.VigenciaConImpresos, [new LineaConfirmadaDeReserva(LineaImpresa, "AYA-01", false, null, null)]);
        entorno.Checkout.Con(nameof(BabelHub.Ecommerce.Application.Carrito.ICheckoutDelCliente.ObtenerParaPagar), _ => Task.FromResult(Resultado<BabelHub.Ecommerce.Application.Carrito.CheckoutVivoParaPagar>.Exito(new(id, BabelHub.Ecommerce.Application.Carrito.EstadoVisibleDelCheckout.EnPago))));
        return id;
    }
}

/// <summary>
/// T30: el cobro de punta a punta sobre PostgreSQL real y el simulador (AC-PAG-03, AC-PAG-05, AC-PAG-06, AC-PAG-14, AC-PAG-15): solicitudes simultáneas con la misma clave y con claves distintas (dos pestañas), un rechazo y el reintento con otro medio, una respuesta de 11 segundos y su consulta cada minuto
/// hasta el vencimiento. Los casos de uso, los repositorios y el simulador son los reales; el reloj, Carrito, Identidad y Pedidos son dobles.
/// </summary>
[Collection(ColeccionPostgresDePagos.Nombre)]
public sealed class PagosConPostgresTests(PostgresDePagosFixture bd) : IAsyncLifetime
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

    private Task<long> Contar(string tabla, string? donde = null) => bd.Consultar<long>($"SELECT count(*) FROM pagos.{tabla}" + (donde is null ? string.Empty : $" WHERE {donde}"))!;

    [Fact]
    [Trait("Criterio", "AC-PAG-03")]
    public async Task Veinte_cobros_simultaneos_con_la_misma_clave_cobran_una_sola_vez_y_crean_un_solo_pedido()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var clave = await Clave(pedido);

        var respuestas = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => Cobrar(pedido, clave, MedioDelContrato.Yape, EscenariosDelSimulador.Aprobado))));

        Assert.All(respuestas, r => Assert.True(r.EsExito));
        Assert.Contains(respuestas, r => r.Valor.Estado == EstadoDelCobro.Aprobado);
        Assert.Equal(1, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(1, await Contar("intentos"));
        Assert.Single(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, await Contar("intenciones", "estado = 'completada'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-14")]
    public async Task Dos_pestanas_o_veinte_claves_distintas_a_la_vez_dejan_un_solo_cobro_y_un_solo_intento()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var claves = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Clave(pedido)));

        var respuestas = await Task.WhenAll(claves.Select(clave => Task.Run(() => Cobrar(pedido, clave, MedioDelContrato.Tarjeta, EscenariosDelSimulador.Aprobado))));

        Assert.Equal(1, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(1, await Contar("intentos"));
        Assert.Single(respuestas, r => r.EsExito && r.Valor.Estado == EstadoDelCobro.Aprobado);
        // Las demás se rechazan: con un conflicto (otro cobro en curso o la intención ya completada) o, pasado el tope de 5 intentos por intención y hora, con el límite.
        Assert.All(respuestas.Where(r => !r.EsExito), r => Assert.Contains(r.Error!.Categoria, new[] { CategoriaDeError.Conflicto, CategoriaDeError.Limite }));
        Assert.Single(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-05")]
    public async Task Un_rechazo_deja_reintentar_con_otro_medio_en_un_intento_nuevo_con_clave_nueva_y_sin_cobro_del_primero()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var primera = await Clave(pedido);

        var rechazo = await Cobrar(pedido, primera, MedioDelContrato.Tarjeta, EscenariosDelSimulador.TarjetaRechazada);
        var segunda = await Clave(pedido);
        var aprobado = await Cobrar(pedido, segunda, MedioDelContrato.Yape, EscenariosDelSimulador.Aprobado);

        Assert.Equal((EstadoDelCobro.Rechazado, MotivoDeRechazoDePago.TarjetaRechazada), (rechazo.Valor.Estado, rechazo.Valor.Motivo!.Value));
        Assert.True(aprobado.Valor.Estado == EstadoDelCobro.Aprobado, $"{aprobado.Valor.Estado}; alertas: {string.Join(',', _entorno.Alertas)}; pedidos: {_entorno.Pedidos.Llamadas}");
        Assert.NotEqual(rechazo.Valor.IntentoId, aprobado.Valor.IntentoId);
        Assert.Equal(1, await Contar("intentos", "estado = 'rechazado'"));
        Assert.Equal(1, await Contar("intentos", "estado = 'aprobado'"));
        Assert.Equal(2, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Single(_entorno.Pedidos.PedidosCreados);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-06")]
    public async Task Una_respuesta_de_once_segundos_deja_el_pago_pendiente_y_se_confirma_por_consulta_sin_cobrar_otra_vez()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var clave = await Clave(pedido);

        var respuesta = await Cobrar(pedido, clave, MedioDelContrato.Tarjeta, EscenariosDelSimulador.Lento);

        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, respuesta.Valor.Estado);
        Assert.Equal(1, await Contar("intentos", "estado = 'pendiente_de_confirmacion'"));
        Assert.Equal(1, _entorno.Pasarela.LlamadasDeCobrar);

        // Pasan los 11 segundos que tarda el cargo y un minuto hasta la siguiente consulta: el trabajo lo aprueba y retoma la compra.
        _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(1));
        using var alcance = _entorno.Alcance();
        await alcance.ServiceProvider.GetRequiredService<ConsultarPagosPendientes>().Ejecutar(50);
        await alcance.ServiceProvider.GetRequiredService<ReanudarComprasAprobadas>().Ejecutar(50);

        Assert.Equal(1, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(1, await Contar("intentos", "estado = 'aprobado'"));
        Assert.Single(_entorno.Pedidos.PedidosCreados);
        Assert.Equal(1, await Contar("intenciones", "estado = 'completada'"));
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-15")]
    public async Task Un_pago_que_sigue_sin_respuesta_se_consulta_cada_minuto_y_a_los_treinta_minutos_la_intencion_vence()
    {
        var pedido = await ComprasDePrueba.Sembrar(_entorno);
        var clave = await Clave(pedido);
        var respuesta = await Cobrar(pedido, clave, MedioDelContrato.Yape, EscenariosDelSimulador.SinRespuesta);
        Assert.Equal(EstadoDelCobro.PendienteDeConfirmacion, respuesta.Valor.Estado);

        for (var minuto = 1; minuto <= 31; minuto++)
        {
            _entorno.Reloj.Avanzar(TimeSpan.FromMinutes(1));
            using var alcance = _entorno.Alcance();
            await alcance.ServiceProvider.GetRequiredService<ConsultarPagosPendientes>().Ejecutar(50);
            await alcance.ServiceProvider.GetRequiredService<CerrarIntencionesVencidas>().Ejecutar(50);
        }

        Assert.Equal(1, _entorno.Pasarela.LlamadasDeCobrar);
        Assert.Equal(1, await Contar("intentos", "estado = 'vencido'"));
        Assert.Equal(1, await Contar("intenciones", "estado = 'cerrada_sin_cobro'"));
        Assert.Empty(_entorno.Pedidos.PedidosCreados);
    }
}
