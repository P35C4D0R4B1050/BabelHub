using System.Text.Json.Serialization;

namespace BabelHub.Shared.Contratos.Ecommerce;

/// <summary>
/// Solicitud de aviso de traslado que el Inventario entrega al E-commerce (<c>transversal-notificaciones</c> plan §4): <c>eventoId</c> con el formato
/// <c>traslado:&lt;id&gt;:&lt;estado&gt;</c>, la plantilla (solo de traslado), el código de la sucursal destinataria (su existencia se comprueba al enviar)
/// y los campos cerrados de la plantilla como texto; el catálogo de plantillas y de campos es del módulo Notificaciones, no de este contrato.
/// </summary>
public sealed record SolicitudDeAvisoDeInventarioDto(
    [property: JsonPropertyName("eventoId")] string EventoId,
    [property: JsonPropertyName("plantilla")] string Plantilla,
    [property: JsonPropertyName("codigoDeSucursal")] string CodigoDeSucursal,
    [property: JsonPropertyName("campos")] IReadOnlyDictionary<string, string> Campos);

/// <summary>
/// El estado de un traslado vinculado a un pedido, para que Pedidos mueva sus líneas (PED-19, TRA-11): en tránsito al despacharse, recibido al
/// llegar (el pedido puede prepararse) o cancelado. <c>eventoId</c> es determinista (<c>traslado:&lt;id&gt;:&lt;estado&gt;</c>) y repetirlo no cambia nada.
/// </summary>
public sealed record EstadoDeTrasladoDePedidoDto(
    [property: JsonPropertyName("eventoId")] string EventoId,
    [property: JsonPropertyName("trasladoId")] Guid TrasladoId,
    [property: JsonPropertyName("pedidoId")] Guid PedidoId,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("fechaEstimadaDeLlegada")] DateOnly? FechaEstimadaDeLlegada,
    [property: JsonPropertyName("ocurridoEn")] DateTimeOffset OcurridoEn);

/// <summary>Estados de traslado que el Inventario informa a Pedidos.</summary>
public static class EstadosDeTrasladoDePedido
{
    public const string EnTransito = "en-transito";

    public const string Recibido = "recibido";

    public const string Cancelado = "cancelado";

    public static readonly IReadOnlyList<string> Todos = [EnTransito, Recibido, Cancelado];
}

/// <summary>Códigos estables de error del E-commerce que interpreta el Inventario al entregar un aviso o un estado.</summary>
public static class CodigosDeErrorDelEcommerce
{
    public const string NoAutenticado = "ECO-NO-AUTENTICADO";

    public const string ValidacionInvalida = "ECO-VALIDACION-INVALIDA";

    /// <summary>El usuario autenticado no puede consultar (no es administrador central, o pide la sucursal de otro); queda como acceso denegado (AUD-05).</summary>
    public const string Prohibido = "ECO-PROHIBIDO";

    public const string LimiteExcedido = "ECO-LIMITE-EXCEDIDO";

    /// <summary>El pedido del estado de traslado no existe en el E-commerce: error permanente, no se reintenta.</summary>
    public const string NoEncontrado = "ECO-NO-ENCONTRADO";

    public const string ServicioNoDisponible = "ECO-SERVICIO-NO-DISPONIBLE";
}
