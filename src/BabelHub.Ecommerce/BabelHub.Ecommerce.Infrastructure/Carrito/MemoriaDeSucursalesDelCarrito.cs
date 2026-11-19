using BabelHub.Ecommerce.Application.Carrito.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// La última lista de sucursales que respondió el Inventario (R05, T26b): un singleton que comparten todas las solicitudes, porque el adaptador del Inventario se resuelve por alcance y su respaldo no debe perderse con cada solicitud.
/// </summary>
public sealed class MemoriaDeSucursalesDelCarrito
{
    private readonly Lock _candado = new();
    private IReadOnlyList<SucursalDeEntrega>? _lista;
    private DateTimeOffset _obtenidaEn;

    public void Guardar(IReadOnlyList<SucursalDeEntrega> lista, DateTimeOffset obtenidaEn)
    {
        ArgumentNullException.ThrowIfNull(lista);
        lock (_candado)
        {
            _lista = lista;
            _obtenidaEn = obtenidaEn;
        }
    }

    /// <summary>La última lista y cuándo se obtuvo; <see langword="null"/> si nunca hubo una.</summary>
    public (IReadOnlyList<SucursalDeEntrega> Lista, DateTimeOffset ObtenidaEn)? Leer()
    {
        lock (_candado)
        {
            return _lista is { } lista ? (lista, _obtenidaEn) : null;
        }
    }
}
