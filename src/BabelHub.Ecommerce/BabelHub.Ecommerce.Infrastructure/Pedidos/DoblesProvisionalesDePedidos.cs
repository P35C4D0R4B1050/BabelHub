using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Pedidos.Interno;
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

/// <summary>La lectura de pedidos para Devoluciones sin Pedidos: ningún pedido está disponible (falla cerrado).</summary>
internal sealed class PedidosParaDevolucionesProvisional : IPedidosParaDevoluciones
{
    public Task<Resultado<PedidoParaDevoluciones>> Obtener(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<PedidoParaDevoluciones>.Fallo(FalloDeEnviosProvisional.NoDisponible()));

    public Task<Resultado<PaginaDePedidosDeUnaCuenta>> ListarDeUnaCuenta(Guid cuentaId, int pagina, int tamano, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<PaginaDePedidosDeUnaCuenta>.Fallo(FalloDeEnviosProvisional.NoDisponible()));
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

/// <summary>Los accesos de Biblioteca sin Biblioteca (ECO-8): no concede ni revoca nada y falla cerrado, de modo que una compra con digitales no crea el pedido y Pagos compensa.</summary>
internal sealed class AccesosDeBibliotecaProvisional : IAccesosDeBiblioteca
{
    public Task<Resultado<ResultadoDeConcederAcceso>> Conceder(ConcederAccesoEntrada entrada, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ResultadoDeConcederAcceso>.Fallo(FalloDeEnviosProvisional.NoDisponible()));

    public Task<Resultado<ResultadoDeRevocarAcceso>> Revocar(RevocarAccesoEntrada entrada, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<ResultadoDeRevocarAcceso>.Fallo(FalloDeEnviosProvisional.NoDisponible()));

    public Task<Resultado<bool>> EstaRevocadoPorReembolso(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default) =>
        Task.FromResult(Resultado<bool>.Fallo(FalloDeEnviosProvisional.NoDisponible()));
}

/// <summary>Las reservas del pedido sin el adaptador real del Inventario (hace falta <c>cerrar</c> en el cliente de Plataforma): cerrar es incierto y liberar «no se pudo», así que no se entrega ni se anula nada contra el stock.</summary>
internal sealed class ReservasDelPedidoProvisional : IReservasDelPedido
{
    public Task<ResultadoDeCerrarReserva> Cerrar(Guid pedidoId, Guid lineaId, string claveDeCierre, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeCerrarReserva.Incierto);

    public Task<ResultadoDeLiberarReserva> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineaIds, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLiberarReserva.NoSePudo);
}

/// <summary>La resolución de sucursales sin Identidad (D15 de <c>docs/03/08</c>): no resuelve ninguna, así que se niega el alcance del encargado (falla cerrado).</summary>
internal sealed class ResolucionDeSucursalProvisional : IResolucionDeSucursal
{
    public Task<Guid?> IdentificadorDe(string codigoDeSucursal, CancellationToken cancelacion = default) => Task.FromResult<Guid?>(null);
}

/// <summary>Los límites de Pedidos sin las políticas de Plataforma: las consultas, la gestión (que toca stock y reembolsos) y la lectura del contacto fallan cerrado (el límite protege contra la enumeración de identificadores).</summary>
internal sealed class LimitadorDePedidosProvisional : ILimitadorDePedidos
{
    public Task<ResultadoDeLimiteDePedidos> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDePedidos.NoDisponible);

    public Task<ResultadoDeLimiteDePedidos> Gestionar(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDePedidos.NoDisponible);

    public Task<ResultadoDeLimiteDePedidos> LeerContacto(Guid usuarioId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDePedidos.NoDisponible);
}

/// <summary>La auditoría de la lectura del contacto sin la acción del catálogo de Auditoría: lanza, así que el personal ve el detalle sin contacto (falla cerrado, D09).</summary>
internal sealed class AuditoriaDeContactoProvisional : IAuditoriaDeContacto
{
    public Task ContactoConsultado(Guid pedidoId, string rol, CancellationToken cancelacion = default) =>
        throw new InvalidOperationException("La acción de auditoría de la lectura del contacto de un pedido aún no existe en el catálogo.");
}

public static class ComposicionProvisionalDePedidos
{
    private static readonly (Type Puerto, Type Doble)[] Dobles =
    [
        (typeof(IDespachoDeEnvios), typeof(DespachoDeEnviosProvisional)),
        (typeof(IProgresoDeEnvios), typeof(ProgresoDeEnviosProvisional)),
        (typeof(IContactoDeEntregaDelPedido), typeof(ContactoDeEntregaProvisional)),
        (typeof(IPedidosParaEnvios), typeof(PedidosParaEnviosProvisional)),
        (typeof(IPedidosParaDevoluciones), typeof(PedidosParaDevolucionesProvisional)),
        (typeof(IAccesosDeBiblioteca), typeof(AccesosDeBibliotecaProvisional)),
        (typeof(IReservasDelPedido), typeof(ReservasDelPedidoProvisional)),
        (typeof(IResolucionDeSucursal), typeof(ResolucionDeSucursalProvisional)),
        (typeof(ILimitadorDePedidos), typeof(LimitadorDePedidosProvisional)),
        (typeof(IAuditoriaDeContacto), typeof(AuditoriaDeContactoProvisional)),
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
        servicios.TryAddSingleton<IPedidosParaDevoluciones, PedidosParaDevolucionesProvisional>();
        servicios.TryAddSingleton<IAccesosDeBiblioteca, AccesosDeBibliotecaProvisional>();
        servicios.TryAddSingleton<IReservasDelPedido, ReservasDelPedidoProvisional>();
        servicios.TryAddSingleton<IResolucionDeSucursal, ResolucionDeSucursalProvisional>();
        servicios.TryAddSingleton<ILimitadorDePedidos, LimitadorDePedidosProvisional>();
        servicios.TryAddSingleton<IAuditoriaDeContacto, AuditoriaDeContactoProvisional>();

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
