using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Valor que nunca debe llegar a un registro, a una auditoría ni a un aviso (contraseña, código de un solo uso, clave): su
/// representación en texto siempre está redactada y solo <see cref="Revelar"/> entrega el valor, a quien lo necesita de verdad.
/// </summary>
public sealed class ValorSecreto
{
    public const string Redactado = "[redactado]";

    private readonly string _valor;

    public ValorSecreto(string valor)
    {
        ArgumentException.ThrowIfNullOrEmpty(valor);
        _valor = valor;
    }

    public string Revelar() => _valor;

    public override string ToString() => Redactado;

    public override bool Equals(object? obj) => obj is ValorSecreto otro && string.Equals(_valor, otro._valor, StringComparison.Ordinal);

    public override int GetHashCode() => _valor.GetHashCode(StringComparison.Ordinal);
}

/// <summary>
/// Detecta y redacta los datos que SEG-10 prohíbe en los registros técnicos, de auditoría y de seguridad y en los avisos por
/// WhatsApp: contraseñas, credenciales, identificadores de sesión, códigos de un solo uso, secretos, datos de tarjeta y
/// enlaces firmados. Es conservador: ante la duda, redacta.
/// </summary>
public static partial class RedactorDeDatosProhibidos
{
    private static readonly TimeSpan Espera = TimeSpan.FromMilliseconds(250);

    private static readonly HashSet<string> NombresSensibles = new(StringComparer.OrdinalIgnoreCase)
    {
        "authorization",
        "cookie",
        "set-cookie",
        "x-credencial-de-servicio",
        "x-credencial-de-origen",
        "x-verificacion-de-persona",
        "x-origen-reenviado",
        "cf-turnstile-response",
    };

    /// <summary>Devuelve el texto con los datos prohibidos reemplazados por «[redactado]».</summary>
    public static string Redactar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return texto ?? string.Empty;
        }

        try
        {
            var resultado = ParDeClaveYValor().Replace(texto, evaluador => $"{evaluador.Groups["clave"].Value}{evaluador.Groups["sep"].Value}{ValorSecreto.Redactado}");
            resultado = Bearer().Replace(resultado, "Bearer " + ValorSecreto.Redactado);
            resultado = Jwt().Replace(resultado, ValorSecreto.Redactado);
            return PosibleTarjeta().Replace(resultado, evaluador => EsTarjeta(evaluador.Value) ? ValorSecreto.Redactado : evaluador.Value);
        }
        catch (RegexMatchTimeoutException)
        {
            // Ante un texto que no se puede analizar a tiempo, no se arriesga nada: se descarta completo.
            return ValorSecreto.Redactado;
        }
    }

    public static bool ContieneDatosProhibidos(string? texto) =>
        !string.IsNullOrEmpty(texto) && !string.Equals(Redactar(texto), texto, StringComparison.Ordinal);

    /// <summary>Si el nombre de una cabecera, un campo o un parámetro es de un valor que nunca se registra.</summary>
    public static bool EsNombreSensible(string? nombre) =>
        !string.IsNullOrWhiteSpace(nombre)
        && (NombresSensibles.Contains(nombre.Trim()) || NombreSensible().IsMatch(nombre.Trim()));

    /// <summary>Pares nombre-valor listos para registrar: el valor de un nombre sensible se oculta y los demás se redactan.</summary>
    public static IReadOnlyList<KeyValuePair<string, string>> RedactarPares(IEnumerable<KeyValuePair<string, string?>> pares)
    {
        ArgumentNullException.ThrowIfNull(pares);
        return pares
            .Select(p => new KeyValuePair<string, string>(p.Key, EsNombreSensible(p.Key) ? ValorSecreto.Redactado : Redactar(p.Value)))
            .ToList();
    }

    private static bool EsTarjeta(string candidato)
    {
        var digitos = candidato.Where(char.IsDigit).ToArray();
        if (digitos.Length is < 13 or > 19)
        {
            return false;
        }

        var suma = 0;
        var doble = false;
        for (var i = digitos.Length - 1; i >= 0; i--)
        {
            var valor = digitos[i] - '0';
            if (doble)
            {
                valor *= 2;
                if (valor > 9)
                {
                    valor -= 9;
                }
            }

            suma += valor;
            doble = !doble;
        }

        return suma % 10 == 0;
    }

    // «clave: valor», «clave=valor» o «"clave":"valor"» donde la clave nombra un dato prohibido (incluye parámetros de un enlace firmado).
    [GeneratedRegex(
        """(?<clave>\b[\w-]*(?:password|passwd|contrase[ñn]a|secret|secreto|token|signature|firma|(?:api|signing|private|encryption|hmac|access|master)[_-]?key|credential|credencial|cookie|session|sesion|authorization|verificacion-de-persona|origen-reenviado)[\w-]*|\b(?:pwd|sid|sig|otp|codigo|code|cvv|cvc|clave)\b)(?<sep>"?\s*[:=]\s*"?)(?:(?:Bearer|Basic)\s+)?[^\s,;&"'}\]]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex ParDeClaveYValor();

    [GeneratedRegex(@"\bBearer\s+[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 250)]
    private static partial Regex Bearer();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]{5,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant, 250)]
    private static partial Regex Jwt();

    [GeneratedRegex(@"(?<![\d])(?:\d[ -]?){12,18}\d(?![\d])", RegexOptions.CultureInvariant, 250)]
    private static partial Regex PosibleTarjeta();

    [GeneratedRegex(
        @"^(?:[\w-]*(?:password|passwd|contrase[ñn]a|secret|secreto|token|signature|firma|(?:api|signing|private|encryption|hmac|access|master)[_-]?key|credential|credencial|cookie|session|sesion|authorization)[\w-]*|pwd|sid|sig|otp|codigo|code|cvv|cvc|clave)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        250)]
    private static partial Regex NombreSensible();
}
