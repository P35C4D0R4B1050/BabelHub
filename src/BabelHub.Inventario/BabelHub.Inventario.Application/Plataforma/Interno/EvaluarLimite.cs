using BabelHub.Inventario.Domain.Plataforma;

namespace BabelHub.Inventario.Application.Plataforma.Interno;

/// <summary>Políticas de límite por clave estable (D04): los valores iniciales de la spec, ajustables por configuración.</summary>
public sealed class CatalogoDePoliticas
{
    private readonly Dictionary<string, PoliticaDeLimite> _politicas;

    public CatalogoDePoliticas(IEnumerable<PoliticaDeLimite> politicas)
    {
        ArgumentNullException.ThrowIfNull(politicas);
        _politicas = politicas.ToDictionary(p => p.Nombre, StringComparer.Ordinal);
    }

    public PoliticaDeLimite Obtener(ClaveDePolitica clave) =>
        _politicas.TryGetValue(clave.Valor, out var politica)
            ? politica
            : throw new ArgumentException($"No existe la política de límite «{clave.Valor}».", nameof(clave));
}

/// <summary>
/// Implementa <see cref="IControlDeLimites"/>: compone el contador de la política (por usuario, origen, combinación o
/// número, siempre con huellas), reserva el cupo en el motor, publica el rechazo como evento de seguridad si la política lo
/// pide, cuenta el rechazo del origen y, si el almacén no responde, cae al motor local solo cuando la política lo permite.
/// </summary>
public sealed class EvaluarLimite(
    CatalogoDePoliticas catalogo,
    IMotorDeLimites motor,
    IMotorDeLimites? respaldoLocal,
    IOrigenDeSolicitud origenes,
    IPublicadorDeEventosDeSeguridad eventos,
    IReloj reloj) : IControlDeLimites
{
    /// <summary>Ventana corta en la que se cuentan los rechazos de un origen para detectar comportamiento automatizado.</summary>
    public static readonly TimeSpan VentanaDeRechazosDelOrigen = TimeSpan.FromMinutes(5);

    public async Task<ResultadoDeLimite> Reservar(
        ClaveDePolitica politica,
        string? sujeto = null,
        string? claveDelLlamador = null,
        ClaveDeIdempotencia? idempotencia = null,
        CancellationToken cancelacion = default)
    {
        var definicion = catalogo.Obtener(politica);
        var origen = origenes.Actual;
        var contador = ComponerContador(definicion, sujeto, origen.Huella);

        ResultadoDeLimite resultado;
        try
        {
            resultado = await motor.Reservar(definicion, contador, claveDelLlamador, idempotencia, cancelacion).ConfigureAwait(false);
        }
        catch (MotorDeLimitesNoDisponibleException) when (definicion.RespaldoLocal && respaldoLocal is not null)
        {
            resultado = await respaldoLocal.Reservar(definicion, contador, claveDelLlamador, idempotencia, cancelacion).ConfigureAwait(false);
        }
        catch (MotorDeLimitesNoDisponibleException)
        {
            return ResultadoDeLimite.NoDisponible();
        }

        if (resultado.Estado == EstadoDeLimite.Excedido)
        {
            await RegistrarRechazo(definicion, sujeto, origen, cancelacion).ConfigureAwait(false);
        }

        return resultado;
    }

    public async Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        var definicion = catalogo.Obtener(new ClaveDePolitica(reserva.Politica));

        // Una política no liberable no devuelve cupo ni siquiera por cancelación (SEG-19, SEG-24).
        if (!definicion.Liberable)
        {
            return;
        }

        await Tolerando(() => motor.Liberar(definicion, reserva, cancelacion)).ConfigureAwait(false);
    }

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        ArgumentNullException.ThrowIfNull(resultado);
        return Tolerando(() => motor.Completar(reserva, resultado, cancelacion));
    }

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reserva);
        return Tolerando(() => motor.Fallar(reserva, cancelacion));
    }

    private static string ComponerContador(PoliticaDeLimite politica, string? sujeto, HuellaDeOrigen origen)
    {
        switch (politica.Tipo)
        {
            case TipoDeSujetoDeLimite.Origen:
                return sujeto is null
                    ? $"o:{origen}"
                    : throw new ArgumentException("Una política por origen no lleva sujeto.", nameof(sujeto));

            case TipoDeSujetoDeLimite.Usuario:
                return sujeto is null
                    ? throw new ArgumentException("Una política por usuario necesita el sujeto.", nameof(sujeto))
                    : $"u:s:{sujeto}";

            case TipoDeSujetoDeLimite.Combinacion:
                return sujeto is null
                    ? throw new ArgumentException("Una política por combinación necesita el sujeto.", nameof(sujeto))
                    : $"c:s:{sujeto}|{origen}";

            default:
                throw new ArgumentOutOfRangeException(nameof(politica), "El Inventario no tiene políticas por número.");
        }
    }

    private async Task RegistrarRechazo(PoliticaDeLimite politica, string? sujeto, OrigenDeSolicitud origen, CancellationToken cancelacion)
    {
        await Tolerando(() => motor.RegistrarRechazoDeOrigen(origen.Huella, VentanaDeRechazosDelOrigen, cancelacion)).ConfigureAwait(false);

        if (politica.PublicarEvento)
        {
            await eventos.Publicar(
                new EventoDeSeguridad(TipoDeEventoDeSeguridad.LimiteExcedido, reloj.Ahora, politica.Nombre, origen.Huella, origen.Direccion, sujeto),
                cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>Un fallo del almacén al devolver un cupo, completar o contar un rechazo no debe tumbar una operación que ya terminó.</summary>
    private static async Task Tolerando(Func<Task> accion)
    {
        try
        {
            await accion().ConfigureAwait(false);
        }
        catch (MotorDeLimitesNoDisponibleException)
        {
            // Se pierde solo la anotación; el cupo expira con su ventana.
        }
    }
}
