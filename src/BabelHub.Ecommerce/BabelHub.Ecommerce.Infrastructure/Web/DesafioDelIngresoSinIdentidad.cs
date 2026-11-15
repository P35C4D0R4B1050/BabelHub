using BabelHub.Ecommerce.Application.Web;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// Implementación provisional de <see cref="IDesafioDelIngreso"/> hasta que exista el contrato de ingreso de Identidad (R12): no sabe nada de ningún desafío, así que ante una cookie de desafío
/// responde <see cref="PasoDelIngresoWeb.Desconocido"/> (la web falla cerrada y lleva al ingreso) y cancelar no hace nada. Un registro real anterior se respeta.
/// </summary>
public sealed class DesafioDelIngresoSinIdentidad : IDesafioDelIngreso
{
    public Task<PasoDelIngresoWeb> PasoPendiente(string valorDelDesafio, CancellationToken cancelacion = default) => Task.FromResult(PasoDelIngresoWeb.Desconocido);

    public Task Cancelar(string valorDelDesafio, CancellationToken cancelacion = default) => Task.CompletedTask;
}
