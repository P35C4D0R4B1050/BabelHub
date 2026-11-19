using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Devoluciones;

/// <summary>
/// Una escritura o lectura del esquema <c>devoluciones</c> que PostgreSQL rechazó por una razón que no es la disponibilidad. Lleva solo el código de estado, la tabla y la restricción: <b>nunca</b> el <c>Detail</c> ni el <c>Statement</c> de la
/// excepción original (un <c>Detail</c> incluye la fila completa y por tanto el motivo y el texto libres, DEV-31).
/// </summary>
public sealed class FallaDeLaBaseDeDevolucionesException(string? codigoDeEstado, string? tabla, string? restriccion)
    : Exception($"La base de datos rechazó la operación de devoluciones (estado {codigoDeEstado ?? "desconocido"}, tabla {tabla ?? "-"}, restricción {restriccion ?? "-"}).")
{
    public string? CodigoDeEstado { get; } = codigoDeEstado;

    public string? Tabla { get; } = tabla;

    public string? Restriccion { get; } = restriccion;
}

/// <summary>
/// Acceso común a las tablas del esquema <c>devoluciones</c>: cada operación corre en la unidad de trabajo en curso bajo el rol <c>rol_devoluciones</c> (<c>EjecutarComo</c>, AD18). Un <c>lock_timeout</c>, un conflicto de serialización, un interbloqueo,
/// una cancelación de la consulta o una conexión caída salen como <see cref="DevolucionesNoDisponiblesException"/> (el caso de uso responde «servicio no disponible»); cualquier otro rechazo, como <see cref="FallaDeLaBaseDeDevolucionesException"/> sin el detalle de la fila.
/// </summary>
public sealed class AccesoAlEsquemaDeDevoluciones(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    private static readonly EsquemaDeModulo Devoluciones = EsquemaDeModulo.Crear("devoluciones");

    /// <summary>Los estados de PostgreSQL que significan «no pude atender a tiempo»: bloqueo no disponible, serialización, interbloqueo, consulta cancelada y la clase 08 (conexión).</summary>
    private static bool EsDisponibilidad(string? estado) => estado is "55P03" or "40001" or "40P01" or "57014" || estado?.StartsWith("08", StringComparison.Ordinal) == true;

    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        T resultado = default!;
        try
        {
            await unidad.Ejecutar(
                async token =>
                {
                    await unidad.EjecutarComo(
                        Devoluciones,
                        async interno =>
                        {
                            resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false);
                        },
                        token).ConfigureAwait(false);
                    return 0;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (PostgresException excepcion)
        {
            // Se descarta la excepción original: su Detail puede traer la fila entera, con el motivo y el texto.
            if (EsDisponibilidad(excepcion.SqlState))
            {
                throw new DevolucionesNoDisponiblesException("base-no-disponible");
            }

            throw new FallaDeLaBaseDeDevolucionesException(excepcion.SqlState, excepcion.TableName, excepcion.ConstraintName);
        }
        catch (NpgsqlException)
        {
            throw new DevolucionesNoDisponiblesException("base-no-disponible");
        }

        return resultado;
    }

    public static object UtcONulo(DateTimeOffset? valor) => valor is { } v ? v.ToUniversalTime() : DBNull.Value;

    public static object ONulo(object? valor) => valor ?? DBNull.Value;

    public static DateTimeOffset? LeerInstante(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<DateTimeOffset>(posicion);
}
