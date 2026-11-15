using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using Npgsql;
using NpgsqlTypes;
using AlertaAgrupada = BabelHub.Ecommerce.Domain.Auditoria.AlertaDeSeguridad;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>
/// <see cref="IAlmacenDeAlertas"/> sobre <c>auditoria.alertas</c> (el registro técnico, T25; AUD-13, AUD-34, AUD-39): la agrupación toma un <b>bloqueo asesor por servicio y motivo</b> (dos ocurrencias
/// simultáneas no abren dos alertas) y <see cref="Obtener"/> toma el mismo, y <see cref="Guardar"/> es un <c>UPSERT</c> monótono (las ocurrencias, lo revisado y el envío por correo nunca retroceden):
/// marcar como revisada y sumar una ocurrencia a la vez no se pisan.
/// </summary>
public sealed class PostgresAlmacenDeAlertas(IUnidadDeTrabajo unidad, IConexionDeLaUnidadDeTrabajo conexion) : IAlmacenDeAlertas
{
    internal const string Columnas = "a.id, a.servicio, a.motivo, a.clase, a.primera_ocurrencia, a.ultima_ocurrencia, a.ocurrencias, a.revisadas_hasta, a.revisada_por, a.revisada_en, a.enviada_por_correo";

    public Task<AlertaAgrupada?> UltimaDelMotivoParaActualizar(ServicioDeOrigen servicio, string motivo, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad<AlertaAgrupada?>(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await Bloquear(c, t, EscrituraDeAuditoria.Texto(servicio), motivo, ct).ConfigureAwait(false);
                await using var comando = EscrituraDeAuditoria.Comando(
                    $"SELECT {Columnas} FROM auditoria.alertas a WHERE a.servicio = @servicio AND a.motivo = @motivo ORDER BY a.primera_ocurrencia DESC LIMIT 1 FOR UPDATE",
                    c,
                    t);
                comando.Parameters.AddWithValue("servicio", EscrituraDeAuditoria.Texto(servicio));
                comando.Parameters.AddWithValue("motivo", motivo);
                return await LeerUna(comando, ct).ConfigureAwait(false);
            },
            cancelacion);

    public Task<AlertaAgrupada?> Obtener(string id, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad<AlertaAgrupada?>(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                // Se lee sin bloqueo para saber de qué servicio y motivo es, se toma el mismo bloqueo asesor que la agrupación y se vuelve a leer ya bloqueada.
                string servicio;
                string motivo;
                await using (var previa = EscrituraDeAuditoria.Comando("SELECT servicio, motivo FROM auditoria.alertas WHERE id = @id", c, t))
                {
                    previa.Parameters.AddWithValue("id", id);
                    await using var lector = await previa.ExecuteReaderAsync(ct).ConfigureAwait(false);
                    if (!await lector.ReadAsync(ct).ConfigureAwait(false))
                    {
                        return null;
                    }

                    servicio = lector.GetString(0);
                    motivo = lector.GetString(1);
                }

                await Bloquear(c, t, servicio, motivo, ct).ConfigureAwait(false);
                await using var comando = EscrituraDeAuditoria.Comando($"SELECT {Columnas} FROM auditoria.alertas a WHERE a.id = @id FOR UPDATE", c, t);
                comando.Parameters.AddWithValue("id", id);
                return await LeerUna(comando, ct).ConfigureAwait(false);
            },
            cancelacion);

