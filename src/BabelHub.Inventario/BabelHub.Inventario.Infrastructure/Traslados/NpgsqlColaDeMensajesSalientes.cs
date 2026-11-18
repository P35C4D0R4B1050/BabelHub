using BabelHub.Inventario.Application.Traslados.Interno;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Inventario.Infrastructure.Traslados;

/// <summary>
/// <see cref="IColaDeMensajesSalientes"/> sobre <c>traslados.mensajes_salientes</c>. <see cref="Reclamar"/> toma los pendientes con <c>FOR UPDATE SKIP LOCKED</c> y los arrienda en una sola sentencia; el estado de un pedido espera a
/// que no haya otro anterior del mismo traslado todavía pendiente (orden por traslado). El resultado de cada envío se registra por columnas, solo las que el rol puede actualizar, y siempre sobre un mensaje todavía pendiente.
/// </summary>
internal sealed class NpgsqlColaDeMensajesSalientes(AccesoATraslados acceso) : IColaDeMensajesSalientes
{
    public Task<IReadOnlyList<MensajeParaEntregar>> Reclamar(int maximo, DateTimeOffset ahora, TimeSpan arrendamiento, CancellationToken cancelacion = default) =>
        acceso.Ejecutar<IReadOnlyList<MensajeParaEntregar>>(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(
                    """
                    WITH candidatos AS (
                        SELECT m.id FROM traslados.mensajes_salientes m
                        WHERE m.estado = 'pendiente' AND m.proxima_fecha <= @ahora AND (m.arrendado_hasta IS NULL OR m.arrendado_hasta <= @ahora)
                          AND (m.tipo <> 'estado-de-pedido' OR NOT EXISTS (
                                SELECT 1 FROM traslados.mensajes_salientes p
                                WHERE p.tipo = 'estado-de-pedido' AND p.estado = 'pendiente'
                                  AND split_part(p.clave_de_evento, ':', 2) = split_part(m.clave_de_evento, ':', 2)
                                  AND p.secuencia < m.secuencia))
                        ORDER BY m.secuencia
                        LIMIT @maximo
                        FOR UPDATE OF m SKIP LOCKED)
                    UPDATE traslados.mensajes_salientes t SET arrendado_hasta = @hasta
                    FROM candidatos c WHERE t.id = c.id
                    RETURNING t.id, t.tipo, t.clave_de_evento, t.sucursal_codigo, t.carga::text, t.intentos, t.creado_en, t.alertado_en IS NOT NULL, t.secuencia
                    """,
                    conexion,
                    transaccion);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                comando.Parameters.Add(new NpgsqlParameter("hasta", NpgsqlDbType.TimestampTz) { Value = (ahora + arrendamiento).ToUniversalTime() });
                comando.Parameters.AddWithValue("maximo", maximo);
                var lote = new List<(long Secuencia, MensajeParaEntregar Mensaje)>();
                await using var lector = await comando.ExecuteReaderAsync(token).ConfigureAwait(false);
                while (await lector.ReadAsync(token).ConfigureAwait(false))
                {
                    lote.Add((lector.GetInt64(8), new MensajeParaEntregar(lector.GetGuid(0), lector.GetString(1), lector.GetString(2), lector.GetString(3), lector.GetString(4), lector.GetInt32(5), lector.GetFieldValue<DateTimeOffset>(6), lector.GetBoolean(7))));
                }

                return lote.OrderBy(m => m.Secuencia).Select(m => m.Mensaje).ToList();
            },
            cancelacion);

    public Task MarcarEnviado(Guid mensajeId, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        Actualizar(
            "UPDATE traslados.mensajes_salientes SET estado = 'enviado', enviado_en = @ahora, arrendado_hasta = NULL WHERE id = @id AND estado = 'pendiente'",
            mensajeId,
            comando => comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() }),
            cancelacion);

    public Task Reprogramar(Guid mensajeId, int intentos, DateTimeOffset proximaFecha, bool alertado, CancellationToken cancelacion = default) =>
        Actualizar(
            "UPDATE traslados.mensajes_salientes SET intentos = @intentos, proxima_fecha = @proxima, arrendado_hasta = NULL, alertado_en = CASE WHEN @alertado THEN coalesce(alertado_en, now()) ELSE alertado_en END WHERE id = @id AND estado = 'pendiente'",
            mensajeId,
            comando =>
            {
                comando.Parameters.AddWithValue("intentos", intentos);
                comando.Parameters.Add(new NpgsqlParameter("proxima", NpgsqlDbType.TimestampTz) { Value = proximaFecha.ToUniversalTime() });
                comando.Parameters.AddWithValue("alertado", alertado);
            },
            cancelacion);

    public Task MarcarFallido(Guid mensajeId, MotivoDeFalloDeMensaje motivo, int intentos, DateTimeOffset ahora, CancellationToken cancelacion = default) =>
        Actualizar(
            "UPDATE traslados.mensajes_salientes SET estado = 'fallido', intentos = @intentos, motivo_de_fallo = @motivo, fallido_en = @ahora, arrendado_hasta = NULL WHERE id = @id AND estado = 'pendiente'",
            mensajeId,
            comando =>
            {
                comando.Parameters.AddWithValue("intentos", intentos);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                comando.Parameters.AddWithValue("motivo", motivo switch { MotivoDeFalloDeMensaje.Credencial => "credencial", MotivoDeFalloDeMensaje.Vencido => "vencido", _ => "permanente" });
            },
            cancelacion);

    public Task<int> PurgarTerminados(DateTimeOffset antesDe, CancellationToken cancelacion = default) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM traslados.mensajes_salientes WHERE estado <> 'pendiente' AND coalesce(enviado_en, fallido_en, creado_en) < @antes", conexion, transaccion);
                comando.Parameters.Add(new NpgsqlParameter("antes", NpgsqlDbType.TimestampTz) { Value = antesDe.ToUniversalTime() });
                return await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
            },
            cancelacion);

    private Task Actualizar(string sql, Guid id, Action<NpgsqlCommand> parametros, CancellationToken cancelacion) =>
        acceso.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                comando.Parameters.AddWithValue("id", id);
                parametros(comando);
                await comando.ExecuteNonQueryAsync(token).ConfigureAwait(false);
                return 0;
            },
            cancelacion);
}
