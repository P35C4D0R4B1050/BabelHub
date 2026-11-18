using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>El caso de uso de recibir un traslado (T08).</summary>
internal sealed partial class EntornoDeTraslados
{
    public RecibirTraslado Recibir() => new(Ejecutor, new ValidadorDeTrasladoPorId<RecibirTrasladoEntrada>(e => e.TrasladoId), Movimientos, Reservas);
}
