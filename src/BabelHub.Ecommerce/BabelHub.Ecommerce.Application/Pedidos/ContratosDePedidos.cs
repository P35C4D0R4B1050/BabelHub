using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pedidos;

// El contrato público del módulo Pedidos que usan la web, el asistente y el Inventario (regla 13): modelos propios, ninguno del Dominio. Los DTO de lectura son tipos distintos por quien los ve (PED-40): el titular recibe el detalle con el contacto
// (<see cref="DetalleDelPedidoDelCliente"/>), logística y la administración central el detalle con el contacto auditado (<see cref="DetalleDelPedidoDePersonal"/>), y el encargado de sucursal y el asistente un tipo **sin contacto** (<see cref="DetalleDelPedidoDeSucursal"/>,
// <see cref="PedidoParaElAsistente"/>); los listados nunca lo llevan. Todo fallo es un <see cref="ErrorDeAplicacion"/> con una clave estable (ECO-…).

public enum EstadoVisibleDelPedido
{
    Confirmado,
    EnTransito,
    Recibido,
    EnPreparacion,
    ListoParaRecojo,
    Despachado,
    Entregado,
    NoRecogido,
    Anulado,
}

public enum EstadoVisibleDeLaLinea
{
    Confirmado,
    EnTransito,
    Recibido,
    EnPreparacion,
    ListoParaRecojo,
    Despachado,
    Entregado,
    NoRecogido,
    Anulado,

    /// <summary>Una línea digital con el acceso concedido en la biblioteca (PED-03).</summary>
    DisponibleEnLaBiblioteca,
}

public enum TipoDeEntregaVisible
{
    Recojo,
    Domicilio,
    Digital,
}

/// <summary>El traslado en curso de una línea y su fecha estimada de llegada (<see langword="null"/> es «a confirmar»): lo ve el cliente (PED-04).</summary>
public sealed record TrasladoVisible(DateOnly? FechaEstimadaDeLlegada, bool EstaEnTransito);

public sealed record LineaVisibleDelPedido(
    Guid LineaId,
    string Titulo,
    string? Isbn,
    bool EsDigital,
    int Cantidad,
    decimal PrecioUnitario,
    decimal Importe,
    EstadoVisibleDeLaLinea Estado,
    string? CodigoDeFuente,
    TrasladoVisible? Traslado,
    DateTimeOffset? ListoEn,
    DateTimeOffset? EntregadaEn,
    DateTimeOffset? AnuladaEn,
    bool Operable = false,
    string? CodigoDeSucursal = null);

/// <summary>Una fila de un listado: nunca lleva contacto, cuenta ni identificador de usuario.</summary>
public sealed record ResumenDePedido(
    Guid PedidoId,
    string Numero,
    DateTimeOffset CreadoEn,
    EstadoVisibleDelPedido Estado,
    TipoDeEntregaVisible TipoDeEntrega,
    string? CodigoDeSucursalDeRecojo,
    decimal Total,
    int CantidadDeLineas,
    IReadOnlyList<string>? CodigosDeSucursales = null);

/// <summary>El filtro y la paginación de un listado: el tamaño tiene un tope de 50 (PED-14).</summary>
public sealed record FiltroDePedidos(EstadoVisibleDelPedido? Estado = null, int Pagina = 1, int Tamano = 20)
{
    public const int TamanoMaximo = 50;

    /// <summary>La página más lejana que se atiende (20 por página: 10 000 filas): la acota la Aplicación y no solo la Api.</summary>
    public const int PaginaMaxima = 500;
}

public sealed record PaginaDePedidos(IReadOnlyList<ResumenDePedido> Pedidos, int Pagina, int Tamano, int Total);

/// <summary>El reembolso de una anulación que quedó pendiente (la pasarela falló, PED-24): visible para el titular y la administración.</summary>
public sealed record ReembolsoPendienteDelPedido(decimal Monto);

/// <summary>Qué le corresponde al cliente cuando no puede cancelar (PED-18): la devolución una vez entregado o la política de reembolso digital.</summary>
public enum AlternativaDeCancelacion
{
    Ninguna,
    Devolucion,
    PoliticaDeReembolsoDigital,
}

