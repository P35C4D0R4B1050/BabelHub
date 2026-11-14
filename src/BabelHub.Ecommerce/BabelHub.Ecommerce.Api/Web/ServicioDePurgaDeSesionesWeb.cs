using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Web;

namespace BabelHub.Ecommerce.Api.Web;

/// <summary>
/// Servicio hospedado de la purga de sesiones y borradores (T13): ejecuta <see cref="TrabajoDePurgaDeSesionesWeb"/> como el actor «sistema» (la revocación de las renovaciones en
/// Identidad no es de ninguna persona). Solo se registra en el modo normal (<see cref="PurgaDeSesionesWebExtensiones.AgregarPurgaDeSesionesWeb"/>).
/// </summary>
public sealed class ServicioDePurgaDeSesionesWeb(TrabajoDePurgaDeSesionesWeb trabajo, IEstablecedorDeActor actor) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        actor.Establecer(ActorActual.DelSistema());
        await trabajo.Correr(stoppingToken).ConfigureAwait(false);
    }
}

public static class PurgaDeSesionesWebExtensiones
{
    /// <summary>
    /// Registra la purga como trabajo de fondo <b>solo en el modo normal</b>: en <c>Ensayo</c> y <c>Restauracion</c> queda apagada (PLA-19, PLA-43). Quien llama debe haber
    /// registrado <see cref="TrabajoDePurgaDeSesionesWeb"/> y <see cref="IEstablecedorDeActor"/>.
    /// </summary>
    public static IServiceCollection AgregarPurgaDeSesionesWeb(this IServiceCollection servicios, ModoDeArranque modo) =>
        servicios.AgregarTrabajoDeFondo<ServicioDePurgaDeSesionesWeb>(modo);
}
