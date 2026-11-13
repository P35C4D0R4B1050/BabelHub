namespace BabelHub.Ecommerce.Domain.Plataforma;

/// <summary>
/// Respuesta única de un límite excedido (SEG-07): el motivo (la clave estable de la política, nunca datos de cuentas),
/// la espera sugerida en segundos y el instante de nueva disponibilidad. El código estable `&lt;SRV&gt;-LIMITE-EXCEDIDO`
/// lo asigna la Api, porque el Dominio no conoce el catálogo de códigos.
/// </summary>
public sealed record RespuestaDeLimite
{
    private RespuestaDeLimite(string motivo, int esperaEnSegundos, DateTimeOffset disponibleDesde)
    {
        Motivo = motivo;
        EsperaEnSegundos = esperaEnSegundos;
        DisponibleDesde = disponibleDesde;
    }

    public string Motivo { get; }

    public int EsperaEnSegundos { get; }

    public DateTimeOffset DisponibleDesde { get; }

    /// <summary>Crea la respuesta; la espera se redondea hacia arriba y nunca es menor de un segundo.</summary>
    public static RespuestaDeLimite Crear(string motivo, DateTimeOffset ahora, DateTimeOffset disponibleDesde)
    {
        if (string.IsNullOrWhiteSpace(motivo))
        {
            throw new ArgumentException("El motivo del límite no puede estar vacío.", nameof(motivo));
        }

        if (disponibleDesde <= ahora)
        {
            throw new ArgumentException("El instante de nueva disponibilidad debe ser posterior al actual.", nameof(disponibleDesde));
        }

        var espera = (int)Math.Max(1, Math.Ceiling((disponibleDesde - ahora).TotalSeconds));
        return new RespuestaDeLimite(motivo, espera, disponibleDesde);
    }
}
