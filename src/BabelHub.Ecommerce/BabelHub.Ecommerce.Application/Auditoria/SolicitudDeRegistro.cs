namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>El resultado de la operación registrada (AUD-01).</summary>
public enum ResultadoAuditado
{
    Exitoso,
    Rechazado,
    Fallido,
}

/// <summary>
/// Lo que un módulo pide registrar (AUD-01): la acción del catálogo, el objeto afectado, la sucursal o fuente de origen y de destino, el motivo (un
/// código del catálogo cerrado de la acción, nunca un texto de la solicitud), el origen (solo si no es el de la conexión en curso) y el detalle
/// (solo las claves que la acción declara; lo demás se descarta y los valores con forma de tarjeta, token, enlace o dirección se redactan, AUD-06).
/// <b>No lleva actor</b>: sale del contexto verificado (<c>IActorActual</c>) y nunca de la solicitud (AUD-23).
/// </summary>
public sealed record SolicitudDeRegistro(AccionAuditable Accion)
{
    private static readonly IReadOnlyDictionary<string, string?> DetalleVacio = new Dictionary<string, string?>();

    /// <summary>El tipo del objeto afectado (por ejemplo, <c>edicion</c>, <c>pedido</c>, <c>usuario</c>).</summary>
    public string? ObjetoTipo { get; init; }

    public string? ObjetoId { get; init; }

    /// <summary>La sucursal o fuente de origen (en un traslado, la que envía).</summary>
    public Guid? SucursalOrigen { get; init; }

    /// <summary>La sucursal o fuente de destino (solo en un traslado).</summary>
    public Guid? SucursalDestino { get; init; }

    public string? Motivo { get; init; }

    /// <summary>
    /// La huella u origen anonimizable de la solicitud. Si no se indica y la acción es un evento de seguridad, se usa la huella del origen de la
    /// conexión en curso (<c>IOrigenDeSolicitud</c>).
    /// </summary>
    public string? Origen { get; init; }

    public IReadOnlyDictionary<string, string?> Detalle { get; init; } = DetalleVacio;

    public ResultadoAuditado Resultado { get; init; } = ResultadoAuditado.Exitoso;
}

/// <summary>
/// La auditoría no está disponible: no se pudo registrar una operación sensible. La operación que la pidió <b>no se aplica</b> (AUD-04) y la Api
/// responde «servicio no disponible» (AC-AUD-03). El mensaje nunca lleva datos de la solicitud.
/// </summary>
public sealed class AuditoriaNoDisponibleException : Exception
{
    public AuditoriaNoDisponibleException()
    {
    }

    public AuditoriaNoDisponibleException(string message)
        : base(message)
    {
    }

    public AuditoriaNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Registra una operación que <b>concede o cambia</b> algo (un precio, un ajuste, un rol, un acceso): si no se puede registrar, lanza
/// <see cref="AuditoriaNoDisponibleException"/> y la operación no se aplica (AUD-04). Debe llamarse dentro de la transacción de la operación para que
/// el fallo revierta el efecto. Rechaza (con <see cref="ArgumentException"/>) una acción del catálogo que no concede nada: esas van por
/// <see cref="IRegistroDeSeguridad"/>.
/// </summary>
public interface IAuditoria
{
    Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default);
}

/// <summary>
/// Registra un evento de seguridad de un efecto que <b>no concede nada</b> (un rechazo, un bloqueo, un vencimiento, un intento no aplicado): nunca
/// interrumpe; si no se puede registrar, mantiene la protección y deja una alerta técnica (AUD-12). Rechaza (con <see cref="ArgumentException"/>)
/// una acción que concede o cambia algo: esas van por <see cref="IAuditoria"/>.
/// </summary>
public interface IRegistroDeSeguridad
{
    Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default);
}
