using BabelHub.Ecommerce.Application.Plataforma;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// Una escritura o lectura del esquema <c>carrito</c> que PostgreSQL rechazó. Lleva solo el código de estado, la tabla y la restricción: <b>nunca</b> el <c>Detail</c> ni el <c>Statement</c> de la excepción original, porque el
/// <c>Detail</c> de un <c>CHECK</c> o de un índice único incluye la fila completa («Failing row contains …») y con ella el nombre, el teléfono y la dirección de entrega (CAR-34, AGENTS §5). No guarda la excepción original como interna.
/// </summary>
public sealed class FallaDeLaBaseDelCarritoException(string? codigoDeEstado, string? tabla, string? restriccion)
    : Exception($"La base de datos rechazó la operación del carrito (estado {codigoDeEstado ?? "desconocido"}, tabla {tabla ?? "-"}, restricción {restriccion ?? "-"}).")
{
    public string? CodigoDeEstado { get; } = codigoDeEstado;

    public string? Tabla { get; } = tabla;

    public string? Restriccion { get; } = restriccion;
}

/// <summary>
/// Acceso común a las tablas del esquema <c>carrito</c>: cada operación corre en la unidad de trabajo en curso (o en una transacción corta propia, si no hay) bajo el rol <c>rol_carrito</c> (<c>EjecutarComo</c>, AD18). Usa solo los
/// contratos de <c>Plataforma</c> (regla 13). La conexión pertenece al flujo asíncrono que abrió la transacción: no se usa desde tareas paralelas. Todo error de PostgreSQL sale como <see cref="FallaDeLaBaseDelCarritoException"/>, sin los datos de la fila.
/// </summary>
public sealed class AccesoAlEsquemaDelCarrito(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    public const string ViolacionDeUnicidad = "23505";

    private static readonly EsquemaDeModulo Carrito = EsquemaDeModulo.Crear("carrito");

    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Carrito,
                    async interno =>
                    {
                        try
                        {
                            resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false);
                        }
                        catch (PostgresException excepcion)
                        {
                            // Se descarta la excepción original: su Detail puede traer la fila entera.
                            throw new FallaDeLaBaseDelCarritoException(excepcion.SqlState, excepcion.TableName, excepcion.ConstraintName);
                        }
                    },
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }

    /// <summary>Un código de estado de unicidad (23505) con una de estas restricciones: lo que las traducciones de «ya existe» buscan.</summary>
    public static bool EsUnicidad(FallaDeLaBaseDelCarritoException falla, params string[] restricciones) =>
        falla.CodigoDeEstado == ViolacionDeUnicidad && falla.Restriccion is { } restriccion && restricciones.Contains(restriccion, StringComparer.Ordinal);

    public static object ONulo(object? valor) => valor ?? DBNull.Value;

    /// <summary>Npgsql solo escribe instantes con desplazamiento 0: se normaliza a UTC.</summary>
    public static object UtcONulo(DateTimeOffset? valor) => valor is { } v ? v.ToUniversalTime() : DBNull.Value;

    public static T? LeerNulable<T>(NpgsqlDataReader filas, int posicion)
        where T : struct => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<T>(posicion);

    public static string? LeerTexto(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetString(posicion);

    public static DateTimeOffset? LeerInstante(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : new DateTimeOffset(filas.GetDateTime(posicion), TimeSpan.Zero);
}
