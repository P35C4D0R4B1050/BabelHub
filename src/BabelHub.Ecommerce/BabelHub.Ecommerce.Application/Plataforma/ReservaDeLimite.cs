namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Identifica un cupo concreto reservado en una política para poder devolverlo con <c>Liberar</c>: la política, la clave
/// opaca del contador (nunca datos personales), el identificador de la entrada, su instante y, si el llamador la aportó,
/// la clave que hace idempotente la reserva (por ejemplo el identificador de un aviso).
/// </summary>
public sealed record ReservaDeLimite
{
    public ReservaDeLimite(string politica, string sujeto, string identificador, DateTimeOffset reservadaEn, string? claveDelLlamador = null, string? claveDeIdempotencia = null)
    {
        Politica = NoVacio(politica, nameof(politica));
        Sujeto = NoVacio(sujeto, nameof(sujeto));
        Identificador = NoVacio(identificador, nameof(identificador));
        ClaveDelLlamador = claveDelLlamador is null ? null : NoVacio(claveDelLlamador, nameof(claveDelLlamador));
        ReservadaEn = reservadaEn;
        ClaveDeIdempotencia = claveDeIdempotencia is null ? null : NoVacio(claveDeIdempotencia, nameof(claveDeIdempotencia));
    }

    public string Politica { get; }

    /// <summary>Clave opaca del contador al que pertenece la entrada.</summary>
    public string Sujeto { get; }

    public string Identificador { get; }

    public DateTimeOffset ReservadaEn { get; }

    public string? ClaveDelLlamador { get; }

    /// <summary>Clave opaca con la que el motor guarda la idempotencia del cliente (SEG-22), para <c>Completar</c> y <c>Fallar</c>.</summary>
    public string? ClaveDeIdempotencia { get; }

    private static string NoVacio(string valor, string nombre) =>
        string.IsNullOrWhiteSpace(valor) ? throw new ArgumentException("El valor no puede estar vacío.", nombre) : valor;
}