    public Task Guardar(AlertaAgrupada alerta, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(alerta);
        return EscrituraDeAuditoria.EnLaUnidad(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    """
                    INSERT INTO auditoria.alertas (id, servicio, motivo, clase, primera_ocurrencia, ultima_ocurrencia, ocurrencias, revisadas_hasta, revisada_por, revisada_en, enviada_por_correo)
                    VALUES (@id, @servicio, @motivo, @clase, @primera, @ultima, @ocurrencias, @revisadas, @por, @en, @enviada)
                    ON CONFLICT (id) DO UPDATE SET
                        ultima_ocurrencia = GREATEST(auditoria.alertas.ultima_ocurrencia, EXCLUDED.ultima_ocurrencia),
                        ocurrencias = GREATEST(auditoria.alertas.ocurrencias, EXCLUDED.ocurrencias),
                        revisada_por = CASE WHEN EXCLUDED.revisadas_hasta > auditoria.alertas.revisadas_hasta THEN EXCLUDED.revisada_por ELSE auditoria.alertas.revisada_por END,
                        revisada_en = CASE WHEN EXCLUDED.revisadas_hasta > auditoria.alertas.revisadas_hasta THEN EXCLUDED.revisada_en ELSE auditoria.alertas.revisada_en END,
                        revisadas_hasta = LEAST(GREATEST(auditoria.alertas.revisadas_hasta, EXCLUDED.revisadas_hasta), GREATEST(auditoria.alertas.ocurrencias, EXCLUDED.ocurrencias)),
                        enviada_por_correo = auditoria.alertas.enviada_por_correo OR EXCLUDED.enviada_por_correo
                    """,
                    c,
                    t);
                comando.Parameters.AddWithValue("id", alerta.Id);
                comando.Parameters.AddWithValue("servicio", EscrituraDeAuditoria.Texto(alerta.Servicio));
                comando.Parameters.AddWithValue("motivo", alerta.Motivo);
                comando.Parameters.AddWithValue("clase", alerta.Clase == ClaseDeAlerta.Seguridad ? "seguridad" : "tecnica");
                comando.Parameters.Add(new NpgsqlParameter("primera", NpgsqlDbType.TimestampTz) { Value = alerta.PrimeraOcurrencia });
                comando.Parameters.Add(new NpgsqlParameter("ultima", NpgsqlDbType.TimestampTz) { Value = alerta.UltimaOcurrencia });
                comando.Parameters.AddWithValue("ocurrencias", alerta.Ocurrencias);
                comando.Parameters.AddWithValue("revisadas", alerta.RevisadasHasta);
                comando.Parameters.Add(new NpgsqlParameter("por", NpgsqlDbType.Uuid) { Value = (object?)alerta.RevisadaPor ?? DBNull.Value });
                comando.Parameters.Add(new NpgsqlParameter("en", NpgsqlDbType.TimestampTz) { Value = (object?)alerta.RevisadaEn ?? DBNull.Value });
                comando.Parameters.AddWithValue("enviada", alerta.EnviadaPorCorreo);
                return await comando.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            },
            cancelacion);
    }

    public Task<IReadOnlyList<AlertaAgrupada>> ListarDeSeguridad(PosicionDeAlerta? despuesDe, int maximo, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad<IReadOnlyList<AlertaAgrupada>>(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    $"""
                    SELECT {Columnas} FROM auditoria.alertas a
                    WHERE a.clase = 'seguridad'
                      AND (@sin_posicion OR a.ultima_ocurrencia < @ultima OR (a.ultima_ocurrencia = @ultima AND a.id > @id))
                    ORDER BY a.ultima_ocurrencia DESC, a.id
                    LIMIT @maximo
                    """,
                    c,
                    t);
                comando.Parameters.AddWithValue("sin_posicion", despuesDe is null);
                comando.Parameters.Add(new NpgsqlParameter("ultima", NpgsqlDbType.TimestampTz) { Value = despuesDe?.UltimaOcurrencia ?? DateTimeOffset.UnixEpoch });
                comando.Parameters.AddWithValue("id", despuesDe?.Id ?? string.Empty);
                comando.Parameters.AddWithValue("maximo", maximo);
                return await LeerVarias(comando, ct).ConfigureAwait(false);
            },
            cancelacion);

    public Task<IReadOnlyList<AlertaAgrupada>> Desde(DateTimeOffset desde, int maximo, CancellationToken cancelacion = default) =>
        EscrituraDeAuditoria.EnLaUnidad<IReadOnlyList<AlertaAgrupada>>(
            unidad,
            conexion,
            async (c, t, ct) =>
            {
                await using var comando = EscrituraDeAuditoria.Comando(
                    $"SELECT {Columnas} FROM auditoria.alertas a WHERE a.ultima_ocurrencia > @desde ORDER BY a.ultima_ocurrencia, a.id LIMIT @maximo",
                    c,
                    t);
                comando.Parameters.Add(new NpgsqlParameter("desde", NpgsqlDbType.TimestampTz) { Value = desde.ToUniversalTime() });
                comando.Parameters.AddWithValue("maximo", maximo);
                return await LeerVarias(comando, ct).ConfigureAwait(false);
            },
            cancelacion);

    internal static async Task Bloquear(NpgsqlConnection conexion, NpgsqlTransaction transaccion, string servicio, string motivo, CancellationToken cancelacion)
    {
        await using var comando = EscrituraDeAuditoria.Comando("SELECT pg_advisory_xact_lock(hashtextextended('auditoria.alertas|' || @servicio || '|' || @motivo, 0))", conexion, transaccion);
        comando.Parameters.AddWithValue("servicio", servicio);
        comando.Parameters.AddWithValue("motivo", motivo);
        await comando.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
    }

    internal static async Task<AlertaAgrupada?> LeerUna(NpgsqlCommand comando, CancellationToken cancelacion)
    {
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        return await lector.ReadAsync(cancelacion).ConfigureAwait(false) ? Leer(lector) : null;
    }

    internal static async Task<IReadOnlyList<AlertaAgrupada>> LeerVarias(NpgsqlCommand comando, CancellationToken cancelacion)
    {
        var alertas = new List<AlertaAgrupada>();
        await using var lector = await comando.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        while (await lector.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            alertas.Add(Leer(lector));
        }

        return alertas;
    }

    internal static AlertaAgrupada Leer(NpgsqlDataReader lector) =>
        AlertaAgrupada.Restaurar(
            lector.GetString(0),
            lector.GetString(1) == "ecommerce" ? ServicioDeOrigen.Ecommerce : ServicioDeOrigen.Inventario,
            lector.GetString(2),
            lector.GetString(3) == "seguridad" ? ClaseDeAlerta.Seguridad : ClaseDeAlerta.Tecnica,
            lector.GetFieldValue<DateTimeOffset>(4).ToUniversalTime(),
            lector.GetFieldValue<DateTimeOffset>(5).ToUniversalTime(),
            lector.GetInt32(6),
            lector.GetInt32(7),
            lector.IsDBNull(8) ? null : lector.GetGuid(8),
            lector.IsDBNull(9) ? null : lector.GetFieldValue<DateTimeOffset>(9).ToUniversalTime(),
            lector.GetBoolean(10));
}
