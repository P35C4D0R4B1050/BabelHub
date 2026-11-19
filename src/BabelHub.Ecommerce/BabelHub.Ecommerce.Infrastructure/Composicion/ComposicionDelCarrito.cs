using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// La composición del módulo Carrito (T26b; <c>ecommerce-carrito-checkout</c>): repositorios de PostgreSQL bajo <c>EjecutarComo(carrito)</c>, el adaptador del Inventario sobre <see cref="IClienteDelInventario"/>, los casos de uso por alcance de solicitud, las
/// implementaciones públicas <see cref="ICarritoDelCliente"/>, <see cref="ICheckoutDelCliente"/> e <see cref="IProgresoDelCheckout"/>, los receptores de Identidad (reemplazo y cancelación de cuenta), <b>el retiro de ediciones de Catálogo</b> (reemplaza al retiro
/// nulo, esté o no ya registrado) y los seis trabajos de fondo con su ejecutor (que la Api hospeda en T29). Lo que otros módulos aún no entregan se registra al final con <c>AgregarDoblesProvisionalesDelCarrito</c>, que en producción <b>falla al arrancar</b> si algún
/// puerto sigue con un doble provisional. Necesita ya registrados: <see cref="IConsultaDeCatalogo"/>, <see cref="IDisponibilidadDeEdiciones"/>, <see cref="IUnidadDeTrabajo"/>, <see cref="IConexionDeLaUnidadDeTrabajo"/>, <see cref="IActorActual"/>,
/// <see cref="IDenegacionDeAcceso"/>, <see cref="IReloj"/>, <see cref="IClienteDelInventario"/>, <see cref="ITextosLegales"/>, <see cref="ICalculadorDeHuella"/>, <see cref="IRegistroDeTrabajosDeFondo"/> e <see cref="IEstablecedorDeActor"/>.
/// </summary>
public static class ComposicionDelCarrito
{
    public static IServiceCollection AgregarCarrito(this IServiceCollection servicios, AppSettingsOpcionesDeEnvio costoDeEnvio, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(costoDeEnvio);

        // ---------- Infraestructura (singletons: sin estado por solicitud) ----------
        servicios.TryAddSingleton<ICostoDeEnvioConfigurado>(costoDeEnvio);
        servicios.TryAddSingleton<IGeneradorDeIdentificadores, CriptoGeneradorDeIdentificadores>();
        servicios.TryAddSingleton<MemoriaDeSucursalesDelCarrito>();

        // ---------- Infraestructura (por solicitud: usa la transacción en curso) ----------
        servicios.TryAddScoped<AccesoAlEsquemaDelCarrito>();
        servicios.TryAddScoped<IRepositorioDeCarritos, RepositorioDeCarritosPostgres>();
        servicios.TryAddScoped<IRepositorioDeCheckouts, RepositorioDeCheckoutsPostgres>();
        servicios.TryAddScoped<IInventarioDelCarrito, RestInventarioDelCarrito>();

        // ---------- Aplicación ----------
        servicios.TryAddScoped(sp => new AutorizadorDelCarrito(sp.GetRequiredService<IActorActual>(), sp.GetRequiredService<IDenegacionDeAcceso>()));
        servicios.TryAddScoped(sp => new EdicionConCheckoutVivo(sp.GetRequiredService<ICreadorDeIntencionesDePago>(), sp.GetRequiredService<IRepositorioDeCheckouts>()));
        servicios.TryAddScoped(sp => new ServiciosDelCarrito(
            sp.GetRequiredService<AutorizadorDelCarrito>(),
            sp.GetRequiredService<IConsultaDeCatalogo>(),
            sp.GetRequiredService<IDisponibilidadDeEdiciones>(),
            sp.GetRequiredService<IPosesionDeEdiciones>(),
            sp.GetRequiredService<IEstadoDeCuenta>(),
            sp.GetRequiredService<ILimitadorDelCarrito>(),
            sp.GetRequiredService<IRepositorioDeCarritos>(),
            sp.GetRequiredService<IRepositorioDeCheckouts>(),
            sp.GetRequiredService<IGeneradorDeIdentificadores>(),
            sp.GetRequiredService<IReloj>(),
            sp.GetRequiredService<IUnidadDeTrabajo>(),
            sp.GetRequiredService<EdicionConCheckoutVivo>(),
            sp.GetRequiredService<IAuditoriaDelCarrito>()));
        servicios.TryAddScoped(sp => new ServiciosDelCheckout(
            sp.GetRequiredService<ServiciosDelCarrito>(),
            sp.GetRequiredService<IInventarioDelCarrito>(),
            sp.GetRequiredService<ICostoDeEnvioConfigurado>(),
            sp.GetRequiredService<ITextosLegales>(),
            sp.GetRequiredService<IConsentimientoDeAvisos>(),
            sp.GetRequiredService<IMarcaDeCancelacion>(),
            sp.GetRequiredService<ICalculadorDeHuella>(),
            sp.GetRequiredService<IAlertasDelCarrito>()));

        // ---------- Contratos públicos y receptores ----------
        servicios.TryAddScoped<ICarritoDelCliente, CarritoDelCliente>();
        servicios.TryAddScoped<ICheckoutDelCliente, CheckoutDelCliente>();
        servicios.TryAddScoped<IProgresoDelCheckout, ProgresoDelCheckout>();
        servicios.TryAddScoped<EliminarCarritoYContacto>();
        servicios.TryAddScoped<ICarritoDeCuentaReemplazada, CarritoDeCuentaReemplazada>();
        servicios.TryAddEnumerable(ServiceDescriptor.Scoped<IAplicadorDeCancelacion, AplicadorDeCancelacionDelCarrito>());

        // Carrito es la implementación real del retiro de ediciones de Catálogo: reemplaza al retiro nulo aunque Catálogo ya lo haya registrado.
        servicios.Replace(ServiceDescriptor.Scoped<IRetiroDeEdicionDeCarritos, RetirarEdicionDelCarrito>());

        // ---------- Trabajos de fondo (el anfitrión añade el servicio que los corre, T29) ----------
        servicios.TryAddScoped<PurgarCarritosDeVisitante>();
        servicios.TryAddScoped<PurgarAvisosVistos>();
        servicios.TryAddScoped<PurgarContactoYCheckoutsVencidos>();
        servicios.TryAddScoped<RecuperarCheckoutsReservando>();
        servicios.TryAddScoped<LiberarReservasPendientes>();
        servicios.TryAddScoped<RevisarCheckoutsPendientes>();

        // ---------- Lo que otros módulos aún no entregan, con su guarda de producción ----------
        return servicios.AgregarDoblesProvisionalesDelCarrito(esProduccion);
    }
}

