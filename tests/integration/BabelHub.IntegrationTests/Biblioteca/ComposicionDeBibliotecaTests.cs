using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>
/// La composición real de Biblioteca (<c>ecommerce-biblioteca-digital</c> T15; R01): Biblioteca es la implementación de <c>IPosesionDeEdiciones</c>, <c>IAccesosDeBiblioteca</c>, <c>IAccesosParaDevoluciones</c> e <c>IDescargasDeLaBiblioteca</c> y <b>reemplaza a los dobles provisionales</b> de Carrito, Pedidos,
/// Devoluciones y <c>web-base</c> esté o no ya registrado uno, en cualquier orden; el contenido sintético en memoria nunca se publica en producción.
/// </summary>
public class ComposicionDeBibliotecaTests
{
    private static readonly OpcionesDelAlmacenDeObjetos Almacen = new(new Uri("https://almacen.prueba"), "us-east-1", "acceso-de-pruebas-0123456789", new ValorSecreto("secreta-de-pruebas-9876543210-abcdefgh"));

    private static readonly Type[] Contratos = [typeof(IPosesionDeEdiciones), typeof(IAccesosDeBiblioteca), typeof(IAccesosParaDevoluciones), typeof(IDescargasDeLaBiblioteca)];

    private static IServiceCollection ConDobles()
    {
        var servicios = new ServiceCollection();
        servicios.AgregarDoblesProvisionalesDelCarrito(esProduccion: false);
        servicios.AgregarDoblesProvisionalesDePedidos(esProduccion: false);
        servicios.AgregarBibliotecaPendiente();
        servicios.AddSingleton<IAccesosParaDevoluciones, ElDobleDeDevoluciones>();
        return servicios;
    }

