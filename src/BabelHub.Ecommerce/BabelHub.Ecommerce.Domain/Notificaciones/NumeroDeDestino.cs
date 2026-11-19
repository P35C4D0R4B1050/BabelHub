using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Notificaciones;

/// <summary>
/// El número de teléfono de destino normalizado a <b>E.164</b> (NOT-10, NOT-13, NOT-14): sin espacios, guiones ni paréntesis, con los dígitos Unicode llevados a ASCII, el prefijo internacional <c>00</c> convertido en <c>+</c>, el
/// <c>0</c> inicial nacional quitado y <b>+51</b> como país por defecto. Así el mismo número escrito de cualquier forma tiene la misma huella (la desactivación y el tope no se eluden cambiando el formato). Nunca se guarda
/// en claro ni se imprime.
/// </summary>
public readonly partial record struct NumeroDeDestino
{
    public const string CodigoDePaisPorDefecto = "+51";

    private NumeroDeDestino(string e164) => E164 = e164;

    public string E164 { get; }

    /// <summary>No imprime el número.</summary>
    public override string ToString() => E164 is null ? "[sin número]" : "[número]";

    public static bool TryNormalizar(string? texto, out NumeroDeDestino numero)
    {
        numero = default;
        if (string.IsNullOrWhiteSpace(texto) || texto.Length > 40)
        {
            return false;
        }

        var limpio = new StringBuilder(texto.Length);
        foreach (var caracter in texto.Trim())
        {
            if (caracter is ' ' or '-' or '(' or ')' or '.' or ' ' or '‑')
            {
                continue;
            }

            if (caracter == '+' && limpio.Length == 0)
            {
                limpio.Append('+');
                continue;
            }

            // Un dígito de cualquier escritura (árabe-índico, de ancho completo…) vale su cifra ASCII.
            if (char.IsDigit(caracter) && CharUnicodeInfo.GetDecimalDigitValue(caracter) is >= 0 and var cifra)
            {
                limpio.Append((char)('0' + cifra));
                continue;
            }

            return false;
        }

        var candidato = limpio.ToString();
        if (candidato.StartsWith("00", StringComparison.Ordinal))
        {
            candidato = "+" + candidato[2..];
        }
        else if (!candidato.StartsWith('+'))
        {
            // Un número nacional: el 0 inicial es el prefijo troncal y se quita; se le antepone el país por defecto.
            candidato = CodigoDePaisPorDefecto + candidato.TrimStart('0');
        }

        if (!FormatoE164().IsMatch(candidato))
        {
            return false;
        }

        numero = new NumeroDeDestino(candidato);
        return true;
    }

    [GeneratedRegex("^\\+[1-9][0-9]{7,14}\\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex FormatoE164();
}
