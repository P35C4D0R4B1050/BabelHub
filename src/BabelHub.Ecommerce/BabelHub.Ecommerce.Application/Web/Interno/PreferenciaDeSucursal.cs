namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>
/// Puerto: la lista de sucursales (la fuente es el Inventario por REST, <c>sucursales.listar</c>, con su última lista conocida). Devuelve
/// <see langword="null"/> o falla si no hay ninguna lista conocida.
/// </summary>
public interface IListaDeSucursales
{
    Task<ListaDeSucursales?> Obtener(CancellationToken cancelacion = default);
}

/// <summary>Implementa <see cref="IPreferenciaDeSucursal"/>: valida contra la lista y nunca afirma una sucursal que no está en ella.</summary>
public sealed class PreferenciaDeSucursal(IListaDeSucursales lista) : IPreferenciaDeSucursal
{
    public async Task<SucursalesParaElegir> Resolver(string? valorGuardado, CancellationToken cancelacion = default)
    {
        var conocida = await Leer(cancelacion).ConfigureAwait(false);
        if (conocida is null)
        {
            return new SucursalesParaElegir(null, [], false, false);
        }

        var elegida = Contiene(conocida, valorGuardado) ? valorGuardado : null;
        return new SucursalesParaElegir(elegida, conocida.Sucursales, true, conocida.Actualizada);
    }

    public async Task<string?> Validar(string? codigo, CancellationToken cancelacion = default)
    {
        var conocida = await Leer(cancelacion).ConfigureAwait(false);
        return conocida is not null && Contiene(conocida, codigo) ? codigo : null;
    }

    private static bool Contiene(ListaDeSucursales conocida, string? codigo) =>
        !string.IsNullOrEmpty(codigo) && conocida.Sucursales.Any(s => string.Equals(s.Codigo, codigo, StringComparison.Ordinal));

    private async Task<ListaDeSucursales?> Leer(CancellationToken cancelacion)
    {
        try
        {
            var conocida = await lista.Obtener(cancelacion).ConfigureAwait(false);
            return conocida is { Sucursales.Count: > 0 } ? conocida : null;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return null;
        }
    }
}
