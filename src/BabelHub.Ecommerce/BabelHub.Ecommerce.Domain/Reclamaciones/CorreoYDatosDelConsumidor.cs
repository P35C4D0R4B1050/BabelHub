using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Reclamaciones;

/// <summary>
/// El correo en su forma normalizada (D03): recortado y en minúsculas, sin resolver alias ni subdirecciones. Con ella se compara el correo de la hoja con el de la sesión. Un correo válido
/// es <b>una sola dirección</b> de hasta 254 caracteres, sin saltos de línea, listas ni nombre para mostrar.
/// </summary>
public static partial class CorreoNormalizado
{
    public const int LongitudMaxima = 254;

    private const int LongitudMaximaDeLaParteLocal = 64;

    public static string Normalizar(string correo)
    {
        ArgumentNullException.ThrowIfNull(correo);
        return correo.Trim().ToLowerInvariant();
    }

    public static bool TryCrear(string? correo, [NotNullWhen(true)] out string? normalizado)
    {
        normalizado = null;
        if (string.IsNullOrWhiteSpace(correo))
        {
            return false;
        }

        var recortado = correo.Trim();
        if (recortado.Length > LongitudMaxima || !DireccionUnica().IsMatch(recortado))
        {
            return false;
        }

        if (recortado.IndexOf('@', StringComparison.Ordinal) > LongitudMaximaDeLaParteLocal)
        {
            return false;
        }

        normalizado = recortado.ToLowerInvariant();
        return true;
    }

    // Una dirección: parte local de caracteres comunes, arroba, y un dominio de etiquetas de hasta 63 caracteres con al menos un punto y una terminación de letras.
    [GeneratedRegex(
        @"\A[A-Za-z0-9!#$&'*+=^_`~-]+(?:\.[A-Za-z0-9!#$&'*+=^_`~-]+)*@(?:[A-Za-z0-9](?:[A-Za-z0-9-]{0,61}[A-Za-z0-9])?\.)+[A-Za-z]{2,24}\z",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 250)]
    private static partial Regex DireccionUnica();
}

/// <summary>
/// Los datos personales que el consumidor escribe en una hoja (REC-03): nombre, documento de identidad, domicilio y correo. Es una clase y no un <c>record</c> para que su texto y su
/// serialización estén siempre redactados: ningún registro, error ni auditoría los repite (RNF07, RNF23).
/// </summary>
[JsonConverter(typeof(ConvertidorDeDatosDelConsumidor))]
public sealed class DatosDelConsumidor
{
    public const string ValorAnonimo = "[anonimizado]";

    public DatosDelConsumidor(string nombre, string documento, string domicilio, string correo)
    {
        Nombre = nombre?.Trim() ?? throw new ArgumentNullException(nameof(nombre));
        Documento = documento?.Trim() ?? throw new ArgumentNullException(nameof(documento));
        Domicilio = domicilio?.Trim() ?? throw new ArgumentNullException(nameof(domicilio));
        Correo = correo?.Trim() ?? throw new ArgumentNullException(nameof(correo));
    }

    public string Nombre { get; }

    public string Documento { get; }

    public string Domicilio { get; }

    /// <summary>El correo de contacto como se escribió, para enviar la respuesta.</summary>
    public string Correo { get; }

    /// <summary>El correo normalizado para coincidir con la sesión; nulo si ya no es una dirección (hoja anonimizada).</summary>
    public string? CorreoNormalizado => Reclamaciones.CorreoNormalizado.TryCrear(Correo, out var normalizado) ? normalizado : null;

    /// <summary>Los valores fijos de una hoja anonimizada a los dos años (REC-17): el correo no es una dirección válida.</summary>
    public static DatosDelConsumidor Anonimizados() => new(ValorAnonimo, ValorAnonimo, ValorAnonimo, ValorAnonimo);

    public override string ToString() => "[redactado]";
}

/// <summary>Escribe los datos del consumidor siempre redactados; no se leen de un JSON.</summary>
public sealed class ConvertidorDeDatosDelConsumidor : JsonConverter<DatosDelConsumidor>
{
    public override DatosDelConsumidor? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        throw new JsonException("Los datos del consumidor no se leen de un JSON.");

    public override void Write(Utf8JsonWriter writer, DatosDelConsumidor value, JsonSerializerOptions options) => writer.WriteStringValue("[redactado]");
}