/// <summary>El detalle para el titular (PED-04, PED-40): líneas con su estado, el traslado con su fecha, el contacto (solo a domicilio y mientras no se anonimice) y el reembolso pendiente.</summary>
public sealed record DetalleDelPedidoDelCliente(
    ResumenDePedido Resumen,
    decimal CostoDeEnvio,
    string? RegionDeEntrega,
    IReadOnlyList<LineaVisibleDelPedido> Lineas,
    ContactoDeEntrega? Contacto,
    bool PuedeCancelar,
    AlternativaDeCancelacion Alternativa,
    ReembolsoPendienteDelPedido? ReembolsoPendiente);

/// <summary>
/// El detalle para logística y la administración central (PED-26, PED-40): con el contacto, cuya lectura se audita y falla cerrada. Si no se pudo auditar, <see cref="ContactoOculto"/> es verdadero y solo falta el contacto: el resto se muestra (D09).
/// </summary>
public sealed record DetalleDelPedidoDePersonal(
    ResumenDePedido Resumen,
    decimal CostoDeEnvio,
    string? RegionDeEntrega,
    IReadOnlyList<LineaVisibleDelPedido> Lineas,
    ContactoDeEntrega? Contacto,
    bool ContactoOculto,
    ReembolsoPendienteDelPedido? ReembolsoPendiente,
    bool ContactoAnonimizado = false,
    bool PuedeAnular = false);

/// <summary>El detalle para el encargado de sucursal (PED-08, PED-40): un pedido de recojo, <b>sin contacto</b> ni cuenta.</summary>
public sealed record DetalleDelPedidoDeSucursal(
    ResumenDePedido Resumen,
    IReadOnlyList<LineaVisibleDelPedido> Lineas);

public sealed record OperarLineasEntrada(Guid PedidoId, IReadOnlyList<Guid> LineaIds);

public enum ResultadoVisibleDeLinea
{
    Aplicada,
    EsperaElTraslado,
    EstadoInvalido,
}

public sealed record ResultadoDeLineaOperada(Guid LineaId, ResultadoVisibleDeLinea Resultado);

/// <summary>Lo que quedó tras preparar, marcar listas o entregar: el estado del pedido y el de cada línea pedida.</summary>
public sealed record LineasOperadas(Guid PedidoId, EstadoVisibleDelPedido Estado, IReadOnlyList<ResultadoDeLineaOperada> Lineas);

public sealed record ResultadoDeCancelacionDePedido(Guid PedidoId, EstadoVisibleDelPedido Estado, decimal Reembolso);

/// <summary>Anular un pedido (PED-13): el motivo es un código de la lista cerrada (<c>pago-duplicado</c>, <c>no-recogido</c>, <c>otro</c>); no hay nota libre.</summary>
public sealed record AnularPedidoEntrada(Guid PedidoId, string Motivo);

public sealed record ResultadoDeAnulacion(Guid PedidoId, EstadoVisibleDelPedido Estado, decimal Reembolso, bool ReembolsoPendiente);

/// <summary>Una operación fallida visible a la administración, sin datos personales: el pedido, el tipo, el motivo y los pasos hechos (PED-24).</summary>
public sealed record OperacionFallidaVisible(Guid OperacionId, Guid PedidoId, string Tipo, string? Motivo, IReadOnlyList<string> PasosHechos, DateTimeOffset CreadaEn);

public sealed record PaginaDeOperacionesFallidas(IReadOnlyList<OperacionFallidaVisible> Operaciones, int Pagina, int Tamano, int Total);

/// <summary>Los pedidos del cliente que ve el titular y los que cancela (PED-07, PED-17, PED-18, PED-41).</summary>
public interface IPedidosDelCliente
{
    /// <summary>Solo los pedidos del actor, sin contacto, del más reciente al más antiguo.</summary>
    Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default);

    /// <summary>El detalle de un pedido del actor; uno ajeno o inexistente es el mismo <c>ECO-NO-ENCONTRADO</c> con el acceso denegado registrado (PED-41).</summary>
    Task<Resultado<DetalleDelPedidoDelCliente>> Obtener(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Cancela un pedido de impresos todos confirmados con el motivo «cancelado por el cliente» (PED-17); con una digital o un impreso en camino se rechaza con la alternativa (PED-18).</summary>
    Task<Resultado<ResultadoDeCancelacionDePedido>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default);
}

