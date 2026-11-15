using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T12: los límites de reservar con PostgreSQL real (SEL-13, SEL-14, SEL-19, SEL-15, SEL-18): el bloqueo del cliente hace atómicas las solicitudes simultáneas, los contadores cuentan en la transacción de la reserva
/// (un rechazo posterior los deshace) y el motor serializa a quienes cuentan lo mismo.
/// </summary>
public sealed class LimitesDeReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(120);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Escenario(Guid Lima, Guid Libro);

    private async Task<Escenario> ConStock(int stock = 1000)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        await bd.AgregarFuente("cusco");
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, stock, 0);
        return new Escenario(lima, libro);
    }

    private static async Task<T[]> Simultaneas<T>(IEnumerable<Func<Task<T>>> trabajos)
    {
        var compuerta = new TaskCompletionSource();
        var tareas = trabajos.Select(trabajo => Task.Run(async () =>
        {
            await compuerta.Task;
            return await trabajo();
        })).ToList();
        compuerta.SetResult();
        return await Task.WhenAll(tareas).WaitAsync(Limite);
    }

    /// <summary>Confirma con SQL las reservas pendientes de un cliente (la confirmación real llega en T14): así deja de ser «una pendiente» y se prueba solo el contador por hora.</summary>
    private Task Confirmar(Guid clienteId) => bd.Ejecutar(
        $"""
        UPDATE reservas.reserva_lineas SET estado = 'confirmada', confirmada_en = now(), estado_cambiado_en = now()
        WHERE reserva_id IN (SELECT id FROM reservas.reservas WHERE cliente_id = '{clienteId}') AND estado = 'pendiente';
        UPDATE reservas.reservas SET fecha_expiracion = NULL WHERE cliente_id = '{clienteId}';
        """);

    private static RechazoPorLimite Rechazo(Resultado<ReservaDelPedido> resultado)
    {
        Assert.Equal((CategoriaDeError.Limite, "limite-excedido"), (resultado.Error!.Categoria, resultado.Error.Clave));
        Assert.True(DetalleDeLimiteDeReservas.TryLeer(resultado.Error.Errores, out var rechazo));
        return rechazo!;
    }

    private async Task<long> EventosDelCliente(Guid clienteId) =>
        await bd.Consultar<long>($"SELECT count(*) FROM plataforma.eventos_limite WHERE clave LIKE 'reservas-por-hora-por-cliente|%{clienteId:D}'");

    [Fact]
    [Trait("Criterio", "AC-SEL-13")]
    public async Task DiezSolicitudesSimultaneasDelMismoClienteEnPedidosDistintos_SoloUnaCreaLaReserva_LasDemasSonLimite()
    {
        var f = await ConStock();
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();

        var resultados = await Simultaneas(Enumerable.Range(0, 10).Select<int, Func<Task<Resultado<ReservaDelPedido>>>>(
            _ => () => pila.Reservar(cliente, ARecojo("lima", [Linea(f.Libro)]))));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal(MotivosDeLimiteDeReservas.PendientePorCliente, Rechazo(r).Motivo));
        Assert.Equal((1000, 1), await bd.StockDe(f.Lima, f.Libro));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        Assert.Equal(9, pila.Eventos.Eventos.Count(e => e.Limite == MotivosDeLimiteDeReservas.PendientePorCliente));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task LaSextaReservaPorHoraDeUnCliente_SeRechazaConElInstanteDeReintentoYUnEventoDelMotor()
    {
        var f = await ConStock();
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        for (var i = 0; i < 5; i++)
        {
            var resultado = await pila.Reservar(cliente, ARecojo("lima", [Linea(f.Libro)]));
            Assert.True(resultado.EsExito, resultado.Error?.Clave);
            await Confirmar(cliente.UsuarioId!.Value);
        }

        var sexta = await pila.Reservar(cliente, ARecojo("lima", [Linea(f.Libro)]));

        var rechazo = Rechazo(sexta);
        Assert.Equal(MotivosDeLimiteDeReservas.PorHoraPorCliente, rechazo.Motivo);
        Assert.True(rechazo.EsperaEnSegundos is > 0 and <= 3600);
        Assert.Equal(5L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        Assert.Equal(5L, await EventosDelCliente(cliente.UsuarioId!.Value));
        var evento = Assert.Single(pila.Eventos.Eventos);
        Assert.Equal(MotivosDeLimiteDeReservas.PorHoraPorCliente, evento.Limite);
        Assert.Equal(cliente.UsuarioId!.Value.ToString("D"), evento.Usuario);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task UnaReservaRechazadaPorFaltaDeStock_NoCuentaComoIniciada()
    {
        var f = await ConStock();
        var agotado = await bd.AgregarEdicion();
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();

        for (var i = 0; i < 7; i++)
        {
            Assert.Equal("stock-insuficiente", (await pila.Reservar(cliente, ARecojo("lima", [Linea(agotado)]))).Error!.Clave);
        }

        Assert.Equal(0L, await EventosDelCliente(cliente.UsuarioId!.Value));
        for (var i = 0; i < 5; i++)
        {
            Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(f.Libro)]))).EsExito);
            await Confirmar(cliente.UsuarioId!.Value);
        }

        Assert.Equal(MotivosDeLimiteDeReservas.PorHoraPorCliente, Rechazo(await pila.Reservar(cliente, ARecojo("lima", [Linea(f.Libro)]))).Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-19")]
    public async Task DoceCuentasDeUnMismoOrigenReservandoALaVez_SeAceptanExactamenteDiez()
    {
        var f = await ConStock();
        using var pila = new PilaDeReservas(bd);
        var origen = OrigenPorFlujo.Nuevo();

        var resultados = await Simultaneas(Enumerable.Range(0, 12).Select<int, Func<Task<Resultado<ReservaDelPedido>>>>(
            _ => () => pila.Reservar(Cliente(), ARecojo("lima", [Linea(f.Libro)]), origen: origen)));

        Assert.Equal(10, resultados.Count(r => r.EsExito));
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal(MotivosDeLimiteDeReservas.PorHoraPorOrigen, Rechazo(r).Motivo));
        Assert.Equal((1000, 10), await bd.StockDe(f.Lima, f.Libro));
        Assert.Equal(2, pila.Eventos.Eventos.Count(e => e.Limite == MotivosDeLimiteDeReservas.PorHoraPorOrigen));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-19")]
    public async Task LaReservaGuardaLaHuellaDeOrigen_YLaDeUnOrigenQueNoRigeNoGuardaNinguna()
    {
        var f = await ConStock();
        using var pila = new PilaDeReservas(bd);
        var origen = OrigenPorFlujo.Nuevo();
        var entrada = ARecojo("lima", [Linea(f.Libro)]);

        Assert.True((await pila.Reservar(Cliente(), entrada, origen: origen)).EsExito);
        var sinOrigen = new BabelHub.Inventario.Application.Plataforma.OrigenDeSolicitud(origen.Huella, origen.Direccion) { AplicaLimitesPorOrigen = false };
        var otra = ARecojo("lima", [Linea(f.Libro)]);
        Assert.True((await pila.Reservar(Cliente(), otra, origen: sinOrigen)).EsExito);

        Assert.Equal(origen.Huella.ToString(), await bd.Consultar<string>($"SELECT huella_origen FROM reservas.reservas WHERE pedido_id = '{entrada.PedidoId}'"));
        Assert.Null(await bd.Consultar<string>($"SELECT huella_origen FROM reservas.reservas WHERE pedido_id = '{otra.PedidoId}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-14")]
    public async Task SiElLimiteDelOrigenRechazaDespuesDeContarAlCliente_ElContadorDelClienteSeDeshace()
    {
        var f = await ConStock();
        using var pila = new PilaDeReservas(bd);
        var origen = OrigenPorFlujo.Nuevo();
        for (var i = 0; i < 10; i++)
        {
            Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(f.Libro)]), origen: origen)).EsExito);
        }

        var cliente = Cliente();
        var rechazada = await pila.Reservar(cliente, ARecojo("lima", [Linea(f.Libro)]), origen: origen);

        Assert.Equal(MotivosDeLimiteDeReservas.PorHoraPorOrigen, Rechazo(rechazada).Motivo);
        Assert.Equal(0L, await EventosDelCliente(cliente.UsuarioId!.Value));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-18")]
    public async Task UnaRepeticionIdempotenteNoCuentaContraLosLimites()
    {
        var f = await ConStock();
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var entrada = ARecojo("lima", [Linea(f.Libro)]);
        Assert.True((await pila.Reservar(cliente, entrada)).EsExito);
        await Confirmar(cliente.UsuarioId!.Value);

        for (var i = 0; i < 8; i++)
        {
            Assert.True((await pila.Reservar(cliente, entrada)).Valor.EsRepeticion);
        }

        Assert.Equal(1L, await EventosDelCliente(cliente.UsuarioId!.Value));
        Assert.Empty(pila.Eventos.Eventos);
    }
}
