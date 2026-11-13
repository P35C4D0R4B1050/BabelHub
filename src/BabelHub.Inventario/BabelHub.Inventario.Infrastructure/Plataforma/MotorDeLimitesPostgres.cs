using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Plataforma;
using Npgsql;
using NpgsqlTypes;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>Definición de <c>plataforma.eventos_limite</c>; la migración del Inventario la incorpora tal cual.</summary>
public static class DefinicionDeEventosDeLimite
{
    public const string Sql = """
        CREATE SCHEMA IF NOT EXISTS plataforma;
        CREATE TABLE IF NOT EXISTS plataforma.eventos_limite (
            id uuid PRIMARY KEY,
            clave text NOT NULL,
            instante timestamptz NOT NULL
        );
        CREATE INDEX IF NOT EXISTS ix_eventos_limite_clave_instante ON plataforma.eventos_limite (clave, instante);
        """;
}

public sealed record OpcionesDeMotorPostgres
{
    /// <summary>
    /// Políticas cuyo conteo va en una transacción propia, confirmada al instante: el presupuesto del personal (SEG-30), donde el
    /// intento cuenta aunque la operación falle. Las demás (reservas, SEL-14 y SEL-19) cuentan en la transacción del llamador.
    /// </summary>
    public IReadOnlySet<string> PoliticasEnTransaccionIndependiente { get; init; } =
        new HashSet<string>(StringComparer.Ordinal) { "ajustes-del-personal", "traslados-del-personal" };

    /// <summary>Los eventos más viejos que esto se purgan (SEG-25, las huellas y eventos no se guardan más de 24 horas).</summary>
    public TimeSpan Retencion { get; init; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Motor de límites del Inventario sobre PostgreSQL (D05): un evento por reserva iniciada en <c>plataforma.eventos_limite</c> y una
/// ventana deslizante evaluada con la regla del Dominio. Un bloqueo asesor por clave serializa a los que cuentan lo mismo, de modo que
/// con 12 cuentas de un origen reservando a la vez se aceptan exactamente 10. Las reservas cuentan en la transacción del llamador:
/// si la reserva se revierte (falta de stock, otro límite), el evento desaparece y la solicitud no cuenta (SEL-14). El presupuesto del
/// personal usa una transacción independiente. No lleva idempotencia ni bloqueos con duración: las reservas del Inventario guardan la
/// suya, y las políticas con bloqueo son del E-commerce.
/// </summary>
public sealed class MotorDeLimitesPostgres(
    UnidadDeTrabajoPostgres delLlamador,
    UnidadDeTrabajoPostgres independiente,
    OpcionesDeMotorPostgres? opciones = null) : IMotorDeLimites
{
    private static readonly EsquemaDeModulo Plataforma = EsquemaDeModulo.Crear("plataforma");

    private readonly OpcionesDeMotorPostgres _opciones = opciones ?? new OpcionesDeMotorPostgres();

    public async Task<ResultadoDeLimite> Reservar(
        PoliticaDeLimite politica,
        string contador,
        string? claveDelLlamador,
        ClaveDeIdempotencia? idempotencia,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(politica);
        ArgumentException.ThrowIfNullOrWhiteSpace(contador);
        if (claveDelLlamador is not null || idempotencia is not null)
        {
            throw new NotSupportedException("El motor del Inventario no lleva idempotencia: la de las reservas es de su módulo.");
        }

        if (politica.DuracionDeBloqueo is not null)
        {
            throw new NotSupportedException("El motor del Inventario no crea bloqueos con duración propia.");
        }

        var unidad = Unidad(politica.Nombre);
        ResultadoDeLimite? resultado = null;
        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Plataforma,
                    async interno => resultado = await Evaluar(unidad, politica, contador, interno).ConfigureAwait(false),
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);

        return resultado!;
    }

    /// <summary>Quita el evento de una reserva que no llegó a iniciarse (por ejemplo, rechazada por stock cuando la transacción se confirma).</summary>
    public async Task Liberar(PoliticaDeLimite politica, ReservaDeLimite reserva, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        var unidad = Unidad(reserva.Politica);

        await unidad.Ejecutar(
            async token =>
            {
                await unidad.EjecutarComo(
                    Plataforma,
                    async interno =>
                    {
                        await using var comando = Comando(unidad, "DELETE FROM plataforma.eventos_limite WHERE id = @id AND clave = @clave");
                        comando.Parameters.AddWithValue("id", Guid.ParseExact(reserva.Identificador, "N"));
                        comando.Parameters.AddWithValue("clave", Clave(reserva.Politica, reserva.Sujeto));
                        await comando.ExecuteNonQueryAsync(interno).ConfigureAwait(false);
                    },
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);
    }

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion) => Task.CompletedTask;

    /// <summary>El Inventario no tiene páginas con verificación de persona: no cuenta rechazos por origen.</summary>
    public Task<int> RegistrarRechazoDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion) => Task.FromResult(0);

    public Task<int> ContarRechazosDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion) => Task.FromResult(0);

