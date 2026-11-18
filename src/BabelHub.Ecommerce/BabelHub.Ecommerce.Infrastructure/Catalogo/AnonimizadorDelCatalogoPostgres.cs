using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// La anonimización de los datos de personal del catálogo en PostgreSQL (T23): una instrucción por tabla bajo <c>rol_catalogo</c>, que solo puede actualizar esas columnas (<see cref="DefinicionDelCatalogo.Permisos"/>). El usuario pasa a <see cref="Guid.Empty"/> y los textos libres a
/// <c>NULL</c>; el estado, los identificadores y las cantidades no cambian. Solo toca lo que aún no está anonimizado (la corrida siguiente no cuenta dos veces).
/// </summary>
public sealed class AnonimizadorDelCatalogoPostgres(AccesoAlEsquemaDelCatalogo acceso) : IAnonimizadorDelCatalogo
{
    public Task<AnonimizacionDelCatalogoRealizada> Anonimizar(DateTimeOffset limite, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                var ingresos = await Actualizar(
                    conexion,
                    transaccion,
                    """
                    UPDATE catalogo.ingresos_solicitados SET usuario_id = @vacio, nota = NULL, referencia = NULL
                    WHERE solicitado_en < @limite AND (usuario_id <> @vacio OR nota IS NOT NULL OR referencia IS NOT NULL)
                    """,
                    limite,
                    token).ConfigureAwait(false);
                var archivos = await Actualizar(
                    conexion,
                    transaccion,
                    "UPDATE catalogo.archivos_digitales SET subido_por = @vacio WHERE subido_en < @limite AND subido_por <> @vacio",
                    limite,
                    token).ConfigureAwait(false);
                var pendientes = await Actualizar(
                    conexion,
                    transaccion,
                    "UPDATE catalogo.archivos_pendientes SET usuario_id = @vacio WHERE creado_en < @limite AND usuario_id <> @vacio",
                    limite,
                    token).ConfigureAwait(false);
                return new AnonimizacionDelCatalogoRealizada(ingresos, archivos, pendientes);
            },
            cancelacion);

    private static async Task<int> Actualizar(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string sql, DateTimeOffset limite, CancellationToken cancelacion)
    {
        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        comando.Parameters.AddWithValue("vacio", Guid.Empty);
        comando.Parameters.AddWithValue("limite", limite.ToUniversalTime());
        return await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }
}
