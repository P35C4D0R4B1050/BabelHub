namespace BabelHub.Shared.Contratos.Ecommerce;

/// <summary>Operaciones que publica el E-commerce (<c>/api/ecommerce/v1/integraciones</c>) y consume el Inventario.</summary>
public static class OperacionesDelEcommerce
{
    public static IReadOnlyList<OperacionDeContrato> Todas { get; } = [];
}
