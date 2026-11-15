using System.Text.Json.Serialization;

namespace BabelHub.Shared.Contratos.Auditoria;

/// <summary>
/// Un registro de auditoría o de seguridad tal como lo entrega la consulta (T21; AUD-01, AUD-06, AUD-24, AUD-25): el mismo cuerpo en <c>GET /api/ecommerce/v1/auditoria</c> y en
/// <c>GET /api/inventario/v1/auditoria</c> e <c>historial</c>. Nunca lleva tarjetas, tokens, enlaces ni contraseñas: el detalle ya salió de <c>DetalleSeguro</c>. El instante viaja en UTC; la hora de Lima
/// (UTC−5) la pone la interfaz al mostrarlo (AC-AUD-22) y todo valor de texto se codifica al mostrarlo (SEG-02). Los valores cerrados (<c>servicio</c>, <c>tipo</c>, <c>actorTipo</c>, <c>resultado</c>) son
/// los nombres de <see cref="ServiciosAuditados"/>, <see cref="TiposDeRegistro"/>, <see cref="TiposDeActorAuditado"/> y <see cref="ResultadosAuditados"/>.
/// </summary>
public sealed record RegistroDto(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("servicio")] string Servicio,
    [property: JsonPropertyName("tipo")] string Tipo,
    [property: JsonPropertyName("actorTipo")] string ActorTipo,
    [property: JsonPropertyName("actorId")] Guid? ActorId,
    [property: JsonPropertyName("rol")] string? Rol,
    [property: JsonPropertyName("instanteUtc")] DateTimeOffset InstanteUtc,
    [property: JsonPropertyName("accion")] string Accion,
    [property: JsonPropertyName("resultado")] string Resultado,
    [property: JsonPropertyName("objetoTipo")] string? ObjetoTipo,
    [property: JsonPropertyName("objetoId")] string? ObjetoId,
    [property: JsonPropertyName("sucursalOrigen")] string? SucursalOrigen,
    [property: JsonPropertyName("sucursalDestino")] string? SucursalDestino,
    [property: JsonPropertyName("motivo")] string? Motivo,
    [property: JsonPropertyName("origen")] string? Origen,
    [property: JsonPropertyName("ruta")] string? Ruta,
    [property: JsonPropertyName("detalle")] string? Detalle,
    [property: JsonPropertyName("clavesPersonales")] IReadOnlyList<string>? ClavesPersonales,
    [property: JsonPropertyName("truncado")] bool Truncado,
    [property: JsonPropertyName("correlacion")] string? Correlacion,
    [property: JsonPropertyName("anonimizadoEn")] DateTimeOffset? AnonimizadoEn);

/// <summary>
/// Una página de registros (AUD-09): de 20 en 20, con el cursor firmado de la siguiente (<c>siguienteCursor</c>, nulo en la última). <c>cursorDeLaPagina</c> solo lo usa el E-commerce al fusionar la página del
/// Inventario con las suyas (AUD-30) y la interfaz lo ignora; <c>faltanDelInventario</c> lo pone el E-commerce cuando el Inventario no respondió (AUD-18) y el Inventario nunca lo envía.
/// </summary>
public sealed record PaginaDeRegistrosDto(
    [property: JsonPropertyName("registros")] IReadOnlyList<RegistroDto> Registros,
    [property: JsonPropertyName("siguienteCursor")] string? SiguienteCursor,
    [property: JsonPropertyName("cursorDeLaPagina")] string? CursorDeLaPagina = null,
    [property: JsonPropertyName("faltanDelInventario")] bool FaltanDelInventario = false);

/// <summary>
/// Una alerta agrupada (AUD-13, AUD-34): el identificador lleva el prefijo de su servicio (<c>eco:</c> o <c>inv:</c>), el motivo es un código del catálogo cerrado (<see cref="MotivosDeAlertaDeAuditoria"/>), nunca
/// el texto de una excepción, y <c>estado</c> es <see cref="EstadosDeAlerta"/> (pendiente mientras haya ocurrencias sin revisar).
/// </summary>
public sealed record AlertaDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("servicio")] string Servicio,
    [property: JsonPropertyName("motivo")] string Motivo,
    [property: JsonPropertyName("clase")] string Clase,
    [property: JsonPropertyName("primeraOcurrencia")] DateTimeOffset PrimeraOcurrencia,
    [property: JsonPropertyName("ultimaOcurrencia")] DateTimeOffset UltimaOcurrencia,
    [property: JsonPropertyName("ocurrencias")] int Ocurrencias,
    [property: JsonPropertyName("revisadasHasta")] int RevisadasHasta,
    [property: JsonPropertyName("revisadaPor")] Guid? RevisadaPor,
    [property: JsonPropertyName("revisadaEn")] DateTimeOffset? RevisadaEn,
    [property: JsonPropertyName("estado")] string? Estado = null);

