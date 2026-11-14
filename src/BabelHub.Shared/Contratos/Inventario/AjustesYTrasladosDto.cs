using System.Text.Json.Serialization;
using BabelHub.Shared.Errores;

namespace BabelHub.Shared.Contratos.Inventario;

/// <summary>
/// Un ajuste manual (AJU-01 a AJU-09): merma o daño con la <c>cantidad</c> a descontar (solo restan, AJU-12), o corrección con el <c>conteoReal</c>.
/// El ingreso de mercadería y el reingreso por devolución tienen su propia operación.
/// </summary>
public sealed record RegistrarAjusteDto(
    [property: JsonPropertyName("codigoDeFuente")] string CodigoDeFuente,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("motivo")] string Motivo,
    [property: JsonPropertyName("cantidad")] int? Cantidad,
    [property: JsonPropertyName("conteoReal")] int? ConteoReal,
    [property: JsonPropertyName("nota")] string? Nota);

/// <summary>Un ajuste ya aplicado: quién, cuándo, con qué motivo y el físico anterior y posterior (AJU-01, AJU-17).</summary>
public sealed record AjusteRegistradoDto(
    [property: JsonPropertyName("ajusteId")] Guid AjusteId,
    [property: JsonPropertyName("codigoDeFuente")] string CodigoDeFuente,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("motivo")] string Motivo,
    [property: JsonPropertyName("usuarioId")] Guid? UsuarioId,
    [property: JsonPropertyName("instante")] DateTimeOffset Instante,
    [property: JsonPropertyName("fisicoAnterior")] int FisicoAnterior,
    [property: JsonPropertyName("fisicoPosterior")] int FisicoPosterior,
    [property: JsonPropertyName("nota")] string? Nota,
    [property: JsonPropertyName("referencia")] string? Referencia);

/// <summary>Cuerpo del <c>422</c> <c>INV-CONTEO-MENOR-QUE-RESERVAS</c>: los pedidos que reservan las unidades (AJU-06).</summary>
public sealed record ProblemaDeConteoDto : ProblemaDto
{
    [JsonPropertyName("pedidosQueReservan")]
    public IReadOnlyList<Guid> PedidosQueReservan { get; init; } = [];
}

/// <summary>
/// Ingreso de mercadería (AJU-13, CAT-20, CAT-22): solo la administración central; <c>codigoDeFuente</c> ausente es la fuente de recepción y otra se
/// rechaza (AJU-19); la cantidad es un entero entre 1 y 5 000.
/// </summary>
public sealed record RegistrarIngresoDto(
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("codigoDeFuente")] string? CodigoDeFuente,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("referencia")] string? Referencia,
    [property: JsonPropertyName("nota")] string? Nota);

/// <summary>Un ingreso de mercadería registrado, con su fecha y su usuario (AJU-20).</summary>
public sealed record IngresoDeMercaderiaDto(
    [property: JsonPropertyName("ingresoId")] Guid IngresoId,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("codigoDeFuente")] string CodigoDeFuente,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("referencia")] string? Referencia,
    [property: JsonPropertyName("nota")] string? Nota,
    [property: JsonPropertyName("usuarioId")] Guid? UsuarioId,
    [property: JsonPropertyName("instante")] DateTimeOffset Instante);

/// <summary>Los últimos ingresos de mercadería, del más reciente al más antiguo.</summary>
public sealed record ListaDeIngresosDto(
    [property: JsonPropertyName("ingresos")] IReadOnlyList<IngresoDeMercaderiaDto> Ingresos);

/// <summary>Una línea de una devolución aceptada.</summary>
public sealed record LineaDeReingresoDto(
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("cantidad")] int Cantidad);

/// <summary>Reingreso de los impresos de una devolución aceptada a la sucursal receptora (AJU-11, DEV-07); la devolución de la ruta identifica el reingreso y repetirlo no suma otra vez (AJU-16).</summary>
public sealed record ReingresarDevolucionDto(
    [property: JsonPropertyName("codigoDeFuente")] string CodigoDeFuente,
    [property: JsonPropertyName("lineas")] IReadOnlyList<LineaDeReingresoDto> Lineas);

