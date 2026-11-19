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
    private static ErrorDeAplicacion NoDisponible => new(CategoriaDeError.NoDisponible, "catalogo-no-disponible");

    public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ObraAlAzar?>.Fallo(NoDisponible));

    public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<PaginaDeObras>.Fallo(NoDisponible));

    public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ResumenDeObra>.Fallo(NoDisponible));

    public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<IReadOnlyList<EdicionConsultada>>.Fallo(NoDisponible));
}

/// <summary>
/// El retiro de una edición de los carritos mientras el módulo <c>Carrito</c> no exista (D07): no hace nada, porque no hay carritos. <b>Una prueba de arquitectura falla en cuanto exista una
/// implementación real</b> de <see cref="IRetiroDeEdicionDeCarritos"/>, para que esta se quite de la composición y no quede sombreando a la de <c>Carrito</c>.
/// </summary>
public sealed class RetiroDeEdicionDeCarritosNulo : IRetiroDeEdicionDeCarritos
{
    public Task Retirar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        return Task.CompletedTask;
    }
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

    /// <summary>
    /// Registra el retiro nulo de carritos (D07) como respaldo con <c>TryAdd</c>. La composición de <c>Carrito</c> registra el suyo con <c>Replace</c>, así que prevalece se componga antes o después; el nulo se puede quitar cuando Catálogo y Carrito se compongan siempre juntos.
    /// </summary>
    public static IServiceCollection AgregarRetiroNuloDeCarritos(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddSingleton<IRetiroDeEdicionDeCarritos, RetiroDeEdicionDeCarritosNulo>();
        return servicios;
    }
}
