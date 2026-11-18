using System.Text.Json.Serialization;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.Inventario.Api.Traslados;

/// <summary>
/// Una fuente que puede ser el origen de un traslado de una edición (TRA-02): solo su código y su nombre, <b>sin cantidades</b> (un encargado no averigua cuánto tiene cada sucursal). Operación que <c>inventario-traslados</c> pide a
/// ECO-0 (<c>GET /ediciones/{edicionId}/origenes-posibles?codigoDeDestino=</c>, nombre <c>inventario.traslados.origenes-posibles</c>): mientras ECO-0 no la defina, el DTO vive en la Api del Inventario como supuesto de contrato, no en
/// <c>BabelHub.Shared</c> (una prueba de arquitectura exige que todo DTO de <c>Shared</c> pertenezca a una operación del catálogo).
/// </summary>
public sealed record OrigenPosibleDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("nombre")] string Nombre);

/// <summary>Los orígenes posibles de una solicitud de traslado, los de ruta más corta primero.</summary>
public sealed record OrigenesPosiblesDto(
    [property: JsonPropertyName("origenes")] IReadOnlyList<OrigenPosibleDto> Origenes);

/// <summary>
/// Los traslados vinculados a un pedido (TRA-11), del más reciente al más antiguo. Operación que <c>inventario-traslados</c> pide a ECO-0 (<c>GET /pedidos/{pedidoId}/traslados</c>, nombre <c>inventario.traslados.de-un-pedido</c>): el
/// DTO vive en la Api del Inventario como supuesto de contrato mientras tanto.
/// </summary>
public sealed record TrasladosDeUnPedidoDto(
    [property: JsonPropertyName("traslados")] IReadOnlyList<TrasladoDto> Traslados);
