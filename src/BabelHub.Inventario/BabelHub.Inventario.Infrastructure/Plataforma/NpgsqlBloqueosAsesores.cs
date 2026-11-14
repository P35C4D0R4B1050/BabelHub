using System.Globalization;
using BabelHub.Inventario.Application.Plataforma;
using Npgsql;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>Opciones de los bloqueos asesores.</summary>
public sealed record OpcionesDeBloqueosAsesores
{
    /// <summary>Espera máxima para obtener un bloqueo (<c>lock_timeout</c>); pasada, se lanza <see cref="EsperaDeBloqueoAgotadaException"/>.</summary>
    public TimeSpan EsperaMaxima { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// <see cref="IBloqueosAsesores"/> sobre <c>pg_advisory_xact_lock</c> con la misma convención del motor de límites
/// (<c>hashtextextended(clave, 0)</c>), pero con claves de prefijo propio por tipo (<c>bloqueo-asesor|tipo|id</c>) que no se parecen a las
/// del motor (<c>política|contador</c>). El bloqueo vive en la transacción de <see cref="IConexionDeLaUnidadDeTrabajo"/> y se libera al
/// confirmarla o revertirla; sin transacción activa falla.
/// </summary>
public sealed class NpgsqlBloqueosAsesores(IConexionDeLaUnidadDeTrabajo unidad, OpcionesDeBloqueosAsesores? opciones = null) : IBloqueosAsesores
{
    private const string PrefijoDeClave = "bloqueo-asesor";
    private const string EsperaAgotada = "55P03";

    private readonly OpcionesDeBloqueosAsesores _opciones = Validar(opciones ?? new OpcionesDeBloqueosAsesores());

    public Task Cliente(Guid clienteId, CancellationToken cancelacion = default) => Tomar("cliente", Identificador(clienteId), cancelacion);

    public Task Pedido(Guid pedidoId, CancellationToken cancelacion = default) => Tomar("pedido", Identificador(pedidoId), cancelacion);

    public Task Recepcion(CancellationToken cancelacion = default) => Tomar("recepcion", "unica", cancelacion);

    public Task Devolucion(Guid devolucionId, CancellationToken cancelacion = default) => Tomar("devolucion", Identificador(devolucionId), cancelacion);

    public async Task<bool> IntentarPedido(Guid pedidoId, CancellationToken cancelacion = default)
    {
        await using var comando = Comando("SELECT pg_try_advisory_xact_lock(hashtextextended(@clave, 0))");
        comando.Parameters.AddWithValue("clave", Clave("pedido", Identificador(pedidoId)));
        return (bool)(await comando.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
    }

    public static string Clave(string tipo, string identificador) => $"{PrefijoDeClave}|{tipo}|{identificador}";

    private static OpcionesDeBloqueosAsesores Validar(OpcionesDeBloqueosAsesores opciones) =>
        opciones.EsperaMaxima > TimeSpan.Zero
            ? opciones
            : throw new ArgumentOutOfRangeException(nameof(opciones), "La espera máxima del bloqueo es positiva.");

    private static string Identificador(Guid id) =>
        id == Guid.Empty ? throw new ArgumentException("El identificador del bloqueo no puede ser vacío.", nameof(id)) : id.ToString("N", CultureInfo.InvariantCulture);

    private async Task Tomar(string tipo, string identificador, CancellationToken cancelacion)
    {
        // El lock_timeout vale solo para este bloqueo: se lee el valor vigente de la transacción y se restaura al terminar.
        string previo;
        await using (var lectura = Comando("SELECT current_setting('lock_timeout')"))
        {
            previo = (string)(await lectura.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!;
        }

        await FijarEspera(((long)Math.Ceiling(_opciones.EsperaMaxima.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture) + "ms", cancelacion).ConfigureAwait(false);
        try
        {
            await using var candado = Comando("SELECT pg_advisory_xact_lock(hashtextextended(@clave, 0))");
            candado.Parameters.AddWithValue("clave", Clave(tipo, identificador));
            await candado.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == EsperaAgotada)
        {
            throw new EsperaDeBloqueoAgotadaException(ex);
        }

        await FijarEspera(previo, cancelacion).ConfigureAwait(false);
    }

    private async Task FijarEspera(string valor, CancellationToken cancelacion)
    {
        await using var comando = Comando("SELECT set_config('lock_timeout', @valor, true)");
        comando.Parameters.AddWithValue("valor", valor);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }

    private NpgsqlCommand Comando(string sql) => new(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
}
