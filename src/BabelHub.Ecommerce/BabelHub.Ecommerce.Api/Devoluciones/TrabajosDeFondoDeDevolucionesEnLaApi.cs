using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Api.Devoluciones;

/// <summary>
/// El anfitrión de los trabajos de fondo de Devoluciones en la Api (T25; DEV-14, DEV-21, DEV-27; AC-DEV-13, AC-DEV-29): la Infraestructura tiene los trabajos (plazo de recepción, conciliación de reembolsos y depuración) pero <b>no fija el actor</b> (la regla de
/// arquitectura lo reserva a la Api). Aquí se les da el alcance que fija «sistema» alrededor de cada corrida y limpia el actor al terminar, aunque falle. El servicio que los corre se registra <b>solo en el modo normal</b> (PLA-19: en <c>Restauracion</c> y <c>Ensayo</c>
/// no corren trabajos ni se llama al Inventario ni a la pasarela) y <b>con la fábrica</b>: la forma genérica lo construiría con una lista vacía de trabajos y no correría ninguno. No referencia nada de <c>Interno</c>.
/// </summary>
public static class TrabajosDeFondoDeDevolucionesEnLaApi
{
    /// <summary>Registra los trabajos de Devoluciones con el actor «sistema» y, si <paramref name="soloEnModoNormal"/>, el servicio que los corre. Se llama <b>después</b> de <c>AgregarDevoluciones</c>.</summary>
    public static IServiceCollection AgregarTrabajosDeDevolucionesEnLaApi(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AgregarTrabajosDeFondoDeDevoluciones(sp =>
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
            servicios.AddHostedService(sp => sp.GetRequiredService<ServicioDeTrabajosDeDevoluciones>());
        }

        return servicios;
    }
}
