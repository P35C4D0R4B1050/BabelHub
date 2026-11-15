using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>Lo que se guarda junto a una cabecera para la idempotencia y la investigación de abusos: la clave del cliente, la huella de su carga canónica y la huella no reversible del origen (se anula a las 24 horas, D11).</summary>
public sealed record DatosDeLaSolicitud(string ClaveDeIdempotencia, string HuellaDeLaCarga, string? HuellaDeOrigen)
{
    /// <summary>La clave y la huella de origen (un dato personal derivado) no deben llegar a un registro por un <c>ToString</c> automático.</summary>
    public override string ToString() => nameof(DatosDeLaSolicitud);
}

/// <summary>Una reserva guardada con la huella de la carga con la que se creó (D03).</summary>
public sealed record ReservaPorClave(ReservaDePedido Reserva, string HuellaDeLaCarga);

/// <summary>Una operación idempotente ya hecha: la huella de su carga y su resultado original.</summary>
public sealed record OperacionGuardada(string HuellaDeLaCarga, string Resultado);

/// <summary>Una línea confirmada que pasó a cerrada: lo necesario para descontar el físico y auditar el cierre.</summary>
public sealed record LineaCerrada(Guid PedidoId, Guid LineaDeReservaId, Guid LineaId, Guid EdicionId, Guid FuenteId, int Cantidad);

/// <summary>Una línea pendiente que pasó a confirmada: lo necesario para solicitar su traslado y auditar la confirmación.</summary>
public sealed record LineaConfirmada(Guid PedidoId, Guid LineaDeReservaId, Guid LineaId, Guid EdicionId, Guid FuenteId, int Cantidad);

