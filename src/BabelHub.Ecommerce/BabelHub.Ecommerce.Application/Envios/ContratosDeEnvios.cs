using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Envios;

/// <summary>El estado de un envío tal como lo ve quien llama al módulo (modelo propio: el contrato público no expone el Dominio).</summary>
public enum EstadoDelEnvio
{
    PendienteDeGuia,
    GuiaGenerada,
    EnReparto,
    Entregado,
    Cancelado,
}

/// <summary>Una fila del listado de logística: sin ningún dato de contacto (ENV-08, ENV-15).</summary>
public sealed record ResumenDeEnvio(
    Guid Id,
    Guid PedidoId,
    string CodigoDeFuente,
    EstadoDelEnvio Estado,
    string? NumeroDeGuia,
    string? Operador,
    int CantidadDeLineas,
    DateTimeOffset CreadoEn,
    DateTimeOffset? GuiaGeneradaEn,
    DateTimeOffset? EnRepartoEn,
    DateTimeOffset? EntregadoEn);

public sealed record PaginaDeEnvios(IReadOnlyList<ResumenDeEnvio> Envios, int Pagina, int Tamano, int Total);

/// <summary>El filtro del listado de logística. El tamaño de página tiene tope de 50.</summary>
public sealed record ListarEnviosEntrada(EstadoDelEnvio? Estado = null, int Pagina = 1, int Tamano = 20);

/// <summary>Un envío con sus líneas, sin contacto (ENV-08).</summary>
public sealed record DetalleDeEnvio(
    Guid Id,
    Guid PedidoId,
    string CodigoDeFuente,
    EstadoDelEnvio Estado,
    string? NumeroDeGuia,
    string? Operador,
    IReadOnlyList<Guid> LineaIds,
    DateTimeOffset CreadoEn,
    DateTimeOffset? GuiaGeneradaEn,
    DateTimeOffset? EnRepartoEn,
    DateTimeOffset? EntregadoEn,
    DateTimeOffset? CanceladoEn);

/// <summary>Un envío del pedido del titular (ENV-05): su estado y su guía; sin número de guía es «pendiente de guía».</summary>
public sealed record EnvioDeMiPedido(Guid EnvioId, string CodigoDeFuente, EstadoDelEnvio Estado, string? NumeroDeGuia, string? Operador, DateTimeOffset? EntregadoEn);

public sealed record EnviosDeMiPedido(Guid PedidoId, IReadOnlyList<EnvioDeMiPedido> Envios);

/// <summary>
/// Las operaciones de logística y de la administración central sobre los envíos (ENV-02, ENV-04, ENV-07, ENV-08, ENV-10, ENV-11, ENV-12): ver, pedir la guía, pasar a en reparto y marcar entregado. Cualquier
/// otro rol recibe «prohibido». Ningún resultado lleva nombre, dirección ni teléfono (ENV-15).
/// </summary>
public interface IEnviosDeLogistica
{
    Task<Resultado<PaginaDeEnvios>> Listar(ListarEnviosEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<DetalleDeEnvio>> Obtener(Guid envioId, CancellationToken cancelacion = default);

    /// <summary>Pide la guía al operador (ENV-02): una segunda solicitud simultánea recibe «guía en curso» y la repetición con una guía ya generada no repite el efecto.</summary>
    Task<Resultado<DetalleDeEnvio>> SolicitarGuia(Guid envioId, CancellationToken cancelacion = default);

    /// <summary>Descuenta el físico del Inventario y pasa a en reparto (ENV-11, ENV-12): ante un fallo el envío sigue en guía generada y se puede reintentar.</summary>
    Task<Resultado<DetalleDeEnvio>> PasarAEnReparto(Guid envioId, CancellationToken cancelacion = default);

    Task<Resultado<DetalleDeEnvio>> MarcarEntregado(Guid envioId, CancellationToken cancelacion = default);
}

/// <summary>
/// Los envíos de un pedido a domicilio del titular (ENV-05): estado y guía, o «pendiente de guía». Un pedido ajeno o inexistente responde igual (PED-41). Lo compone la Web dentro del detalle del pedido:
/// Pedidos no depende de Envíos.
/// </summary>
public interface IEnviosDelCliente
{
    Task<Resultado<EnviosDeMiPedido>> ObtenerDeUnPedido(Guid pedidoId, CancellationToken cancelacion = default);
}