public static class ComposicionDeLosTrabajosDelCarrito
{
    /// <summary>
    /// Registra los seis trabajos de fondo de Carrito y el servicio que los corre (T29). <paramref name="alcanceDeSistema"/> lo construye el anfitrión —la Api— con sus servicios y fija el actor «sistema» alrededor de cada corrida: la Infraestructura de Carrito no fija el actor.
    /// Se llama <b>solo en modo normal</b> y después de <c>AgregarCarrito</c>; quien la llame añade también <c>AddHostedService&lt;ServicioDeTrabajosDelCarrito&gt;()</c>.
    /// </summary>
    public static IServiceCollection AgregarTrabajosDeFondoDelCarrito(this IServiceCollection servicios, Func<IServiceProvider, AlcanceDeSistema> alcanceDeSistema)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        ArgumentNullException.ThrowIfNull(alcanceDeSistema);
        servicios.TryAddSingleton(sp => TrabajosDeFondoDelCarrito.Todos(
            sp.GetRequiredService<IServiceScopeFactory>(),
            sp.GetRequiredService<IRegistroDeTrabajosDeFondo>(),
            alcanceDeSistema(sp),
            sp.GetRequiredService<IAlertasDelCarrito>(),
            sp.GetRequiredService<IReloj>()));
        servicios.TryAddSingleton(sp => new ServicioDeTrabajosDelCarrito(sp.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeCarrito>>()));
        return servicios;
    }
}