/// <summary>Una página de alertas de seguridad (AUD-13) con el aviso de que faltan las del Inventario (AUD-18). La lista de alertas que lee el E-commerce del Inventario es un arreglo de <see cref="AlertaDto"/>.</summary>
public sealed record PaginaDeAlertasDto(
    [property: JsonPropertyName("alertas")] IReadOnlyList<AlertaDto> Alertas,
    [property: JsonPropertyName("siguienteCursor")] string? SiguienteCursor,
    [property: JsonPropertyName("faltanDelInventario")] bool FaltanDelInventario = false);

/// <summary>El cuerpo de <c>POST …/alertas/{id}/revisada</c> (AUD-34): el contador de ocurrencias que el administrador veía; marcar de nuevo nunca retrocede lo ya revisado.</summary>
public sealed record MarcarAlertaRevisadaDto(
    [property: JsonPropertyName("contadorVisto")] int ContadorVisto);

/// <summary>Los servicios que escriben registros (AUD-01), con el nombre que viaja en el contrato.</summary>
public static class ServiciosAuditados
{
    public const string Ecommerce = "Ecommerce";

    public const string Inventario = "Inventario";

    public static readonly IReadOnlyList<string> Todos = [Ecommerce, Inventario];
}

/// <summary>El tipo de un registro: de auditoría, de seguridad o ambos a la vez (AUD-01, AUD-20).</summary>
public static class TiposDeRegistro
{
    public const string Auditoria = "Auditoria";

    public const string Seguridad = "Seguridad";

    public const string Ambos = "Ambos";

    public static readonly IReadOnlyList<string> Todos = [Auditoria, Seguridad, Ambos];
}

/// <summary>Quién actuó (AUD-02): una persona o uno de los tres actores que fija el servidor, nunca la solicitud.</summary>
public static class TiposDeActorAuditado
{
    public const string Usuario = "Usuario";

    public const string Sistema = "Sistema";

    public const string Integracion = "Integracion";

    public const string Anonimo = "Anonimo";

    public static readonly IReadOnlyList<string> Todos = [Usuario, Sistema, Integracion, Anonimo];
}

/// <summary>El resultado de la acción registrada (AUD-01).</summary>
public static class ResultadosAuditados
{
    public const string Exitoso = "Exitoso";

    public const string Rechazado = "Rechazado";

    public const string Fallido = "Fallido";

    public static readonly IReadOnlyList<string> Todos = [Exitoso, Rechazado, Fallido];
}

/// <summary>La clase de una alerta (AUD-13): las de seguridad tienen pantalla; las técnicas solo registro y correo.</summary>
public static class ClasesDeAlerta
{
    public const string Seguridad = "Seguridad";

    public const string Tecnica = "Tecnica";

    public static readonly IReadOnlyList<string> Todas = [Seguridad, Tecnica];
}

/// <summary>El estado de una alerta (AUD-34, AUD-39).</summary>
public static class EstadosDeAlerta
{
    public const string Pendiente = "Pendiente";

    public const string Revisada = "Revisada";

    public static readonly IReadOnlyList<string> Todos = [Pendiente, Revisada];
}

/// <summary>Los prefijos del identificador de una alerta: dicen a qué servicio reenviar la marca de revisada.</summary>
public static class PrefijosDeAlerta
{
    public const string Ecommerce = "eco:";

    public const string Inventario = "inv:";
}

/// <summary>Los parámetros de la consulta de auditoría, tal como viajan en la URL (AUD-07), y sus topes (AUD-09, AUD-24).</summary>
public static class ParametrosDeAuditoria
{
    public const string Accion = "accion";

    public const string UsuarioId = "usuarioId";

    public const string Origen = "origen";

    public const string Desde = "desde";

    public const string Hasta = "hasta";

    public const string Texto = "texto";

    public const string Cursor = "cursor";

    public const string Sucursal = "sucursal";

    /// <summary>Registros por página (AUD-09).</summary>
    public const int TamanoDePagina = 20;

    /// <summary>El texto del filtro mide al menos esto (AUD-07).</summary>
    public const int MinimoDeCaracteresDelTexto = 3;

    /// <summary>El tope del detalle serializado de un registro (AUD-24).</summary>
    public const int MaximoDeCaracteresDelDetalle = 1000;
}
