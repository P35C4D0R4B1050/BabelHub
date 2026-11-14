using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// Implementación provisional de <see cref="IConsultaDeCatalogo"/> hasta que el módulo Catálogo exista (<c>ecommerce-catalogo</c>, ECO-2): siempre
/// responde «no disponible». No simula obras ni stock; el registro real la reemplaza.
/// </summary>
public sealed class ConsultaDeCatalogoPendiente : IConsultaDeCatalogo
{
    public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ObraAlAzar?>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "catalogo-no-disponible")));
}

public static class CatalogoPendienteExtensiones
{
    /// <summary>Registra la implementación provisional hasta que ECO-2 registre la real (con <c>Replace</c> o antes de esta llamada: <c>TryAdd</c> respeta lo ya registrado).</summary>
    public static IServiceCollection AgregarCatalogoPendiente(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddScoped<IConsultaDeCatalogo, ConsultaDeCatalogoPendiente>();
        return servicios;
    }
}
