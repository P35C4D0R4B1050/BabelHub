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
}
