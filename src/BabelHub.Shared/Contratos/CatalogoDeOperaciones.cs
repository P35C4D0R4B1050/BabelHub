using BabelHub.Shared.Contratos.Ecommerce;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.Shared.Contratos;

/// <summary>Todas las operaciones de los contratos v1 entre el E-commerce y el Inventario, en los dos sentidos.</summary>
public static class CatalogoDeOperaciones
{
    public static IReadOnlyList<OperacionDeContrato> V1 { get; } = [.. OperacionesDelInventario.Todas, .. OperacionesDelEcommerce.Todas];
}
