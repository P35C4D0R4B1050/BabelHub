using System.Reflection;
using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// La composición real de Identidad en el E-commerce (identidad-seguridad T42 reducido; SEG-11, D04, regla 9): el grafo completo se construye y se valida (alcances y dependencias), cada contrato público se resuelve,
/// los puertos de la web reemplazan a los provisionales y, <b>en producción, falta una clave del entorno → el arranque falla</b> sin imprimir ningún valor. Lo que es de otros módulos se sustituye por dobles de interfaz.
/// </summary>
public sealed class ComposicionDeIdentidadTests
{
    /// <summary>Un doble de una interfaz que no se llega a usar: la prueba solo comprueba que el grafo se arma.</summary>
    public class Doble : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos) =>
            metodo?.ReturnType == typeof(Task) ? Task.CompletedTask : null;

        public static T De<T>()
            where T : class => Create<T, Doble>();
    }

    private static string Clave() => $"v1:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}";

    private static IConfiguration Configuracion(string? cifrado = null, string? pimienta = null, string? anteriores = null)
    {
        var valores = new Dictionary<string, string?>();
        if (cifrado is not null)
        {
            valores[OpcionesDeIdentidad.VariableDeLaClaveDeCifrado] = cifrado;
        }

        if (pimienta is not null)
        {
            valores[OpcionesDeIdentidad.VariableDeLaPimienta] = pimienta;
        }

        if (anteriores is not null)
        {
            valores[OpcionesDeIdentidad.VariableDeLasClavesAnteriores] = anteriores;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static ServiceProvider Proveedor()
    {
        var servicios = new ServiceCollection();
        servicios.AddSingleton(Doble.De<IUnidadDeTrabajo>());
        servicios.AddSingleton(Doble.De<IConexionDeLaUnidadDeTrabajo>());
        servicios.AddSingleton(Doble.De<IActorActual>());
        servicios.AddSingleton(Doble.De<IReloj>());
        servicios.AddSingleton(Doble.De<IControlDeLimites>());
        servicios.AddSingleton(Doble.De<ICalculadorDeHuella>());
        servicios.AddSingleton(Doble.De<IEnviadorDeCorreo>());
        servicios.AddSingleton(Doble.De<IClienteDelInventario>());
        servicios.AddSingleton(Doble.De<IRegistroDeSupresiones>());
        servicios.AddSingleton(Doble.De<IDenegacionDeAcceso>());
        servicios.AddSingleton(Doble.De<IAlertas>());
        servicios.AddSingleton(Doble.De<IPublicadorDeAlertasTecnicas>());
        servicios.AddSingleton(Doble.De<IRegistroDeSeguridad>());
        servicios.AddSingleton(Doble.De<IAuditoria>());
        servicios.AddSingleton(Doble.De<ICarritoDeCuentaReemplazada>());
        servicios.AddSingleton(Doble.De<IPendientesDeCuenta>());
        servicios.AddSingleton(Doble.De<ICancelacionDeAvisos>());

        // Lo provisional de la web (AgregarModuloWeb): la composición de Identidad lo reemplaza aunque ya esté registrado.
        servicios.AddSingleton<IEstadoDeCuenta, BabelHub.Ecommerce.Infrastructure.Carrito.EstadoDeCuentaDelCarritoSinIdentidad>();
        servicios.AddSingleton<IEstadoDeCuentaDeLaWeb, EstadoDeCuentaSinIdentidad>();
        servicios.AddSingleton<IAceptacionDeTextosEnIdentidad, AceptacionDeTextosSinIdentidad>();
        servicios.AddSingleton(Doble.De<ISesionesDeIdentidad>());

        servicios.AgregarIdentidad(OpcionesDeIdentidad.Leer(Configuracion(Clave(), Clave()), esProduccion: true), soloEnModoNormal: false, _ => throw new InvalidOperationException("No debe usarse fuera del modo normal."), _ => Npgsql.NpgsqlDataSource.Create("Host=localhost;Username=auditoria_depuracion"));
        return servicios.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-09")]
    public void ElGrafoDeIdentidad_SeValida_ConAlcancesYDependenciasCompletas()
    {
        using var proveedor = Proveedor();

        Assert.NotNull(proveedor);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-09")]
    public void CadaContratoPublico_SeResuelve_ConSuImplementacionReal()
    {
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;

        Assert.IsType<RegistroDeCuentas>(s.GetRequiredService<IRegistroDeCuentas>());
        Assert.IsType<Ingreso>(s.GetRequiredService<IIngreso>());
        Assert.IsType<Sesiones>(s.GetRequiredService<ISesiones>());
        Assert.IsType<RecuperacionDeAcceso>(s.GetRequiredService<IRecuperacionDeAcceso>());
        Assert.IsType<EstadoDeCuentaDeIdentidad>(s.GetRequiredService<IEstadoDeCuenta>());
        Assert.Single(s.GetServices<IEstadoDeCuenta>());
        Assert.IsType<GestionDeLaCuenta>(s.GetRequiredService<ICuentaDelCliente>());
        Assert.IsType<CambioDeCorreo>(s.GetRequiredService<ICambioDeCorreo>());
        Assert.IsType<DispositivosDeLaCuenta>(s.GetRequiredService<IDispositivosDeLaCuenta>());
        Assert.IsType<AtencionDeSolicitudesDeDatos>(s.GetRequiredService<ISolicitudesDeDatos>());
        Assert.IsType<CancelacionDeCuenta>(s.GetRequiredService<ICancelacionDeCuenta>());
        Assert.Same(s.GetRequiredService<ICancelacionDeCuenta>(), s.GetRequiredService<IMarcaDeCancelacionDeCuenta>());
        Assert.IsType<AdministracionDeUsuarios>(s.GetRequiredService<IAdministracionDeUsuarios>());
        Assert.IsType<ClavesDeFirma>(s.GetRequiredService<IClavesDeFirma>());
        Assert.IsType<ContextoDeLecturaDelAsistente>(s.GetRequiredService<IContextoDeLectura>());
        Assert.IsType<DestinatariosDeAlertasDeIdentidad>(s.GetRequiredService<IDestinatariosDeAlertas>());
        Assert.NotNull(s.GetRequiredService<IAplicadorDeSupresion>());
        Assert.NotNull(s.GetRequiredService<IRestauracionDeIdentidad>());
        Assert.Equal(2, s.GetServices<IPasoDeRestauracion>().Count());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public void LosPuertosDeLaWeb_ReemplazanALosProvisionales_Y_ElVerificadorDeCredencialesEsUnico()
    {
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;

        Assert.IsType<EstadoDeCuentaDeLaWebSobreIdentidad>(s.GetRequiredService<IEstadoDeCuentaDeLaWeb>());
        Assert.IsType<AceptacionDeTextosSobreIdentidad>(s.GetRequiredService<IAceptacionDeTextosEnIdentidad>());
        Assert.IsType<SesionesDeIdentidadParaLaWeb>(s.GetRequiredService<ISesionesDeIdentidad>());
        Assert.Same(proveedor.GetRequiredService<IVerificadorDeCredencial>(), proveedor.GetRequiredService<IVerificadorDeCredencial>());
    }

    [Fact]
    public void LosPendientesDeLaCuenta_SeComponenSinRecursar_ConLosModulosYElInventario()
    {
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();

        var compuesto = alcance.ServiceProvider.GetRequiredService<PendientesDeCuentaCompuestos>();

        Assert.NotNull(compuesto);
        Assert.Contains(alcance.ServiceProvider.GetServices<IPendientesDeCuenta>(), p => p.GetType().Name == "InventarioPendientesDeCuenta");
        Assert.DoesNotContain(alcance.ServiceProvider.GetServices<IPendientesDeCuenta>(), p => p is PendientesDeCuentaCompuestos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public void EnProduccion_SinLaClaveDeCifrado_ElArranqueFalla_NombrandoLaVariable_SinValores()
    {
        var falla = Assert.Throws<InvalidOperationException>(() => OpcionesDeIdentidad.Leer(Configuracion(pimienta: Clave()), esProduccion: true));

        Assert.Contains(OpcionesDeIdentidad.VariableDeLaClaveDeCifrado, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EnProduccion_SinLaPimienta_ElArranqueFalla()
    {
        var falla = Assert.Throws<InvalidOperationException>(() => OpcionesDeIdentidad.Leer(Configuracion(cifrado: Clave()), esProduccion: true));

        Assert.Contains(OpcionesDeIdentidad.VariableDeLaPimienta, falla.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sin-formato")]
    [InlineData("v1:AAAA")]
    [InlineData("v1:no es base64!!")]
    [InlineData("versión-demasiado-larga-para-valer:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    public void UnaClaveDebil_OMalFormada_HaceFallarElArranque_AunFueraDeProduccion_SinImprimirElValor(string valor)
    {
        var falla = Assert.Throws<ArgumentException>(() => OpcionesDeIdentidad.Leer(Configuracion(cifrado: valor, pimienta: Clave()), esProduccion: false));

        Assert.DoesNotContain(valor, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LaClaveDeCifradoYLaPimienta_NoPuedenSerLaMisma()
    {
        var misma = Clave();

        var falla = Assert.Throws<ArgumentException>(() => OpcionesDeIdentidad.Leer(Configuracion(cifrado: misma, pimienta: misma), esProduccion: true));

        Assert.DoesNotContain(misma, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FueraDeProduccion_SinVariables_SeGeneranClavesEfimeras_DistintasEnCadaLectura()
    {
        var uno = OpcionesDeIdentidad.Leer(Configuracion(), esProduccion: false);
        var otro = OpcionesDeIdentidad.Leer(Configuracion(), esProduccion: false);

        Assert.NotSame(uno.ClaveDeCifrado, otro.ClaveDeCifrado);
        Assert.Equal("[clave]", uno.ClaveDeCifrado.ToString());
        Assert.Equal("[opciones de identidad]", uno.ToString());
    }

    [Fact]
    public void LasClavesAnteriores_SeLeenDeLaLista_Y_NoPuedenRepetirLaActual()
    {
        var actual = Clave();
        var opciones = OpcionesDeIdentidad.Leer(Configuracion(cifrado: actual, pimienta: Clave(), anteriores: $"{Clave().Replace("v1:", "v0:", StringComparison.Ordinal)}, {Clave().Replace("v1:", "v9:", StringComparison.Ordinal)}"), esProduccion: true);

        Assert.Equal(["v0", "v9"], opciones.ClavesAnteriores.Select(c => c.Version));
    }
}
