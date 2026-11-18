using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Traslados;

namespace BabelHub.Inventario.Application.Traslados.Interno;

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>INV-…</c> de <c>Shared</c> (D11).</summary>
internal static class ClavesDeTraslados
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string BloqueoOcupado = "bloqueo-ocupado";

    public const string TrasladoNoEncontrado = "traslado-no-encontrado";

    public const string FuenteNoEncontrada = "fuente-no-encontrada";

    public const string EdicionNoEncontrada = "edicion-no-encontrada";

    /// <summary>La edición es digital: un libro digital nunca se traslada (regla 12, TRA-20).</summary>
    public const string EdicionNoImpresa = "edicion-no-impresa";

    /// <summary>El origen no tiene vendible suficiente, o un traslado vinculado ya no tiene la reserva confirmada de su pedido (TRA-06, TRA-20). El mensaje es uniforme y no lleva cifras.</summary>
    public const string StockInsuficiente = "stock-insuficiente";

    /// <summary>La transición no está permitida desde el estado actual (TRA-03), o la reserva ya no se puede mover (TRA-23).</summary>
    public const string TrasladoEstadoInvalido = "traslado-estado-invalido";

    /// <summary>Un traslado vinculado a un pedido solo se aprueba: para detenerlo se anula el pedido (TRA-16).</summary>
    public const string TrasladoVinculadoNoSeRechaza = "traslado-vinculado-no-se-rechaza";

    public const string LimitesNoDisponibles = "limites-no-disponibles";
}

/// <summary>Los fallos esperados del módulo como <see cref="Resultado{T}"/> (la Api los traduce a los códigos <c>INV-…</c>).</summary>
internal static class FallosDeTraslados
{
    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static Resultado<T> Validacion<T>(ResultadoDeValidacion validacion) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDeTraslados.ValidacionFallida, validacion.Errores));

    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave) => Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave));

    public static Resultado<T> BloqueoOcupado<T>() => De<T>(CategoriaDeError.NoDisponible, ClavesDeTraslados.BloqueoOcupado);

    /// <summary>El 400 de una solicitud cuyo origen es igual al destino o cuya cantidad no es positiva: el campo y la clave, nunca el valor (SEG-01).</summary>
    public static Resultado<T> Invalido<T>(string campo, string clave) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDeTraslados.ValidacionFallida, [new ErrorDeCampo(campo, clave)]));
}

/// <summary>Los tipos de mensaje del buzón de salida (plan §3).</summary>
internal static class TiposDeMensaje
{
    /// <summary>El aviso de un cambio de estado a la sucursal de origen o a la de destino (TRA-10).</summary>
    public const string Aviso = "aviso";

    /// <summary>El estado del traslado de un pedido, que se informa al E-commerce (TRA-11).</summary>
    public const string EstadoDePedido = "estado-de-pedido";
}

/// <summary>
/// Un mensaje del buzón de salida (plan §3): se guarda con el cambio de estado, en la misma transacción, y un trabajo de fondo lo entrega al E-commerce. <see cref="Carga"/> es JSON con solo códigos, identificadores y campos
/// tipados: nunca el motivo, la guía, nombres ni datos de clientes (D07). Único por <c>(clave de evento, sucursal, tipo)</c>.
/// </summary>
internal sealed record MensajeSaliente(Guid Id, string Tipo, string ClaveDeEvento, string SucursalCodigo, string Carga, DateTimeOffset CreadoEn);

/// <summary>Los traslados guardados. Las lecturas corren con el alcance del actor (la política de filas es la segunda capa): un traslado que el encargado no ve es, para él, un traslado que no existe.</summary>
internal interface IRepositorioDeTraslados
{
    /// <summary>Guarda un traslado nuevo y le asigna su número (generado por la base). Un traslado de reposición no choca con ningún índice único.</summary>
    Task Insertar(Traslado traslado, CancellationToken cancelacion = default);

    /// <summary>El traslado por su identificador, sin bloquear (el primer paso de toda operación: leer, autorizar y recién entonces bloquear).</summary>
    Task<Traslado?> PorId(Guid trasladoId, CancellationToken cancelacion = default);

    /// <summary>El traslado bloqueado para actualizar (<c>FOR UPDATE</c>): el estado que se revalida es el de esta lectura, no el de la anterior.</summary>
    Task<Traslado?> PorIdBloqueado(Guid trasladoId, CancellationToken cancelacion = default);

    /// <summary>
    /// Guarda los cambios de un traslado con una sola sentencia condicional por su versión anterior (<c>UPDATE … WHERE id = @id AND version = @anterior</c>): devuelve falso si otra operación ya lo cambió. Con la fila bloqueada
    /// no puede pasar; un falso es un estado imposible que revierte la transacción.
    /// </summary>
    Task<bool> Guardar(Traslado traslado, long versionAnterior, CancellationToken cancelacion = default);

    /// <summary>
    /// Inserta un traslado vinculado con <c>INSERT … ON CONFLICT DO NOTHING</c> sobre el índice único parcial <c>(pedido, edición, origen)</c> de los no cancelados: devuelve verdadero si lo creó (y le asigna su número) y falso si
    /// ya existía uno, sin abortar la transacción de la confirmación (plan §5).
    /// </summary>
    Task<bool> InsertarVinculado(Traslado traslado, CancellationToken cancelacion = default);

