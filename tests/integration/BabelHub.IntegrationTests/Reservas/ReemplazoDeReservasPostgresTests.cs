using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T13: el reemplazo de la reserva pendiente del mismo pedido (SEL-16, SEL-36, SEL-37, AC-SEL-38) con PostgreSQL real.</summary>
public sealed class ReemplazoDeReservasPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<(Guid Lima, Guid Libro)> ConStock(int stock)
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, stock, 0);
        return (lima, libro);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-16")]
    public async Task UnReemplazoLiberaLaAnteriorConAuditoriaYReservaLaNueva_EnUnaSolaTransaccion()
    {
        var (lima, libro) = await ConStock(10);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var pedido = Guid.NewGuid();
        Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 4)], pedido))).EsExito);

        var nueva = await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 2)], pedido));

        Assert.True(nueva.EsExito, nueva.Error?.Clave);
        Assert.Equal((10, 2), await bd.StockDe(lima, libro));
        Assert.Equal(["liberada", "pendiente"], (await Estados(pedido)).Order(StringComparer.Ordinal));
        var liberada = Assert.Single(pila.Auditoria.Registros, r => r.Accion == "ReservaLiberada");
        Assert.Equal(("reemplazo", "4", pedido.ToString("D")), (liberada.Detalle["causa"], liberada.Detalle["cantidad"], liberada.ObjetoId));
        Assert.Equal(1, await bd.Consultar<int>($"SELECT max(reemplazos) FROM reservas.reservas WHERE pedido_id = '{pedido}'"));
        Assert.Empty(pila.Eventos.Eventos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-37")]
    public async Task UnReemplazoSinStock_DejaLaAnteriorIntactaYNoIncrementaLaCuenta()
    {
        var (lima, libro) = await ConStock(5);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var pedido = Guid.NewGuid();
        Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 3)], pedido))).EsExito);

        var fallido = await pila.Reservar(cliente, ARecojo("lima", [Linea(libro, 80)], pedido));

        Assert.Equal("stock-insuficiente", fallido.Error!.Clave);
        Assert.Equal((5, 3), await bd.StockDe(lima, libro));
        Assert.Equal(["pendiente"], await Estados(pedido));
        Assert.Equal(0, await bd.Consultar<int>($"SELECT max(reemplazos) FROM reservas.reservas WHERE pedido_id = '{pedido}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-37")]
    public async Task ElCuartoReemplazoSeRechazaConElTopeYElInstanteDeLaVigente_ConUnEventoDeSeguridad()
    {
        var (lima, libro) = await ConStock(10);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var pedido = Guid.NewGuid();
        for (var i = 0; i < 4; i++)
        {
            Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro)], pedido))).EsExito);
        }

        var cuarto = await pila.Reservar(cliente, ARecojo("lima", [Linea(libro)], pedido));

        Assert.Equal((CategoriaDeError.Limite, "limite-excedido"), (cuarto.Error!.Categoria, cuarto.Error.Clave));
        Assert.True(DetalleDeLimiteDeReservas.TryLeer(cuarto.Error.Errores, out var rechazo));
        Assert.Equal(MotivosDeLimiteDeReservas.ReemplazosPorPedido, rechazo!.Motivo);
        Assert.Equal(pila.Reloj.Ahora.AddMinutes(15), rechazo.DisponibleDesde);
        Assert.Equal((10, 1), await bd.StockDe(lima, libro));
        var evento = Assert.Single(pila.Eventos.Eventos);
        Assert.Equal((MotivosDeLimiteDeReservas.ReemplazosPorPedido, cliente.UsuarioId!.Value.ToString("D")), (evento.Limite, evento.Usuario));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-38")]
    public async Task UnPedidoCuyaReservaVencioOSeLibero_RespondeEstadoInvalido()
    {
        var (lima, libro) = await ConStock(10);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var pedido = Guid.NewGuid();
        Assert.True((await pila.Reservar(cliente, ARecojo("lima", [Linea(libro)], pedido))).EsExito);
        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(20);

        var vencida = await pila.Reservar(cliente, ARecojo("lima", [Linea(libro)], pedido));

        Assert.Equal((CategoriaDeError.Conflicto, "reserva-estado-invalido"), (vencida.Error!.Categoria, vencida.Error.Clave));
        Assert.Equal((10, 1), await bd.StockDe(lima, libro));
    }

    private async Task<List<string>> Estados(Guid pedido)
    {
        await using var comando = bd.Administracion.CreateCommand($"SELECT estado FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}'");
        var estados = new List<string>();
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            estados.Add(lector.GetString(0));
        }

        return estados;
    }
}
