using BabelHub.Inventario.Application.Reservas;

namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// El adaptador de <see cref="IGeneradorDeTrasladosDePedido"/> mientras <c>Traslados</c> no existe: <b>falla a la vista</b> si hay algo que trasladar, en lugar de confirmar un pedido de recojo sin su traslado (un
/// «nada» silencioso perdería la mercadería en tránsito). Sin solicitudes (todo sale de la sucursal de recojo) no hay nada que pedir y responde vacío. El anfitrión de producción registra el real de <c>Traslados</c>
/// antes (<c>TryAdd</c>: el real gana).
/// </summary>
internal sealed class NuloGeneradorDeTrasladosDePedido : IGeneradorDeTrasladosDePedido
{
    public Task<IReadOnlyList<TrasladoVinculadoAlPedido>> Generar(Guid pedidoId, Guid sucursalDeRecojoId, IReadOnlyCollection<SolicitudDeTrasladoDePedido> solicitudes, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitudes);

        return solicitudes.Count == 0
            ? Task.FromResult<IReadOnlyList<TrasladoVinculadoAlPedido>>([])
            : throw new InvalidOperationException("Traslados no está compuesto: no se puede confirmar un pedido de recojo cuya mercadería sale de otra sucursal.");
    }
}