    /// <summary>El traslado vinculado no cancelado de esa pareja de pedido, edición y origen, o <see langword="null"/>: lo que una confirmación repetida devuelve en lugar de crear otro.</summary>
    Task<Traslado?> VinculadoActivo(Guid pedidoId, Guid edicionId, Guid origenId, CancellationToken cancelacion = default);

    /// <summary>Los traslados por identificador, bloqueados <c>FOR UPDATE</c> y ordenados por identificador (la anulación de un pedido los toma después del pedido y antes de las líneas).</summary>
    Task<IReadOnlyList<Traslado>> PorIdsBloqueados(IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default);

    /// <summary>Una página de traslados que cumplen el filtro, del más reciente al más antiguo (fecha de solicitud e identificador descendentes), con el total.</summary>
    Task<PaginaDeTrasladosGuardados> Listar(FiltroDeTraslados filtro, int pagina, int tamano, CancellationToken cancelacion = default);

    /// <summary>Los traslados vinculados a un pedido (cualquier estado), del más reciente al más antiguo.</summary>
    Task<IReadOnlyList<Traslado>> DelPedido(Guid pedidoId, CancellationToken cancelacion = default);
}

/// <summary>
/// Los filtros de la lista de traslados: por estado, por fuente (origen <b>o</b> destino), vinculados o no, y los <c>solicitado</c> con fecha de solicitud no posterior a <see cref="SolicitadoAntesDe"/> (la demora mínima ya
/// convertida en un instante por quien llama).
/// </summary>
internal sealed record FiltroDeTraslados(EstadoDeTraslado? Estado, Guid? FuenteId, bool? Vinculado, DateTimeOffset? SolicitadoAntesDe);

/// <summary>Una página de traslados leída del repositorio.</summary>
internal sealed record PaginaDeTrasladosGuardados(IReadOnlyList<Traslado> Elementos, int Total);

/// <summary>
/// El buzón de salida: los mensajes del cambio de estado se insertan <b>en la misma transacción</b> con <c>INSERT … ON CONFLICT DO NOTHING</c>, de modo que un duplicado no aborta la transacción de quien confirma (plan §5). Los
/// mensajes de una operación se entregan después, de fondo y fuera de toda transacción (T18).
/// </summary>
internal interface IBuzonDeSalida
{
    Task Insertar(IReadOnlyCollection<MensajeSaliente> mensajes, CancellationToken cancelacion = default);
}

/// <summary>Cómo terminó el envío de un mensaje al E-commerce: el puerto <b>nunca lanza</b> al caso de uso, devuelve uno de estos resultados (plan §5, ECO-0 §9).</summary>
internal enum ResultadoDeEnvio
{
    /// <summary>El E-commerce lo aceptó (2xx, también el de una repetición idempotente).</summary>
    Entregado,

    /// <summary>Falla transitoria (red, 5xx, 429, tiempo agotado, respuesta con forma inesperada): se reintenta.</summary>
    Reintentable,

    /// <summary>Error permanente (400, 404, 409 o 422): no se reintenta; el mensaje pasa a fallido con una alerta técnica.</summary>
    Permanente,

    /// <summary>401 o 403 (por ejemplo una credencial mal rotada): alerta técnica y reencolado a mano, sin repetir cada hora.</summary>
    CredencialRechazada,
}

/// <summary>El aviso de un cambio de estado para una sucursal (ECO-0 D04): código de sucursal y campos tipados, nunca texto libre.</summary>
internal sealed record AvisoDeTrasladoParaEnviar(string EventoId, string CodigoDeSucursal, long NumeroDeTraslado, string Estado, string RolDeLaSucursal, int Cantidad, DateTimeOffset OcurridoEn);

/// <summary>El estado del traslado de un pedido (<c>en-transito</c>, <c>recibido</c> o <c>cancelado</c>) que se informa al E-commerce, con su fecha estimada de llegada o «por confirmar».</summary>
internal sealed record EstadoDeTrasladoDePedidoParaEnviar(string EventoId, Guid PedidoId, Guid TrasladoId, string Estado, DateOnly? FechaEstimadaDeLlegada, bool EstimadaPorConfirmar, DateTimeOffset OcurridoEn);

/// <summary>
/// La salida hacia el E-commerce (TRA-10, TRA-11): <b>modelos propios</b>, sin los DTOs de <c>Shared</c>, y nunca lanza (un E-commerce caído deja el mensaje pendiente, no interrumpe una transición, regla 10). Quien la implementa
/// valida la respuesta como no confiable. La implementa <c>HttpEnvioAlEcommerce</c> (T19).
/// </summary>
internal interface IEnvioAlEcommerce
{
    Task<ResultadoDeEnvio> EnviarAviso(AvisoDeTrasladoParaEnviar aviso, CancellationToken cancelacion = default);

    Task<ResultadoDeEnvio> InformarEstadoDelPedido(EstadoDeTrasladoDePedidoParaEnviar estado, CancellationToken cancelacion = default);
}
