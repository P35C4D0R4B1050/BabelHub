using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>
/// El paso de restauración de la depuración (AUD-31) tal como lo ve el ejecutor de pasos, que es un singleton resuelto antes de abrir el servicio: la depuración y sus almacenes son por alcance y exigen el actor «sistema»
/// (<see cref="DepurarAuditoria"/> lo comprueba), así que cada ejecución crea su propio alcance, fija el actor «sistema» de forma explícita y lo limpia al terminar, aunque falle, como hacen los trabajos de fondo.
/// </summary>
public sealed class PasoDeRestauracionDeDepuracionEnAlcance(IServiceScopeFactory fabrica, IEstablecedorDeActor actor) : IPasoDeRestauracion
{
    public int Orden => OrdenDePasoDeRestauracion.Auditoria;

    public string Nombre => PasoDeRestauracionDeDepuracion.NombreDelPaso;

    public async Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default)
    {
        actor.Establecer(ActorActual.DelSistema());
        try
        {
            await using var alcance = fabrica.CreateAsyncScope();
            return await alcance.ServiceProvider.GetRequiredService<PasoDeRestauracionDeDepuracion>().Ejecutar(cancelacion).ConfigureAwait(false);
        }
        finally
        {
            actor.Limpiar();
        }
    }
}
