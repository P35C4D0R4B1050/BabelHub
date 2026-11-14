using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web.Interno;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// <see cref="IVaciadoDeLaWeb"/> sobre PostgreSQL (T13, D14, PLA-39, PLA-43): vacía <c>web.sesiones_web</c> y <c>web.borradores</c> en una sola transacción con el rol del módulo. Los
/// respaldos y el archivado de WAL contienen las sesiones cifradas y ninguna sesión restaurada debe seguir válida; el anillo de Data Protection (<c>web.claves_de_proteccion</c>) <b>no</b>
/// se toca. Usa <c>DELETE</c> y no <c>TRUNCATE</c>: el rol del módulo nunca tiene permisos más amplios que el necesario.
/// </summary>
public sealed class VaciadoDeLaWebPostgres(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexionDeLaUnidad) : IVaciadoDeLaWeb
{
    public Task VaciarSesionesYBorradores(CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM web.sesiones_web; DELETE FROM web.borradores;", conexion, transaccion);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
}
