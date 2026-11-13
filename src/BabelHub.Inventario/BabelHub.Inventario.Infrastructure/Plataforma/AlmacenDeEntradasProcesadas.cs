using BabelHub.Inventario.Application.Plataforma;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>Definición de <c>plataforma.entradas_procesadas</c>; la migración la incorpora tal cual.</summary>
public static class DefinicionDeEntradasProcesadas
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS plataforma;
        CREATE TABLE IF NOT EXISTS plataforma.entradas_procesadas (
            clave text PRIMARY KEY,
            estado text NOT NULL CHECK (estado IN ('en_curso', 'completada')),
            desenlace text,
            id_del_recurso text,
            fecha timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_entradas_procesadas_fecha ON plataforma.entradas_procesadas (fecha);
        """;
}

public sealed record OpcionesDeEntradasProcesadas
{
    /// <summary>Las entradas más viejas que esto se purgan.</summary>
    public TimeSpan Retencion { get; init; } = TimeSpan.FromDays(30);
}

/// <summary>
/// Registro de entradas de integración ya vistas (SEG-12) en <c>plataforma.entradas_procesadas</c>, con la clave única «proveedor +
/// identificador del evento». La reclamación va en la <b>misma transacción que el efecto</b>: si el efecto falla, la reclamación se
/// revierte con él y el evento no queda marcado como procesado. Un duplicado que llega mientras el primero sigue en curso (otra
/// transacción abierta con la misma clave) recibe «en curso» sin esperar; uno posterior recibe el resultado guardado.
/// </summary>
public sealed class AlmacenDeEntradasProcesadas(UnidadDeTrabajoPostgres unidad, OpcionesDeEntradasProcesadas? opciones = null)
    : IAlmacenDeEntradasProcesadas
{
    private static readonly EsquemaDeModulo Plataforma = EsquemaDeModulo.Crear("plataforma");

    private readonly OpcionesDeEntradasProcesadas _opciones = opciones ?? new OpcionesDeEntradasProcesadas();

    public async Task<ReclamoDeEntrada> Reclamar(string proveedor, string idDeEntrada, CancellationToken cancelacion = default)
    {
        var clave = Clave(proveedor, idDeEntrada);
        ReclamoDeEntrada? reclamo = null;

        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(Plataforma, async interno => reclamo = await Reclamar(clave, interno).ConfigureAwait(false), token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);

        return reclamo!;
    }

    public async Task Completar(string proveedor, string idDeEntrada, ResultadoMinimo resultado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(resultado);
        var clave = Clave(proveedor, idDeEntrada);

        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Plataforma,
                    async interno =>
                    {
                        await using var comando = Comando("UPDATE plataforma.entradas_procesadas SET estado = 'completada', desenlace = @desenlace, id_del_recurso = @recurso WHERE clave = @clave");
                        comando.Parameters.AddWithValue("desenlace", resultado.Desenlace);
                        comando.Parameters.AddWithValue("recurso", (object?)resultado.IdDelRecurso ?? DBNull.Value);
                        comando.Parameters.AddWithValue("clave", clave);
                        await comando.ExecuteNonQueryAsync(interno).ConfigureAwait(false);
                    },
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>Borra las entradas de más de 30 días; lo invoca un trabajo de fondo. Devuelve cuántas borró.</summary>
    public async Task<int> PurgarVencidas(CancellationToken cancelacion = default)
    {
        var borradas = 0;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Plataforma,
                    async interno =>
                    {
                        await using var comando = Comando("DELETE FROM plataforma.entradas_procesadas WHERE fecha < clock_timestamp() - @retencion");
                        comando.Parameters.AddWithValue("retencion", _opciones.Retencion);
                        borradas = await comando.ExecuteNonQueryAsync(interno).ConfigureAwait(false);
                    },
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);

        return borradas;
    }

    private static string Clave(string proveedor, string idDeEntrada)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proveedor);
        ArgumentException.ThrowIfNullOrWhiteSpace(idDeEntrada);
        return $"{proveedor}|{idDeEntrada}";
    }

    private NpgsqlCommand Comando(string sql) => new(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);

    private async Task<ReclamoDeEntrada> Reclamar(string clave, CancellationToken cancelacion)
    {
        // Si otra transacción ya tiene esta clave en curso, no se espera a que termine: el duplicado concurrente recibe «en curso».
        await using (var intento = Comando("SELECT pg_try_advisory_xact_lock(hashtextextended(@clave, 0))"))
        {
            intento.Parameters.AddWithValue("clave", clave);
            if (!(bool)(await intento.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!)
            {
                return new ReclamoDeEntrada(EstadoDeEntrada.EnCurso);
            }
        }

        await using (var insercion = Comando("INSERT INTO plataforma.entradas_procesadas (clave, estado, fecha) VALUES (@clave, 'en_curso', clock_timestamp()) ON CONFLICT (clave) DO NOTHING"))
        {
            insercion.Parameters.AddWithValue("clave", clave);
            if (await insercion.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false) == 1)
            {
                return new ReclamoDeEntrada(EstadoDeEntrada.Nueva);
            }
        }

        await using var lectura = Comando("SELECT estado, desenlace, id_del_recurso FROM plataforma.entradas_procesadas WHERE clave = @clave");
        lectura.Parameters.AddWithValue("clave", clave);
        await using var filas = await lectura.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
        if (!await filas.ReadAsync(cancelacion).ConfigureAwait(false))
        {
            // La borró la purga entre el intento y la lectura: se trata como una entrada nueva en la próxima llamada.
            return new ReclamoDeEntrada(EstadoDeEntrada.EnCurso);
        }

        if (filas.GetString(0) != "completada" || filas.IsDBNull(1))
        {
            return new ReclamoDeEntrada(EstadoDeEntrada.EnCurso);
        }

        return new ReclamoDeEntrada(
            EstadoDeEntrada.Procesada,
            new ResultadoMinimo(filas.GetString(1), filas.IsDBNull(2) ? null : filas.GetString(2)));
    }
}
