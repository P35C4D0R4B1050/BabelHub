namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Los nombres con los que la Api fija el actor «integración» (<c>ActorActual.DeIntegracion</c>) cuando una llamada de otro servicio llega con su credencial: un solo lugar para la Api (que lo establece) y la Aplicación
/// (que lo comprueba, defensa en profundidad, AGENTS.md §5).
/// </summary>
public static class IntegracionesDeServicio
{
    /// <summary>El Inventario (solicitudes de avisos y estados de traslado).</summary>
    public const string Inventario = "inventario";
}
