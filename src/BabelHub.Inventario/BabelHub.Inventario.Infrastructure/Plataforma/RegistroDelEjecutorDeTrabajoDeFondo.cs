using BabelHub.Inventario.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>
/// Registro de <see cref="EjecutorDeTrabajoDeFondo"/>. Vive en la Infraestructura de <c>Plataforma</c> porque construirlo exige
/// <see cref="IEstablecedorDeActor"/>, y solo la Infraestructura de <c>Plataforma</c> y de <c>Auditoria</c> y la Api pueden fijar el actor
/// (<c>ReglasDelActorTests</c>): los módulos (por ejemplo <c>Fuentes</c>) piden este registro en lugar de tocar el puerto.
/// </summary>
public static class RegistroDelEjecutorDeTrabajoDeFondo
{
    /// <summary>Registra el ejecutor de trabajos de fondo como «sistema» (una sola vez, aunque varios módulos lo pidan).</summary>
    public static IServiceCollection AgregarEjecutorDeTrabajoDeFondo(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddSingleton<EjecutorDeTrabajoDeFondo>(sp => new EjecutorDeTrabajoDeFondo(sp.GetRequiredService<IEstablecedorDeActor>(), sp.GetRequiredService<IActorActual>()));
        return servicios;
    }
}
