using System.Text.Json.Serialization;

namespace BabelHub.Shared.Contratos;

/// <summary>Una página de resultados: <c>?pagina=1&amp;tamano=20</c> con tope (<see cref="LimitesDeContrato.TamanoMaximoDePagina"/>) y el total.</summary>
public sealed record PaginaDto<T>(
    [property: JsonPropertyName("elementos")] IReadOnlyList<T> Elementos,
    [property: JsonPropertyName("pagina")] int Pagina,
    [property: JsonPropertyName("tamano")] int Tamano,
    [property: JsonPropertyName("total")] int Total);
