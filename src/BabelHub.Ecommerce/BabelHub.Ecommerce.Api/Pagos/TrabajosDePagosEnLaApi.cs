using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Api.Pagos;

/// <summary>
/// El anfitrión de los trabajos de fondo de Pagos en la Api (T29; PAG-18, AC-PAG-08): la Infraestructura tiene el ejecutor y los nueve trabajos pero <b>no fija el actor «sistema»</b>; aquí se le da el <see cref="AlcanceDeSistemaDePagos"/> que lo fija alrededor de cada
/// corrida y lo limpia al terminar, aunque falle. El servicio que los corre se registra <b>solo en modo normal</b> (en <c>Restauracion</c> y <c>Ensayo</c> no corre ningún trabajo, PLA-19). No referencia nada de <c>Interno</c>.
/// </summary>
public static class TrabajosDePagosEnLaApi
{
    /// <summary>
    /// Registra los trabajos de Pagos con el actor «sistema» y, si <paramref name="soloEnModoNormal"/>, el servicio que los corre. Se llama <b>después</b> de <c>AgregarPagos</c>. El anfitrión ya registró <see cref="IEstablecedorDeActor"/>,
    /// <see cref="IRegistroDeTrabajosDeFondo"/>, <see cref="IReloj"/> y el publicador de alertas técnicas.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDePagosEnLaApi(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AgregarTrabajosDeFondoDePagos(sp => AlcanceDeSistema(sp.GetRequiredService<IEstablecedorDeActor>()));

        if (soloEnModoNormal)
        {
            servicios.AddHostedService(sp => sp.GetRequiredService<ServicioDeTrabajosDePagos>());
        }

        return servicios;
    }

    /// <summary>El alcance de cada corrida: fija «sistema» alrededor de ella y limpia el actor al terminar, aunque falle. Un trabajo sin actor es un defecto, no un permiso (los casos de uso lo rechazan).</summary>
    public static AlcanceDeSistemaDePagos AlcanceDeSistema(IEstablecedorDeActor actor)
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
