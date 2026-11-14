using BabelHub.Ecommerce.Application.Plataforma;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// Acceso común a las tablas del esquema <c>web</c>: cada operación corre en la unidad de trabajo en curso (o en una propia, si no hay) bajo el rol <c>rol_web</c>
/// (<c>EjecutarComo</c>, AD18). Usa solo los contratos de <c>Plataforma</c> (<see cref="IUnidadDeTrabajo"/> y <see cref="IConexionDeLaUnidadDeTrabajo"/>): la
/// Infraestructura de un módulo no depende de la de otro (AGENTS.md §3, regla 13). La conexión pertenece al flujo asíncrono que abrió la transacción.
/// </summary>
internal static class AccesoAlEsquemaWeb
{
    private static readonly EsquemaDeModulo Web = EsquemaDeModulo.Crear("web");

    public static Task<T> Ejecutar<T>(
        IUnidadDeTrabajo unidad,
        IConexionDeLaUnidadDeTrabajo conexion,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(unidad);
        ArgumentNullException.ThrowIfNull(conexion);
        ArgumentNullException.ThrowIfNull(bloque);
        return EjecutarInterno(unidad, conexion, bloque, cancelacion);
    }

    /// <summary>El valor del parámetro, o <see cref="DBNull"/> si no lo hay (también para un <c>Nullable</c> sin valor, que al encajarse queda en <see langword="null"/>).</summary>
    public static object ONulo(object? valor) => valor ?? DBNull.Value;

    public static T? LeerNulable<T>(NpgsqlDataReader filas, int posicion)
        where T : struct => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<T>(posicion);

    public static byte[]? LeerBytes(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<byte[]>(posicion);

    private static async Task<T> EjecutarInterno<T>(
        IUnidadDeTrabajo unidad,
        IConexionDeLaUnidadDeTrabajo conexion,
        Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque,
        CancellationToken cancelacion)
    {
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Web,
                    async interno => resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false),
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }
}
