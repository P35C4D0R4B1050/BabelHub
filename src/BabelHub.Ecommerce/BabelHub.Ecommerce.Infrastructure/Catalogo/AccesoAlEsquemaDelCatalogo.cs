using BabelHub.Ecommerce.Application.Plataforma;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// Acceso común a las tablas del esquema <c>catalogo</c>: cada operación corre en la unidad de trabajo en curso (o en una transacción corta propia, si no hay) bajo el rol <c>rol_catalogo</c> (<c>EjecutarComo</c>, AD18). Usa solo los
/// contratos de <c>Plataforma</c> (regla 13). La conexión pertenece al flujo asíncrono que abrió la transacción: no se usa desde tareas paralelas.
/// </summary>
public sealed class AccesoAlEsquemaDelCatalogo(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion)
{
    private static readonly EsquemaDeModulo Catalogo = EsquemaDeModulo.Crear("catalogo");

    public async Task<T> Ejecutar<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> bloque, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(bloque);
        T resultado = default!;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Catalogo,
                    async interno => resultado = await bloque((NpgsqlConnection)conexion.Conexion, (NpgsqlTransaction)conexion.Transaccion, interno).ConfigureAwait(false),
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
        return resultado;
    }

    public static object ONulo(object? valor) => valor ?? DBNull.Value;

    /// <summary>Npgsql solo escribe instantes con desplazamiento 0: se normaliza a UTC (la hora de Lima se calcula al leer, con <c>IReloj</c>).</summary>
    public static object UtcONulo(DateTimeOffset? valor) => valor is { } v ? v.ToUniversalTime() : DBNull.Value;

    public static T? LeerNulable<T>(NpgsqlDataReader filas, int posicion)
        where T : struct => filas.IsDBNull(posicion) ? null : filas.GetFieldValue<T>(posicion);

    public static string? LeerTexto(NpgsqlDataReader filas, int posicion) => filas.IsDBNull(posicion) ? null : filas.GetString(posicion);
}
