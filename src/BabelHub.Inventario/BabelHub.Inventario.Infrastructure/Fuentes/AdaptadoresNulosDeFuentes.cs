using System.Reflection;
using BabelHub.Inventario.Application.Fuentes;

namespace BabelHub.Inventario.Infrastructure.Fuentes;

/// <summary>
/// Un adaptador nulo de un puerto que <c>Fuentes</c> declara y que implementa otro módulo del Inventario (<c>Reservas</c> o <c>Ajustes</c>), hasta que ese módulo exista. Un nulo responde «nada»: sin
/// reservas vencidas, sin ventas y sin reingresos. Eso es correcto mientras el módulo real no exista y <b>silenciosamente incorrecto</b> si ya existe y se sigue usando el nulo, así que la composición de producción
/// lo rechaza (<see cref="ComposicionDeFuentes.ExigirAdaptadoresReales"/>).
/// </summary>
internal interface IAdaptadorNuloDeFuentes
{
    /// <summary>El puerto que sustituye.</summary>
    Type Puerto { get; }
}

/// <summary>Sin <c>Reservas</c> no hay reservas: ninguna vencida, ninguna liberada, ningún pedido que aparte unidades.</summary>
internal sealed class NuloReservasDeStock : IReservasDeStock, IAdaptadorNuloDeFuentes
{
    public Type Puerto => typeof(IReservasDeStock);

    public Task<IReadOnlyList<UnidadesVencidas>> VencidasPorEdicion(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<UnidadesVencidas>>([]);

    public Task<IReadOnlyList<UnidadesLiberadas>> LiberarVencidas(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<UnidadesLiberadas>>([]);

    public Task<PedidosQueApartanUnidades> PedidosQueApartan(Guid fuenteId, Guid edicionId, int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult(new PedidosQueApartanUnidades([], 0));
}

/// <summary>Sin <c>Reservas</c> no hay pedidos pagados: ninguna venta.</summary>
internal sealed class NuloOrigenDeUnidadesVendidas : IOrigenDeUnidadesVendidas, IAdaptadorNuloDeFuentes
{
    public Type Puerto => typeof(IOrigenDeUnidadesVendidas);

    public Task<IReadOnlyList<VentaAtribuida>> Ventas(Guid? fuenteId, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<VentaAtribuida>>([]);
}

/// <summary>Sin <c>Ajustes</c> no hay reingresos por devolución: ninguno.</summary>
internal sealed class NuloReingresosPorDevolucion : IReingresosPorDevolucion, IAdaptadorNuloDeFuentes
{
    public Type Puerto => typeof(IReingresosPorDevolucion);

    public Task<IReadOnlyList<UnidadesReingresadas>> PorPedidos(IReadOnlyCollection<Guid> pedidoIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<UnidadesReingresadas>>([]);
}

internal static class AdaptadoresNulosDeFuentes
{
    /// <summary>
    /// <see langword="true"/> si en <paramref name="ensamblado"/> existe una clase concreta, distinta de un adaptador nulo, que implementa <paramref name="puerto"/>: el módulo real ya se construyó y
    /// el nulo ya no debe usarse. Se mira el ensamblado de Infraestructura porque ahí vivirán los adaptadores reales.
    /// </summary>
    public static bool ExisteAdaptadorReal(Type puerto, Assembly ensamblado)
    {
        ArgumentNullException.ThrowIfNull(puerto);
        ArgumentNullException.ThrowIfNull(ensamblado);

        return ensamblado.GetTypes().Any(t =>
            t is { IsClass: true, IsAbstract: false }
            && puerto.IsAssignableFrom(t)
            && !typeof(IAdaptadorNuloDeFuentes).IsAssignableFrom(t));
    }
}
