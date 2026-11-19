using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Notificaciones;

/// <summary>
/// <see cref="ICancelacionDeAvisos"/> provisional hasta que exista la persistencia de Notificaciones (<c>transversal-notificaciones</c> T09 en adelante): como todavía no se guarda ningún aviso, no hay nada que cancelar. <b>En producción
/// falla el arranque</b> (<see cref="AgregarNotificacionesPendientes"/>): cancelar avisos pendientes al cambiar el consentimiento o cancelar la cuenta (IDE-31, IDE-69) no puede ser un no-hacer silencioso.
/// </summary>
public sealed class CancelacionDeAvisosPendiente : ICancelacionDeAvisos
{
    public Task CancelarLosAvisosDe(Guid usuarioId, CancellationToken cancelacion = default) => Task.CompletedTask;
}

/// <summary>
/// <see cref="INotificador"/> provisional hasta que exista la persistencia de Notificaciones: <b>no guarda nada</b> y responde <see cref="ResultadoDeLaSolicitudDeAviso.NoSeGuardo"/>, que es lo que cada módulo ya trata sin interrumpir su operación
/// (NOT-04: un aviso que no se guardó no cancela el cambio de estado). <b>En producción falla el arranque</b>.
/// </summary>
public sealed class NotificadorPendiente : INotificador
{
    public Task<ResultadoDeLaSolicitudDeAviso> Solicitar(SolicitudDeAviso solicitud, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaSolicitudDeAviso.NoSeGuardo);
}

public static class NotificacionesPendientesExtensiones
{
    /// <summary>Registra el doble provisional (un registro real anterior se respeta) y, en producción, <b>falla nombrándolo</b> si es lo que quedó.</summary>
    public static IServiceCollection AgregarNotificacionesPendientes(this IServiceCollection servicios, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddScoped<ICancelacionDeAvisos, CancelacionDeAvisosPendiente>();
        servicios.TryAddScoped<INotificador, NotificadorPendiente>();
        if (esProduccion && servicios.Any(d => d.ServiceType == typeof(ICancelacionDeAvisos) && d.ImplementationType == typeof(CancelacionDeAvisosPendiente)))
        {
            throw new InvalidOperationException($"En producción {nameof(ICancelacionDeAvisos)} sigue con su doble provisional ({nameof(CancelacionDeAvisosPendiente)}): falta la persistencia de Notificaciones.");
        }

        if (esProduccion && servicios.Any(d => d.ServiceType == typeof(INotificador) && d.ImplementationType == typeof(NotificadorPendiente)))
        {
            throw new InvalidOperationException($"En producción {nameof(INotificador)} sigue con su doble provisional ({nameof(NotificadorPendiente)}): falta la persistencia de Notificaciones.");
        }

        return servicios;
    }
}
