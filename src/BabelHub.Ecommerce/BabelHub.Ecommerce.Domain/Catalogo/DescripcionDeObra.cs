using System.Globalization;
using System.Text;

namespace BabelHub.Ecommerce.Domain.Catalogo;

/// <summary>Por qué se rechaza una descripción (CAT-01, CAT-25): el Dominio solo distingue el largo del resto; la Aplicación los traduce a su clave de validación.</summary>
public enum MotivoDeDescripcionInvalida
{
    DemasiadoLarga,

    /// <summary>Un carácter de control (salvo el salto de línea), de formato bidireccional, un separador de línea o de párrafo de Unicode o un sustituto suelto.</summary>
    CaracterProhibido,
}

/// <summary>
/// La descripción opcional de una obra (CAT-01, CAT-24, CAT-25, D18): texto plano de hasta 600 puntos de código tras recortar los espacios de los extremos y normalizar CR+LF a LF.
/// Vacía o de solo espacios equivale a no tenerla (<see langword="null"/>). Solo el salto de línea es un carácter de control admitido: se rechazan el tabulador, el CR suelto, los demás
/// controles (categoría Unicode Cc), los de formato bidireccional (U+202A a U+202E y U+2066 a U+2069), U+2028, U+2029 y los sustitutos UTF-16 sueltos (que PostgreSQL no puede guardar).
/// El tope se cuenta en puntos de código, como <c>char_length</c> de la base. Nunca guarda ni devuelve el texto rechazado en un mensaje.
/// </summary>
public static class DescripcionDeObra
{
    public const int LongitudMaxima = 600;

    /// <summary>Normaliza la descripción: <see langword="null"/> si no hay texto; si no, el texto limpio. <paramref name="motivo"/> es <see langword="null"/> si es válida.</summary>
    public static string? Normalizar(string? texto, out MotivoDeDescripcionInvalida? motivo)
    {
        motivo = null;
        if (texto is null)
        {
            return null;
        }

        var normalizado = texto.Replace("\r\n", "\n", StringComparison.Ordinal);

        // Se comprueba antes de recortar: un tabulador o un separador en un extremo se rechaza, no se pierde en silencio.
        if (TieneCaracterProhibido(normalizado))
        {
            return Rechazar(out motivo, MotivoDeDescripcionInvalida.CaracterProhibido);
        }

        var recortado = normalizado.Trim();
        if (recortado.Length == 0)
        {
            // Un texto de solo espacios y saltos de línea equivale a no tenerlo.
            return null;
        }

        return recortado.EnumerateRunes().Count() > LongitudMaxima ? Rechazar(out motivo, MotivoDeDescripcionInvalida.DemasiadoLarga) : recortado;
    }

    private static string? Rechazar(out MotivoDeDescripcionInvalida? motivo, MotivoDeDescripcionInvalida valor)
    {
        motivo = valor;
        return null;
    }

    private static bool TieneCaracterProhibido(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= texto.Length || !char.IsLowSurrogate(texto[i + 1]))
                {
                    return true;
                }

                i++;
                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                return true;
            }

            if (c == '\n')
            {
                continue;
            }

            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Control
                || c is (char)0x2028 or (char)0x2029
                || c is >= (char)0x202A and <= (char)0x202E
                || c is >= (char)0x2066 and <= (char)0x2069)
            {
                return true;
            }
        }

        return false;
    }
}
