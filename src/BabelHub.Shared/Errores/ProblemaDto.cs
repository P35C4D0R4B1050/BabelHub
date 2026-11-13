using System.Text.Json.Serialization;

namespace BabelHub.Shared.Errores;

/// <summary>Un campo de la solicitud que no cumple su regla: su nombre y una clave simbólica (nunca el valor recibido).</summary>
public sealed record ErrorDeCampoDto(
    [property: JsonPropertyName("campo")] string Campo,
    [property: JsonPropertyName("clave")] string Clave);

/// <summary>
/// Error uniforme de los dos servicios (SEG-03), en <c>application/problem+json</c>: un código estable, el identificador de
/// seguimiento y, si aplica, los campos inválidos. Nunca lleva trazas ni datos internos.
/// </summary>
public record ProblemaDto
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "about:blank";

    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public int Status { get; init; }

    [JsonPropertyName("detail")]
    public string Detail { get; init; } = string.Empty;

    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("errores")]
    public IReadOnlyList<ErrorDeCampoDto> Errores { get; init; } = [];
}

/// <summary>
/// Respuesta única de un límite excedido (SEG-07): el código estable, el motivo (la clave de la política, nunca datos de cuentas), la
/// espera sugerida en segundos y el instante de nueva disponibilidad.
/// </summary>
public sealed record RespuestaDeLimiteDto : ProblemaDto
{
    [JsonPropertyName("motivo")]
    public string Motivo { get; init; } = string.Empty;

    [JsonPropertyName("retryAfterSeconds")]
    public int RetryAfterSeconds { get; init; }

    [JsonPropertyName("disponibleDesde")]
    public DateTimeOffset DisponibleDesde { get; init; }
}
