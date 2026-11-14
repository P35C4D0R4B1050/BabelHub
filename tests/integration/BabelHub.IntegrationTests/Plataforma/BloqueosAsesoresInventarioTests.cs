using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// <c>IBloqueosAsesores</c> contra PostgreSQL real (inventario-fuentes T02): claves de prefijo propio por tipo, espera máxima, la variante
/// que no espera y la liberación al terminar la transacción. Cada «transacción» es una unidad de trabajo distinta con su propia conexión.
/// </summary>
public sealed class BloqueosAsesoresInventarioTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>
{
    private static readonly TimeSpan EsperaCorta = TimeSpan.FromMilliseconds(400);

    private UnidadDeTrabajoPostgres Unidad() => new(bd.Acceso);

    private static NpgsqlBloqueosAsesores Bloqueos(UnidadDeTrabajoPostgres unidad, TimeSpan? espera = null) =>
        new(unidad, new OpcionesDeBloqueosAsesores { EsperaMaxima = espera ?? EsperaCorta });

    /// <summary>Corre <paramref name="primero"/> dentro de una transacción que se mantiene abierta mientras corre <paramref name="durante"/>.</summary>
    private async Task MantenerYProbar(Func<NpgsqlBloqueosAsesores, Task> primero, Func<Task> durante)
    {
        var unidad = Unidad();
        var tomado = new TaskCompletionSource();
        var soltar = new TaskCompletionSource();
        var tenedor = unidad.Ejecutar<int>(async token =>
        {
            await primero(Bloqueos(unidad));
            tomado.SetResult();
            await soltar.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            return 1;
        });

        await tomado.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await durante();
        }
        finally
        {
            soltar.SetResult();
            await tenedor;
        }
    }

    [Fact]
    public async Task Cliente_ConElMismoIdentificadorQueUnPedido_NoChocaConEl()
    {
        var id = Guid.NewGuid();

        await MantenerYProbar(
            b => b.Cliente(id),
            async () =>
            {
                var otra = Unidad();
                await otra.Ejecutar<int>(async token =>
                {
                    // Otro tipo con el mismo identificador tiene otra clave: no espera ni agota el tiempo.
                    await Bloqueos(otra).Pedido(id, token);
                    Assert.True(await Bloqueos(otra).IntentarPedido(Guid.NewGuid(), token));
                    return 1;
                });
            });
    }

    [Fact]
    public async Task Cliente_ConElMismoTipoYElMismoIdentificador_EsperaYAgotaElTiempo()
    {
        var id = Guid.NewGuid();

        await MantenerYProbar(
            b => b.Cliente(id),
            async () =>
            {
                var otra = Unidad();
                await Assert.ThrowsAsync<EsperaDeBloqueoAgotadaException>(() => otra.Ejecutar<int>(async token =>
                {
                    await Bloqueos(otra).Cliente(id, token);
                    return 1;
                }));
            });

        // Al terminar la primera transacción, el bloqueo queda libre.
        var libre = Unidad();
        await libre.Ejecutar<int>(async token =>
        {
            await Bloqueos(libre).Cliente(id, token);
            return 1;
        });
    }

    [Fact]
    public async Task Recepcion_EsUnaSolaClaveQueSerializaAQuienLaToma()
    {
        await MantenerYProbar(
            b => b.Recepcion(),
            async () =>
            {
                var otra = Unidad();
                await Assert.ThrowsAsync<EsperaDeBloqueoAgotadaException>(() => otra.Ejecutar<int>(async token =>
                {
                    await Bloqueos(otra).Recepcion(token);
                    return 1;
                }));
            });
    }

    [Fact]
    public async Task Devolucion_SerializaPorIdentificadorYNoChocaConOtraDevolucion()
    {
        var id = Guid.NewGuid();

        await MantenerYProbar(
            b => b.Devolucion(id),
            async () =>
            {
                var otra = Unidad();
                await otra.Ejecutar<int>(async token =>
                {
                    await Bloqueos(otra).Devolucion(Guid.NewGuid(), token);
                    return 1;
                });
                await Assert.ThrowsAsync<EsperaDeBloqueoAgotadaException>(() => otra.Ejecutar<int>(async token =>
                {
                    await Bloqueos(otra).Devolucion(id, token);
                    return 1;
                }));
            });
    }

    [Fact]
    public async Task IntentarPedido_ConElPedidoBloqueadoPorOtra_DevuelveFalsoSinEsperarYLuegoVerdadero()
    {
        var id = Guid.NewGuid();

        await MantenerYProbar(
            b => b.Pedido(id),
            async () =>
            {
                var otra = Unidad();
                var cronometro = System.Diagnostics.Stopwatch.StartNew();
                var obtenido = await otra.Ejecutar(token => Bloqueos(otra, TimeSpan.FromSeconds(20)).IntentarPedido(id, token));
                cronometro.Stop();

                Assert.False(obtenido);
                Assert.True(cronometro.Elapsed < TimeSpan.FromSeconds(10), "IntentarPedido no debe esperar el bloqueo.");
            });

        var libre = Unidad();
        Assert.True(await libre.Ejecutar(token => Bloqueos(libre).IntentarPedido(id, token)));
    }

    [Fact]
    public async Task IntentarPedido_BloqueaAlQueLoObtuvoHastaElFinDeSuTransaccion()
    {
        var id = Guid.NewGuid();
        var tomado = new TaskCompletionSource();
        var soltar = new TaskCompletionSource();
        var primera = Unidad();
        var tenedor = primera.Ejecutar<int>(async token =>
        {
            Assert.True(await Bloqueos(primera).IntentarPedido(id, token));
            tomado.SetResult();
            await soltar.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            return 1;
        });
        await tomado.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var otra = Unidad();
        await Assert.ThrowsAsync<EsperaDeBloqueoAgotadaException>(() => otra.Ejecutar<int>(async token =>
        {
            await Bloqueos(otra).Pedido(id, token);
            return 1;
        }));

        soltar.SetResult();
        await tenedor;
    }

    [Fact]
    public async Task UnBloqueo_NoCambiaElLockTimeoutDeLaTransaccion()
    {
        var unidad = Unidad();

        await unidad.Ejecutar<int>(async token =>
        {
            await EjecutarSql(unidad, "SET LOCAL lock_timeout = '7s'");
            await Bloqueos(unidad).Cliente(Guid.NewGuid(), token);

            Assert.Equal("7s", await Escalar(unidad, "SELECT current_setting('lock_timeout')"));
            return 1;
        });
    }

    [Fact]
    public async Task SinUnaTransaccionActiva_Lanza()
    {
        var unidad = Unidad();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Bloqueos(unidad).Cliente(Guid.NewGuid()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bloqueos(unidad).IntentarPedido(Guid.NewGuid()));
    }

    [Fact]
    public async Task ConUnIdentificadorVacio_Rechaza()
    {
        var unidad = Unidad();

        await unidad.Ejecutar<int>(async token =>
        {
            await Assert.ThrowsAsync<ArgumentException>(() => Bloqueos(unidad).Pedido(Guid.Empty, token));
            return 1;
        });
    }

    [Fact]
    public void LasClaves_TienenPrefijoPropioYNoSeParecenALasDelMotorDeLimites()
    {
        var id = Guid.NewGuid().ToString("N");

        Assert.NotEqual(NpgsqlBloqueosAsesores.Clave("cliente", id), NpgsqlBloqueosAsesores.Clave("pedido", id));
        Assert.StartsWith("bloqueo-asesor|", NpgsqlBloqueosAsesores.Clave("devolucion", id), StringComparison.Ordinal);
        Assert.DoesNotContain("||", NpgsqlBloqueosAsesores.Clave("recepcion", "unica"), StringComparison.Ordinal);
    }

    private static async Task EjecutarSql(UnidadDeTrabajoPostgres unidad, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<string?> Escalar(UnidadDeTrabajoPostgres unidad, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
        return (string?)await comando.ExecuteScalarAsync();
    }
}
