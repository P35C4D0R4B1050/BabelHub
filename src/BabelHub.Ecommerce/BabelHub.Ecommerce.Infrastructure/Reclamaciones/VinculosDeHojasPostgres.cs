using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Reclamaciones;

/// <summary>
/// Las operaciones de las hojas que cambian o leen el vínculo con la cuenta (T31, D14, D15, REC-22, REC-28): cada una es <b>una sola sentencia</b> condicional y atómica, bajo <c>EjecutarComo(reclamaciones)</c> y dentro de la transacción en curso (la misma de la auditoría y, en la cancelación,
/// de Identidad). La condición <c>cuenta_id IS NULL</c> del reclamo es la que hace ganar a una sola de dos operaciones simultáneas; el guardián de la base impide además pasar de una cuenta a otra. <b>El correo viaja solo como parámetro</b>: ni en una excepción ni en un mensaje.
/// </summary>
internal sealed class VinculosDeHojasPostgres(AccesoAlEsquemaDeReclamaciones acceso) : IVinculosDeHojas
{
    /// <summary>La clave del bloqueo asesor del cursor de la vinculación inicial: dos instancias no lo mueven a la vez.</summary>
    private const long ClaveDelBloqueoDeLaVinculacionInicial = 0x42_41_42_45_4C_52_45_43; // «BABELREC»

