using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// <see cref="IContextoDelAsistente"/> sobre el contrato público de Identidad (<see cref="IContextoDeLectura"/>): <b>sin caché</b> (cada consulta pide un contexto nuevo), solo para el cliente de la sesión y con el
/// mismo error que Identidad cuando no se puede (la web no decide quién puede leer qué: lo decide Identidad, que además lo compara con el actor de la solicitud).
/// </summary>
public sealed class ContextoDelAsistenteDeLaWeb(IContextoDeLectura identidad) : IContextoDelAsistente
{
    private static readonly ErrorDeAplicacion Prohibido = new(CategoriaDeError.Prohibido, "acceso-denegado");

    public async Task<Resultado<ValorSecreto>> Pedir(ActorVerificado actor, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (actor.Rol != RolWeb.Cliente || actor.UsuarioId == Guid.Empty)
        {
            return Resultado<ValorSecreto>.Fallo(Prohibido);
        }

        return await identidad.Emitir(actor.UsuarioId, cancelacion).ConfigureAwait(false);
    }
}
