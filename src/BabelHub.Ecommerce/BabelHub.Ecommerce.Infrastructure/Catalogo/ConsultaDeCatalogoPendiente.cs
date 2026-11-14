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
