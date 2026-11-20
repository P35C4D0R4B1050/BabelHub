using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Api.Pedidos;

/// <summary>
/// El anfitrión de los trabajos de fondo de Pedidos en la Api (T32; PED-19, PED-24, PED-39, PED-40): la Infraestructura tiene los cinco trabajos pero <b>no fija el actor</b> (la regla de arquitectura lo reserva a la Api).
/// Aquí se les da el alcance que fija «sistema» alrededor de cada corrida y limpia el actor al terminar, aunque falle. El servicio que los corre se registra <b>solo en el modo normal</b> (PLA-19: en <c>Restauracion</c> y
/// <c>Ensayo</c> no corren trabajos ni se llama al Inventario ni a la pasarela). No referencia nada de <c>Interno</c>.
/// </summary>
public static class TrabajosDeFondoDePedidosEnLaApi
{
    /// <summary>Registra los trabajos de Pedidos con el actor «sistema» y, si <paramref name="soloEnModoNormal"/>, el servicio que los corre. Se llama <b>después</b> de <c>AgregarPedidos</c>.</summary>
    public static IServiceCollection AgregarTrabajosDePedidosEnLaApi(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AgregarTrabajosDeFondoDePedidos(sp =>
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
            servicios.AddHostedService(sp => sp.GetRequiredService<ServicioDeTrabajosDePedidos>());
        }

        return servicios;
    }
}
