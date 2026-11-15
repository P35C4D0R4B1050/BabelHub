using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Application.Reservas.Interno;
using Microsoft.Extensions.DependencyInjection;
using static BabelHub.IntegrationTests.Reservas.PilaDeReservas;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// T10: la concurrencia de AC01 con PostgreSQL real, el rol real de la aplicación y el contenedor de producción (con la transacción y el reintento por interbloqueo). Las solicitudes arrancan juntas desde una compuerta y se
/// comprueba la <b>consecuencia observable</b> (una sola gana, el vendible nunca es negativo, lo reservado coincide con las líneas activas) y que <b>no hubo interbloqueos</b>: el reintento por interbloqueo los taparía, así
/// que se cuentan los que PostgreSQL detectó. Cada solicitud es una transacción con su conexión: con el pool de la prueba (60) y 100 solicitudes, las que no caben esperan su conexión y terminan.
/// </summary>
public sealed class ConcurrenciaDeReservasTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(120);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed record Escenario(Guid Lima, Guid Cusco, Guid Ayacucho);

    private async Task<Escenario> TresFuentes() =>
        new(await bd.AgregarFuente("lima", recepcion: true), await bd.AgregarFuente("cusco"), await bd.AgregarFuente("ayacucho"));

    /// <summary>Lanza todas las tareas desde una compuerta (para que compitan de verdad) y espera a que terminen.</summary>
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

    private async Task<long> Interbloqueos() => await bd.Consultar<long>("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()");

    /// <summary>Las estadísticas de PostgreSQL se publican con un pequeño retraso: se espera antes de leer el contador.</summary>
    private async Task<long> InterbloqueosDespuesDe(long antes)
    {
        await Task.Delay(TimeSpan.FromSeconds(2));
        return await Interbloqueos() - antes;
    }

    private async Task AfirmarCoherencia(params Guid[] ediciones)
    {
        foreach (var edicion in ediciones)
        {
            // Lo reservado de la red es la suma de las líneas activas, nunca negativo y nunca sobre el físico (los CHECK de la tabla son la última defensa; aquí se comprueba la consecuencia).
            Assert.Equal(await bd.ReservadoDeLaRed(edicion), await bd.UnidadesDeLineasActivas(edicion));
            Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM fuentes.stock WHERE edicion_id = '{edicion}' AND (reservado < 0 OR reservado > fisico)"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task CienClientesPorUnaUnidad_SoloUnoLaObtieneYElVendibleNuncaEsNegativo()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 1, 0);
        using var pila = new PilaDeReservas(bd);
        var antes = await Interbloqueos();

        // El pool es menor que la carga: cada solicitud es una transacción con su conexión y las que no caben esperan la suya (el pool de producción debe dimensionarse para eso, ver decisiones T10).
        Assert.True(new Npgsql.NpgsqlConnectionStringBuilder(bd.Acceso.ConnectionString).MaxPoolSize < 100);

        var resultados = await Simultaneas(Enumerable.Range(0, 100).Select<int, Func<Task<Resultado<ReservaDelPedido>>>>(
            _ => () => pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro)]))));

        var exitos = resultados.Where(r => r.EsExito).ToList();
        var fallos = resultados.Where(r => !r.EsExito).ToList();
        Assert.Single(exitos);
        Assert.Equal(99, fallos.Count);
        Assert.All(fallos, r => Assert.Equal((CategoriaDeError.Conflicto, "stock-insuficiente"), (r.Error!.Categoria, r.Error.Clave)));
        Assert.Equal((1, 1), await bd.StockDe(f.Lima, libro));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reservas"));
        await AfirmarCoherencia(libro);
        Assert.Equal(0, await InterbloqueosDespuesDe(antes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task CienClientesPorVeinteUnidades_SeConcedenExactamenteVeinte_ConElEscalonDeStockAgotado()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 20, 0);
        using var pila = new PilaDeReservas(bd);

        var resultados = await Simultaneas(Enumerable.Range(0, 100).Select<int, Func<Task<Resultado<ReservaDelPedido>>>>(
            _ => () => pila.Reservar(Cliente(), ARecojo("lima", [Linea(libro)]))));

        Assert.Equal(20, resultados.Count(r => r.EsExito));
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal("stock-insuficiente", r.Error!.Clave));
        Assert.Equal((20, 20), await bd.StockDe(f.Lima, libro));
        await AfirmarCoherencia(libro);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-26")]
    [InlineData(5)]
    [InlineData(0)]
    public async Task DosClientesDeRecojoEnAyacuchoConUnaUnidadAllí_UnoRecibeAyacuchoYElOtroEsReasignadoORechazado(int enCusco)
    {
        var f = await TresFuentes();
        await bd.DefinirRuta(f.Cusco, f.Ayacucho, 3);
        using var pila = new PilaDeReservas(bd);
        var antes = await Interbloqueos();

        // Varias rondas con ediciones nuevas: la carrera cae en un orden distinto cada vez.
        for (var ronda = 0; ronda < 15; ronda++)
        {
            var libro = await bd.AgregarEdicion();
            await bd.PonerStock(f.Ayacucho, libro, 1, 0);
            await bd.PonerStock(f.Cusco, libro, enCusco, 0);

            var resultados = await Simultaneas(Enumerable.Range(0, 2).Select<int, Func<Task<Resultado<ReservaDelPedido>>>>(
                _ => () => pila.Reservar(Cliente(), ARecojo("ayacucho", [Linea(libro)]))));

            Assert.Equal(1, resultados.Count(r => r.EsExito && r.Valor.Lineas.Single().CodigoDeFuente == "ayacucho" && !r.Valor.Lineas.Single().RequiereTraslado));
            if (enCusco > 0)
            {
                var reasignada = Assert.Single(resultados, r => r.EsExito && r.Valor.Lineas.Single().CodigoDeFuente == "cusco").Valor.Lineas.Single();
                Assert.True(reasignada.RequiereTraslado);
                Assert.Equal(3, reasignada.PlazoEstimadoEnDias);
                Assert.Equal((enCusco, 1), await bd.StockDe(f.Cusco, libro));
            }
            else
            {
                var rechazada = Assert.Single(resultados, r => !r.EsExito);
                Assert.Equal("stock-insuficiente", rechazada.Error!.Clave);
            }

            Assert.Equal((1, 1), await bd.StockDe(f.Ayacucho, libro));
            await AfirmarCoherencia(libro);
        }

        Assert.Equal(0, await InterbloqueosDespuesDe(antes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task DosReservasConEdicionesCruzadas_NoSeInterbloquean()
    {
        var f = await TresFuentes();
        var a = await bd.AgregarEdicion();
        var b = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, a, 100, 0);
        await bd.PonerStock(f.Lima, b, 100, 0);
        using var pila = new PilaDeReservas(bd);
        var antes = await Interbloqueos();

        // Mitad pide (a, b) y mitad (b, a): el orden único de bloqueo (edición, fuente) lo hace inocuo.
        var resultados = await Simultaneas(Enumerable.Range(0, 40).Select<int, Func<Task<Resultado<ReservaDelPedido>>>>(
            i => () => pila.Reservar(Cliente(), ARecojo("lima", i % 2 == 0 ? [Linea(a), Linea(b)] : [Linea(b), Linea(a)]))));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal((100, 40), await bd.StockDe(f.Lima, a));
        Assert.Equal((100, 40), await bd.StockDe(f.Lima, b));
        await AfirmarCoherencia(a, b);
        Assert.Equal(0, await InterbloqueosDespuesDe(antes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-27")]
    public async Task VencidasConEdicionesCruzadasEnOrdenContrario_SeExpiranUnaSolaVezYSinInterbloqueos()
    {
        var f = await TresFuentes();
        var a = await bd.AgregarEdicion();
        var b = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, a, 1000, 0);
        await bd.PonerStock(f.Lima, b, 1000, 0);
        using var pila = new PilaDeReservas(bd);

        // Doce pedidos con líneas en (a, b) o en (b, a) que vencen sin que nadie los barra: 24 líneas.
        var vencidos = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var pedido = Guid.NewGuid();
            vencidos.Add(pedido);
            Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", i % 2 == 0 ? [Linea(a, 2), Linea(b, 3)] : [Linea(b, 2), Linea(a, 3)], pedido))).EsExito);
        }

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);
        var antes = await Interbloqueos();

        // Muchas transacciones a la vez barren las mismas vencidas desde lados distintos: reservas de (a, b) y de (b, a), y la expiración por pedido del trabajo de fondo.
        var trabajos = new List<Func<Task<bool>>>();
        for (var i = 0; i < 16; i++)
        {
            var cruzada = i % 2 == 0;
            trabajos.Add(async () => (await pila.Reservar(Cliente(), ARecojo("lima", cruzada ? [Linea(a), Linea(b)] : [Linea(b), Linea(a)]))).EsExito);
        }

        foreach (var pedido in vencidos.Take(6))
        {
            trabajos.Add(async () => (await pila.Ejecutar<ExpirarReservasEntrada, ExpiracionDelPedido>(ActorActual.DelSistema(), new ExpirarReservasEntrada(pedido))).EsExito);
        }

        var resultados = await Simultaneas(trabajos);

        // Un barrido final deja todo expirado (una transacción pudo omitir un pedido que otra tenía: error conservador, nunca un error de más).
        Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", [Linea(a), Linea(b)]))).EsExito);

        Assert.All(resultados, ok => Assert.True(ok));
        Assert.Equal(24L, await bd.Consultar<long>("SELECT count(*) FROM reservas.reserva_lineas WHERE estado = 'expirada'"));
        Assert.Equal(24, pila.Auditoria.Contar("ReservaExpirada"));
        Assert.Equal(24, pila.Auditoria.Registros.Where(r => r.Accion == "ReservaExpirada").Select(r => (r.ObjetoId, r.Detalle["edicionId"])).Distinct().Count());
        Assert.All(pila.Auditoria.Registros.Where(r => r.Accion == "ReservaExpirada"), r => Assert.Equal(TipoDeActor.Sistema, r.Actor));
        await AfirmarCoherencia(a, b);
        Assert.Equal(0, await InterbloqueosDespuesDe(antes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task LaPrimeraVezQueUnaFuenteNuevaTieneFilas_ConVencidasYEdicionesCruzadas_NoHayInterbloqueo()
    {
        var f = await TresFuentes();
        var a = await bd.AgregarEdicion();
        var b = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, a, 500, 0);
        await bd.PonerStock(f.Lima, b, 500, 0);
        using var pila = new PilaDeReservas(bd);

        // Vencidas que obligan a bloquear líneas por id…
        for (var i = 0; i < 6; i++)
        {
            Assert.True((await pila.Reservar(Cliente(), ARecojo("lima", i % 2 == 0 ? [Linea(a), Linea(b)] : [Linea(b), Linea(a)]))).EsExito);
        }

        pila.Reloj.Ahora = pila.Reloj.Ahora.AddMinutes(16);

        // …y una fuente nueva sin ninguna fila: todas las transacciones intentan crear sus filas (a la vez, en el mismo orden) antes de bloquear líneas y filas.
        await bd.AgregarFuente("arequipa");
        var antes = await Interbloqueos();

        var resultados = await Simultaneas(Enumerable.Range(0, 24).Select<int, Func<Task<Resultado<ReservaDelPedido>>>>(
            i => () => pila.Reservar(Cliente(), ARecojo("lima", i % 2 == 0 ? [Linea(a), Linea(b)] : [Linea(b), Linea(a)]))));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.stock WHERE fuente_id = (SELECT id FROM fuentes.fuentes WHERE codigo = 'arequipa')"));
        await AfirmarCoherencia(a, b);
        Assert.Equal(0, await InterbloqueosDespuesDe(antes));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-04")]
    public async Task LasSolicitudesDelMismoClienteSeSerializan_YElMismoPedidoNoSeReservaDosVeces()
    {
        var f = await TresFuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(f.Lima, libro, 50, 0);
        using var pila = new PilaDeReservas(bd);
        var cliente = Cliente();
        var pedido = Guid.NewGuid();

        // Diez intentos simultáneos del mismo cliente sobre el mismo pedido: solo uno crea la reserva, los demás ven «reserva-existente» (hasta que T11 los trate como repetición idempotente).
        var resultados = await Simultaneas(Enumerable.Range(0, 10).Select<int, Func<Task<Resultado<ReservaDelPedido>>>>(
            _ => () => pila.Reservar(cliente, ARecojo("lima", [Linea(libro)], pedido))));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal("reserva-existente", r.Error!.Clave));
        Assert.Equal((50, 1), await bd.StockDe(f.Lima, libro));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM reservas.reserva_lineas WHERE pedido_id = '{pedido}'"));
    }
}
