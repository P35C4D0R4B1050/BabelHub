using System.Text.Json.Serialization;
using BabelHub.Shared.Errores;

namespace BabelHub.Shared.Contratos.Inventario;

/// <summary>Registra una edición en el Inventario con su formato (lo hace el Catálogo al dar de alta una edición; <c>PUT</c> idempotente).</summary>
public sealed record RegistrarEdicionDto(
    [property: JsonPropertyName("formato")] string Formato);

/// <summary>Vendible de una edición en una fuente (FUE-05, FUE-18); el físico y el reservado no salen del Inventario salvo a la sucursal dueña (FUE-08).</summary>
public sealed record FuenteConVendibleDto(
    [property: JsonPropertyName("codigoDeFuente")] string CodigoDeFuente,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("vendible")] int Vendible,
    [property: JsonPropertyName("estadoDeStock")] string EstadoDeStock);

/// <summary>Disponibilidad consolidada de la red y desglose por fuente (FUE-05).</summary>
public sealed record DisponibilidadDeEdicionDto(
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("vendibleEnLaRed")] int VendibleEnLaRed,
    [property: JsonPropertyName("fuentes")] IReadOnlyList<FuenteConVendibleDto> Fuentes,
    [property: JsonPropertyName("consultadaEn")] DateTimeOffset ConsultadaEn);

/// <summary>Consulta de la disponibilidad de varias ediciones a la vez (listados del catálogo); hasta <see cref="LimitesDeContrato.MaximoDeEdicionesPorConsulta"/>.</summary>
public sealed record ConsultaDeDisponibilidadDto(
    [property: JsonPropertyName("edicionIds")] IReadOnlyList<Guid> EdicionIds);

/// <summary>Disponibilidad de varias ediciones; las que el Inventario no conoce van aparte, sin fallar toda la consulta.</summary>
public sealed record DisponibilidadDeEdicionesDto(
    [property: JsonPropertyName("ediciones")] IReadOnlyList<DisponibilidadDeEdicionDto> Ediciones,
    [property: JsonPropertyName("noRegistradas")] IReadOnlyList<Guid> NoRegistradas);

/// <summary>Una línea impresa de un pedido o carrito: su identificador (el que asigna el E-commerce), la edición y la cantidad.</summary>
public sealed record LineaDeSolicitudDto(
    [property: JsonPropertyName("lineaId")] Guid LineaId,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("cantidad")] int Cantidad);

/// <summary>
/// Consulta previa de la selección de fuentes de un carrito, sin reservar (SEL-20). Recojo: <c>codigoDeSucursalDeRecojo</c>; domicilio:
/// <c>regionDeEntrega</c>, el identificador de región de la dirección en minúsculas y con guiones (el mismo de la fuente).
/// </summary>
public sealed record ConsultaPreviaDto(
    [property: JsonPropertyName("tipoDeEntrega")] string TipoDeEntrega,
    [property: JsonPropertyName("codigoDeSucursalDeRecojo")] string? CodigoDeSucursalDeRecojo,
    [property: JsonPropertyName("regionDeEntrega")] string? RegionDeEntrega,
    [property: JsonPropertyName("lineas")] IReadOnlyList<LineaDeSolicitudDto> Lineas);

/// <summary>Fuente que se seleccionaría para una línea, si implica traslado y su plazo en días (<c>null</c> es «a confirmar», FUE-13), o la falta de stock.</summary>
public sealed record SeleccionDeLineaDto(
    [property: JsonPropertyName("lineaId")] Guid LineaId,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("resultado")] string Resultado,
    [property: JsonPropertyName("codigoDeFuente")] string? CodigoDeFuente,
    [property: JsonPropertyName("requiereTraslado")] bool RequiereTraslado,
    [property: JsonPropertyName("plazoEstimadoDias")] int? PlazoEstimadoDias,
    [property: JsonPropertyName("disponibleEnLaRed")] int DisponibleEnLaRed);

/// <summary>Respuesta de la consulta previa: una entrada por línea impresa; las digitales se ignoran (SEL-20).</summary>
public sealed record ConsultaPreviaRespuestaDto(
    [property: JsonPropertyName("lineas")] IReadOnlyList<SeleccionDeLineaDto> Lineas,
    [property: JsonPropertyName("consultadaEn")] DateTimeOffset ConsultadaEn);

/// <summary>
/// Reserva de los impresos de un pedido (SEL-01): todas las líneas o ninguna. <c>pedidoId</c> es el identificador opaco que el E-commerce
/// asigna al iniciar el pago; repetirlo con una reserva pendiente la reemplaza (SEL-16). La expiración es de 15 minutos desde la creación.
/// </summary>
public sealed record CrearReservaDto(
    [property: JsonPropertyName("pedidoId")] Guid PedidoId,
    [property: JsonPropertyName("tipoDeEntrega")] string TipoDeEntrega,
    [property: JsonPropertyName("codigoDeSucursalDeRecojo")] string? CodigoDeSucursalDeRecojo,
    [property: JsonPropertyName("regionDeEntrega")] string? RegionDeEntrega,
    [property: JsonPropertyName("lineas")] IReadOnlyList<LineaDeSolicitudDto> Lineas);

