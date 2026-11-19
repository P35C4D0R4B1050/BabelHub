using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Envios;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Api.Envios;

/// <summary>
/// El anfitrión de los trabajos de fondo de Envíos en la Api (T17; ENV-07, ENV-11): la Infraestructura de Envíos tiene el ejecutor y los dos trabajos pero <b>no fija el actor</b> (la regla de arquitectura lo reserva a la Api).
/// Aquí se le da el <see cref="AlcanceDeSistemaDeEnvios"/> que fija «sistema» alrededor de cada corrida y limpia el actor al terminar, aunque falle. El servicio que los corre se registra <b>solo en el modo normal</b>
/// (PLA-19: en <c>Restauracion</c> y <c>Ensayo</c> no corren trabajos ni se llama al operador ni al Inventario). No referencia nada de <c>Interno</c>.
/// </summary>
public static class TrabajosDeFondoDeEnviosEnLaApi
{
    /// <summary>
    /// Registra los trabajos de Envíos con el actor «sistema» y, si <paramref name="soloEnModoNormal"/>, el servicio que los corre. Se llama <b>después</b> de <c>AgregarEnvios</c>. El anfitrión aporta
    /// <see cref="IEstablecedorDeActor"/>, <see cref="IRegistroDeTrabajosDeFondo"/>, <see cref="IReloj"/> y el publicador de alertas técnicas.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeEnviosEnLaApi(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AgregarTrabajosDeFondoDeEnvios(sp =>
        {
            var actor = sp.GetRequiredService<IEstablecedorDeActor>();
            return async (corrida, cancelacion) =>
            {
                // El actor se fija en la propia corrida: un trabajo sin actor es un defecto, no un permiso (los casos de uso lo rechazan).
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
        });

        if (soloEnModoNormal)
        {
            servicios.AddHostedService(sp => sp.GetRequiredService<ServicioDeTrabajosDeEnvios>());
        }

        return servicios;
    }
}
