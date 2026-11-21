using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Biblioteca;

/// <summary>Cómo respondió Biblioteca al conceder el acceso a una línea digital (BIB-01, BIB-19, BIB-20): concedido, reactivado, ya concedido por este mismo pedido (una repetición) o ya poseído por otra compra (se compensa el cobro).</summary>
public enum ResultadoDeConcederAcceso
{
    Concedido,
    Reactivado,
    YaConcedidoPorEstePedido,
    YaPoseidoPorOtraCompra,
}

public enum ResultadoDeRevocarAcceso
{
    Revocado,
    YaRevocado,
}

public enum MotivoDeRevocacionDeAcceso
{
    PedidoAnulado,

    /// <summary>Un reembolso (automático o aprobado) revocó el acceso (BIB-24): lo añade Biblioteca (ECO-8) como cambio aditivo, junto con <c>anulacion-de-pedido</c> y <c>reporte-aprobado</c> (su plan §4, D01).</summary>
    Reembolso,

    /// <summary>La administración central aprobó el reporte de un archivo defectuoso (DEV-06): lo añade Devoluciones como cambio aditivo; Biblioteca (ECO-8) lo adopta junto con <c>reembolso</c> (su plan §4).</summary>
    ReporteAprobado,
}

public sealed record ConcederAccesoEntrada(Guid CuentaId, Guid PedidoId, Guid EdicionId);

public sealed record RevocarAccesoEntrada(Guid PedidoId, Guid EdicionId, MotivoDeRevocacionDeAcceso Motivo);

/// <summary>
/// La <b>firma mínima</b> con la que Pedidos pide los accesos digitales a Biblioteca (D12 de <c>ecommerce-pedidos</c>; precedente: <c>IDescargasDeLaBiblioteca</c>): <c>Conceder</c> corre en la transacción de Pagos al crear el pedido y <c>Revocar</c> al anular, aunque el libro
/// se haya descargado. Ambas son idempotentes. El plan de <c>ecommerce-biblioteca-digital</c> la adopta o la ajusta.
/// </summary>
public interface IAccesosDeBiblioteca
{
    Task<Resultado<ResultadoDeConcederAcceso>> Conceder(ConcederAccesoEntrada entrada, CancellationToken cancelacion = default);

    Task<Resultado<ResultadoDeRevocarAcceso>> Revocar(RevocarAccesoEntrada entrada, CancellationToken cancelacion = default);

    /// <summary>Si el acceso de esa compra ya se revocó por un reembolso (aprobado o automático) o por el reporte aprobado de un archivo defectuoso: Pedidos no vuelve a reembolsar esa línea al anular el pedido (el tope de Pagos la rechazaría). Solo lee.</summary>
    Task<Resultado<bool>> EstaRevocadoPorReembolso(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default);
}
