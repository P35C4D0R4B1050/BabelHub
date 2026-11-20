using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>Registra la referencia del archivo de una edición digital que Catálogo entrega solo a Biblioteca (D13, D17): vive en el módulo Catálogo para que la prueba de arquitectura vea que nadie más depende de <see cref="IReferenciaDeArchivosDelCatalogo"/>.</summary>
internal static class RegistroDeLaReferenciaDeArchivos
{
    public static IServiceCollection AgregarLaReferenciaDeArchivos(this IServiceCollection servicios)
    {
        servicios.TryAddScoped<IReferenciaDeArchivosDelCatalogo, ReferenciaDeArchivosDelCatalogoDeLaAplicacion>();
        return servicios;
    }
}
