using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;
using Npgsql;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// <see cref="IAlmacenDeBorradores"/> sobre <c>web.borradores</c> (T10). Un borrador por usuario y plantilla (<c>INSERT … ON CONFLICT DO UPDATE</c>) y la lectura con
/// <c>FOR UPDATE</c>: dos entregas simultáneas del mismo borrador se serializan y la segunda ya no lo encuentra, de modo que la entrega es única de verdad (WEB-25).
/// El cuerpo llega y se guarda cifrado; la base nunca lo ve en claro.
/// </summary>
public sealed class AlmacenDeBorradoresPostgres(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexionDeLaUnidad) : IAlmacenDeBorradores
{
    public Task<int> Contar(Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM web.borradores WHERE usuario_id = @usuario AND vence_en > @ahora", conexion, transaccion);
                comando.Parameters.AddWithValue("usuario", usuarioId);
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                return (int)(long)(await comando.ExecuteScalarAsync(token).ConfigureAwait(false))!;
            },
            cancelacion);

    public Task<BorradorGuardado?> Cargar(Guid usuarioId, string plantillaDeRuta, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "SELECT id, cuerpo_cifrado, version_de_clave, creado_en FROM web.borradores WHERE usuario_id = @usuario AND plantilla_de_ruta = @plantilla FOR UPDATE",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("usuario", usuarioId);
                comando.Parameters.AddWithValue("plantilla", plantillaDeRuta);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    return null;
                }

                // Los campos están cifrados en el cuerpo: aquí solo se reconstruye el sobre (quién, qué plantilla y cuándo).
                var borrador = BorradorDePantalla.Reconstruir(filas.GetGuid(0), usuarioId, plantillaDeRuta, new Dictionary<string, string>(), filas.GetFieldValue<DateTimeOffset>(3), entregado: false);
                return new BorradorGuardado(borrador, filas.GetFieldValue<byte[]>(1), filas.GetInt16(2));
            },
            cancelacion);

    public Task Guardar(BorradorGuardado borrador, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(borrador);
        return AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    INSERT INTO web.borradores (id, usuario_id, plantilla_de_ruta, cuerpo_cifrado, version_de_clave, creado_en, vence_en)
                    VALUES (@id, @usuario, @plantilla, @cuerpo, @version, @creado, @vence)
                    ON CONFLICT (usuario_id, plantilla_de_ruta) DO UPDATE
                        SET id = EXCLUDED.id, cuerpo_cifrado = EXCLUDED.cuerpo_cifrado, version_de_clave = EXCLUDED.version_de_clave,
                            creado_en = EXCLUDED.creado_en, vence_en = EXCLUDED.vence_en
                    """,
                    conexion,
                    transaccion);
                var dato = borrador.Borrador;
                comando.Parameters.AddWithValue("id", dato.Id);
                comando.Parameters.AddWithValue("usuario", dato.UsuarioId);
                comando.Parameters.AddWithValue("plantilla", dato.Plantilla);
                comando.Parameters.AddWithValue("cuerpo", borrador.CuerpoCifrado);
                comando.Parameters.AddWithValue("version", borrador.VersionDeClave);
                comando.Parameters.AddWithValue("creado", dato.CreadoEn.ToUniversalTime());
                comando.Parameters.AddWithValue("vence", dato.VenceEn.ToUniversalTime());
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
    }

    public Task<int> BorrarVencidos(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "DELETE FROM web.borradores WHERE id IN (SELECT id FROM web.borradores WHERE vence_en <= @ahora ORDER BY vence_en LIMIT @maximo FOR UPDATE SKIP LOCKED)",
                    conexion,
                    transaccion);
                comando.Parameters.AddWithValue("ahora", ahora.ToUniversalTime());
                comando.Parameters.AddWithValue("maximo", maximo);
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    public Task Borrar(Guid id, CancellationToken cancelacion = default) =>
        AccesoAlEsquemaWeb.Ejecutar(
            unidad,
            conexionDeLaUnidad,
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM web.borradores WHERE id = @id", conexion, transaccion);
                comando.Parameters.AddWithValue("id", id);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
}
