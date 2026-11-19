using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pedidos;

// El contrato de lectura de Pedidos para Devoluciones (D01 de ecommerce-devoluciones; hallazgo para ECO-6). Lo declara Devoluciones **como cambio aditivo mínimo** para poder consumirlo (el carril de Pedidos lo adopta o lo ajusta
// con su implementación); es la raíz pública del módulo Pedidos (regla 13): modelos propios, sin Dominio ni `Interno`, y **sin el contacto de entrega ni datos de pago**. Es una llamada hacia abajo (Devoluciones → Pedidos).

/// <summary>El estado de una línea tal como Devoluciones lo necesita (DEV-02, DEV-03, DEV-19): una impresa aún sin entregar, entregada, una digital disponible o una anulada.</summary>
public enum EstadoDeLineaParaDevoluciones
{
    NoEntregada,
    Entregada,
    Disponible,
    Anulada,
}

/// <summary>Una línea del pedido: su identificador, la edición, si es digital, la cantidad, el precio unitario, el importe, su estado y, en una impresa entregada, cuándo se entregó (el plazo de 15 días cuenta desde ahí, DEV-15).</summary>
public sealed record LineaDePedidoParaDevoluciones(Guid LineaId, Guid EdicionId, bool EsDigital, int Cantidad, decimal PrecioUnitario, decimal Importe, EstadoDeLineaParaDevoluciones Estado, DateTimeOffset? EntregadaEn);

/// <summary>El pedido para Devoluciones: su número (el aviso), la <b>cuenta opaca</b> (la propiedad y el destinatario salen siempre de aquí, nunca del actor), cuándo se creó (la fecha de pago de los plazos de 14 y 30 días, D05) y sus líneas.</summary>
public sealed record PedidoParaDevoluciones(Guid PedidoId, string Numero, Guid CuentaId, DateTimeOffset CreadoEn, IReadOnlyList<LineaDePedidoParaDevoluciones> Lineas);

/// <summary>Los pedidos de una cuenta, acotados y paginados (la cancelación de la cuenta los lee antes de abrir el rol del esquema).</summary>
public sealed record PaginaDePedidosDeLaCuenta(IReadOnlyList<Guid> PedidoIds, int Total);

public interface IPedidosParaDevoluciones
{
    /// <summary>El pedido o <c>NoEncontrado</c>; un fallo del almacén es «no disponible», nunca «no existe». Sin bloqueo: una línea entregada no cambia de estado.</summary>
    Task<Resultado<PedidoParaDevoluciones>> Obtener(Guid pedidoId, CancellationToken cancelacion = default);

    /// <summary>Los identificadores de los pedidos de la cuenta, <b>solo identificadores</b> (DEV-23, DEV-24).</summary>
    Task<Resultado<PaginaDePedidosDeLaCuenta>> ListarPedidosDeLaCuenta(Guid cuentaId, int pagina, int tamano, CancellationToken cancelacion = default);
}
