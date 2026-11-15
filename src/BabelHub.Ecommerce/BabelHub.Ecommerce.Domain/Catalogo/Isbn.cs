using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace BabelHub.Ecommerce.Domain.Catalogo;

/// <summary>
/// El ISBN de una edición (CAT-02, CAT-17, D02): se normaliza (guiones y espacios fuera), debe tener 10 o 13 dígitos con un dígito de control válido
/// (la <c>X</c> del ISBN-10 vale 10) y, si es de 13, el prefijo 978 o 979. Se guarda <b>canonicalizado a ISBN-13</b> (un ISBN-10 se convierte), de modo que un
/// ISBN-10 y su ISBN-13 son la misma edición, junto al texto como lo escribió la persona. La igualdad es por el ISBN-13: es lo que garantiza el índice único.
/// </summary>
public sealed class Isbn : IEquatable<Isbn>
{
    /// <summary>El tope del texto crudo: un ISBN con guiones cabe de sobra y no se acepta un relleno arbitrario.</summary>
    public const int LongitudMaximaDelTexto = 40;

    private Isbn(string isbn13, string textoIngresado)
    {
        Isbn13 = isbn13;
        TextoIngresado = textoIngresado;
    }

    /// <summary>Los 13 dígitos, sin guiones: la clave única del catálogo.</summary>
    public string Isbn13 { get; }

    /// <summary>El texto como lo escribió la persona (sin espacios en los extremos).</summary>
    public string TextoIngresado { get; }

    public static bool TryCrear(string? texto, [NotNullWhen(true)] out Isbn? isbn)
    {
        isbn = null;
        if (string.IsNullOrWhiteSpace(texto) || texto.Length > LongitudMaximaDelTexto)
        {
            return false;
        }

        var ingresado = texto.Trim();
        var limpio = new char[ingresado.Length];
        var n = 0;
        foreach (var c in ingresado)
        {
            if (c is '-' or ' ')
            {
                continue;
            }

            limpio[n++] = c;
        }

        var caracteres = limpio.AsSpan(0, n);
        string? trece = caracteres.Length switch
        {
            13 => Normalizar13(caracteres),
            10 => Convertir10(caracteres),
            _ => null,
        };

        if (trece is null)
        {
            return false;
        }

        isbn = new Isbn(trece, ingresado);
        return true;
    }

    public static Isbn Crear(string? texto) =>
        TryCrear(texto, out var isbn) ? isbn : throw new ArgumentException("El ISBN no tiene 10 o 13 dígitos con un dígito de control válido.", nameof(texto));

    public bool Equals(Isbn? other) => other is not null && string.Equals(Isbn13, other.Isbn13, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as Isbn);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Isbn13);

    public override string ToString() => Isbn13;

    private static string? Normalizar13(ReadOnlySpan<char> c)
    {
        foreach (var caracter in c)
        {
            if (!char.IsAsciiDigit(caracter))
            {
                return null;
            }
        }

        if (!c.StartsWith("978", StringComparison.Ordinal) && !c.StartsWith("979", StringComparison.Ordinal))
        {
            return null;
        }

        return DigitoDeControl13(c[..12]) == c[12] - '0' ? new string(c) : null;
    }

    private static string? Convertir10(ReadOnlySpan<char> c)
    {
        var suma = 0;
        for (var i = 0; i < 10; i++)
        {
            int valor;
            if (c[i] is 'X' or 'x')
            {
                if (i != 9)
                {
                    return null;
                }

                valor = 10;
            }
            else if (char.IsAsciiDigit(c[i]))
            {
                valor = c[i] - '0';
            }
            else
            {
                return null;
            }

            suma += (10 - i) * valor;
        }

        if (suma % 11 != 0)
        {
            return null;
        }

        Span<char> trece = stackalloc char[13];
        "978".CopyTo(trece);
        c[..9].CopyTo(trece[3..]);
        trece[12] = (char)('0' + DigitoDeControl13(trece[..12]));
        return new string(trece);
    }

    private static int DigitoDeControl13(ReadOnlySpan<char> doce)
    {
        var suma = 0;
        for (var i = 0; i < 12; i++)
        {
            suma += (doce[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (10 - (suma % 10)) % 10;
    }
}

/// <summary>El precio de una edición en soles (D09): no negativo, con a lo más dos decimales y dentro de <c>numeric(12,2)</c>. El cero es válido: la spec no impone «mayor que cero» ni un máximo propio.</summary>
public sealed class Precio : IEquatable<Precio>
{
    /// <summary>El mayor importe que cabe en <c>numeric(12,2)</c>.</summary>
    public const decimal Maximo = 9_999_999_999.99m;

    private Precio(decimal valor) => Valor = decimal.Round(valor, 2);

    public decimal Valor { get; }

    public static bool TryCrear(decimal valor, [NotNullWhen(true)] out Precio? precio)
    {
        precio = null;
        if (valor < 0 || valor > Maximo || decimal.Round(valor, 2) != valor)
        {
            return false;
        }

        precio = new Precio(valor);
        return true;
    }

    public static Precio Crear(decimal valor) =>
        TryCrear(valor, out var precio) ? precio : throw new ArgumentException("El precio es un importe en soles, no negativo, con a lo más dos decimales.", nameof(valor));

    /// <summary>El valor con dos decimales y punto decimal (para la auditoría y el almacenamiento, independiente de la cultura).</summary>
    public string ATexto() => Valor.ToString("0.00", CultureInfo.InvariantCulture);

    public bool Equals(Precio? other) => other is not null && Valor == other.Valor;

    public override bool Equals(object? obj) => Equals(obj as Precio);

    public override int GetHashCode() => Valor.GetHashCode();

    public override string ToString() => ATexto();
}
