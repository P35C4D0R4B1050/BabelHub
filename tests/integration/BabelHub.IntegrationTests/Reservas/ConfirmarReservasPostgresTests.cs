using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T14: <c>ConfirmarReservas</c> con PostgreSQL real, el rol real de la aplicación y el contenedor de producción (SEL-03, SEL-06, SEL-32).</summary>
public sealed class ConfirmarReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Escenario(Guid Lima, Guid Cusco, Guid Libro);

    private async Task<Escenario> Preparar(int enLima = 10, int enCusco = 10)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var cusco = await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, enLima, 0);
        await bd.PonerStock(cusco, libro, enCusco, 0);
        return new Escenario(lima, cusco, libro);
    }

    private static Task<Resultado<ReservaDelPedido>> Confirmar(PilaDeReservas pila, Guid pedido) =>
        pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(Servicio, new ConfirmarReservasEntrada(pedido, Guid.NewGuid().ToString("N")));

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task Confirmar_PasaLaReservaAConfirmada_SinExpiracion_ConElStockIntactoYAuditoria()
    {
        var f = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(f.Libro, 3)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);

        var resultado = await Confirmar(pila, entrada.PedidoId);

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((EstadoDeReserva.Confirmada, null), (resultado.Valor.Estado, resultado.Valor.ExpiraEn));
        Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Null(await bd.Consultar<DateTime?>($"SELECT fecha_expiracion FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.NotNull(await bd.Consultar<DateTime?>($"SELECT confirmada_en FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Equal((10, 3), await bd.StockDe(f.Lima, f.Libro));
        var auditada = Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaConfirmada");
        Assert.Equal((TipoDeActor.Integracion, "3"), (auditada.Actor, auditada.Detalle["cantidad"]));
        Assert.Equal(await bd.ReservadoDeLaRed(f.Libro), await bd.UnidadesDeLineasActivas(f.Libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task UnaReservaConfirmadaNoExpiraAunqueNadieLaBarra_YLaExpiracionNoLaToca()
    {
        var f = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(f.Libro, 3)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        Assert.True((await Confirmar(pila, entrada.PedidoId)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddHours(3);

        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(f.Libro, 1)]))).EsExito);

        Assert.Equal("confirmada", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Equal(0, pila.Auditoria.Contar("ReservaExpirada"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task DiezConfirmacionesSimultaneasDelMismoPedido_ConfirmanUnaVezYLasDemasSonRepeticion()
    {
        var f = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(f.Libro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        var compuerta = new TaskCompletionSource();
        var tareas = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await compuerta.Task;
            return await Confirmar(pila, entrada.PedidoId);
        })).ToList();
        compuerta.SetResult();
        var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(1, resultados.Count(r => !r.Valor.EsRepeticion));
        Assert.Equal(1, pila.Auditoria.Contar("ReservaConfirmada"));
        Assert.Equal((10, 2), await bd.StockDe(f.Lima, f.Libro));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-06")]
    public async Task ConfirmarUnaReservaVencida_SeRechazaYLaLiberaLaExpiracion()
    {
        var f = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(f.Libro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);

        var resultado = await Confirmar(pila, entrada.PedidoId);

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task UnPedidoDeRecojoConLineaEnOtraSucursal_SolicitaElTrasladoEnLaMismaTransaccion_YFijaElTrasladoDeLaLinea()
    {
        var f = await Preparar(enLima: 0, enCusco: 5);
        await bd.DefinirRuta(f.Cusco, f.Lima, 3);
        var traslados = new TrasladosDeIntegracionDoble(bd);
        using var pila = new PilaDeReservas(bd, s => s.AddScoped<IGeneradorDeTrasladosDePedido>(_ => traslados));
        var entrada = ARecojo("lima", [Linea(f.Libro, 2)]);
        var creada = await pila.Reservar(Cliente(), entrada);
        Assert.True(creada.Valor.Lineas.Single().RequiereTraslado);

        var resultado = await Confirmar(pila, entrada.PedidoId);

        var traslado = Assert.Single(resultado.Valor.Traslados);
        Assert.Equal(("cusco", "lima", 2), (traslado.CodigoDeOrigen, traslado.CodigoDeDestino, traslado.Cantidad));
        Assert.Equal(traslado.TrasladoId, await bd.Consultar<Guid>($"SELECT traslado_id FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));

        var repetida = await Confirmar(pila, entrada.PedidoId);
        Assert.True(repetida.Valor.EsRepeticion);
        Assert.Equal(traslado.TrasladoId, Assert.Single(repetida.Valor.Traslados).TrasladoId);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-03")]
    public async Task SinTrasladosCompuesto_UnPedidoDeRecojoQueLoNecesitaFallaALaVista_YNoQuedaConfirmado()
    {
        var f = await Preparar(enLima: 0, enCusco: 5);
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(f.Libro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Confirmar(pila, entrada.PedidoId));

        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-32")]
    public async Task SiLaAuditoriaFalla_LaConfirmacionSeRevierte()
    {
        var f = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(f.Libro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        pila.Auditoria.Falla = true;

        await Assert.ThrowsAsync<AuditoriaNoDisponibleException>(() => Confirmar(pila, entrada.PedidoId));

        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.NotNull(await bd.Consultar<DateTime?>($"SELECT fecha_expiracion FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-35")]
    public async Task ConfirmarComoClienteOSinActor_SeRechazaSinTocarNada()
    {
        var f = await Preparar();
        using var pila = new PilaDeReservas(bd);
        var entrada = ARecojo("lima", [Linea(f.Libro, 2)]);
        var cliente = Cliente();
        Assert.True((await pila.Reservar(cliente, entrada)).EsExito);

        var comoCliente = await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(cliente, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")));
        var sinActor = await pila.Ejecutar<ConfirmarReservasEntrada, ReservaDelPedido>(null, new ConfirmarReservasEntrada(entrada.PedidoId, Guid.NewGuid().ToString("N")));

        Assert.Equal(CategoriaDeError.Prohibido, comoCliente.Error!.Categoria);
        Assert.Equal(CategoriaDeError.NoAutenticado, sinActor.Error!.Categoria);
        Assert.Equal("pendiente", await bd.Consultar<string>($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{entrada.PedidoId}'"));
    }
}

/// <summary>T15: <c>ExtenderReservas</c> con PostgreSQL real (SEL-22, SEL-30).</summary>
public sealed class ExtenderReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly ActorActual Servicio = ActorActual.DeIntegracion("expiracion-de-reservas");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Task<Resultado<ExtensionDeReserva>> Extender(PilaDeReservas pila, Guid pedido) =>
        pila.Ejecutar<ExtenderReservasEntrada, ExtensionDeReserva>(Servicio, new ExtenderReservasEntrada(pedido));

    private async Task<(Guid Libro, ReservarEntrada Entrada)> Reservada(PilaDeReservas pila)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 10, 0);
        var entrada = ARecojo("lima", [Linea(libro, 2)]);
        Assert.True((await pila.Reservar(Cliente(), entrada)).EsExito);
        return (libro, entrada);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task Extender_LlevaLaExpiracionATreintaMinutos_UnaSolaVez()
    {
        using var pila = new PilaDeReservas(bd);
        var (_, entrada) = await Reservada(pila);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(10);

        var primera = await Extender(pila, entrada.PedidoId);
        var segunda = await Extender(pila, entrada.PedidoId);

        Assert.True(primera.Valor.Extendida);
        Assert.Equal(pila.Reloj.Ahora.AddMinutes(-10).AddMinutes(30), primera.Valor.Reserva.ExpiraEn);
        Assert.False(segunda.Valor.Extendida);
        Assert.True(await bd.Consultar<bool>($"SELECT extendida FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task DiezAvisosSimultaneos_ExtiendenUnaSolaVez()
    {
        using var pila = new PilaDeReservas(bd);
        var (_, entrada) = await Reservada(pila);
        var compuerta = new TaskCompletionSource();
        var tareas = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            await compuerta.Task;
            return await Extender(pila, entrada.PedidoId);
        })).ToList();
        compuerta.SetResult();

        var resultados = await Task.WhenAll(tareas).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.All(resultados, r => Assert.True(r.EsExito));
        Assert.Equal(1, resultados.Count(r => r.Valor.Extendida));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-30")]
    public async Task UnAvisoDespuesDeVencida_EsExitoSinCambios()
    {
        using var pila = new PilaDeReservas(bd);
        var (_, entrada) = await Reservada(pila);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(20);

        var resultado = await Extender(pila, entrada.PedidoId);

        Assert.True(resultado.EsExito);
        Assert.False(resultado.Valor.Extendida);
        Assert.False(await bd.Consultar<bool>($"SELECT extendida FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-24")]
    public async Task UnReemplazoDespuesDeExtender_HeredaLaMarca_YNoPuedeExtenderOtraVez()
    {
        using var pila = new PilaDeReservas(bd);
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 10, 0);
        var cliente = Cliente();
        var pedido = Guid.NewGuid();
        Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro)], pedido))).EsExito);
        Assert.True((await Extender(pila, pedido)).Valor.Extendida);

        Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro)], pedido))).EsExito);
        var otra = await Extender(pila, pedido);

        Assert.False(otra.Valor.Extendida);
    }
}