    public Task<int> ReclamarDelCorreo(Guid cuentaId, string correoNormalizado, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correoNormalizado);
        return acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Una sola sentencia que toma las filas en orden (DD-26 impl. 13): dos operaciones multifila con planes distintos no forman interbloqueos. Bajo READ COMMITTED, FOR UPDATE reevalúa la condición tras esperar: la fila ya vinculada por otra se omite.
                await AccesoAlEsquemaDeReclamaciones.FijarEsperaDelBloqueo(conexion, transaccion, token).ConfigureAwait(false);
                await using var comando = new NpgsqlCommand(
                    "WITH tomadas AS (SELECT id FROM reclamaciones.hojas_reclamacion WHERE cuenta_id IS NULL AND correo_normalizado = @correo AND NOT de_cuenta_cancelada AND anonimizada_en IS NULL ORDER BY id FOR UPDATE) "
                    + "UPDATE reclamaciones.hojas_reclamacion h SET cuenta_id = @cuenta FROM tomadas t WHERE h.id = t.id",
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("cuenta", NpgsqlDbType.Uuid) { Value = cuentaId });
                comando.Parameters.Add(new NpgsqlParameter("correo", NpgsqlDbType.Text) { Value = correoNormalizado });
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);
    }

    public Task<ListadoDeHojas> ListarDeLaCuenta(Guid cuentaId, int pagina, int tamano, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
                await RepositorioDeHojasPostgres.Paginar(
                    conexion,
                    transaccion,
                    "cuenta_id = @cuenta AND NOT de_cuenta_cancelada AND anonimizada_en IS NULL",
                    [new NpgsqlParameter("cuenta", NpgsqlDbType.Uuid) { Value = cuentaId }],
                    pagina,
                    tamano,
                    token).ConfigureAwait(false),
            cancelacion);

    public Task<int> ContarPendientesDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    "SELECT count(*) FROM reclamaciones.hojas_reclamacion WHERE cuenta_id = @cuenta AND estado = 'pendiente' AND NOT de_cuenta_cancelada",
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("cuenta", NpgsqlDbType.Uuid) { Value = cuentaId });
                return Convert.ToInt32(await comando.ExecuteScalarAsync(token).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            },
            cancelacion);

    public Task<int> MarcarYDesvincularDeLaCuenta(Guid cuentaId, string? correoVerificadoNormalizado, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                // Una sola sentencia: las vinculadas a la cuenta y, solo con correo verificado, las aún sin cuenta de ese correo. El CHECK exige cuenta_id nulo con la marca puesta.
                await AccesoAlEsquemaDeReclamaciones.FijarEsperaDelBloqueo(conexion, transaccion, token).ConfigureAwait(false);
                await using var comando = new NpgsqlCommand(
                    "WITH tomadas AS (SELECT id FROM reclamaciones.hojas_reclamacion WHERE NOT de_cuenta_cancelada AND (cuenta_id = @cuenta OR (cuenta_id IS NULL AND @correo::text IS NOT NULL AND correo_normalizado = @correo)) ORDER BY id FOR UPDATE) "
                    + "UPDATE reclamaciones.hojas_reclamacion h SET de_cuenta_cancelada = true, cuenta_id = NULL FROM tomadas t WHERE h.id = t.id",
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("cuenta", NpgsqlDbType.Uuid) { Value = cuentaId });
                comando.Parameters.Add(new NpgsqlParameter("correo", NpgsqlDbType.Text) { Value = (object?)correoVerificadoNormalizado ?? DBNull.Value });
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<int> DesvincularDeLaCuenta(Guid cuentaId, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await AccesoAlEsquemaDeReclamaciones.FijarEsperaDelBloqueo(conexion, transaccion, token).ConfigureAwait(false);
                await using var comando = new NpgsqlCommand(
                    "WITH tomadas AS (SELECT id FROM reclamaciones.hojas_reclamacion WHERE cuenta_id = @cuenta ORDER BY id FOR UPDATE) UPDATE reclamaciones.hojas_reclamacion h SET cuenta_id = NULL FROM tomadas t WHERE h.id = t.id",
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("cuenta", NpgsqlDbType.Uuid) { Value = cuentaId });
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    public Task<EstadoDeLaVinculacionInicial> EstadoDeLaVinculacionInicial(CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("SELECT ultimo_correo, iniciada_en, completada_en FROM reclamaciones.vinculacion_inicial WHERE id = 1", conexion, transaccion);
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                if (!await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    return new EstadoDeLaVinculacionInicial(null, null, null);
                }

                return new EstadoDeLaVinculacionInicial(
                    AccesoAlEsquemaDeReclamaciones.LeerTexto(filas, 0),
                    AccesoAlEsquemaDeReclamaciones.LeerInstante(filas, 1),
                    AccesoAlEsquemaDeReclamaciones.LeerInstante(filas, 2));
            },
            cancelacion);

    public Task<IReadOnlyList<string>> CorreosSinVincularDespuesDe(string? cursor, int maximo, CancellationToken cancelacion = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximo, 1);
        return acceso.Ejecutar<IReadOnlyList<string>>(
            async (conexion, transaccion, token) =>
            {
                var correos = new List<string>();
                await using var comando = new NpgsqlCommand(
                    "SELECT DISTINCT correo_normalizado FROM reclamaciones.hojas_reclamacion "
                    + "WHERE cuenta_id IS NULL AND NOT de_cuenta_cancelada AND anonimizada_en IS NULL AND correo_normalizado IS NOT NULL AND (@cursor::text IS NULL OR correo_normalizado > @cursor) "
                    + "ORDER BY correo_normalizado LIMIT @maximo",
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("cursor", NpgsqlDbType.Text) { Value = (object?)cursor ?? DBNull.Value });
                comando.Parameters.Add(new NpgsqlParameter("maximo", NpgsqlDbType.Integer) { Value = maximo });
                await using var filas = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await filas.ReadAsync(token).ConfigureAwait(false))
                {
                    correos.Add(filas.GetString(0));
                }

                return correos;
            },
            cancelacion);
    }

    public Task GuardarElAvanceDeLaVinculacionInicial(string? ultimoCorreo, bool completada, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await AccesoAlEsquemaDeReclamaciones.FijarEsperaDelBloqueo(conexion, transaccion, token).ConfigureAwait(false);
                await using (var bloqueo = new NpgsqlCommand("SELECT pg_catalog.pg_advisory_xact_lock(@clave)", conexion, transaccion))
                {
                    bloqueo.Parameters.Add(new NpgsqlParameter("clave", NpgsqlDbType.Bigint) { Value = ClaveDelBloqueoDeLaVinculacionInicial });
                    await bloqueo.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                }

                // La primera vez fija el inicio. El cursor solo avanza (nunca retrocede por una instancia atrasada) y un recorrido completado no se toca.
                await using var comando = new NpgsqlCommand(
                    "INSERT INTO reclamaciones.vinculacion_inicial AS v (id, ultimo_correo, iniciada_en, completada_en) VALUES (1, @cursor, @ahora, @completada) "
                    + "ON CONFLICT (id) DO UPDATE SET ultimo_correo = EXCLUDED.ultimo_correo, completada_en = EXCLUDED.completada_en "
                    + "WHERE v.completada_en IS NULL AND (EXCLUDED.completada_en IS NOT NULL OR v.ultimo_correo IS NULL OR EXCLUDED.ultimo_correo > v.ultimo_correo)",
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("cursor", NpgsqlDbType.Text) { Value = completada ? DBNull.Value : (object?)ultimoCorreo ?? DBNull.Value });
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                comando.Parameters.Add(new NpgsqlParameter("completada", NpgsqlDbType.TimestampTz) { Value = completada ? ahora.ToUniversalTime() : DBNull.Value });
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
}
