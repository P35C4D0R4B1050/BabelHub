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

    /// <summary>Un usuario autenticado: 100 solicitudes por minuto (SEG-05).</summary>
    public static PoliticaDeLimite SolicitudesPorUsuario { get; } = new(
        "solicitudes-por-usuario", 100, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.Usuario, respaldoLocal: true);

    /// <summary>Límites generales de uso: con el almacén caído caen al límite local por instancia (SEG-26).</summary>
    public static IReadOnlyList<PoliticaDeLimite> Generales { get; } = [SolicitudesPorUsuario];

    public static IReadOnlyList<PoliticaDeLimite> Todas { get; } = [AjustesDelPersonal, TrasladosDelPersonal];
}
