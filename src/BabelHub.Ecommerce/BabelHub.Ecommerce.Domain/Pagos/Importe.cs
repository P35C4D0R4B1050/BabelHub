using System.Globalization;

namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>
/// Un importe en soles con dos decimales (PAG-09). La moneda es siempre <c>PEN</c>: no existe otra y por eso no se guarda por valor. Nunca es negativo; el total que se cobra, los
/// montos de las líneas y los reembolsos son importes. Se compara por valor y se opera en céntimos exactos.
/// </summary>
public readonly record struct Importe : IComparable<Importe>
{
    /// <summary>La única moneda del cobro (PAG-09).</summary>
    public const string Moneda = "PEN";

    private Importe(decimal valor) => Valor = valor;

    public static Importe Cero => new(0.00m);

    /// <summary>El valor en soles, con dos decimales.</summary>
    public decimal Valor { get; }

    /// <summary>El valor en céntimos: lo que se envía a la pasarela.</summary>
    public long EnCentimos => (long)(Valor * 100m);

    /// <summary>Crea un importe: no negativo y con a lo más dos decimales significativos.</summary>
    public static Importe De(decimal valor)
    {
        if (valor < 0m || decimal.Round(valor, 2, MidpointRounding.ToZero) != valor)
        {
            throw new ArgumentOutOfRangeException(nameof(valor), "Un importe es un monto en soles no negativo con a lo más dos decimales.");
        }

        return new Importe(decimal.Round(valor, 2, MidpointRounding.ToZero));
    }

    public static Importe DesdeCentimos(long centimos)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(centimos);
        return new Importe(centimos / 100m);
    }

    public static Importe operator +(Importe a, Importe b) => new(a.Valor + b.Valor);

    /// <summary>La resta nunca deja un importe negativo: quien la pide comprueba antes que alcance.</summary>
    public static Importe operator -(Importe a, Importe b) =>
        a.Valor < b.Valor ? throw new InvalidOperationException("Un importe no puede quedar negativo.") : new Importe(a.Valor - b.Valor);

    public static bool operator <(Importe a, Importe b) => a.Valor < b.Valor;

    public static bool operator >(Importe a, Importe b) => a.Valor > b.Valor;

    public static bool operator <=(Importe a, Importe b) => a.Valor <= b.Valor;

    public static bool operator >=(Importe a, Importe b) => a.Valor >= b.Valor;

    public int CompareTo(Importe otro) => Valor.CompareTo(otro.Valor);

    public override string ToString() => Valor.ToString("0.00", CultureInfo.InvariantCulture);
}
