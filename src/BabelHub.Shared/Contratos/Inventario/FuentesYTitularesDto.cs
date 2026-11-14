using System.Text.Json.Serialization;

namespace BabelHub.Shared.Contratos.Inventario;

/// <summary>Una sucursal como la ve cualquier visitante: código, nombre y región; sin contacto ni cifras de stock.</summary>
public sealed record SucursalPublicaDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("region")] string Region);

/// <summary>Las sucursales para elegir el recojo o la sucursal preferida (CAR-08, WEB-16).</summary>
public sealed record SucursalesDto(
    [property: JsonPropertyName("sucursales")] IReadOnlyList<SucursalPublicaDto> Sucursales);

/// <summary>Una fuente en un listado (FUE-01, FUE-11): el encargado solo ve la suya (FUE-08).</summary>
public sealed record FuenteDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("esFuenteDeRecepcion")] bool EsFuenteDeRecepcion);

/// <summary>Las fuentes visibles para quien consulta.</summary>
public sealed record ListaDeFuentesDto(
    [property: JsonPropertyName("fuentes")] IReadOnlyList<FuenteDto> Fuentes);

/// <summary>Una fuente con sus totales de stock, su encargado y su contacto de notificación (FUE-11); el contacto solo lo ve la administración central.</summary>
public sealed record FuenteDetalleDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("contactoDeNotificacion")] string? ContactoDeNotificacion,
    [property: JsonPropertyName("esFuenteDeRecepcion")] bool EsFuenteDeRecepcion,
    [property: JsonPropertyName("fisicoTotal")] int FisicoTotal,
    [property: JsonPropertyName("reservadoTotal")] int ReservadoTotal,
    [property: JsonPropertyName("vendibleTotal")] int VendibleTotal);

/// <summary>Registra una fuente nueva con stock cero (FUE-06): código único, nombre, región y contacto de notificación (FUE-01).</summary>
public sealed record RegistrarFuenteDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("contactoDeNotificacion")] string? ContactoDeNotificacion);

/// <summary>Edita los datos de una fuente; el código no cambia (FUE-16).</summary>
public sealed record EditarFuenteDto(
    [property: JsonPropertyName("nombre")] string Nombre,
    [property: JsonPropertyName("region")] string Region,
    [property: JsonPropertyName("contactoDeNotificacion")] string? ContactoDeNotificacion);

/// <summary>Designa la única fuente de recepción de mercadería nueva (FUE-19, FUE-20).</summary>
public sealed record DesignarRecepcionDto(
    [property: JsonPropertyName("codigo")] string Codigo);

/// <summary>Físico, reservado y vendible de una edición en una fuente, con su estado de stock (FUE-09, FUE-18).</summary>
public sealed record ExistenciaDto(
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("fisico")] int Fisico,
    [property: JsonPropertyName("reservado")] int Reservado,
    [property: JsonPropertyName("vendible")] int Vendible,
    [property: JsonPropertyName("estadoDeStock")] string EstadoDeStock);

/// <summary>Plazo del traslado de una fuente a otra, en días; <c>null</c> si la ruta no está definida (FUE-13).</summary>
public sealed record RutaDto(
    [property: JsonPropertyName("codigoDeOrigen")] string CodigoDeOrigen,
    [property: JsonPropertyName("codigoDeDestino")] string CodigoDeDestino,
    [property: JsonPropertyName("dias")] int? Dias);

/// <summary>Las rutas definidas por la administración central.</summary>
public sealed record ListaDeRutasDto(
    [property: JsonPropertyName("rutas")] IReadOnlyList<RutaDto> Rutas);

/// <summary>Define o cambia el plazo de una ruta: mayor que cero y entre dos fuentes distintas (FUE-12, FUE-17).</summary>
public sealed record DefinirRutaDto(
    [property: JsonPropertyName("dias")] int Dias);

/// <summary>Unidades vendidas de una fuente en los últimos 30 días (FUE-21).</summary>
public sealed record UnidadesDeFuenteDto(
    [property: JsonPropertyName("codigoDeFuente")] string CodigoDeFuente,
    [property: JsonPropertyName("unidades")] int Unidades);

/// <summary>Unidades vendidas en los últimos <c>dias</c> días: la fuente del encargado o, para la administración central, el total de la red con su desglose.</summary>
public sealed record UnidadesVendidasDto(
    [property: JsonPropertyName("dias")] int Dias,
    [property: JsonPropertyName("total")] int Total,
    [property: JsonPropertyName("porFuente")] IReadOnlyList<UnidadesDeFuenteDto> PorFuente);

/// <summary>Número de contacto de notificación de una sucursal, para que el E-commerce entregue los avisos (NOT, <c>transversal-notificaciones</c>). Nunca se registra.</summary>
public sealed record ContactoDeSucursalDto(
    [property: JsonPropertyName("codigo")] string Codigo,
    [property: JsonPropertyName("numero")] string Numero);

/// <summary>Pregunta cuántas reservas activas tiene un titular (IDE-27, SEL-33). El identificador viaja en el cuerpo, nunca en la ruta.</summary>
public sealed record ConsultaDePendientesDto(
    [property: JsonPropertyName("titularId")] Guid TitularId);

/// <summary>Reservas confirmadas o pendientes no vencidas del titular, sin datos de las ediciones (SEL-33).</summary>
public sealed record PendientesDeTitularDto(
    [property: JsonPropertyName("reservasActivas")] int ReservasActivas);

/// <summary>Baja de un titular de cuenta cancelada: el Inventario sustituye su identificador por uno anónimo estable (IDE-72, SEL-34, SEL-35).</summary>
public sealed record BajaDeTitularDto(
    [property: JsonPropertyName("titularId")] Guid TitularId);