/// <summary>Una línea que pasó a liberada: lo necesario para devolver sus unidades y auditar la liberación (sin datos del cliente).</summary>
public sealed record LineaLiberada(Guid PedidoId, Guid LineaDeReservaId, Guid LineaId, Guid EdicionId, Guid FuenteId, int Cantidad, Guid? TrasladoId, bool EstabaConfirmada);

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

    /// <summary>La reserva que ese cliente creó con esa clave de idempotencia y la huella de su carga, o <see langword="null"/> si la clave no se usó (SEL-09, SEL-31). La clave es única por cliente.</summary>
    Task<ReservaPorClave?> PorClave(Guid clienteId, string clave, CancellationToken cancelacion = default);

    /// <summary><see langword="true"/> si el titular figura en las bajas recibidas: una cuenta cancelada no reserva (SEL-35).</summary>
    Task<bool> ClienteDadoDeBaja(Guid clienteId, CancellationToken cancelacion = default);

    /// <summary>
    /// El instante de expiración de la reserva <b>pendiente y no vencida</b> de ese cliente en <b>otro pedido</b> que no sea <paramref name="exceptoPedidoId"/>, o <see langword="null"/> si no tiene (SEL-13). Se lee bajo el
    /// bloqueo del cliente, de modo que dos solicitudes simultáneas del mismo cliente se serializan.
    /// </summary>
    Task<DateTimeOffset?> ExpiracionDeLaPendienteDelCliente(Guid clienteId, Guid exceptoPedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default);

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

    /// <summary>
    /// Pasa a <b>liberadas</b> las líneas activas del pedido (pendientes, y confirmadas si <paramref name="incluirConfirmadas"/>), opcionalmente solo las de esas líneas del pedido (<paramref name="lineaIds"/>), con una
    /// sola sentencia condicional que las bloquea por id y en orden y devuelve <b>solo las que de verdad cambiaron</b>. Quien llama ya tomó el bloqueo del pedido y bloqueó las filas de stock.
    /// </summary>
    Task<IReadOnlyList<LineaLiberada>> LiberarLineas(Guid pedidoId, bool incluirConfirmadas, IReadOnlyCollection<Guid>? lineaIds, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>
    /// Pasa a <b>liberadas</b> exactamente esas líneas de reserva (por su identificador) si siguen pendientes o confirmadas, con una sola sentencia que las bloquea por id y en orden y devuelve solo las que de
    /// verdad cambiaron. Quien llama ya tomó el bloqueo del pedido y bloqueó las filas de stock.
    /// </summary>
    Task<IReadOnlyList<LineaLiberada>> LiberarLineasPorId(IReadOnlyCollection<Guid> lineaDeReservaIds, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Marca como anulada la venta (<c>anulada_en</c>) de esas líneas cerradas por traslado si todavía no lo estaban; devuelve las que cambiaron (TRA-19). No toca el stock.</summary>
    Task<IReadOnlyList<LineaLiberada>> AnularVentas(IReadOnlyCollection<Guid> lineaDeReservaIds, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>La operación idempotente guardada con esa clave para el pedido y el tipo (<c>cerrar</c>), con la huella de su carga y su resultado original, o <see langword="null"/> si la clave no se usó (SEL-38). La clave se acota por pedido y operación.</summary>
    Task<OperacionGuardada?> ObtenerOperacion(Guid pedidoId, string tipo, string clave, CancellationToken cancelacion = default);

    /// <summary>Guarda la operación con su huella y su resultado mínimo (solo el desenlace por línea, nunca datos de clientes), en la misma transacción que el cambio.</summary>
    Task GuardarOperacion(Guid pedidoId, string tipo, string clave, string huellaDeLaCarga, string resultado, CancellationToken cancelacion = default);

    /// <summary>
    /// Pasa a <b>cerradas</b> con su causa esas líneas confirmadas, con una sola sentencia que las bloquea por id y en orden y devuelve solo las que de verdad cambiaron: la primera operación que llega gana (SEL-29).
    /// Quien llama ya tomó el bloqueo del pedido y bloqueó las filas de stock.
    /// </summary>
    Task<IReadOnlyList<LineaCerrada>> CerrarLineas(IReadOnlyCollection<Guid> lineaDeReservaIds, CausaDeCierre causa, Guid? envioId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>El envío con el que se cerró cada una de esas líneas (por identificador de línea de reserva), si lo tiene: solo el reparto lo lleva. Sirve para que <c>ya-cerrada</c> exija el mismo envío (SEL-38).</summary>
    Task<IReadOnlyDictionary<Guid, Guid?>> EnviosDeCierre(IReadOnlyCollection<Guid> lineaDeReservaIds, CancellationToken cancelacion = default);

    /// <summary>
    /// Bloquea por id y en orden esas líneas (<c>SELECT … FOR UPDATE</c>) <b>sin cambiarlas</b>: el escalón «líneas de reserva» de la jerarquía de bloqueos (D04), que se toma <b>antes</b> de las filas de stock. Las sentencias
    /// condicionales que siguen las vuelven a bloquear sin costo (ya son de esta transacción).
    /// </summary>
    Task BloquearLineas(IReadOnlyCollection<Guid> lineaDeReservaIds, CancellationToken cancelacion = default);

    /// <summary>Marca como recreada en el destino la línea cerrada por traslado (con una sentencia condicional: solo si lo está, no estaba recreada y su venta no se anuló). Devuelve si cambió algo.</summary>
    Task<bool> MarcarRecreadaEnDestino(Guid lineaDeReservaId, CancellationToken cancelacion = default);

    /// <summary>Inserta la línea que un traslado crea ya confirmada en el destino (misma línea del pedido, otra fuente) en la cabecera dada.</summary>
    Task InsertarLineaDeTraslado(Guid reservaId, Guid pedidoId, LineaDeReserva linea, CancellationToken cancelacion = default);

    /// <summary>
    /// Confirma las líneas <b>pendientes no vencidas</b> del pedido con una sola sentencia condicional (las bloquea por id y en orden, repite el vencimiento con <paramref name="ahora"/>) y devuelve solo las que de verdad
    /// cambiaron; si cambió alguna, la cabecera deja de expirar. Quien llama ya tomó el bloqueo del pedido.
    /// </summary>
    Task<IReadOnlyList<LineaConfirmada>> ConfirmarLineas(Guid pedidoId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>
    /// Extiende la expiración de la cabecera con una sola sentencia condicional: solo si todavía no se extendió, tiene una fecha y esa fecha no llegó a <paramref name="ahora"/>. Devuelve si cambió algo (SEL-22, SEL-30).
    /// Quien llama ya tomó el bloqueo del pedido.
    /// </summary>
    Task<bool> ExtenderExpiracion(Guid reservaId, DateTimeOffset nuevaExpiracion, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Fija el traslado vinculado de cada línea de reserva (por el identificador de la línea de reserva); repetirlo con el mismo traslado no cambia nada.</summary>
    Task VincularTraslados(IReadOnlyCollection<(Guid LineaDeReservaId, Guid TrasladoId)> vinculos, CancellationToken cancelacion = default);

    /// <summary>Hasta <paramref name="maximo"/> pedidos que apartan unidades de la pareja (líneas pendientes no vencidas o confirmadas) y el total de pedidos, solo identificadores (AJU-06).</summary>
    Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>
    /// Las unidades vendidas con la confirmación entre <paramref name="desde"/> y <paramref name="hasta"/> (los dos extremos incluidos): líneas confirmadas o cerradas por cualquier causa, creadas por la venta (no por un traslado) y
    /// no anuladas, atribuidas a la sucursal de recojo en el recojo y a la fuente de la línea a domicilio, sumadas por pedido, edición y fuente (FUE-21, FUE-22). Con <paramref name="fuenteId"/> nulo, las de toda la red. Solo lee, sin bloquear.
    /// </summary>
    Task<IReadOnlyList<VentaAtribuida>> VentasAtribuidas(Guid? fuenteId, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default);

    /// <summary>
    /// Cuántos pedidos del cliente tienen una línea confirmada, una pendiente que no venció en <paramref name="ahora"/> o una cerrada por traslado que sigue en tránsito (sin recrear en el destino y sin anular): una reserva
    /// confirmada cuya línea viaja sigue siendo una reserva confirmada (SEL-33). Cada pedido cuenta una sola vez. Solo lee, sin bloquear y sin devolver datos de las ediciones.
    /// </summary>
    Task<int> PedidosActivosDelCliente(Guid clienteId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Registra la baja del titular en <c>bajas_recibidas</c> (<c>ON CONFLICT DO NOTHING</c>). Devuelve si la baja es nueva: falso cuando ya estaba registrada (SEL-34). Quien llama ya tomó el bloqueo del cliente.</summary>
    Task<bool> RegistrarBaja(Guid titularId, DateTimeOffset ahora, CancellationToken cancelacion = default);

    /// <summary>Sustituye <c>cliente_id</c> de las cabeceras del titular por el identificador anónimo y anula su <c>huella_origen</c>, en una sentencia (SEL-34, SEL-35). Devuelve cuántas cabeceras cambió; no toca líneas ni stock.</summary>
    Task<int> AnonimizarCliente(Guid titularId, Guid identificadorAnonimo, CancellationToken cancelacion = default);

    /// <summary>Hasta <paramref name="maximo"/> bajas que <c>Auditoria</c> aún no consumió, de la más antigua a la más reciente.</summary>
    Task<IReadOnlyList<Guid>> BajasPendientesDeAuditoria(int maximo, CancellationToken cancelacion = default);

    /// <summary>Marca las bajas como entregadas a <c>Auditoria</c> en <paramref name="ahora"/> (arranca su retención de 30 días); repetirlo no cambia la fecha de las ya marcadas.</summary>
    Task MarcarBajasEntregadas(IReadOnlyCollection<Guid> titularIds, DateTimeOffset ahora, CancellationToken cancelacion = default);
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

    /// <summary>Una cabecera o una línea activa concurrentes chocaron con la restricción única al insertar (nunca un error del servidor).</summary>
    public const string ReservaExistente = "reserva-existente";

    /// <summary>La clave de idempotencia ya se usó con otra carga (SEL-31).</summary>
    public const string IdempotenciaEnConflicto = "idempotencia-en-conflicto";

    /// <summary>El titular fue dado de baja: una cuenta cancelada no reserva (SEL-35).</summary>
    public const string TitularDadoDeBaja = "titular-dado-de-baja";

    public const string ReservaEstadoInvalido = "reserva-estado-invalido";

    public const string ReservaNoEncontrada = "reserva-no-encontrada";
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
