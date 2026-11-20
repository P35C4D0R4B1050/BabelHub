using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Biblioteca;

/// <summary>
/// Una escritura o lectura del esquema <c>biblioteca</c> que PostgreSQL rechazó por una razón que no es la disponibilidad. Lleva solo el código de estado, la tabla y la restricción: <b>nunca</b> el <c>Detail</c> ni el <c>Statement</c> de la excepción original (un <c>Detail</c> incluye la fila completa).
/// </summary>
public sealed class FallaDeLaBaseDeBibliotecaException(string? codigoDeEstado, string? tabla, string? restriccion)
    : Exception($"La base de datos rechazó la operación de la biblioteca (estado {codigoDeEstado ?? "desconocido"}, tabla {tabla ?? "-"}, restricción {restriccion ?? "-"}).")
{
    public string? CodigoDeEstado { get; } = codigoDeEstado;

    public string? Tabla { get; } = tabla;

    public string? Restriccion { get; } = restriccion;
}

/// <summary>
/// Acceso común a las tablas del esquema <c>biblioteca</c>: cada operación corre en la unidad de trabajo en curso bajo el rol <c>rol_biblioteca</c> (<c>EjecutarComo</c>, AD18). Un <c>lock_timeout</c>, un conflicto de serialización, un interbloqueo, una cancelación de la consulta o una conexión
/// caída salen como <see cref="BibliotecaNoDisponibleException"/> (el caso de uso responde «servicio no disponible» y la transacción se revierte); cualquier otro rechazo, como <see cref="FallaDeLaBaseDeBibliotecaException"/> sin el detalle de la fila.
/// </summary>
public sealed class AccesoAlEsquemaDeBiblioteca(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    public const string ViolacionDeUnicidad = "23505";

    public const string ViolacionDeClaveForanea = "23503";

    private static readonly EsquemaDeModulo Biblioteca = EsquemaDeModulo.Crear("biblioteca");

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
                        Biblioteca,
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
            // Se descarta la excepción original: su Detail puede traer la fila entera.
            if (EsDisponibilidad(excepcion.SqlState))
            {
                throw new BibliotecaNoDisponibleException("base-no-disponible");
            }

            throw new FallaDeLaBaseDeBibliotecaException(excepcion.SqlState, excepcion.TableName, excepcion.ConstraintName);
        }
        catch (NpgsqlException)
        {
            throw new BibliotecaNoDisponibleException("base-no-disponible");
        }

        return resultado;
    }

    public static object UtcONulo(DateTimeOffset? valor) => valor is { } v ? v.ToUniversalTime() : DBNull.Value;

    public static object ONulo(object? valor) => valor ?? DBNull.Value;

    public static DateTimeOffset? LeerInstante(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<DateTimeOffset>(posicion);
}
