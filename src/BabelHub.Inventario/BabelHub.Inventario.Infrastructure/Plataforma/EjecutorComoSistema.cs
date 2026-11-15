using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>
/// <see cref="IEjecutorComoSistema"/>: fija <see cref="ActorActual.DelSistema"/> mientras dura el trabajo y vuelve al actor anterior (o a ninguno) al terminar, también si falla. Vive en la Infraestructura de
/// <c>Plataforma</c>, la única que puede fijar el actor (junto con la de <c>Auditoria</c> y la Api).
/// </summary>
public sealed class EjecutorComoSistema(IEstablecedorDeActor establecedor, IActorActual actores) : IEjecutorComoSistema
{
    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(trabajo);

        var anterior = actores.Actual;
        establecedor.Establecer(ActorActual.DelSistema());
        try
        {
            return await trabajo(cancelacion).ConfigureAwait(false);
        }
        finally
        {
            if (anterior is null)
            {
                establecedor.Limpiar();
            }
            else
            {
                establecedor.Establecer(anterior);
            }
        }
    }
}
