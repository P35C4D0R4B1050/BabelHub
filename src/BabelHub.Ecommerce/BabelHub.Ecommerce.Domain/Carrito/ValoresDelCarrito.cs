using System.Diagnostics.CodeAnalysis;

namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>Las unidades de una línea (CAR-02, D14): de 1 a 99. Una edición digital siempre lleva una (CAR-03).</summary>
public sealed record Cantidad
{
    public const int Minima = 1;

    public const int Maxima = 99;

    private Cantidad(int valor) => Valor = valor;

    public static Cantidad Uno { get; } = new(1);

    public int Valor { get; }

    public static bool TryCrear(int valor, [NotNullWhen(true)] out Cantidad? cantidad)
    {
        cantidad = valor is >= Minima and <= Maxima ? new Cantidad(valor) : null;
        return cantidad is not null;
    }

    public static Cantidad Crear(int valor) =>
        TryCrear(valor, out var cantidad) ? cantidad : throw new ArgumentOutOfRangeException(nameof(valor), "La cantidad debe estar entre 1 y 99.");
}

/// <summary>Quién es el dueño del carrito: una cuenta, o un visitante identificado solo por el SHA-256 de su cookie (D01). Nunca los dos.</summary>
public sealed class PropietarioDelCarrito
{
    public const int LongitudDeLaHuella = 32;

    private readonly byte[]? _huella;

    private PropietarioDelCarrito(Guid? cuentaId, byte[]? huella)
    {
        CuentaId = cuentaId;
        _huella = huella;
    }

    public Guid? CuentaId { get; }

    public bool EsDeVisitante => _huella is not null;

    /// <summary>El SHA-256 de la cookie del visitante (32 bytes); vacío si el carrito es de una cuenta.</summary>
    public ReadOnlySpan<byte> HuellaDelVisitante => _huella;

    public static PropietarioDelCarrito DeCuenta(Guid cuentaId) =>
        cuentaId == Guid.Empty ? throw new ArgumentException("La cuenta necesita su identificador.", nameof(cuentaId)) : new PropietarioDelCarrito(cuentaId, null);

    public static PropietarioDelCarrito DeVisitante(byte[] huella)
    {
        ArgumentNullException.ThrowIfNull(huella);
        return huella.Length == LongitudDeLaHuella
            ? new PropietarioDelCarrito(null, [.. huella])
            : throw new ArgumentException("La huella del visitante es un SHA-256 de 32 bytes.", nameof(huella));
    }
}

/// <summary>Una línea del carrito: una edición con su cantidad (una por edición, CAR-02).</summary>
public sealed class LineaDeCarrito
{
    private LineaDeCarrito(Guid edicionId, bool esDigital, Cantidad cantidad, DateTimeOffset agregadaEn)
    {
        EdicionId = edicionId;
        EsDigital = esDigital;
        Cantidad = cantidad;
        AgregadaEn = agregadaEn;
    }

    public Guid EdicionId { get; }

    public bool EsDigital { get; }

    public Cantidad Cantidad { get; }

    public DateTimeOffset AgregadaEn { get; }

    public static LineaDeCarrito Crear(Guid edicionId, bool esDigital, Cantidad cantidad, DateTimeOffset agregadaEn)
    {
        ArgumentNullException.ThrowIfNull(cantidad);
        if (edicionId == Guid.Empty)
        {
            throw new ArgumentException("La línea necesita su edición.", nameof(edicionId));
        }

        return esDigital && cantidad.Valor != 1
            ? throw new ArgumentException("Una edición digital lleva siempre una unidad.", nameof(cantidad))
            : new LineaDeCarrito(edicionId, esDigital, cantidad, agregadaEn);
    }

    internal LineaDeCarrito ConCantidad(Cantidad cantidad) => new(EdicionId, EsDigital, cantidad, AgregadaEn);
}

/// <summary>Los importes en soles (CAR-23, D17): <c>decimal</c> con dos decimales, redondeando <b>alejándose de cero</b> (el 5 siempre sube).</summary>
public static class Soles
{
    public const int Decimales = 2;

    public static decimal Redondear(decimal importe) => Math.Round(importe, Decimales, MidpointRounding.AwayFromZero);

    /// <summary>El importe de una línea: precio por cantidad a dos decimales.</summary>
    public static decimal Importe(decimal precioUnitario, int cantidad) => Redondear(precioUnitario * cantidad);
}
