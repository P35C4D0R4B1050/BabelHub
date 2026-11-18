using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>El caso de uso de despachar un traslado (T07).</summary>
internal sealed partial class EntornoDeTraslados
{
    public DespacharTraslado Despachar() => new(Ejecutor, new ValidadorDeTrasladoPorId<DespacharTrasladoEntrada>(e => e.TrasladoId), Movimientos, Reservas);
}
