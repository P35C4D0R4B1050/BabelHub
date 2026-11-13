namespace BabelHub.Inventario.Domain.Plataforma;

/// <summary>Sobre qué se cuenta una política (SEG-05 a SEG-30).</summary>
public enum TipoDeSujetoDeLimite
{
    Usuario,
    Origen,
    Combinacion,

    /// <summary>Número de WhatsApp de un cliente (tope de avisos, NOT-14).</summary>
    Numero,
}

/// <summary>
/// Política de límite de uso: un máximo de eventos en una ventana deslizante (SEG-21) y, opcionalmente, un bloqueo de
/// duración propia al alcanzar el máximo. Sus atributos fijan cómo se comporta el motor: <see cref="Liberable"/>
/// (un éxito o una cancelación devuelve el cupo), <see cref="PublicarEvento"/> (el rechazo se publica como evento de
/// seguridad) y <see cref="RespaldoLocal"/> (cae al motor local si el almacén no responde).
/// </summary>
public sealed class PoliticaDeLimite
{
    public PoliticaDeLimite(
        string nombre,
        int maximo,
        TimeSpan ventana,
        TipoDeSujetoDeLimite tipo,
        TimeSpan? duracionDeBloqueo = null,
        bool liberable = false,
        bool publicarEvento = true,
        bool respaldoLocal = false)
    {
        if (string.IsNullOrWhiteSpace(nombre))
        {
            throw new ArgumentException("La política necesita una clave estable.", nameof(nombre));
        }

        if (maximo <= 0)
        {
            throw new ArgumentException("El máximo debe ser positivo.", nameof(maximo));
        }

        if (ventana <= TimeSpan.Zero)
        {
            throw new ArgumentException("La ventana debe ser positiva.", nameof(ventana));
        }

        if (duracionDeBloqueo is { } bloqueo && bloqueo <= TimeSpan.Zero)
        {
            throw new ArgumentException("La duración del bloqueo debe ser positiva.", nameof(duracionDeBloqueo));
        }

        if (tipo == TipoDeSujetoDeLimite.Numero && respaldoLocal)
        {
            throw new ArgumentException("Las políticas por número fallan cerradas: no pueden caer al motor local (NOT-15).", nameof(respaldoLocal));
        }

        if (!publicarEvento && tipo != TipoDeSujetoDeLimite.Numero)
        {
            throw new ArgumentException("Solo las políticas por número pueden omitir la publicación automática del evento.", nameof(publicarEvento));
        }

        Nombre = nombre;
        Maximo = maximo;
        Ventana = ventana;
        Tipo = tipo;
        DuracionDeBloqueo = duracionDeBloqueo;
        Liberable = liberable;
        PublicarEvento = publicarEvento;
        RespaldoLocal = respaldoLocal;
    }

    public string Nombre { get; }

    public int Maximo { get; }

    public TimeSpan Ventana { get; }

    public TipoDeSujetoDeLimite Tipo { get; }

    public TimeSpan? DuracionDeBloqueo { get; }

    public bool Liberable { get; }

    public bool PublicarEvento { get; }

    public bool RespaldoLocal { get; }

    /// <summary>
    /// Evalúa una solicitud en <paramref name="ahora"/> contra los instantes de las solicitudes ya contadas y el bloqueo
    /// vigente. Un rechazo no agrega entradas ni extiende ni recrea el bloqueo; aceptar la solicitud que alcanza el máximo
    /// de una política con bloqueo lo crea. La ventana no se vacía al bloquear (decisión D26).
    /// </summary>
    public DecisionDeLimite Evaluar(IReadOnlyCollection<DateTimeOffset> entradas, DateTimeOffset? bloqueoHasta, DateTimeOffset ahora)
    {
        ArgumentNullException.ThrowIfNull(entradas);

        if (bloqueoHasta is { } hasta && hasta > ahora)
        {
            return DecisionDeLimite.Rechazar(RespuestaDeLimite.Crear(Nombre, ahora, hasta));
        }

        var limite = ahora - Ventana;
        var vivas = entradas.Where(e => e > limite).OrderBy(e => e).ToList();

        if (vivas.Count >= Maximo)
        {
            // Deben vencer (vivas - máximo + 1) entradas para aceptar una más: la última en salir fija el instante.
            var disponibleDesde = vivas[vivas.Count - Maximo] + Ventana;
            return DecisionDeLimite.Rechazar(RespuestaDeLimite.Crear(Nombre, ahora, disponibleDesde));
        }

        DateTimeOffset? bloqueoNuevo = null;
        if (DuracionDeBloqueo is { } duracion && vivas.Count + 1 >= Maximo)
        {
            bloqueoNuevo = ahora + duracion;
        }

        return DecisionDeLimite.Permitir(bloqueoNuevo);
    }
}
