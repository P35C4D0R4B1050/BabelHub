using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Api.Carrito;

/// <summary>
/// El anfitrión de los trabajos de fondo de Carrito en la Api (T29; CAR-29, CAR-34; AC-CAR-25, AC-CAR-30). La Infraestructura de Carrito tiene el ejecutor y los seis trabajos pero <b>no fija el actor</b> (la regla de arquitectura solo lo permite a Plataforma, a Auditoría y
/// a la Api): aquí se le da el <see cref="AlcanceDeSistema"/> que fija «sistema» alrededor de cada corrida y limpia el actor al terminar, aunque falle. El servicio que los corre se registra <b>solo en el modo normal</b>: en restauración o ensayo no corre ningún trabajo
/// (PLA-19). No referencia nada de <c>Interno</c>.
/// </summary>
public static class TrabajosDeFondoDelCarritoEnLaApi
{
    /// <summary>
    /// Registra los trabajos de Carrito con el actor «sistema» y, si <paramref name="soloEnModoNormal"/>, el servicio que los corre. Se llama <b>después</b> de <c>AgregarCarrito</c>. El anfitrión aporta <see cref="IEstablecedorDeActor"/>,
    /// <see cref="IRegistroDeTrabajosDeFondo"/>, <see cref="IReloj"/> y las alertas del carrito.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDelCarritoEnLaApi(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AgregarTrabajosDeFondoDelCarrito(sp =>
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
            servicios.AddHostedService(sp => sp.GetRequiredService<ServicioDeTrabajosDelCarrito>());
        }

        return servicios;
    }

}
