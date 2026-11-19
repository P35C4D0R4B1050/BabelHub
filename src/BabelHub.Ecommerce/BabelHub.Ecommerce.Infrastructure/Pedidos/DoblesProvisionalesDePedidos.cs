using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Pedidos;

// Dobles provisionales de los contratos de Pedidos que consume Envíos (ecommerce-pedidos T07/T08; ecommerce-envios D01). Mientras no existan las implementaciones reales (Pedidos las trae con ECO-6; Envíos implementa
// `IDespachoDeEnvios` con ECO-9), todos **fallan cerrado**: nada se despacha, se avanza ni se lee un contacto sin la implementación real, y la guarda de producción impide publicar con uno de ellos.

internal static class FalloDeEnviosProvisional
{
    public static ErrorDeAplicacion NoDisponible() => new(CategoriaDeError.NoDisponible, "servicio-no-disponible");
}

/// <summary>El despacho de envíos sin Envíos: no crea ni cancela nada (falla cerrado).</summary>
internal sealed class DespachoDeEnviosProvisional : IDespachoDeEnvios
{
    public Task<Resultado<ResultadoDeCrearEnvio>> Crear(CrearEnvioEntrada entrada, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ResultadoDeCrearEnvio>.Fallo(FalloDeEnviosProvisional.NoDisponible()));

    public Task<Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>> Cancelar(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>>.Fallo(FalloDeEnviosProvisional.NoDisponible()));
}

/// <summary>El progreso de los envíos sin Pedidos: no avanza ninguna línea (falla cerrado; Envíos lo reintenta).</summary>
internal sealed class ProgresoDeEnviosProvisional : IProgresoDeEnvios
{
    public Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEnReparto(EnvioEnRepartoEntrada entrada, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ProgresoDeEnvioAplicado>.Fallo(FalloDeEnviosProvisional.NoDisponible()));

    public Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEntregado(EnvioEntregadoEntrada entrada, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ProgresoDeEnvioAplicado>.Fallo(FalloDeEnviosProvisional.NoDisponible()));
}

/// <summary>El contacto de entrega sin Pedidos: nunca hay contacto (la lectura es «no disponible», reintentable).</summary>
internal sealed class ContactoDeEntregaProvisional : IContactoDeEntregaDelPedido
{
    public Task<LecturaDelContactoDeEntrega> Leer(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult(LecturaDelContactoDeEntrega.NoDisponible());
}

/// <summary>La lectura de pedidos para Envíos sin Pedidos: ningún pedido está disponible (falla cerrado).</summary>
internal sealed class PedidosParaEnviosProvisional : IPedidosParaEnvios
{
    public Task<Resultado<PedidoParaEnvios>> Obtener(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<PedidoParaEnvios>.Fallo(FalloDeEnviosProvisional.NoDisponible()));
}

public static class ComposicionProvisionalDePedidos
{
    private static readonly (Type Puerto, Type Doble)[] Dobles =
    [
        (typeof(IDespachoDeEnvios), typeof(DespachoDeEnviosProvisional)),
        (typeof(IProgresoDeEnvios), typeof(ProgresoDeEnviosProvisional)),
        (typeof(IContactoDeEntregaDelPedido), typeof(ContactoDeEntregaProvisional)),
        (typeof(IPedidosParaEnvios), typeof(PedidosParaEnviosProvisional)),
    ];

    /// <summary>
    /// Registra los dobles provisionales de los contratos de Pedidos que consume Envíos con <c>TryAdd</c> (no pisan a las implementaciones reales registradas antes). Con <paramref name="esProduccion"/>, si cualquiera de los puertos
    /// termina resuelto por un doble provisional, el arranque falla nombrando cuál: nunca se publica con un despacho, un progreso o un contacto simulados. Se llama <b>después</b> de registrar las implementaciones reales.
    /// </summary>
    public static IServiceCollection AgregarDoblesProvisionalesDePedidos(this IServiceCollection servicios, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddSingleton<IDespachoDeEnvios, DespachoDeEnviosProvisional>();
        servicios.TryAddSingleton<IProgresoDeEnvios, ProgresoDeEnviosProvisional>();
        servicios.TryAddSingleton<IContactoDeEntregaDelPedido, ContactoDeEntregaProvisional>();
        servicios.TryAddSingleton<IPedidosParaEnvios, PedidosParaEnviosProvisional>();

        if (esProduccion)
        {
            ExigirQueNingunoSeaProvisional(servicios);
        }

        return servicios;
    }

    private static void ExigirQueNingunoSeaProvisional(IServiceCollection servicios)
    {
        var provisionales = Dobles
            .Where(d => servicios.Any(s => s.ServiceType == d.Puerto && (s.ImplementationType == d.Doble || s.ImplementationInstance?.GetType() == d.Doble)))
            .Select(d => d.Puerto.Name)
            .ToList();
        if (provisionales.Count > 0)
        {
            throw new InvalidOperationException(
                $"En producción no se permiten dobles provisionales de Pedidos. Falta la implementación real de: {string.Join(", ", provisionales)}.");
        }
    }
}
