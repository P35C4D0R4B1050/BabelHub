using BabelHub.Inventario.Application.Fuentes.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>
/// El costo por solicitud de la consulta de disponibilidad (RNF02, AC-FUE-08; hallazgo de capacidad de K6-1): cada ida a PostgreSQL gasta CPU de la base, que es el cuello de botella medido.
/// Con PostgreSQL real y el rol real de la aplicación se cuentan las <b>sentencias que viajan a la base</b> (las que registra Npgsql) por consulta: un presupuesto que sube solo si alguien lo
/// decide, y la comprobación de que reducirlas <b>no debilitó</b> el aislamiento: el rol y el alcance de una transacción no sobreviven en la conexión que vuelve al grupo (ya no hay
/// <c>DISCARD ALL</c>).
/// </summary>
public sealed class CostoPorSolicitudDeDisponibilidadTests(PostgresDeFuentesFixture bd) : IClassFixture<PostgresDeFuentesFixture>, IAsyncLifetime
{
    /// <summary>Presupuesto de sentencias de una consulta de disponibilidad de una edición, sin el tramo de Reservas (que aquí es un doble): la misma consulta enviaba 21 antes de la reducción (más 4 del tramo de Reservas).</summary>
    private const int PresupuestoDeSentencias = 12;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ActorActual Cliente => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente, null);

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task Disponibilidad_DeUnaEdicion_NoPasaDelPresupuestoDeSentencias()
    {
        var (origen, sentencias) = AbrirContando();
        await using var _ = origen;
        var edicion = await Sembrar();
        var pila = Pila(origen, Cliente);

        sentencias.Clear();
        var disponibilidad = await Consultar(pila, edicion);

        Assert.Single(disponibilidad);
        Assert.True(
            sentencias.Count <= PresupuestoDeSentencias,
            $"Una consulta de disponibilidad envió {sentencias.Count} sentencias (presupuesto {PresupuestoDeSentencias}):{Environment.NewLine}{string.Join(Environment.NewLine, sentencias.Select(s => "  " + s))}");
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task Disponibilidad_NoPreguntaPorElUsuarioNiPorElAlcanceNiLeeLaConfiguracionPrevia()
    {
        var (origen, sentencias) = AbrirContando();
        await using var _ = origen;
        var edicion = await Sembrar();
        var pila = Pila(origen, Cliente);

        sentencias.Clear();
        await Consultar(pila, edicion);

        Assert.DoesNotContain(sentencias, s => s.Contains("current_user", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(sentencias, s => s.Contains("current_setting", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-08")]
    public async Task ElRolYElAlcance_NoSobrevivenALaTransaccionEnLaConexionQueVuelveAlGrupo()
    {
        // Un grupo de una sola conexión: la siguiente apertura recibe exactamente la misma conexión física, sin reinicio de sesión.
        var (origen, _) = AbrirContando(maximoDeConexiones: 1);
        await using var _ = origen;
        var edicion = await Sembrar();
        var pila = Pila(origen, Cliente);
        await Consultar(pila, edicion);

        await using var conexion = await origen.OpenConnectionAsync();
        await using var comando = new NpgsqlCommand("SELECT current_user, coalesce(current_setting('babelhub.alcance', true), ''), current_setting('lock_timeout'), current_setting('transaction_read_only')", conexion);
        await using var lector = await comando.ExecuteReaderAsync();
        Assert.True(await lector.ReadAsync());
        Assert.Equal("acceso", lector.GetString(0));
        Assert.Equal(string.Empty, lector.GetString(1));
        Assert.Equal("0", lector.GetString(2));
        Assert.Equal("off", lector.GetString(3));
    }

    /// <summary>Lo que hace <c>ConsultarDisponibilidad</c>: en una sola transacción de solo lectura, las fichas de las ediciones y su disponibilidad (fuentes, stock y vencidas).</summary>
    private static Task<IReadOnlyList<BabelHub.Inventario.Application.Fuentes.DisponibilidadDeEdicion>> Consultar(PilaDeFuentes pila, Guid edicion) =>
        pila.Unidad.EjecutarLectura(async token =>
        {
            await pila.Fichas.PorIds([edicion], token);
            return await pila.Consulta.Disponibilidad([edicion], token);
        });

    private async Task<Guid> Sembrar()
    {
        var fuente = await bd.AgregarFuente("lima");
        await bd.AgregarFuente("cusco", region: "cusco");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(fuente, edicion, 10, 2);
        return edicion;
    }

    private PilaDeFuentes Pila(NpgsqlDataSource origen, ActorActual actor) => new(bd, actor, origen: origen);

    /// <summary>Abre el origen de datos como lo abre la Api (<c>BasesDelInventario.Abrir</c>) con un registro que anota cada sentencia que Npgsql envía a la base.</summary>
    private (NpgsqlDataSource Origen, List<string> Sentencias) AbrirContando(int maximoDeConexiones = 16)
    {
        var sentencias = new List<string>();
        var fabrica = LoggerFactory.Create(b => b.SetMinimumLevel(LogLevel.Trace).AddProvider(new ProveedorDeSentencias(sentencias)));
        var origen = BasesDelInventario.Abrir(new NpgsqlConnectionStringBuilder(bd.CadenaDeAcceso) { MaxPoolSize = maximoDeConexiones }.ConnectionString, fabrica);
        return (origen, sentencias);
    }

    private sealed class ProveedorDeSentencias(List<string> sentencias) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoria) => new Registro(sentencias);

        public void Dispose()
        {
        }

        private sealed class Registro(List<string> sentencias) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState estado) where TState : notnull => null;

            public bool IsEnabled(LogLevel nivel) => true;

            public void Log<TState>(LogLevel nivel, EventId evento, TState estado, Exception? error, Func<TState, Exception?, string> formato)
            {
                // «Command execution completed (duration=…ms): <sql>» es lo que Npgsql registra por cada comando que viaja a la base.
                var mensaje = formato(estado, error);
                var marca = "Command execution completed";
                if (mensaje.StartsWith(marca, StringComparison.Ordinal))
                {
                    var dosPuntos = mensaje.IndexOf("): ", StringComparison.Ordinal);
                    lock (sentencias)
                    {
                        sentencias.Add(dosPuntos < 0 ? mensaje : mensaje[(dosPuntos + 3)..]);
                    }
                }
            }
        }
    }
}
