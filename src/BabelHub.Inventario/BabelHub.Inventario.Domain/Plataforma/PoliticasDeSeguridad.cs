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

    /// <summary>Tope general de la dirección directa de una llamada entre servicios cuya dirección reenviada se ignoró (SEG-32): 120 por minuto, configurable.</summary>
    public static PoliticaDeLimite SolicitudesPorHuellaDirecta { get; } = new(
        "solicitudes-por-huella-directa", 120, TimeSpan.FromMinutes(1), TipoDeSujetoDeLimite.OrigenDirecto, respaldoLocal: true);

    /// <summary>
    /// Reservas temporales iniciadas por un cliente: 5 por hora (SEL-14). Sin respaldo local (falla cerrada: con el motor caído se responde «no disponible» y se revierte), sin liberación (una
    /// reserva iniciada cuenta aunque luego se pague o se cancele) y evaluada <b>en la transacción de la reserva</b>, de modo que un rechazo por stock o por otro límite no deja el evento (SEL-14, SEL-18).
    /// </summary>
    public static PoliticaDeLimite ReservasPorHoraPorCliente { get; } = new(
        "reservas-por-hora-por-cliente", 5, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Usuario);

    /// <summary>Reservas temporales iniciadas por un mismo origen: 10 por hora (SEL-19), con las mismas propiedades que <see cref="ReservasPorHoraPorCliente"/>.</summary>
    public static PoliticaDeLimite ReservasPorHoraPorOrigen { get; } = new(
        "reservas-por-hora-por-origen", 10, TimeSpan.FromHours(1), TipoDeSujetoDeLimite.Origen);

    /// <summary>Límites generales de uso: con el almacén caído caen al límite local por instancia (SEG-26).</summary>
    public static IReadOnlyList<PoliticaDeLimite> Generales { get; } =
        [SolicitudesPorUsuario, SolicitudesPorHuellaDirecta];

    public static IReadOnlyList<PoliticaDeLimite> Todas { get; } = [AjustesDelPersonal, TrasladosDelPersonal, ReservasPorHoraPorCliente, ReservasPorHoraPorOrigen];
}
