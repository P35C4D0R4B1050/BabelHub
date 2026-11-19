using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Application.Pagos;

/// <summary>El medio de pago que elige el cliente (PAG-03): modelo de la raíz del módulo que usa la Api, que no referencia el Dominio.</summary>
public enum MedioDePago
{
    Yape,
    Tarjeta,
}

/// <summary>
/// El identificador del medio de pago que entrega la pasarela (PAG-02): un token de un solo uso, nunca el número de la tarjeta. Vive solo en la solicitud de cobro y no se guarda: su
/// texto y su serialización están siempre redactados, de modo que ni un registro, ni una excepción, ni el re-render del formulario, ni <c>ECO-VALIDACION-INVALIDA</c> lo repiten. Solo
/// <see cref="Revelar"/> entrega el valor, a quien lo envía a la pasarela.
/// </summary>
[JsonConverter(typeof(ConvertidorDelTokenDelMedioDePago))]
public sealed partial class TokenDelMedioDePago
{
    public const string Redactado = "[redactado]";

    public const int LongitudMaxima = 200;

    private readonly string _valor;

    private TokenDelMedioDePago(string valor) => _valor = valor;

    /// <summary>Crea el token o lanza <see cref="ArgumentException"/> sin repetir el valor recibido.</summary>
    public static TokenDelMedioDePago Crear(string? valor) =>
        TryCrear(valor, out var token) ? token : throw new ArgumentException("El identificador del medio de pago no es válido.", nameof(valor));

    /// <summary>
    /// Un token tiene de 1 a 200 letras, dígitos, guion, guion bajo o punto, sin espacios, y no es solo dígitos: un número de tarjeta nunca se acepta como token.
    /// </summary>
    public static bool TryCrear(string? valor, [NotNullWhen(true)] out TokenDelMedioDePago? token)
    {
        token = null;
        if (string.IsNullOrWhiteSpace(valor) || valor.Length > LongitudMaxima || !CaracteresPermitidos().IsMatch(valor) || SoloDigitos().IsMatch(valor.Replace("-", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal).Replace("_", string.Empty, StringComparison.Ordinal)))
        {
            return false;
        }

        token = new TokenDelMedioDePago(valor);
        return true;
    }

    /// <summary>El valor, solo para quien lo envía a la pasarela.</summary>
    public string Revelar() => _valor;

    public override string ToString() => Redactado;

    [GeneratedRegex(@"\A[A-Za-z0-9_.-]+\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex CaracteresPermitidos();

    [GeneratedRegex(@"\A[0-9]+\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 250)]
    private static partial Regex SoloDigitos();
}

/// <summary>Lee el token de un JSON (para el enlace del modelo) y siempre lo escribe redactado.</summary>
public sealed class ConvertidorDelTokenDelMedioDePago : JsonConverter<TokenDelMedioDePago>
{
    public override TokenDelMedioDePago? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String && TokenDelMedioDePago.TryCrear(reader.GetString(), out var token)
            ? token
            : throw new JsonException("El identificador del medio de pago no es válido.");

    public override void Write(Utf8JsonWriter writer, TokenDelMedioDePago value, JsonSerializerOptions options) => writer.WriteStringValue(TokenDelMedioDePago.Redactado);
}
