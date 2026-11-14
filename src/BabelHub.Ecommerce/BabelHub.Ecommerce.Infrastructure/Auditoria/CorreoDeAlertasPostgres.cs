using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary><see cref="IAlmacenDeCorreoDeAlertas"/> sobre <c>auditoria.alertas</c> (T18, T25; AUD-40): las alertas enviables con el correo pendiente y el reintento con esperas crecientes.</summary>
public sealed class PostgresAlmacenDeCorreoDeAlertas(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IAlmacenDeCorreoDeAlertas
{
    public Task<IReadOnlyList<AlertaConCorreoPendiente>> ConCorreoPendiente(DateTimeOffset ahora, int maximo, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad<IReadOnlyList<AlertaConCorreoPendiente>>(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    $"""
                    SELECT {PostgresAlmacenDeAlertas.Columnas}, a.intentos_de_correo FROM auditoria.alertas a
                    WHERE NOT a.enviada_por_correo AND (a.proximo_intento_de_correo IS NULL OR a.proximo_intento_de_correo <= @ahora)
                    ORDER BY a.primera_ocurrencia, a.id LIMIT @maximo
                    """,
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = ahora.ToUniversalTime() });
                comando.Parameters.AddWithValue("maximo", maximo);
                var lista = new List<AlertaConCorreoPendiente>();
                await using var lector = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
                while (await lector.ReadAsync(ct).ConfigureAwait(false))
                {
                    lista.Add(new AlertaConCorreoPendiente(PostgresAlmacenDeAlertas.Leer(lector), lector.GetInt32(11)));
                }

                return lista;
            },
            cancelacion);

    public Task RegistrarIntentoFallido(string alertaId, int intentos, DateTimeOffset proximoIntento, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    "UPDATE auditoria.alertas SET intentos_de_correo = @intentos, proximo_intento_de_correo = @proximo WHERE id = @id AND NOT enviada_por_correo",
                    c,
                    t);
                comando.Parameters.AddWithValue("id", alertaId);
                comando.Parameters.AddWithValue("intentos", intentos);
                comando.Parameters.Add(new NpgsqlParameter("proximo", NpgsqlDbType.TimestampTz) { Value = proximoIntento.ToUniversalTime() });
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
}

/// <summary><see cref="IAlertasYaEnviadas"/> sobre <c>auditoria.alertas_ya_enviadas</c> (AUD-38, AUD-39): una alerta del Inventario se notifica una sola vez aunque se lea varias veces.</summary>
public sealed class PostgresAlertasYaEnviadas(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion, TimeProvider? reloj = null) : IAlertasYaEnviadas
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public Task<bool> Contiene(string alertaId, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando("SELECT EXISTS (SELECT 1 FROM auditoria.alertas_ya_enviadas WHERE alerta_id = @id)", c, t);
                comando.Parameters.AddWithValue("id", alertaId);
                return (bool)(await comando.ExecuteScalarAsync(ct).ConfigureAwait(false))!;
            },
            cancelacion);

    public Task Marcar(string alertaId, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando("INSERT INTO auditoria.alertas_ya_enviadas (alerta_id, enviada_en) VALUES (@id, @ahora) ON CONFLICT (alerta_id) DO NOTHING", c, t);
                comando.Parameters.AddWithValue("id", alertaId);
                comando.Parameters.Add(new NpgsqlParameter("ahora", NpgsqlDbType.TimestampTz) { Value = _reloj.GetUtcNow() });
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
}

/// <summary><see cref="IMarcaDeLecturaDeAlertas"/> sobre la fila única de <c>auditoria.marca_de_lectura_de_alertas</c>: hasta dónde se leyeron las alertas del Inventario sin pendientes (AUD-38); persistente.</summary>
public sealed class PostgresMarcaDeLecturaDeAlertas(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IMarcaDeLecturaDeAlertas
{
    public Task<DateTimeOffset?> Obtener(CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad<DateTimeOffset?>(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando("SELECT marca FROM auditoria.marca_de_lectura_de_alertas WHERE id = 1", c, t);
                var valor = await comando.ExecuteScalarAsync(ct).ConfigureAwait(false);
                return valor is DateTimeOffset marca ? marca.ToUniversalTime() : valor is DateTime instante ? new DateTimeOffset(DateTime.SpecifyKind(instante, DateTimeKind.Utc)) : null;
            },
            cancelacion);

    public Task Guardar(DateTimeOffset marca, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    "INSERT INTO auditoria.marca_de_lectura_de_alertas (id, marca) VALUES (1, @marca) ON CONFLICT (id) DO UPDATE SET marca = GREATEST(auditoria.marca_de_lectura_de_alertas.marca, EXCLUDED.marca)",
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("marca", NpgsqlDbType.TimestampTz) { Value = marca.ToUniversalTime() });
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
}