/// <summary>La gestión de pedidos de recojo del encargado de su sucursal (PED-08, PED-09, PED-11): <b>solo</b> el encargado de esa sucursal; la administración central no figura en esta capacidad.</summary>
public interface IGestionDeRecojo
{
    Task<Resultado<PaginaDePedidos>> Listar(string codigoDeSucursal, FiltroDePedidos filtro, CancellationToken cancelacion = default);

    Task<Resultado<DetalleDelPedidoDeSucursal>> Obtener(string codigoDeSucursal, Guid pedidoId, CancellationToken cancelacion = default);

    Task<Resultado<LineasOperadas>> Preparar(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<LineasOperadas>> MarcarListas(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Pide al Inventario descontar el físico y cerrar la reserva de cada línea y, solo si lo logra, la pasa a entregada (PED-11, PED-25).</summary>
    Task<Resultado<LineasOperadas>> Entregar(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion = default);
}

/// <summary>La gestión de pedidos a domicilio de logística y la administración central (PED-26, PED-28): preparar y marcar listas; al marcar listas se crea el envío.</summary>
public interface IGestionDeDespacho
{
    Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default);

    Task<Resultado<DetalleDelPedidoDePersonal>> Obtener(Guid pedidoId, CancellationToken cancelacion = default);

    Task<Resultado<LineasOperadas>> Preparar(OperarLineasEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<LineasOperadas>> MarcarListas(OperarLineasEntrada entrada, CancellationToken cancelacion = default);
}

/// <summary>La administración central: todos los pedidos, anular y reintentar el reembolso (PED-13, PED-14, PED-16, PED-24).</summary>
public interface IAdministracionDePedidos
{
    Task<Resultado<PaginaDePedidos>> Listar(FiltroDePedidos filtro, CancellationToken cancelacion = default);

    Task<Resultado<DetalleDelPedidoDePersonal>> Obtener(Guid pedidoId, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeAnulacion>> Anular(AnularPedidoEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Reintenta el reembolso de una anulación sin repetir los pasos ya hechos (PED-24).</summary>
    Task<Resultado<ResultadoDeAnulacion>> ReintentarElReembolso(Guid pedidoId, CancellationToken cancelacion = default);

    Task<Resultado<PaginaDeOperacionesFallidas>> ListarOperacionesFallidas(int pagina, int tamano, CancellationToken cancelacion = default);
}

public sealed record LineaParaElAsistente(string Titulo, EstadoVisibleDeLaLinea Estado);

/// <summary>Lo que el asistente puede decir de un pedido: número, fecha, estado, tipo de entrega y título y estado por línea. Sin contacto, cuenta, usuario, traslados ni notas.</summary>
public sealed record PedidoParaElAsistente(string Numero, DateTimeOffset Fecha, EstadoVisibleDelPedido Estado, TipoDeEntregaVisible TipoDeEntrega, IReadOnlyList<LineaParaElAsistente> Lineas);

/// <summary>El estado de los pedidos para el asistente: la cuenta sale del contexto de lectura de 5 minutos (nunca de la solicitud) y se filtra siempre por ese cliente.</summary>
public interface IPedidosParaElAsistente
{
    /// <summary>Busca por número o devuelve los cinco pedidos más recientes del cliente del contexto; un contexto inválido es no autenticado.</summary>
    Task<Resultado<IReadOnlyList<PedidoParaElAsistente>>> Consultar(ValorSecreto contexto, string? numeroDePedido, CancellationToken cancelacion = default);
}

public enum EstadoDelTrasladoInformado
{
    EnTransito,
    Recibido,
    Cancelado,
}

/// <summary>Un estado de traslado que informa el Inventario (PED-19): el <c>eventoId</c> debe ser <c>traslado:&lt;trasladoId&gt;:&lt;estado&gt;</c> y coincidir con el cuerpo.</summary>
public sealed record EstadoDeTrasladoRecibido(string EventoId, Guid TrasladoId, Guid PedidoId, Guid EdicionId, int Cantidad, EstadoDelTrasladoInformado Estado, DateOnly? FechaEstimadaDeLlegada);

/// <summary>La entrada de los estados de traslado desde el Inventario (credencial de servicio): idempotente por <c>eventoId</c>; un cruce que no coincide es validación y un pedido o línea inexistente, un error permanente.</summary>
public interface IReceptorDeEstadosDeTraslado
{
    Task<Resultado<bool>> Recibir(EstadoDeTrasladoRecibido estado, CancellationToken cancelacion = default);
}
