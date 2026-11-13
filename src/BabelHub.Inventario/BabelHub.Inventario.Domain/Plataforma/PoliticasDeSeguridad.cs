namespace BabelHub.Inventario.Domain.Plataforma;

/// <summary>
/// Presupuesto por hora de operaciones sensibles del personal (SEG-30), con los valores iniciales de la spec: 60 ajustes de
/// inventario y 30 solicitudes de traslado por usuario. El exceso se rechaza con la respuesta uniforme de límite excedido y
/// se publica como evento de seguridad.
/// </summary>
public static class PoliticasDeSeguridad
{
    public static PoliticaDeLimite AjustesDelPersonal { get; } = new(
        "ajustes-del-personal", 60, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    public static PoliticaDeLimite TrasladosDelPersonal { get; } = new(
        "traslados-del-personal", 30, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    public static IReadOnlyList<PoliticaDeLimite> Todas { get; } = [AjustesDelPersonal, TrasladosDelPersonal];
}