    /// <summary>Borra los eventos más viejos que la retención (24 horas); lo invoca un trabajo de fondo. Devuelve cuántos borró.</summary>
    public async Task<int> PurgarVencidos(CancellationToken cancelacion = default)
    {
        var borrados = 0;
        await independiente.Ejecutar(
            async token =>
            {
                await independiente.EjecutarComo(
                    Plataforma,
                    async interno =>
                    {
                        await using var comando = Comando(independiente, "DELETE FROM plataforma.eventos_limite WHERE instante < clock_timestamp() - @retencion");
                        comando.Parameters.AddWithValue("retencion", _opciones.Retencion);
                        borrados = await comando.ExecuteNonQueryAsync(interno).ConfigureAwait(false);
                    },
                    token).ConfigureAwait(false);
                return 0;
            },
            cancelacion).ConfigureAwait(false);

        return borrados;
    }

    private static string Clave(string politica, string contador) => $"{politica}|{contador}";

    private static NpgsqlCommand Comando(UnidadDeTrabajoPostgres unidad, string sql) =>
        new(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);

    private UnidadDeTrabajoPostgres Unidad(string politica) =>
        _opciones.PoliticasEnTransaccionIndependiente.Contains(politica) ? independiente : delLlamador;

    private static async Task<ResultadoDeLimite> Evaluar(UnidadDeTrabajoPostgres unidad, PoliticaDeLimite politica, string contador, CancellationToken cancelacion)
    {
        var clave = Clave(politica.Nombre, contador);

        // El bloqueo asesor dura hasta el fin de la transacción: quien cuenta lo mismo espera a que esta termine.
        await using (var candado = Comando(unidad, "SELECT pg_advisory_xact_lock(hashtextextended(@clave, 0))"))
        {
            candado.Parameters.AddWithValue("clave", clave);
            await candado.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }

        DateTimeOffset ahora;
        await using (var reloj = Comando(unidad, "SELECT clock_timestamp()"))
        {
            ahora = new DateTimeOffset((DateTime)(await reloj.ExecuteScalarAsync(cancelacion).ConfigureAwait(false))!, TimeSpan.Zero);
        }

        var vivas = new List<DateTimeOffset>();
        await using (var lectura = Comando(unidad, "SELECT instante FROM plataforma.eventos_limite WHERE clave = @clave AND instante > @corte ORDER BY instante"))
        {
            lectura.Parameters.AddWithValue("clave", clave);
            lectura.Parameters.Add(new NpgsqlParameter("corte", NpgsqlDbType.TimestampTz) { Value = (ahora - politica.Ventana).UtcDateTime });
            await using var filas = await lectura.ExecuteReaderAsync(cancelacion).ConfigureAwait(false);
            while (await filas.ReadAsync(cancelacion).ConfigureAwait(false))
            {
                vivas.Add(new DateTimeOffset(filas.GetDateTime(0), TimeSpan.Zero));
            }
        }

        var decision = politica.Evaluar(vivas, null, ahora);
        if (!decision.Permitida)
        {
            var respuesta = decision.Respuesta!;
            return ResultadoDeLimite.Excedido(respuesta.Motivo, respuesta.EsperaEnSegundos, respuesta.DisponibleDesde);
        }

        var id = Guid.NewGuid();
        await using (var insercion = Comando(unidad, "INSERT INTO plataforma.eventos_limite (id, clave, instante) VALUES (@id, @clave, @instante)"))
        {
            insercion.Parameters.AddWithValue("id", id);
            insercion.Parameters.AddWithValue("clave", clave);
            insercion.Parameters.Add(new NpgsqlParameter("instante", NpgsqlDbType.TimestampTz) { Value = ahora.UtcDateTime });
            await insercion.ExecuteNonQueryAsync(cancelacion).ConfigureAwait(false);
        }

        return ResultadoDeLimite.Permitido(new ReservaDeLimite(politica.Nombre, contador, id.ToString("N"), ahora));
    }
}
