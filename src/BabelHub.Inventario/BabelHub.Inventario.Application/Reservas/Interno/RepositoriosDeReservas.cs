using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>Lo que se guarda junto a una cabecera para la idempotencia y la investigación de abusos: la clave del cliente, la huella de su carga canónica y la huella no reversible del origen (se anula a las 24 horas, D11).</summary>
public sealed record DatosDeLaSolicitud(string ClaveDeIdempotencia, string HuellaDeLaCarga, string? HuellaDeOrigen)
{
    /// <summary>La clave y la huella de origen (un dato personal derivado) no deben llegar a un registro por un <c>ToString</c> automático.</summary>
    public override string ToString() => nameof(DatosDeLaSolicitud);
}

/// <summary>Una línea pendiente cuya cabecera ya venció: lo necesario para devolver sus unidades y auditar su expiración (sin datos del cliente). Es lo que devuelve la lectura de vencidas y lo que devuelve su paso a expirada.</summary>
public sealed record LineaVencida(Guid PedidoId, Guid LineaDeReservaId, Guid LineaId, Guid EdicionId, Guid FuenteId, int Cantidad);

/// <summary>
/// Repositorio de reservas (esquema <c>reservas</c>, D01, D07). Cada operación de escritura es una sola sentencia condicional por estado y devuelve lo que de verdad cambió (<c>RETURNING</c>): el stock se ajusta solo por
/// las líneas realmente actualizadas. Las comparaciones de vencimiento reciben <c>ahora</c> (D12) y no usan la hora de la base. Los bloqueos asesores los toma la Aplicación por <c>IBloqueosAsesores</c>.
/// </summary>
public interface IRepositorioDeReservas
{
    /// <summary>La cabecera más reciente del pedido con sus líneas, o <see langword="null"/> si el pedido nunca reservó.</summary>
    Task<ReservaDePedido?> UltimaDelPedido(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Inserta la cabecera y sus líneas. Una violación de unicidad por una cabecera concurrente nunca llega como error 500: la Aplicación la toma bajo el bloqueo del pedido.</summary>
    Task Insertar(ReservaDePedido reserva, DatosDeLaSolicitud solicitud, CancellationToken cancelacion = default);

    /// <summary>Las unidades de líneas pendientes ya vencidas (la cabecera expiró antes de <paramref name="ahora"/>) de esas ediciones, por pareja (fuente, edición), sin bloquear nada (FUE-03, SEL-27).</summary>
    Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Los pedidos que tienen líneas pendientes vencidas de esas ediciones (todas si es nulo), sin bloquear: los candidatos de la expiración. Ordenados para que dos expiraciones los tomen en el mismo orden.</summary>
    Task<IReadOnlyList<Guid>> PedidosConVencidas(IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Las líneas pendientes vencidas del pedido que todavía no pasaron a expiradas, sin bloquear nada: de ahí salen las ediciones cuyo stock hay que bloquear antes de expirarlas.</summary>
    Task<IReadOnlyList<LineaVencida>> VencidasDelPedido(Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>
    /// Bloquea por id las líneas pendientes vencidas del pedido (de esas ediciones, o de todas si es nulo), <b>repite la comprobación de vencimiento con <paramref name="ahora"/></b> y las pasa a expiradas
    /// con una sola sentencia condicional; devuelve solo las que de verdad cambiaron. Quien llama ya tomó el bloqueo asesor del pedido.
    /// </summary>
    Task<IReadOnlyList<LineaVencida>> ExpirarLineasVencidas(Guid pedidoId, IReadOnlyCollection<Guid>? edicionIds, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Hasta <paramref name="maximo"/> pedidos que apartan unidades de la pareja (líneas pendientes no vencidas o confirmadas) y el total de pedidos, solo identificadores (AJU-06).</summary>
    Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, DateTimeOffset ahora, CancellationToken cancelacion = default);
}

/// <summary>Las claves simbólicas de los fallos del módulo; la Api las traduce a los códigos <c>INV-…</c> de <c>Shared</c> (D11).</summary>
internal static class ClavesDeReservas
{
    public const string ValidacionFallida = "validacion-fallida";

    public const string BloqueoOcupado = "bloqueo-ocupado";

    public const string EdicionNoEncontrada = "edicion-no-encontrada";

    public const string EdicionNoImpresa = "edicion-no-impresa";

    public const string FuenteNoEncontrada = "fuente-no-encontrada";

    public const string StockInsuficiente = "stock-insuficiente";

    /// <summary>El pedido ya tiene una reserva: hasta que el reemplazo y la idempotencia (T11 a T13) la traten, se rechaza.</summary>
    public const string ReservaExistente = "reserva-existente";

    public const string ReservaEstadoInvalido = "reserva-estado-invalido";
}

/// <summary>
/// Una cabecera o una línea activa de otra transacción chocó con la que se insertaba (la restricción única es la última defensa de la clave de idempotencia y de «una línea activa por línea de pedido»). El bloqueo del
/// pedido y el del cliente ya lo impiden; si ocurre igual, el caso de uso lo traduce a un conflicto y nunca a un error del servidor.
/// </summary>
public sealed class ReservaDuplicadaException : Exception
{
    public ReservaDuplicadaException()
        : base("Otra reserva del mismo pedido o con la misma clave de idempotencia ya existe.")
    {
    }

    public ReservaDuplicadaException(Exception interna)
        : base("Otra reserva del mismo pedido o con la misma clave de idempotencia ya existe.", interna)
    {
    }

    public ReservaDuplicadaException(string message, Exception interna)
        : base(message, interna)
    {
    }
}
