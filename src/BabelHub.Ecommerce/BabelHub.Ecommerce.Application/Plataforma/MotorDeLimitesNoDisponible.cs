namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// El almacén de contadores no responde (SEG-26). Los motores de Infraestructura la lanzan, `EvaluarLimite` decide qué hacer y la Api,
/// si llegara a escapar, responde «servicio no disponible».
/// </summary>
public sealed class MotorDeLimitesNoDisponibleException : Exception
{
    public MotorDeLimitesNoDisponibleException()
    {
    }

    public MotorDeLimitesNoDisponibleException(string message)
        : base(message)
    {
    }

    public MotorDeLimitesNoDisponibleException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
