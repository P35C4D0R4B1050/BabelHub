using BabelHub.Inventario.Application.Traslados;
using BabelHub.Inventario.Application.Traslados.Interno;

namespace BabelHub.Inventario.UnitTests.Traslados;

/// <summary>Los casos de uso de consulta de traslados (T14), armados sobre el mismo entorno en memoria.</summary>
internal sealed partial class EntornoDeTraslados
{
    public ConsultarTraslado Detalle() => new(Autorizador, new ValidadorDeTrasladoPorId<ConsultarTrasladoEntrada>(e => e.TrasladoId), Directorio, Traslados);

    public ListarTraslados Lista() => new(Autorizador, new ValidadorDeListarTraslados(), Directorio, Traslados, Base.Reloj);

    public ConsultarTrasladosDeUnPedido DelPedido() => new(Autorizador, new ValidadorDeConsultarTrasladosDeUnPedido(), Directorio, Traslados);
}
