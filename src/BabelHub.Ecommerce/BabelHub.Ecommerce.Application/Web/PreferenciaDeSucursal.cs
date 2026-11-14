namespace BabelHub.Ecommerce.Application.Web;

/// <summary>Una sucursal de la lista que la persona puede elegir (código estable y nombre).</summary>
public sealed record SucursalDeLaLista(string Codigo, string Nombre);

/// <summary>La lista de sucursales y si es la de la consulta más reciente (<see cref="Actualizada"/>) o la última conocida.</summary>
public sealed record ListaDeSucursales(IReadOnlyList<SucursalDeLaLista> Sucursales, bool Actualizada);

/// <summary>Lo que el selector de sucursal necesita: la lista, cuál está elegida (solo si está en la lista) y de dónde viene.</summary>
public sealed record SucursalesParaElegir(string? CodigoElegido, IReadOnlyList<SucursalDeLaLista> Sucursales, bool ListaDisponible, bool ListaActualizada);

/// <summary>
/// Contrato público: la sucursal preferida del cliente entre sesiones (WEB-16, D16). El valor guardado en la cookie se valida contra la
/// lista de sucursales antes de usarse: un valor que no está en la lista no se usa nunca.
/// </summary>
public interface IPreferenciaDeSucursal
{
    Task<SucursalesParaElegir> Resolver(string? valorGuardado, CancellationToken cancelacion = default);

    /// <summary>El código si pertenece a la lista de sucursales; <see langword="null"/> si no, o si no hay ninguna lista conocida.</summary>
    Task<string?> Validar(string? codigo, CancellationToken cancelacion = default);
}
