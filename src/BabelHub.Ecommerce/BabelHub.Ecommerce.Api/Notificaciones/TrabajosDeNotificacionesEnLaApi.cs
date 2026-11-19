using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Api.Notificaciones;

/// <summary>
/// El anfitrión de los trabajos de fondo de Notificaciones en la Api (T14; NOT-04, PLA-19, SEG-15): la Infraestructura tiene los trabajos (envío y purga) pero <b>no fija el actor «sistema»</b>; aquí se le da el
/// <see cref="AlcanceDeSistemaDeNotificaciones"/>, que lo fija alrededor de cada corrida y lo limpia al terminar, aunque falle. El servicio que los corre se registra <b>solo en modo normal</b>: en <c>Restauracion</c> y <c>Ensayo</c> no
/// corre ningún trabajo (los avisos pendientes restaurados traen números reales y no deben enviarse hasta pasar la revalidación). No referencia nada de <c>Interno</c>.
/// </summary>
public static class TrabajosDeNotificacionesEnLaApi
{
    /// <summary>Registra los trabajos con el actor «sistema» y, si <paramref name="soloEnModoNormal"/>, el servicio que los corre. Se llama <b>después</b> de <c>AgregarNotificaciones</c>.</summary>
    public static IServiceCollection AgregarTrabajosDeNotificacionesEnLaApi(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        if (!soloEnModoNormal)
        {
            return servicios;
        }

        servicios.AgregarTrabajosDeNotificaciones(sp => AlcanceDeSistema(sp.GetRequiredService<IEstablecedorDeActor>()));
        servicios.AddHostedService(sp => sp.GetRequiredService<ServicioDeTrabajosDeNotificaciones>());
        return servicios;
    }

    /// <summary>El alcance de cada corrida: fija «sistema» alrededor de ella y limpia el actor al terminar, aunque falle.</summary>
    public static AlcanceDeSistemaDeNotificaciones AlcanceDeSistema(IEstablecedorDeActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return async (corrida, cancelacion) =>
        {
            actor.Establecer(ActorActual.DelSistema());
            try
            {
                await corrida(cancelacion).ConfigureAwait(false);
            }
            finally
            {
                actor.Limpiar();
            }
        };
    }
}