    private static bool EsDeBiblioteca(IServiceCollection servicios, Type contrato)
    {
        var ultimo = servicios.Last(d => d.ServiceType == contrato);
        return ultimo.ImplementationType is null && ultimo.ImplementationInstance is null && ultimo.ImplementationFactory is not null && ultimo.Lifetime == ServiceLifetime.Scoped;
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public void Biblioteca_reemplaza_a_los_dobles_provisionales_de_los_cuatro_contratos()
    {
        var servicios = ConDobles();
        Assert.All(Contratos, c => Assert.False(EsDeBiblioteca(servicios, c), $"{c.Name} debía empezar con un doble provisional."));

        servicios.AgregarBiblioteca(esProduccion: false);

        Assert.All(Contratos, c => Assert.True(EsDeBiblioteca(servicios, c), $"{c.Name} debía resolver a Biblioteca."));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public void El_resultado_no_depende_del_orden_de_registro()
    {
        var antes = new ServiceCollection();
        antes.AgregarBiblioteca(esProduccion: false);
        antes.AgregarDoblesProvisionalesDelCarrito(esProduccion: false);
        antes.AgregarDoblesProvisionalesDePedidos(esProduccion: false);

        // Con TryAdd, los dobles no pisan lo que ya estaba: el último registro de cada contrato sigue siendo el de Biblioteca.
        Assert.All(Contratos.Where(c => c != typeof(IAccesosParaDevoluciones)), c => Assert.True(EsDeBiblioteca(antes, c), $"{c.Name} debía seguir resolviendo a Biblioteca."));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-01")]
    public void En_produccion_las_guardas_de_los_otros_modulos_ya_no_ven_esos_contratos_como_provisionales()
    {
        var servicios = new ServiceCollection();
        servicios.AgregarBiblioteca(esProduccion: true, Almacen);
        servicios.AgregarDoblesProvisionalesDelCarrito(esProduccion: false);
        servicios.AgregarDoblesProvisionalesDePedidos(esProduccion: false);

        // La guarda de Pedidos nombra solo lo que sigue siendo doble: IAccesosDeBiblioteca ya no figura (los demás contratos de Pedidos no son de Biblioteca).
        var error = Assert.Throws<InvalidOperationException>(() => servicios.AgregarDoblesProvisionalesDePedidos(esProduccion: true));
        Assert.DoesNotContain(nameof(IAccesosDeBiblioteca), error.Message, StringComparison.Ordinal);

        var carrito = Assert.Throws<InvalidOperationException>(() => servicios.AgregarDoblesProvisionalesDelCarrito(esProduccion: true));
        Assert.DoesNotContain(nameof(IPosesionDeEdiciones), carrito.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void Sin_el_almacen_de_libros_el_arranque_de_produccion_falla_y_fuera_de_produccion_usa_el_contenido_sintetico()
    {
        var produccion = Assert.Throws<ConfiguracionDeBibliotecaInvalidaException>(() => new ServiceCollection().AgregarBiblioteca(esProduccion: true));
        Assert.Contains("Almacen__Biblioteca", produccion.Message, StringComparison.Ordinal);

        var desarrollo = new ServiceCollection();
        desarrollo.AgregarBiblioteca(esProduccion: false);
        Assert.Equal(typeof(EntregaDeArchivosEnMemoria), desarrollo.Last(d => d.ServiceType == typeof(IEntregaDeArchivos)).ImplementationType);

        var real = new ServiceCollection();
        real.AgregarBiblioteca(esProduccion: true, Almacen);
        Assert.Null(real.Last(d => d.ServiceType == typeof(IEntregaDeArchivos)).ImplementationType);
        Assert.DoesNotContain(real, d => d.ServiceType == typeof(IEntregaDeArchivos) && d.ImplementationType == typeof(EntregaDeArchivosEnMemoria));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-25")]
    public void La_parte_de_biblioteca_de_la_cancelacion_se_suma_a_la_enumeracion_de_identidad_y_no_se_duplica()
    {
        var servicios = new ServiceCollection();

        servicios.AgregarBiblioteca(esProduccion: false);
        servicios.AgregarBiblioteca(esProduccion: false);

        Assert.Single(servicios, d => d.ServiceType == typeof(BabelHub.Ecommerce.Application.Identidad.IAplicadorDeCancelacion) && d.ImplementationType == typeof(EliminarBibliotecaDeUnaCuenta));
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-23")]
    public void Los_trabajos_de_fondo_se_registran_y_las_depuraciones_tocan_desde_las_0330_de_lima_una_vez_por_dia()
    {
        var servicios = new ServiceCollection();
        servicios.AgregarTrabajosDeFondoDeBiblioteca(_ => (corrida, token) => corrida(token));
        Assert.Contains(servicios, d => d.ServiceType == typeof(ServicioDeTrabajosDeBiblioteca));

        // 08:29 UTC = 03:29 en Lima; 08:30 UTC = 03:30.
        Assert.False(TrabajosDeBiblioteca.TocaHoy(new RelojDePrueba(new DateTimeOffset(2026, 11, 20, 8, 29, 0, TimeSpan.Zero)), null));
        Assert.True(TrabajosDeBiblioteca.TocaHoy(new RelojDePrueba(new DateTimeOffset(2026, 11, 20, 8, 30, 0, TimeSpan.Zero)), null));
        Assert.True(TrabajosDeBiblioteca.TocaHoy(new RelojDePrueba(new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero)), new DateOnly(2026, 11, 19)));
        Assert.False(TrabajosDeBiblioteca.TocaHoy(new RelojDePrueba(new DateTimeOffset(2026, 11, 20, 20, 0, 0, TimeSpan.Zero)), new DateOnly(2026, 11, 20)));
    }

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class ElDobleDeDevoluciones : IAccesosParaDevoluciones
    {
        public Task<Resultado<AccesoParaDevoluciones?>> Consultar(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeRevocarSiNoHayEnlace>> RevocarSiNoHayEnlace(Guid cuentaId, Guid pedidoId, Guid edicionId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeSuspenderAcceso>> Suspender(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ResultadoDeReanudarAcceso>> Reanudar(Guid cuentaId, Guid accesoId, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }
}
