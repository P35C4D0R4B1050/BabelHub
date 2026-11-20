using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Api.Biblioteca;

/// <summary>
/// El anfitrión de los trabajos de fondo de Biblioteca en la Api (T18; BIB-26, BIB-29): la Infraestructura de Biblioteca tiene el ejecutor y los tres trabajos (purga horaria de claves y depuraciones diarias a las 03:30 de Lima) pero <b>no fija el actor</b> (la regla de arquitectura lo reserva a la Api).
/// Aquí se le da el <see cref="AlcanceDeSistemaDeBiblioteca"/> que fija «sistema» alrededor de cada corrida y limpia el actor al terminar, aunque falle. El servicio que los corre se registra <b>solo en el modo normal</b> (en <c>Restauracion</c> y <c>Ensayo</c> no corren trabajos).
/// No referencia nada de <c>Interno</c>.
/// </summary>
public static class TrabajosDeFondoDeBibliotecaEnLaApi
{
    /// <summary>Registra los trabajos de Biblioteca con el actor «sistema» y, si <paramref name="soloEnModoNormal"/>, el servicio que los corre. Se llama <b>después</b> de <c>AgregarBiblioteca</c>.</summary>
    public static IServiceCollection AgregarTrabajosDeBibliotecaEnLaApi(this IServiceCollection servicios, bool soloEnModoNormal)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.AgregarTrabajosDeFondoDeBiblioteca(sp =>
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
            servicios.AddHostedService(sp => sp.GetRequiredService<ServicioDeTrabajosDeBiblioteca>());
        }

        return servicios;
    }
}
