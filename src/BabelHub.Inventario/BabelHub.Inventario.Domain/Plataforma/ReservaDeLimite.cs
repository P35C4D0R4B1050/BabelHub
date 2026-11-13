namespace BabelHub.Inventario.Domain.Plataforma;

/// <summary>
/// Identifica un cupo concreto reservado en una política para poder devolverlo con <c>Liberar</c>: la política, la clave
/// opaca del contador (nunca datos personales), el identificador de la entrada, su instante y, si el llamador la aportó,
/// la clave que hace idempotente la reserva (por ejemplo el identificador de un aviso).
/// </summary>
public sealed record ReservaDeLimite
{
    public ReservaDeLimite(string politica, string sujeto, string identificador, DateTimeOffset reservadaEn, string? claveDelLlamador = null)
    {
        Politica = NoVacio(politica, nameof(politica));
        Sujeto = NoVacio(sujeto, nameof(sujeto));
        Identificador = NoVacio(identificador, nameof(identificador));
        ClaveDelLlamador = claveDelLlamador is null ? null : NoVacio(claveDelLlamador, nameof(claveDelLlamador));
        ReservadaEn = reservadaEn;
    }

    public string Politica { get; }

    /// <summary>Clave opaca del contador al que pertenece la entrada.</summary>
    public string Sujeto { get; }

    public string Identificador { get; }

    public DateTimeOffset ReservadaEn { get; }

    public string? ClaveDelLlamador { get; }

    private static string NoVacio(string valor, string nombre) =>
        string.IsNullOrWhiteSpace(valor) ? throw new ArgumentException("El valor no puede estar vacío.", nombre) : valor;
}
