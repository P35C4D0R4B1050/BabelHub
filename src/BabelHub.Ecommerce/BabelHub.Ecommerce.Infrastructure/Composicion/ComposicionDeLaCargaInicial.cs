using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

public static class ComposicionDeLaCargaInicial
{
    /// <summary>
    /// Agrega la carga inicial del catálogo (transversal-carga-inicial): el generador del archivo de muestra, el caso de uso con el registro de la corrida en su propia transacción y <b>reemplaza</b> el Inventario del catálogo por el diferido
    /// (D04: la carga no llama al Inventario). Solo la llama la orden <c>cargar-catalogo</c>, después de <see cref="ComposicionDelCatalogo.AgregarCatalogo"/>; nunca la composición del servidor que escucha.
    /// </summary>
    public static IServiceCollection AgregarCargaInicialDelCatalogo(this IServiceCollection servicios)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.Replace(ServiceDescriptor.Scoped<IInventarioDelCatalogo, InventarioDiferidoDeLaCarga>());
        servicios.TryAddSingleton<IGeneradorDeLibroDeMuestra, GeneradorDeLibroDeMuestra>();
        servicios.TryAddScoped<ICasoDeUso<CorridaDeCargaDelCatalogo, bool>>(sp => new TransaccionPorCasoDeUso<CorridaDeCargaDelCatalogo, bool>(
            new RegistrarCorridaDeCargaDelCatalogo(sp.GetRequiredService<AutorizadorDelCatalogo>(), sp.GetRequiredService<IAuditoria>()),
            sp.GetRequiredService<IUnidadDeTrabajo>()));
        servicios.TryAddScoped<ICargaInicialDelCatalogo>(sp => new CargarCatalogoInicial(
            sp.GetRequiredService<AutorizadorDelCatalogo>(),
            sp.GetRequiredService<IAdministracionDelCatalogo>(),
            sp.GetRequiredService<IArchivosDelCatalogo>(),
            sp.GetRequiredService<IGeneradorDeLibroDeMuestra>(),
            sp.GetRequiredService<ICasoDeUso<CorridaDeCargaDelCatalogo, bool>>()));
        return servicios;
    }
}
