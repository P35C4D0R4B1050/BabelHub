using System.Text.Json.Serialization;

namespace BabelHub.Ecommerce.Api.Identidad;

/// <summary>
/// Una clave de firma tal como la entregan los endpoints de administración de claves (identidad-seguridad T44; IDE-36, IDE-61): solo el identificador, el algoritmo y el estado. Nunca lleva la clave privada ni sus
/// parámetros; la clave pública se publica en <c>/.well-known/jwks.json</c>. Vive en la Api y no en <c>Shared</c> porque solo lo consume la administración desde el servidor (no es un contrato entre servicios).
/// </summary>
public sealed record ClaveDeFirmaDto(
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("algoritmo")] string Algoritmo,
    [property: JsonPropertyName("estado")] string Estado);

/// <summary>El cuerpo de <c>POST /api/ecommerce/v1/identidad/admin/claves/comprometida</c>: el <c>kid</c> de la clave que el administrador central declara comprometida (IDE-61).</summary>
public sealed record DeclararClaveComprometidaDto([property: JsonPropertyName("kid")] string Kid);
