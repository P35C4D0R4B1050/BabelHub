using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using StackExchange.Redis;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>Opciones del motor de límites en Redis.</summary>
public sealed record OpcionesDeMotorRedis
{
    /// <summary>Tope de claves de la instancia: con más, no se crean contadores nuevos y el motor responde «no disponible» (SEG-26).</summary>
    public long TopeDeClaves { get; init; } = 200_000;

    /// <summary>Margen sobre la ventana antes de que Redis borre un contador, para no perder entradas por el redondeo.</summary>
    public TimeSpan MargenDeExpiracion { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Vigencia de «en curso» de una clave de idempotencia: un caso de uso caído no bloquea la clave 24 horas.</summary>
    public TimeSpan VigenciaEnCurso { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Vigencia de una clave de idempotencia completada (SEG-22).</summary>
    public TimeSpan VigenciaCompletada { get; init; } = TimeSpan.FromHours(24);
}

/// <summary>
/// Motor de límites sobre el Redis dedicado de contadores: una ventana deslizante por contador (conjunto ordenado en
/// milisegundos del reloj de Redis), con reserva atómica del cupo, bloqueo con duración propia y liberación atómica, todo en
/// scripts Lua. Una falla de conexión, un tiempo agotado o un tope de claves se informan como
/// <see cref="MotorDeLimitesNoDisponibleException"/> (SEG-26). Solo se usa con el usuario ACL restringido del motor.
/// </summary>
public sealed class MotorDeLimitesRedis(IConnectionMultiplexer conexion, OpcionesDeMotorRedis opciones) : IMotorDeLimites
{
    private const char Separador = '\u001f';

    private const int Permitido = 1;
    private const int Excedido = 2;
    private const int RepeticionCompletada = 3;
    private const int RepeticionEnCurso = 4;
    private const int ClaveEnConflicto = 5;
    private const int SinCapacidad = 6;

    public async Task<ResultadoDeLimite> Reservar(
        PoliticaDeLimite politica,
        string contador,
        string? claveDelLlamador,
        ClaveDeIdempotencia? idempotencia,
        CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(politica);
        ArgumentException.ThrowIfNullOrWhiteSpace(contador);

        var claveContador = ClaveDelContador(politica.Nombre, contador);
        var claveBloqueo = $"blq:{politica.Nombre}:{contador}";
        var claveReserva = claveDelLlamador is null ? "rsv:ninguna" : $"rsv:{politica.Nombre}:{claveDelLlamador}";
        var claveIdem = idempotencia is null ? "idem:ninguna" : ClaveDeIdempotenciaDe(politica.Nombre, contador, idempotencia);
        var identificador = Guid.NewGuid().ToString("N");

        RedisValue[] argumentos =
        [
            politica.Maximo,
            (long)politica.Ventana.TotalMilliseconds,
            politica.DuracionDeBloqueo is { } bloqueo ? (long)bloqueo.TotalMilliseconds : 0L,
            identificador,
            opciones.TopeDeClaves,
            (long)opciones.MargenDeExpiracion.TotalMilliseconds,
            claveDelLlamador is null ? "0" : "1",
            (long)politica.Ventana.TotalMilliseconds,
            idempotencia is null ? "0" : "1",
            (long)opciones.VigenciaEnCurso.TotalMilliseconds,
            idempotencia?.HuellaDelCuerpo ?? string.Empty,
        ];

        var resultadoDelScript = await Ejecutar(
            ScriptsDeLimitesRedis.Reservar,
            [claveContador, claveBloqueo, claveReserva, claveIdem],
            argumentos).ConfigureAwait(false);
        var respuesta = (RedisResult[])resultadoDelScript!;

        var estado = (int)respuesta[0];
        var ahora = (long)respuesta[1];

        switch (estado)
        {
            case Permitido:
            {
                var id = (string)respuesta[2]!;
                var instante = DateTimeOffset.FromUnixTimeMilliseconds(ahora);
                return ResultadoDeLimite.Permitido(new ReservaDeLimite(
                    politica.Nombre,
                    contador,
                    id,
                    instante,
                    claveDelLlamador,
                    idempotencia is null ? null : claveIdem));
            }

            case Excedido:
            {
                var disponible = DateTimeOffset.FromUnixTimeMilliseconds((long)respuesta[2]);
                var detalle = RespuestaDeLimite.Crear(politica.Nombre, DateTimeOffset.FromUnixTimeMilliseconds(ahora), disponible);
                return ResultadoDeLimite.Excedido(detalle.Motivo, detalle.EsperaEnSegundos, detalle.DisponibleDesde);
            }

            case RepeticionCompletada:
                return ResultadoDeLimite.RepeticionCompletada(new ResultadoMinimo((string)respuesta[2]!, Vacio((string?)respuesta[3])));

            case RepeticionEnCurso:
                return ResultadoDeLimite.RepeticionEnCurso();

            case ClaveEnConflicto:
                return ResultadoDeLimite.ClaveEnConflicto();

            case SinCapacidad:
                throw new MotorDeLimitesNoDisponibleException("La instancia de contadores alcanzó su tope de claves.");

            default:
                throw new InvalidOperationException($"El script de reserva devolvió un estado desconocido ({estado}).");
        }
    }

    public async Task Liberar(PoliticaDeLimite politica, ReservaDeLimite reserva, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(politica);
        ArgumentNullException.ThrowIfNull(reserva);

        var conReserva = reserva.ClaveDelLlamador is not null;
        await Ejecutar(
            ScriptsDeLimitesRedis.Liberar,
            [
                ClaveDelContador(reserva.Politica, reserva.Sujeto),
                $"blq:{reserva.Politica}:{reserva.Sujeto}",
                conReserva ? $"rsv:{reserva.Politica}:{reserva.ClaveDelLlamador}" : "rsv:ninguna",
            ],
            [reserva.Identificador, conReserva ? "1" : "0"]).ConfigureAwait(false);
    }

    public async Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        ArgumentNullException.ThrowIfNull(resultado);

        if (reserva.ClaveDeIdempotencia is null)
        {
            return;
        }

        if (resultado.Desenlace.Contains(Separador, StringComparison.Ordinal) || (resultado.IdDelRecurso?.Contains(Separador, StringComparison.Ordinal) ?? false))
        {
            throw new ArgumentException("El resultado no puede contener caracteres de control.", nameof(resultado));
        }

        await Ejecutar(
            ScriptsDeLimitesRedis.Completar,
            [reserva.ClaveDeIdempotencia],
            [resultado.Desenlace, resultado.IdDelRecurso ?? string.Empty, (long)opciones.VigenciaCompletada.TotalMilliseconds]).ConfigureAwait(false);
    }

    public async Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(reserva);

        if (reserva.ClaveDeIdempotencia is null)
        {
            return;
        }

        // DEL explícito: la ACL del motor no incluye UNLINK, que es lo que usa `KeyDeleteAsync`.
        await Proteger(() => conexion.GetDatabase().ExecuteAsync("DEL", reserva.ClaveDeIdempotencia)).ConfigureAwait(false);
    }

