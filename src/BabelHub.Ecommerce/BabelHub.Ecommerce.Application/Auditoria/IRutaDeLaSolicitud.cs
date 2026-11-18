namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>
/// La ruta cruda de la solicitud en curso, para los accesos denegados que registra la Aplicación (un caso de uso no conoce HTTP y pasa una ruta nula): el módulo la reduce a su plantilla sin parámetros ni consulta antes de guardarla (AUD-05).
/// La Api la implementa con la solicitud; un host sin solicitud (un trabajo de fondo) no la registra y el evento queda con «sin-coincidencia».
/// </summary>
public interface IRutaDeLaSolicitud
{
    /// <summary>La ruta tal como llegó (sin la consulta), o <see langword="null"/> si no hay una solicitud en curso.</summary>
    string? Actual { get; }
}
