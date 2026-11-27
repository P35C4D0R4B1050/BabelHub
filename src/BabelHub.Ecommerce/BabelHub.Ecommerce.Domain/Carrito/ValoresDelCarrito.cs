using System.Diagnostics.CodeAnalysis;

namespace BabelHub.Ecommerce.Domain.Carrito;

/// <summary>Las unidades de una línea (CAR-02, D14): de 1 a 20 (el tope de unidades por línea de una reserva, decisión de la persona del 2026-11-19; antes 99). Una edición digital siempre lleva una (CAR-03).</summary>
public sealed record Cantidad
{
    public const int Minima = 1;

    public const int Maxima = 20;

    private Cantidad(int valor) => Valor = valor;

    public static Cantidad Uno { get; } = new(1);

    public int Valor { get; }

    public static bool TryCrear(int valor, [NotNullWhen(true)] out Cantidad? cantidad)
    {
        cantidad = valor is >= Minima and <= Maxima ? new Cantidad(valor) : null;
        return cantidad is not null;
    }

    public static Cantidad Crear(int valor) =>
        TryCrear(valor, out var cantidad) ? cantidad : throw new ArgumentOutOfRangeException(nameof(valor), "La cantidad debe estar entre 1 y 20.");
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

/// <summary>
/// El código de la sucursal que atiende un impreso (CAR-39): de 1 a 40 caracteres, sin espacios en los extremos ni caracteres de control. Que sea una sucursal registrada lo comprueba la Aplicación contra la lista del Inventario.
/// </summary>
public static class CodigosDeSucursal
{
    public const int LongitudMaxima = 40;

    public static bool EsValido(string? codigo) =>
        !string.IsNullOrEmpty(codigo) && codigo.Length <= LongitudMaxima && !codigo.Any(char.IsControl) && codigo.Trim() == codigo;
}

/// <summary>
/// Una línea del carrito: un libro impreso en la sucursal que lo atiende, o una edición digital (CAR-39, CAR-40). Tiene identidad propia (<see cref="LineaId"/>): puede haber varias líneas del mismo libro y sucursal (lotes). Un impreso
/// sin sucursal es una línea anterior a la venta por sucursal (CAR-45). <see cref="RetiroPendiente"/> la marca cuando Catálogo retiró la edición mientras había un checkout <c>reservando</c>, <c>en_pago</c> o <c>pago_pendiente</c>:
/// para la persona ya está retirada (no se puede modificar ni pagar) y el retiro se aplica al salir de ese estado (D21).
/// </summary>
public sealed class LineaDeCarrito
{
    private LineaDeCarrito(Guid lineaId, Guid edicionId, bool esDigital, string? codigoDeSucursal, Cantidad cantidad, DateTimeOffset agregadaEn, bool retiroPendiente)
    {
        LineaId = lineaId;
        EdicionId = edicionId;
        EsDigital = esDigital;
        CodigoDeSucursal = codigoDeSucursal;
        Cantidad = cantidad;
        AgregadaEn = agregadaEn;
        RetiroPendiente = retiroPendiente;
    }

    public Guid LineaId { get; }

    public Guid EdicionId { get; }

    public bool EsDigital { get; }

    /// <summary>La sucursal que atiende un impreso; nulo en una digital y en una línea anterior a la venta por sucursal.</summary>
    public string? CodigoDeSucursal { get; }

    public bool EsImpresoSinSucursal => !EsDigital && CodigoDeSucursal is null;

    public Cantidad Cantidad { get; }

    public DateTimeOffset AgregadaEn { get; }

    public bool RetiroPendiente { get; }

    public static LineaDeCarrito Crear(Guid lineaId, Guid edicionId, bool esDigital, string? codigoDeSucursal, Cantidad cantidad, DateTimeOffset agregadaEn, bool retiroPendiente = false)
    {
        ArgumentNullException.ThrowIfNull(cantidad);
        if (lineaId == Guid.Empty || edicionId == Guid.Empty)
        {
            throw new ArgumentException("La línea necesita su identificador y su edición.", nameof(lineaId));
        }

        if (codigoDeSucursal is not null && (esDigital || !CodigosDeSucursal.EsValido(codigoDeSucursal)))
        {
            throw new ArgumentException("La sucursal de la línea no es válida.", nameof(codigoDeSucursal));
        }

        return esDigital && cantidad.Valor != 1
            ? throw new ArgumentException("Una edición digital lleva siempre una unidad.", nameof(cantidad))
            : new LineaDeCarrito(lineaId, edicionId, esDigital, codigoDeSucursal, cantidad, agregadaEn, retiroPendiente);
    }

    internal LineaDeCarrito ConCantidad(Cantidad cantidad) => new(LineaId, EdicionId, EsDigital, CodigoDeSucursal, cantidad, AgregadaEn, RetiroPendiente);

    internal LineaDeCarrito ConRetiroPendiente() => new(LineaId, EdicionId, EsDigital, CodigoDeSucursal, Cantidad, AgregadaEn, retiroPendiente: true);
}

/// <summary>Los importes en soles (CAR-23, D17): <c>decimal</c> con dos decimales, redondeando <b>alejándose de cero</b> (el 5 siempre sube).</summary>
public static class Soles
{
    public const int Decimales = 2;

    public static decimal Redondear(decimal importe) => Math.Round(importe, Decimales, MidpointRounding.AwayFromZero);

    /// <summary>El importe de una línea: precio por cantidad a dos decimales.</summary>
    public static decimal Importe(decimal precioUnitario, int cantidad) => Redondear(precioUnitario * cantidad);
}