    public async Task<int> RegistrarRechazoDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(origen);
        var resultado = await Ejecutar(
            ScriptsDeLimitesRedis.RechazosDelOrigen,
            [$"rech:{origen}"],
            [(long)ventana.TotalMilliseconds, Guid.NewGuid().ToString("N"), "1", opciones.TopeDeClaves, (long)opciones.MargenDeExpiracion.TotalMilliseconds]).ConfigureAwait(false);
        return (int)resultado;
    }

    public async Task<int> ContarRechazosDeOrigen(HuellaDeOrigen origen, TimeSpan ventana, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(origen);
        var resultado = await Ejecutar(
            ScriptsDeLimitesRedis.RechazosDelOrigen,
            [$"rech:{origen}"],
            [(long)ventana.TotalMilliseconds, string.Empty, "0", opciones.TopeDeClaves, (long)opciones.MargenDeExpiracion.TotalMilliseconds]).ConfigureAwait(false);
        return (int)resultado;
    }

    internal static string ClaveDelContador(string politica, string contador) => $"lim:{politica}:{contador}";

    private static string ClaveDeIdempotenciaDe(string politica, string contador, ClaveDeIdempotencia idempotencia) =>
        $"idem:{politica}:{contador}:{idempotencia.Ruta}:{idempotencia.ClaveDelCliente}";

    private static string? Vacio(string? valor) => string.IsNullOrEmpty(valor) ? null : valor;

    /// <summary>Ejecuta un script por su huella; si el servidor lo olvidó (<c>NOSCRIPT</c>) lo carga y reintenta.</summary>
    private async Task<RedisResult> Ejecutar(string script, RedisKey[] claves, RedisValue[] argumentos)
    {
        var hash = Convert.FromHexString(ScriptsDeLimitesRedis.Huella(script));
        var bd = conexion.GetDatabase();

        return await Proteger(async () =>
        {
            try
            {
                return await bd.ScriptEvaluateAsync(hash, claves, argumentos).ConfigureAwait(false);
            }
            catch (RedisServerException ex) when (ex.Message.StartsWith("NOSCRIPT", StringComparison.OrdinalIgnoreCase))
            {
                await bd.ExecuteAsync("SCRIPT", "LOAD", script).ConfigureAwait(false);
                return await bd.ScriptEvaluateAsync(hash, claves, argumentos).ConfigureAwait(false);
            }
        }).ConfigureAwait(false);
    }

    private static async Task<T> Proteger<T>(Func<Task<T>> accion)
    {
        try
        {
            return await accion().ConfigureAwait(false);
        }
        catch (Exception ex) when (EsDeDisponibilidad(ex))
        {
            throw new MotorDeLimitesNoDisponibleException("El almacén de contadores no está disponible.", ex);
        }
    }

    private static bool EsDeDisponibilidad(Exception ex) => ex switch
    {
        RedisConnectionException => true,
        RedisTimeoutException => true,
        TimeoutException => true,
        RedisServerException servidor => servidor.Message.StartsWith("OOM", StringComparison.OrdinalIgnoreCase)
            || servidor.Message.StartsWith("LOADING", StringComparison.OrdinalIgnoreCase)
            || servidor.Message.StartsWith("BUSY", StringComparison.OrdinalIgnoreCase)
            || servidor.Message.StartsWith("READONLY", StringComparison.OrdinalIgnoreCase)
            || servidor.Message.StartsWith("MISCONF", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };
}
