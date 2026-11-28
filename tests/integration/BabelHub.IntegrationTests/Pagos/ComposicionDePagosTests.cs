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
/// T27: la composición de Pagos sin base de datos (AC-PAG-10, PAG-12): la guarda de arranque corre antes de registrar nada, el simulador y la pasarela nula se eligen como corresponde, Culqi compone su adaptador solo con la llave de su entorno, los contratos públicos y
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

    /// <summary>
    /// Compone Pagos sin pasarela (<c>ninguna</c>) sobre lo que el anfitrión de la prueba ya registró; todo lo que falte es un doble que falla si se lo llama (el actor, las denegaciones, el reloj y Carrito ya los puso el anfitrión).
    /// </summary>
    internal static void ComponerSinPasarela(IServiceCollection servicios)
    {
        var yaPuestos = servicios.Select(d => d.ServiceType).ToHashSet();
        foreach (var descriptor in ConExternos().Where(d => !yaPuestos.Contains(d.ServiceType)))
        {
            servicios.Add(descriptor);
        }

        servicios.AgregarPagos(Opciones("ninguna", "Staging", nula: true), esProduccion: false);
    }

    public class ProxyQueNoHaceNada : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => throw new NotSupportedException("Un doble de composición no se llama.");
    }

    [Fact]
    public void Con_un_modo_sin_llamadas_reales_y_el_adaptador_nulo_la_pasarela_es_la_nula()
    {
        var servicios = ConExternos().AgregarPagos(Opciones(sinLlamadasReales: true, nula: true), esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider();

        Assert.IsType<NuloPasarelaDePago>(proveedor.GetRequiredService<IPasarelaDePago>());
    }

    [Fact]
    [Trait("Criterio", "AC-PAG-43")]
    public void Sin_pasarela_la_composicion_usa_el_adaptador_nulo_y_cierra_el_cobro_y_con_las_otras_lo_deja_abierto()
    {
        using var sinPasarela = ConExternos().AgregarPagos(Opciones("ninguna", "Staging", nula: true), esProduccion: false).BuildServiceProvider();
        using var simulado = ConExternos().AgregarPagos(Opciones(entorno: "Staging"), esProduccion: false).BuildServiceProvider();
        using var alRestaurar = ConExternos().AgregarPagos(Opciones(sinLlamadasReales: true, nula: true), esProduccion: false).BuildServiceProvider();

        Assert.IsType<NuloPasarelaDePago>(sinPasarela.GetRequiredService<IPasarelaDePago>());
        Assert.True(sinPasarela.GetRequiredService<OpcionesDelCobro>().Cerrado);
        Assert.False(simulado.GetRequiredService<OpcionesDelCobro>().Cerrado);
        Assert.False(alRestaurar.GetRequiredService<OpcionesDelCobro>().Cerrado);
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

    [Theory]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("Test", "sk_" + "test_llave-de-integracion-0123456789")]
    [InlineData("Development", "sk_" + "test_llave-de-integracion-0123456789")]
    [InlineData("Production", "sk_" + "live_llave-de-produccion-0123456789")]
    public void Culqi_con_la_llave_de_su_entorno_compone_el_adaptador_real_con_su_cliente_resiliente(string entorno, string llave)
    {
        var servicios = ConExternos().AgregarPagos(Opciones("culqi", entorno, llave: llave), esProduccion: false);
        using var proveedor = servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });

        Assert.IsType<CulqiPasarelaDePago>(proveedor.GetRequiredService<IPasarelaDePago>());
        using var cliente = proveedor.GetRequiredService<IHttpClientFactory>().CreateClient(OpcionesDeCulqi.NombreDelCliente);
        Assert.Equal("https://api.culqi.com/v2/", cliente.BaseAddress!.ToString());
        Assert.DoesNotContain(llave, proveedor.GetRequiredService<OpcionesDeCulqi>().ToString(), StringComparison.Ordinal);
        Assert.True(proveedor.GetRequiredService<OpcionesDelCobro>().EnviarElCorreoDelTitular);
    }

    [Theory]
    [Trait("Criterio", "AC-PAG-10")]
    [Trait("Criterio", "AC-PAG-30")]
    [InlineData("Production", "sk_" + "test_llave-de-integracion-0123456789")]
    [InlineData("Staging", "sk_" + "live_llave-de-produccion-0123456789")]
    [InlineData("Staging", "sk_" + "test_llave-de-integracion-0123456789")]
    [InlineData("Test", "sk_" + "live_llave-de-produccion-0123456789")]
    [InlineData("Production", "llave-sin-prefijo-de-culqi-0123456789")]
    public void Culqi_con_la_llave_de_otro_entorno_o_sin_prefijo_falla_el_arranque_sin_repetir_la_llave_y_sin_registrar_nada(string entorno, string llave)
    {
        var servicios = ConExternos();
        var antes = servicios.Count;

        var falla = Assert.Throws<ConfiguracionDePagosInvalidaException>(() => servicios.AgregarPagos(Opciones("culqi", entorno, llave: llave), esProduccion: false));

        Assert.Contains(GuardaDeArranqueDePagos.VariableLlavePrivada, falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(llave, falla.Message, StringComparison.Ordinal);
        Assert.Equal(antes, servicios.Count);
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
    public void Lo_ausente_o_en_blanco_es_nulo_y_el_texto_de_las_opciones_no_repite_los_secretos()
    {
        var opciones = LectorDeOpcionesDePagos.Leer(Configuracion(("Pagos:LlavePrivada", "   "), ("Pagos:ClaveDeIntentos", "clave-secreta-actual")), "Development", false, false);

        Assert.Null(opciones.Pasarela);
        Assert.Null(opciones.LlavePrivada);
        Assert.Null(opciones.ClaveDeIntentosAnterior);
        Assert.DoesNotContain("clave-secreta-actual", opciones.ToString(), StringComparison.Ordinal);
    }
}

/// <summary>T27 (revisión): lo que el lector, la guarda y la composición deciden juntos, y que el servicio hospedado reciba de verdad los nueve trabajos.</summary>
public sealed class ArranqueDePagosEncadenadoTests
{
    private static IConfiguration Configuracion(string pasarela) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Pagos:Pasarela"] = pasarela, ["Pagos:ClaveDeIntentos"] = "clave-de-intentos-de-prueba-0123456789-abcdef" }).Build();

    [Theory]
    [InlineData("simulador")]
    [InlineData("culqi")]
    public void Un_modo_sin_llamadas_reales_arranca_en_produccion_con_la_pasarela_nula_sin_llave_privada(string pasarela)
    {
        var opciones = LectorDeOpcionesDePagos.Leer(Configuracion(pasarela), "Production", sinLlamadasReales: true, pedidosDisponible: false);
        var servicios = new ServiceCollection();
        servicios.AddSingleton(typeof(IReloj), DispatchProxy.Create<IReloj, ComposicionDePagosTests.ProxyQueNoHaceNada>());

        GuardaDeArranqueDePagos.Exigir(opciones);
        servicios.AgregarPagos(opciones, esProduccion: false);

        using var proveedor = servicios.BuildServiceProvider();
        Assert.IsType<NuloPasarelaDePago>(proveedor.GetRequiredService<IPasarelaDePago>());
    }
}
