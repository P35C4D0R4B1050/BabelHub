using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Reclamaciones;

/// <summary>
/// Una escritura o lectura del esquema <c>reclamaciones</c> que PostgreSQL rechazó. Lleva solo el código de estado, la tabla y la restricción: <b>nunca</b> el <c>Detail</c> ni el <c>Statement</c> de la excepción original, porque el <c>Detail</c> de un <c>CHECK</c> o de un índice
/// único incluye la fila completa («Failing row contains …») y con ella el nombre, el documento, el domicilio y el correo (AGENTS §5, REC-06). No guarda la excepción original como interna.
/// </summary>
public sealed class FallaDeLaBaseDeReclamacionesException(string? codigoDeEstado, string? tabla, string? restriccion)
    : Exception($"La base de datos rechazó la operación de reclamaciones (estado {codigoDeEstado ?? "desconocido"}, tabla {tabla ?? "-"}, restricción {restriccion ?? "-"}).")
{
    public string? CodigoDeEstado { get; } = codigoDeEstado;

    public string? Tabla { get; } = tabla;

    public string? Restriccion { get; } = restriccion;
}

/// <summary>
/// Acceso común a las tablas del esquema <c>reclamaciones</c>: cada operación corre <b>en la unidad de trabajo en curso</b> (o en una transacción corta propia, si no hay) bajo el rol <c>rol_reclamaciones</c> (<c>EjecutarComo</c>), de modo que el registro, la auditoría y la cancelación de la
/// cuenta son todo o nada. Un <c>lock_timeout</c> vencido (<c>55P03</c>) sale como <see cref="HojasNoDisponiblesException"/> («servicio no disponible», sin dejar nada a medias); todo otro error de PostgreSQL sale como <see cref="FallaDeLaBaseDeReclamacionesException"/>, sin los datos de la fila.
/// </summary>
internal sealed class AccesoAlEsquemaDeReclamaciones(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    public const string BloqueoNoDisponible = "55P03";

    /// <summary>Lo que espera una lectura <c>FOR UPDATE</c> o el avance de la secuencia a que otra transacción suelte la fila.</summary>
    public const string EsperaDelBloqueo = "5s";

    private static readonly EsquemaDeModulo Reclamaciones = EsquemaDeModulo.Crear("reclamaciones");

    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Reclamaciones,
                    async interno =>
                    {
                        try
                        {
                            resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false);
                        }
                        catch (PostgresException excepcion) when (excepcion.SqlState == BloqueoNoDisponible)
                        {
                            throw new HojasNoDisponiblesException("Otra operación mantiene bloqueada la fila de la hoja o de la secuencia.");
                        }
                        catch (PostgresException excepcion)
                        {
                            // Se descarta la excepción original: su Detail puede traer la fila entera.
                            throw new FallaDeLaBaseDeReclamacionesException(excepcion.SqlState, excepcion.TableName, excepcion.ConstraintName);
                        }
                    },
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }

    /// <summary>Fija la espera del bloqueo de la transacción en curso: se ejecuta antes de una lectura <c>FOR UPDATE</c> o de avanzar la secuencia.</summary>
    public static async Task FijarEsperaDelBloqueo(NpgsqlConnection conexion, NpgsqlTransaction transaccion, CancellationToken token)
    {
        await using var comando = new NpgsqlCommand($"SET LOCAL lock_timeout = '{EsperaDelBloqueo}'", conexion, transaccion);
        await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
    }

    public static object UtcONulo(DateTimeOffset? valor) => valor is { } v ? v.ToUniversalTime() : DBNull.Value;

    public static string? LeerTexto(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetString(posicion);

    public static DateTimeOffset? LeerInstante(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : new DateTimeOffset(filas.GetDateTime(posicion), TimeSpan.Zero);
}