/// <summary>Una línea reservada con la fuente asignada (nunca se divide entre fuentes, SEL-25).</summary>
public sealed record LineaDeReservaDto(
    [property: JsonPropertyName("lineaId")] Guid LineaId,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("codigoDeFuente")] string CodigoDeFuente,
    [property: JsonPropertyName("requiereTraslado")] bool RequiereTraslado,
    [property: JsonPropertyName("plazoEstimadoDias")] int? PlazoEstimadoDias);

/// <summary>Un traslado vinculado a un pedido; <c>fechaEstimadaDeLlegada</c> es <c>null</c> si la ruta no está definida (TRA-21).</summary>
public sealed record TrasladoVinculadoDto(
    [property: JsonPropertyName("trasladoId")] Guid TrasladoId,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("codigoDeOrigen")] string CodigoDeOrigen,
    [property: JsonPropertyName("codigoDeDestino")] string CodigoDeDestino,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("fechaEstimadaDeLlegada")] DateOnly? FechaEstimadaDeLlegada);

/// <summary>La reserva de un pedido; <c>traslados</c> solo trae contenido después de confirmarla (SEL-03).</summary>
public sealed record ReservaDto(
    [property: JsonPropertyName("pedidoId")] Guid PedidoId,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("creadaEn")] DateTimeOffset CreadaEn,
    [property: JsonPropertyName("expiraEn")] DateTimeOffset? ExpiraEn,
    [property: JsonPropertyName("lineas")] IReadOnlyList<LineaDeReservaDto> Lineas,
    [property: JsonPropertyName("traslados")] IReadOnlyList<TrasladoVinculadoDto> Traslados);

/// <summary>Resultado de extender la reserva por un pago pendiente (SEL-22): <c>extendida</c> es falso si ya se extendió o la reserva ya no está pendiente (SEL-30).</summary>
public sealed record ExtensionDeReservaDto(
    [property: JsonPropertyName("extendida")] bool Extendida,
    [property: JsonPropertyName("reserva")] ReservaDto Reserva);

/// <summary>Libera la reserva de un pedido o solo algunas líneas (<c>lineaIds</c> vacío o ausente son todas). Repetirla no cambia el stock (SEL-07).</summary>
public sealed record LiberarReservaDto(
    [property: JsonPropertyName("motivo")] string Motivo,
    [property: JsonPropertyName("lineaIds")] IReadOnlyList<Guid>? LineaIds);

/// <summary>Descuenta el físico de las líneas indicadas y cierra sus reservas confirmadas (SEL-11, SEL-21); <c>envioId</c> solo en el reparto (ENV-12).</summary>
public sealed record CerrarReservaDto(
    [property: JsonPropertyName("motivo")] string Motivo,
    [property: JsonPropertyName("lineaIds")] IReadOnlyList<Guid> LineaIds,
    [property: JsonPropertyName("envioId")] Guid? EnvioId);

/// <summary>Resultado de cerrar una línea: <c>cerrada</c> ahora o <c>ya-cerrada</c> (el descuento ya estaba aplicado y no se repite).</summary>
public sealed record ResultadoDeCierreDto(
    [property: JsonPropertyName("lineaId")] Guid LineaId,
    [property: JsonPropertyName("resultado")] string Resultado);

/// <summary>Resultado del cierre de las líneas de una reserva.</summary>
public sealed record CierreDeReservaDto(
    [property: JsonPropertyName("pedidoId")] Guid PedidoId,
    [property: JsonPropertyName("lineas")] IReadOnlyList<ResultadoDeCierreDto> Lineas);

/// <summary>Una línea sin stock suficiente y lo máximo que hay en la red (CAR-04, CAR-27).</summary>
public sealed record LineaSinStockDto(
    [property: JsonPropertyName("lineaId")] Guid LineaId,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("solicitada")] int Solicitada,
    [property: JsonPropertyName("disponibleEnLaRed")] int DisponibleEnLaRed);

/// <summary>Cuerpo del <c>409</c> con el código <c>INV-STOCK-INSUFICIENTE</c>: qué líneas no tienen stock; no se reservó ninguna.</summary>
public sealed record ProblemaDeStockDto : ProblemaDto
{
    [JsonPropertyName("lineas")]
    public IReadOnlyList<LineaSinStockDto> Lineas { get; init; } = [];
}

/// <summary>Resultados posibles de una línea en la consulta previa.</summary>
public static class ResultadosDeSeleccion
{
    public const string Disponible = "disponible";

    public const string SinStock = "sin-stock";

    public static readonly IReadOnlyList<string> Todos = [Disponible, SinStock];
}

/// <summary>Resultados de cerrar una línea.</summary>
public static class ResultadosDeCierre
{
    public const string Cerrada = "cerrada";

    public const string YaCerrada = "ya-cerrada";

    public static readonly IReadOnlyList<string> Todos = [Cerrada, YaCerrada];
}
