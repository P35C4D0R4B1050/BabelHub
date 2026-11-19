using System.Reflection;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Pagos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Pagos;

/// <summary>
/// T27: la composición de Pagos sin base de datos (AC-PAG-10, PAG-12): la guarda de arranque corre antes de registrar nada, el simulador y la pasarela nula se eligen como corresponde, Culqi falla el arranque hasta que exista su adaptador, los contratos públicos y
/// los nueve trabajos de fondo se resuelven con sus casos de uso, Pagos reemplaza al creador de intenciones provisional de Carrito y en producción un doble provisional de Identidad o de Pedidos falla el arranque nombrándolo.
/// </summary>
public sealed class ComposicionDePagosTests
{
    private const string ClaveDeIntentos = "clave-de-intentos-de-prueba-0123456789-abcdef";

    private static OpcionesDeArranqueDePagos Opciones(string pasarela = "simulador", string entorno = "Test", bool sinLlamadasReales = false, bool nula = false, string? llave = null) =>
        new(pasarela, entorno, sinLlamadasReales, nula, PedidosDisponible: true, llave, ClaveDeIntentos, ClaveDeIntentosAnterior: null);

    private static ServiceCollection ConExternos()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton(Falso<IUnidadDeTrabajo>());
        servicios.AddSingleton(Falso<IConexionDeLaUnidadDeTrabajo>());
        servicios.AddSingleton(Falso<IActorActual>());
        servicios.AddSingleton(Falso<IDenegacionDeAcceso>());
        servicios.AddSingleton(Falso<IReloj>());
        servicios.AddSingleton(Falso<IClienteDelInventario>());
        servicios.AddSingleton(Falso<IControlDeLimites>());
        servicios.AddSingleton(Falso<IProcesadorDeEntradasDeIntegracion>());
        servicios.AddSingleton(Falso<IEnviadorDeCorreo>());
        servicios.AddSingleton(Falso<IAuditoria>());
        servicios.AddSingleton(Falso<IPublicadorDeAlertasTecnicas>());
        servicios.AddSingleton(Falso<IRegistroDeTrabajosDeFondo>());
        servicios.AddSingleton(Falso<ICheckoutDelCliente>());
        servicios.AddSingleton(Falso<IProgresoDelCheckout>());
        servicios.AddSingleton(Falso<IPosesionDeEdiciones>());
        return servicios;
    }

    private static T Falso<T>()
        where T : class => DispatchProxy.Create<T, ProxyQueNoHaceNada>();

    public class ProxyQueNoHaceNada : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException("Un doble de composición no se llama.");
    }

    [Fact]
    public void Con_el_simulador_en_Test_se_resuelven_los_contratos_publicos_y_la_pasarela_es_el_simulador()
    {
        var servicios = ConExternos().AgregarPagos(Opciones(), esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
        using var alcance = proveedor.CreateScope();

        Assert.IsType<SimuladorPasarelaDePago>(proveedor.GetRequiredService<IPasarelaDePago>());
        Assert.NotNull(alcance.ServiceProvider.GetRequiredService<IPagos>());
        Assert.NotNull(alcance.ServiceProvider.GetRequiredService<IReembolsos>());
        Assert.NotNull(alcance.ServiceProvider.GetRequiredService<IAvisosDeLaPasarela>());
        Assert.NotNull(alcance.ServiceProvider.GetRequiredService<ILiberacionDeContactoDePago>());
        Assert.NotNull(alcance.ServiceProvider.GetRequiredService<ICreadorDeIntencionesDePago>());
        Assert.IsType<IntencionesDePagoDeLaAplicacion>(alcance.ServiceProvider.GetRequiredService<ICreadorDeIntencionesDePago>());
        Assert.Contains(alcance.ServiceProvider.GetServices<IPendientesDeCuenta>(), p => p is ContarPendientesDeCuenta);
        Assert.Contains(alcance.ServiceProvider.GetServices<IAplicadorDeCancelacion>(), p => p is DesvincularPagosDeUnaCuenta);
        Assert.IsType<RestReservasDelPago>(alcance.ServiceProvider.GetRequiredService<IReservasDelPago>());
    }

    [Fact]
    public void Pagos_reemplaza_al_creador_de_intenciones_provisional_que_Carrito_ya_registro()
    {
        var servicios = ConExternos();
        servicios.AddScoped<ICreadorDeIntencionesDePago>(_ => Falso<ICreadorDeIntencionesDePago>());
        servicios.AgregarPagos(Opciones(), esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();
        using var alcance = proveedor.CreateScope();

        Assert.IsType<IntencionesDePagoDeLaAplicacion>(alcance.ServiceProvider.GetRequiredService<ICreadorDeIntencionesDePago>());
    }

    [Fact]
    public void Con_un_modo_sin_llamadas_reales_y_el_adaptador_nulo_la_pasarela_es_la_nula()
    {
        var servicios = ConExternos().AgregarPagos(Opciones(sinLlamadasReales: true, nula: true), esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();

        Assert.IsType<NuloPasarelaDePago>(proveedor.GetRequiredService<IPasarelaDePago>());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void La_guarda_corre_antes_de_registrar_nada_y_el_simulador_no_arranca_en_produccion()
    {
        var servicios = ConExternos();
        var antes = servicios.Count;

        var falla = Assert.Throws<ConfiguracionDePagosInvalidaException>(() => servicios.AgregarPagos(Opciones(entorno: "Production"), esProduccion: true));

        Assert.Contains("simulador", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(ClaveDeIntentos, falla.Message, StringComparison.Ordinal);
        Assert.Equal(antes, servicios.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-10")]
    public void Un_modo_sin_llamadas_reales_con_un_adaptador_que_no_es_nulo_falla_el_arranque()
    {
        Assert.Throws<ConfiguracionDePagosInvalidaException>(() => ConExternos().AgregarPagos(Opciones(sinLlamadasReales: true, nula: false), esProduccion: false));
    }

    [Fact]
    public void Culqi_falla_el_arranque_hasta_que_exista_su_adaptador_sin_repetir_la_llave()
    {
        var llave = "sk_test_llave-de-pruebas-0123456789";

        var falla = Assert.Throws<ConfiguracionDePagosInvalidaException>(() => ConExternos().AgregarPagos(Opciones("culqi", llave: llave), esProduccion: false));

        Assert.Contains("T37", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(llave, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void En_produccion_los_dobles_provisionales_de_Identidad_y_de_Pedidos_fallan_el_arranque_nombrandolos()
    {
        var falla = Assert.Throws<InvalidOperationException>(() => ConExternos().AgregarPagos(Opciones(), esProduccion: true));

        Assert.Contains(nameof(ICuentaDelPagador), falla.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(IConfirmacionDeCompra), falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IPasarelaDePago), falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IReservasDelPago), falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Los_nueve_trabajos_de_fondo_se_registran_con_nombres_unicos_y_cada_caso_de_uso_se_resuelve()
    {
        var servicios = ConExternos().AgregarPagos(Opciones(), esProduccion: false);
        servicios.AgregarTrabajosDeFondoDePagos(_ => (corrida, cancelacion) => corrida(cancelacion));
        using var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var trabajos = proveedor.GetRequiredService<IReadOnlyList<TrabajoDeFondoDePagos>>();
        using var alcance = proveedor.CreateScope();

        Assert.Equal(9, trabajos.Count);
        Assert.Equal(9, trabajos.Select(t => t.Nombre).Distinct().Count());
        Assert.All(trabajos, t => Assert.StartsWith("pagos-", t.Nombre, StringComparison.Ordinal));
        foreach (var tipo in new[]
        {
            typeof(ConsultarPagosPendientes), typeof(ReanudarComprasAprobadas), typeof(CerrarIntencionesVencidas), typeof(ReintentarReembolsosPendientes), typeof(AvisarACarrito), typeof(EnviarAvisosDePago),
            typeof(PurgarContactoDeIntenciones), typeof(DepurarIntencionesAntiguas), typeof(PurgarAvisosDePagos),
        })
        {
            Assert.NotNull(alcance.ServiceProvider.GetRequiredService(tipo));
        }

        Assert.NotNull(proveedor.GetRequiredService<ServicioDeTrabajosDePagos>());
    }
}

/// <summary>T27: la lectura de la configuración de Pagos (PAG-12): las rutas, el valor anterior opcional y que un modo sin llamadas reales no pida la llave privada y use siempre el adaptador nulo.</summary>
public sealed class LectorDeOpcionesDePagosTests
{
    private static IConfiguration Configuracion(params (string Ruta, string? Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.ToDictionary(v => v.Ruta, v => v.Valor)).Build();

    [Fact]
    public void Lee_la_pasarela_la_llave_la_clave_de_intentos_y_su_valor_anterior()
    {
        var opciones = LectorDeOpcionesDePagos.Leer(
            Configuracion(("Pagos:Pasarela", "culqi"), ("Pagos:LlavePrivada", "sk_test_abc"), ("Pagos:ClaveDeIntentos", "clave-actual"), ("Pagos:ClaveDeIntentosAnterior", "clave-anterior")),
            "Test",
            sinLlamadasReales: false,
            pedidosDisponible: true);

        Assert.Equal(("culqi", "Test", false, false, true), (opciones.Pasarela, opciones.Entorno, opciones.SinLlamadasReales, opciones.PasarelaEsNula, opciones.PedidosDisponible));
        Assert.Equal(("sk_test_abc", "clave-actual", "clave-anterior"), (opciones.LlavePrivada, opciones.ClaveDeIntentos, opciones.ClaveDeIntentosAnterior));
    }

    [Fact]
    public void Lo_ausente_o_en_blanco_es_nulo_y_el_texto_de_las_opciones_no_repite_los_secretos()
    {
        var opciones = LectorDeOpcionesDePagos.Leer(Configuracion(("Pagos:LlavePrivada", "   "), ("Pagos:ClaveDeIntentos", "clave-secreta-actual")), "Development", false, false);

        Assert.Null(opciones.Pasarela);
        Assert.Null(opciones.LlavePrivada);
        Assert.Null(opciones.ClaveDeIntentosAnterior);
        Assert.DoesNotContain("clave-secreta-actual", opciones.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Un_modo_sin_llamadas_reales_usa_el_adaptador_nulo_y_no_lee_la_llave_privada()
    {
        var opciones = LectorDeOpcionesDePagos.Leer(
            Configuracion(("Pagos:Pasarela", "simulador"), ("Pagos:LlavePrivada", "sk_live_no_se_lee"), ("Pagos:ClaveDeIntentos", "clave-actual")),
            "Production",
            sinLlamadasReales: true,
            pedidosDisponible: true);

        Assert.True(opciones.PasarelaEsNula);
        Assert.Null(opciones.LlavePrivada);
    }
}
