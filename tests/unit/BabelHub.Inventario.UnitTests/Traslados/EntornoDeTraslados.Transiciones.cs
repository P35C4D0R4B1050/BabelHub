using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>Los casos de uso de aprobar, rechazar y cancelar un traslado (T06), armados sobre el mismo entorno en memoria.</summary>
internal sealed partial class EntornoDeTraslados
{
    public EjecutorDeTransiciones Ejecutor => new(Autorizador, Traslados, Bloqueos, Directorio, Buzon, Auditoria, Base.Reloj);

    public AprobarTraslado Aprobar() => new(Ejecutor, new ValidadorDeTrasladoPorId<AprobarTrasladoEntrada>(e => e.TrasladoId));

    public RechazarTraslado Rechazar() => new(Ejecutor, new ValidadorDeRechazarTraslado());

    public CancelarTraslado Cancelar() => new(Ejecutor, new ValidadorDeCancelarTraslado());
}
