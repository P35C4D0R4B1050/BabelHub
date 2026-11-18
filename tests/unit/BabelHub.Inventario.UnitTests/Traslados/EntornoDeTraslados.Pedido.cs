using BabelHub.Inventario.Application.Traslados.Interno;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>Los casos de uso que implementan los puertos de <c>Reservas</c> (T10 y T11), armados sobre el mismo entorno en memoria.</summary>
internal sealed partial class EntornoDeTraslados
{
    public GenerarTrasladosDePedido Generador() => new(Actor, Directorio, Traslados, Buzon, Auditoria, Base.Reloj);

    public CancelarTrasladosDePedido CancelacionPorPedido() => new(Traslados, Directorio, Buzon, Auditoria, Base.Reloj);
}
