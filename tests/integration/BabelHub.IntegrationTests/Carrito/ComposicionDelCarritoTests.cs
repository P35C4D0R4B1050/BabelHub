using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T26b: la composición del módulo Carrito resuelve todo lo que declara (contratos públicos, receptores de Identidad, casos de uso, trabajos de fondo), el retiro de ediciones de Carrito <b>reemplaza</b> al nulo de Catálogo en cualquier orden y, en producción,
/// el arranque falla nombrando cada puerto que siga con un doble provisional (AC-CAR-21).
/// </summary>
public sealed class ComposicionDelCarritoTests
{
    /// <summary>Un doble que lanza en cualquier llamada: solo sirve para que el proveedor pueda construir lo que depende de otros módulos.</summary>
    public class DobleQueLanza : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos) => throw new NotSupportedException();
    }

    private static T Doble<T>()
        where T : class => DispatchProxy.Create<T, DobleQueLanza>();

    private static ServiceCollection ConDependenciasDeOtrosModulos()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton(Doble<IConsultaDeCatalogo>());
        servicios.AddSingleton(Doble<IDisponibilidadDeEdiciones>());
        servicios.AddSingleton(Doble<IUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IConexionDeLaUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IActorActual>());
        servicios.AddSingleton(Doble<IDenegacionDeAcceso>());
        servicios.AddSingleton(Doble<IReloj>());
        servicios.AddSingleton(Doble<IClienteDelInventario>());
        servicios.AddSingleton(Doble<ITextosLegales>());
        servicios.AddSingleton(Doble<ICalculadorDeHuella>());
        servicios.AddSingleton(Doble<IRegistroDeTrabajosDeFondo>());
        return servicios;
    }

    private static readonly AppSettingsOpcionesDeEnvio Costo = AppSettingsOpcionesDeEnvio.Desde("12.00");

    [Fact]
    public void En_desarrollo_se_resuelven_los_contratos_publicos_los_receptores_de_Identidad_y_los_casos_de_uso()
    {
        var servicios = ConDependenciasDeOtrosModulos();
        servicios.AgregarCarrito(Costo, esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;

        Assert.IsType<CarritoDelCliente>(s.GetRequiredService<ICarritoDelCliente>());
        Assert.IsType<CheckoutDelCliente>(s.GetRequiredService<ICheckoutDelCliente>());
        Assert.IsType<ProgresoDelCheckout>(s.GetRequiredService<IProgresoDelCheckout>());
        Assert.IsType<CarritoDeCuentaReemplazada>(s.GetRequiredService<ICarritoDeCuentaReemplazada>());
        Assert.Contains(s.GetServices<IAplicadorDeCancelacion>(), a => a.Modulo == "carrito");
        Assert.IsType<RestInventarioDelCarrito>(s.GetRequiredService<IInventarioDelCarrito>());
        Assert.IsType<RepositorioDeCarritosPostgres>(s.GetRequiredService<IRepositorioDeCarritos>());
        Assert.IsType<RepositorioDeCheckoutsPostgres>(s.GetRequiredService<IRepositorioDeCheckouts>());
        Assert.Equal(12.00m, s.GetRequiredService<ICostoDeEnvioConfigurado>().Valor.Valor);
        foreach (var caso in new[] { typeof(PurgarCarritosDeVisitante), typeof(PurgarAvisosVistos), typeof(PurgarContactoYCheckoutsVencidos), typeof(RecuperarCheckoutsReservando), typeof(LiberarReservasPendientes), typeof(RevisarCheckoutsPendientes) })
        {
            Assert.NotNull(s.GetRequiredService(caso));
        }
    }

    [Fact]
    public void El_retiro_de_carritos_de_Carrito_reemplaza_al_nulo_de_Catalogo_se_componga_antes_o_despues()
    {
        var catalogoPrimero = ConDependenciasDeOtrosModulos();
        catalogoPrimero.AgregarRetiroNuloDeCarritos();
        catalogoPrimero.AgregarCarrito(Costo, esProduccion: false);
        var carritoPrimero = ConDependenciasDeOtrosModulos();
        carritoPrimero.AgregarCarrito(Costo, esProduccion: false);
        carritoPrimero.AgregarRetiroNuloDeCarritos();

        foreach (var servicios in new[] { catalogoPrimero, carritoPrimero })
        {
            using var proveedor = servicios.BuildServiceProvider();
            using var alcance = proveedor.CreateScope();
            Assert.IsType<RetirarEdicionDelCarrito>(alcance.ServiceProvider.GetRequiredService<IRetiroDeEdicionDeCarritos>());
        }
    }

    [Fact]
    public void En_produccion_el_arranque_falla_nombrando_cada_puerto_que_sigue_con_un_doble_provisional()
    {
        var servicios = ConDependenciasDeOtrosModulos();

        var error = Assert.Throws<InvalidOperationException>(() => servicios.AgregarCarrito(Costo, esProduccion: true));

        foreach (var puerto in new[] { nameof(IPosesionDeEdiciones), nameof(ICreadorDeIntencionesDePago), nameof(IEstadoDeCuenta), nameof(ILimitadorDelCarrito), nameof(IAuditoriaDelCarrito), nameof(IMarcaDeCancelacion), nameof(IConsentimientoDeAvisos), nameof(IAlertasDelCarrito) })
        {
            Assert.Contains(puerto, error.Message, StringComparison.Ordinal);
        }

        // El adaptador real del Inventario aún no tiene consulta previa, reservar ni liberar (T25b): la guarda impide publicarlo hasta entonces.
        Assert.Contains(nameof(IInventarioDelCarrito), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_trabajos_de_fondo_se_registran_aparte_con_el_alcance_de_sistema_del_anfitrion_y_son_seis_con_nombres_estables()
    {
        var servicios = ConDependenciasDeOtrosModulos();
        servicios.AgregarCarrito(Costo, esProduccion: false);
        servicios.AgregarTrabajosDeFondoDelCarrito(_ => (corrida, cancelacion) => corrida(cancelacion));
        using var proveedor = servicios.BuildServiceProvider();

        var trabajos = proveedor.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeCarrito>>();

        Assert.Equal(
            [
                "carrito-purga-de-visitantes",
                "carrito-purga-de-avisos",
                "carrito-contacto-y-checkouts-vencidos",
                "carrito-recuperacion-de-reservando",
                "carrito-liberacion-de-reservas",
                "carrito-revision-de-pagos-pendientes",
            ],
            trabajos.Select(t => t.Nombre));
        Assert.NotNull(proveedor.GetRequiredService<ServicioDeTrabajosDelCarrito>());
        Assert.Throws<ArgumentNullException>(() => servicios.AgregarTrabajosDeFondoDelCarrito(null!));
    }
}
