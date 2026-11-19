using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Envios;
using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Envios;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// La composición del módulo Envíos (T15; <c>ecommerce-envios</c>): el repositorio de PostgreSQL bajo <c>EjecutarComo(envios)</c>, los casos de uso por alcance de solicitud, las implementaciones públicas
/// <see cref="IEnviosDeLogistica"/>, <see cref="IEnviosDelCliente"/> e <see cref="IDespachoDeEnvios"/> y, al final, lo que otros módulos aún no entregan. <b>Envíos es la implementación real de <see cref="IDespachoDeEnvios"/></b>:
/// reemplaza al doble provisional de Pedidos esté o no ya registrado (sin referenciar el tipo del doble). <b>Orden en producción:</b> <c>AgregarEnvios</c> va <b>antes</b> de <c>AgregarDoblesProvisionalesDePedidos(esProduccion: true)</c> (su guarda exige que las implementaciones reales ya estén) y, cuando exista, el cliente del Inventario (<see cref="IClienteDelInventario"/>) se registra <b>antes</b> de <c>AgregarEnvios</c>: si está, el cierre de reservas es <c>RestReservasDelEnvio</c> (T13); si no, el doble provisional y la guarda de producción lo rechaza. El operador logístico lo registra aparte
/// <see cref="ComposicionDelOperadorLogistico.AgregarOperadorLogisticoDeEnvios"/> (el nulo por defecto). Necesita ya registrados: <see cref="IUnidadDeTrabajo"/>, <see cref="IConexionDeLaUnidadDeTrabajo"/>, <see cref="IActorActual"/>,
/// <see cref="IDenegacionDeAcceso"/>, <see cref="IReloj"/>, <see cref="IControlDeLimites"/>, <see cref="BabelHub.Ecommerce.Application.Auditoria.IAuditoria"/>, <see cref="IPublicadorDeAlertasTecnicas"/> y
/// <see cref="BabelHub.Ecommerce.Application.Notificaciones.INotificador"/>, más los contratos de lectura de Pedidos (reales o provisionales).
/// </summary>
public static class ComposicionDeEnvios
{
    public static IServiceCollection AgregarEnvios(this IServiceCollection servicios, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);

        // ---------- Infraestructura (por solicitud: usa la transacción en curso) ----------
        servicios.TryAddScoped<AccesoAlEsquemaDeEnvios>();
        servicios.TryAddScoped<IRepositorioDeEnvios, RepositorioDeEnviosPostgres>();

        // ---------- Aplicación ----------
        servicios.TryAddScoped(sp => new AutorizadorDeEnvios(sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IDenegacionDeAcceso>()));
        servicios.TryAddScoped(sp => new LimitesDeEnvios(sp.GetRequiredService<IControlDeLimites>(), sp.GetRequiredService<IActorActual>()));
        servicios.TryAddScoped<AvisosDeEnvios>();
        servicios.TryAddScoped<InformadorDeProgreso>();
        servicios.TryAddScoped<CrearEnvio>();
        servicios.TryAddScoped<CancelarEnviosDelPedido>();
        servicios.TryAddScoped<SolicitarGuia>();
        servicios.TryAddScoped<PasarAEnReparto>();
        servicios.TryAddScoped<MarcarEntregado>();
        servicios.TryAddScoped<ListarEnvios>();
        servicios.TryAddScoped<ObtenerEnvio>();
        servicios.TryAddScoped<ObtenerEnviosDeUnPedido>();
        servicios.TryAddScoped<InformarProgresoAPedidos>();
        servicios.TryAddScoped<RevisarEnviosEstancados>();

        // ---------- Contratos públicos ----------
        servicios.TryAddScoped<IEnviosDeLogistica, EnviosDeLogisticaDeLaAplicacion>();
        servicios.TryAddScoped<IEnviosDelCliente, EnviosDelClienteDeLaAplicacion>();
        servicios.TryAddScoped<DespachoDeEnviosDeLaAplicacion>();

        // Envíos es la implementación real del despacho que declara Pedidos: reemplaza al doble provisional aunque ya esté registrado.
        servicios.Replace(ServiceDescriptor.Scoped<IDespachoDeEnvios>(sp => sp.GetRequiredService<DespachoDeEnviosDeLaAplicacion>()));

        // Sin un operador elegido por el anfitrión (guarda de arranque), el nulo: falla cerrado con «no disponible» en vez de un error de resolución.
        servicios.TryAddSingleton<IOperadorLogistico, NuloOperadorLogistico>();

        // El cierre de reservas del envío sobre el cliente del Inventario de Plataforma, si ya está registrado (T13); si no, queda el doble provisional que falla cerrado y la guarda de producción lo nombra.
        if (servicios.Any(d => d.ServiceType == typeof(IClienteDelInventario)))
        {
            servicios.TryAddScoped<IReservasDelEnvio, RestReservasDelEnvio>();
        }

        // ---------- Lo que aún no existe, con su guarda de producción ----------
        servicios.TryAddScoped<IReservasDelEnvio, ReservasDelEnvioProvisional>();
        if (esProduccion && servicios.Any(d => d.ServiceType == typeof(IReservasDelEnvio) && d.ImplementationType == typeof(ReservasDelEnvioProvisional)))
        {
            throw new ConfiguracionDeEnviosInvalidaException("Envíos: el cierre de reservas del Inventario todavía es el doble provisional (falla cerrado) y no se publica con él.");
        }

        return servicios;
    }
}

public static class ComposicionDeLosTrabajosDeEnvios
{
    /// <summary>
    /// Registra los dos trabajos de fondo de Envíos y el servicio que los corre. <paramref name="alcanceDeSistema"/> lo construye el anfitrión —la Api— con sus servicios y fija el actor «sistema» alrededor de cada corrida.
    /// Se llama <b>solo en modo normal</b> y después de <see cref="ComposicionDeEnvios.AgregarEnvios"/>; quien la llame añade también <c>AddHostedService&lt;ServicioDeTrabajosDeEnvios&gt;()</c>.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeFondoDeEnvios(this IServiceCollection servicios, Func<IServiceProvider, AlcanceDeSistemaDeEnvios> alcanceDeSistema)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        servicios.TryAddSingleton(sp => TrabajosDeEnvios.Todos(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            alcanceDeSistema(sp),
            sp.GetRequiredService<IPublicadorDeAlertasTecnicas>(),
            sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton(sp => new ServicioDeTrabajosDeEnvios(sp.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeEnvios>>()));
        return servicios;
    }
}

/// <summary>El cierre de reservas sin el adaptador del Inventario: falla cerrado con un resultado incierto (el envío queda en guía generada y se reintenta). Lo sustituye <c>RestReservasDelEnvio</c> (T13).</summary>
internal sealed class ReservasDelEnvioProvisional : IReservasDelEnvio
{
    public Task<ResultadoDeCierreDeReservas> Cerrar(Guid pedidoId, Guid envioId, IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default) =>
        Task.FromResult<ResultadoDeCierreDeReservas>(new ResultadoDeCierreDeReservas.Incierto());
}