/// <summary>Resultado del reingreso: <c>yaAplicado</c> es verdadero si esa devolución ya se reingresó y no se sumó nada.</summary>
public sealed record ReingresoDeDevolucionDto(
    [property: JsonPropertyName("devolucionId")] Guid DevolucionId,
    [property: JsonPropertyName("yaAplicado")] bool YaAplicado,
    [property: JsonPropertyName("ajustes")] IReadOnlyList<AjusteRegistradoDto> Ajustes);

/// <summary>Solicitud de traslado de reposición entre dos fuentes (TRA-01); los vinculados a un pedido nacen de la confirmación de la reserva (SEL-03).</summary>
public sealed record SolicitarTrasladoDto(
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("codigoDeOrigen")] string CodigoDeOrigen,
    [property: JsonPropertyName("codigoDeDestino")] string CodigoDeDestino,
    [property: JsonPropertyName("motivo")] string Motivo);

/// <summary>Un traslado con su estado, su vínculo con un pedido, su guía y su fecha estimada (TRA-01 a TRA-21).</summary>
public sealed record TrasladoDto(
    [property: JsonPropertyName("trasladoId")] Guid TrasladoId,
    [property: JsonPropertyName("edicionId")] Guid EdicionId,
    [property: JsonPropertyName("cantidad")] int Cantidad,
    [property: JsonPropertyName("codigoDeOrigen")] string CodigoDeOrigen,
    [property: JsonPropertyName("codigoDeDestino")] string CodigoDeDestino,
    [property: JsonPropertyName("estado")] string Estado,
    [property: JsonPropertyName("motivo")] string? Motivo,
    [property: JsonPropertyName("motivoDeCierre")] string? MotivoDeCierre,
    [property: JsonPropertyName("pedidoId")] Guid? PedidoId,
    [property: JsonPropertyName("pedidoAnulado")] bool PedidoAnulado,
    [property: JsonPropertyName("guia")] string? Guia,
    [property: JsonPropertyName("solicitadoEn")] DateTimeOffset SolicitadoEn,
    [property: JsonPropertyName("fechaEstimadaDeLlegada")] DateOnly? FechaEstimadaDeLlegada);

/// <summary>El motivo de rechazar o cancelar un traslado (TRA-05, TRA-18).</summary>
public sealed record MotivoDeTrasladoDto(
    [property: JsonPropertyName("motivo")] string Motivo);

/// <summary>La guía de un traslado aprobado o en tránsito, que no cambia su estado (TRA-12).</summary>
public sealed record RegistrarGuiaDto(
    [property: JsonPropertyName("guia")] string Guia);

/// <summary>Los ajustes y traslados de la sucursal del token, paginados con los mismos <c>pagina</c> y <c>tamano</c> (AJU-17).</summary>
public sealed record HistorialDeSucursalDto(
    [property: JsonPropertyName("ajustes")] PaginaDto<AjusteRegistradoDto> Ajustes,
    [property: JsonPropertyName("traslados")] PaginaDto<TrasladoDto> Traslados);

/// <summary>Motivos de un ajuste de stock (AJU-02); los tres manuales son los que acepta <c>POST /ajustes</c>.</summary>
public static class MotivosDeAjuste
{
    public const string Merma = "merma";

    public const string Danio = "danio";

    public const string CorreccionDeConteo = "correccion-de-conteo";

    public const string IngresoDeMercaderia = "ingreso-de-mercaderia";

    public const string Devolucion = "devolucion";

    public static readonly IReadOnlyList<string> Todos = [Merma, Danio, CorreccionDeConteo, IngresoDeMercaderia, Devolucion];

    public static readonly IReadOnlyList<string> Manuales = [Merma, Danio, CorreccionDeConteo];
}

/// <summary>Ciclo de vida de un traslado (TRA-03): solo estas transiciones.</summary>
public static class EstadosDeTraslado
{
    public const string Solicitado = "solicitado";

    public const string Aprobado = "aprobado";

    public const string EnTransito = "en-transito";

    public const string Recibido = "recibido";

    public const string Rechazado = "rechazado";

    public const string Cancelado = "cancelado";

    public static readonly IReadOnlyList<string> Todos = [Solicitado, Aprobado, EnTransito, Recibido, Rechazado, Cancelado];
}
