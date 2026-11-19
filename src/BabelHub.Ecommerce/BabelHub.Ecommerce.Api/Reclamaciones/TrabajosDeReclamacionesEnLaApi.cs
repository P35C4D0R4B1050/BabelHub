using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Reclamaciones;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Api.Reclamaciones;

/// <summary>
/// El anfitrión de los trabajos de fondo del Libro de Reclamaciones en la Api (T16; REC-17, AC-REC-15): la Infraestructura tiene el ejecutor y los dos trabajos pero <b>no fija el actor «sistema»</b>; aquí se le da el <see cref="AlcanceDeSistemaDeReclamaciones"/> que lo fija alrededor de cada corrida y
/// lo limpia al terminar, aunque falle. El servicio que los corre se registra <b>solo en modo normal</b> (en <c>Restauracion</c> y <c>Ensayo</c> no corre ningún trabajo, PLA-19). No referencia nada de <c>Interno</c>.
/// </summary>
public static class TrabajosDeReclamacionesEnLaApi
{
    /// <summary>Registra los trabajos con el actor «sistema» y, si <paramref name="soloEnModoNormal"/>, el servicio que los corre. Se llama <b>después</b> de <c>AgregarReclamaciones</c>; el anfitrión ya registró <see cref="IEstablecedorDeActor"/>.</summary>
    public static IServiceCollection AgregarTrabajosDeReclamacionesEnLaApi(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AgregarTrabajosDeFondoDeReclamaciones(sp => AlcanceDeSistema(sp.GetRequiredService<IEstablecedorDeActor>()));
        if (soloEnModoNormal)
        {
            servicios.AddHostedService(sp => sp.GetRequiredService<ServicioDeTrabajosDeReclamaciones>());
        }

        return servicios;
    }

    /// <summary>El alcance de cada corrida: fija «sistema» alrededor de ella y limpia el actor al terminar, aunque falle. Un trabajo sin actor es un defecto, no un permiso (el caso de uso lo rechaza).</summary>
    public static AlcanceDeSistemaDeReclamaciones AlcanceDeSistema(IEstablecedorDeActor actor)
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
