using System.Reflection;
using BabelHub.Ecommerce.Api.Carrito;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T29: la Api hospeda los trabajos de fondo de Carrito (CAR-29, CAR-34; AC-CAR-25, AC-CAR-30) <b>solo en el modo normal</b> y con el actor «sistema» fijado alrededor de cada corrida (y limpiado al terminar). La corrida real contra PostgreSQL es de T30.
/// </summary>
public sealed class TrabajosDelCarritoEnLaApiTests
{
    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora { get; } = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.UtcDateTime);
    }

    private sealed class RegistroEspia : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas { get; } = [];

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            Corridas.Add((trabajo, resultado));
            return Task.CompletedTask;
        }
    }

    private sealed class DenegacionesEspia : IDenegacionDeAcceso
    {
        public int Registradas { get; private set; }

        public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
        {
            Registradas++;
            return Task.CompletedTask;
        }
    }

    public class DobleQueLanza : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos) => throw new NotSupportedException();
    }

    private static T Doble<T>()
        where T : class => DispatchProxy.Create<T, DobleQueLanza>();

    private static (ServiceCollection Servicios, ActorDelFlujo Actor, RegistroEspia Registro, DenegacionesEspia Denegaciones) Crear()
    {
        var actor = new ActorDelFlujo();
        var (registro, denegaciones) = (new RegistroEspia(), new DenegacionesEspia());
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IActorActual>(actor);
        servicios.AddSingleton<IEstablecedorDeActor>(actor);
        servicios.AddSingleton<IRegistroDeTrabajosDeFondo>(registro);
        servicios.AddSingleton<IDenegacionDeAcceso>(denegaciones);
        servicios.AddSingleton<IReloj>(new RelojFijo());
        servicios.AddSingleton(Doble<IConsultaDeCatalogo>());
        servicios.AddSingleton(Doble<IDisponibilidadDeEdiciones>());
        servicios.AddSingleton(Doble<IUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IConexionDeLaUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IClienteDelInventario>());
        servicios.AddSingleton(Doble<ITextosLegales>());
        servicios.AddSingleton(Doble<ICalculadorDeHuella>());
        servicios.AgregarCarrito(AppSettingsOpcionesDeEnvio.Desde("12.00"), esProduccion: false);
        return (servicios, actor, registro, denegaciones);
    }

    [Fact]
    public void El_servicio_que_corre_los_trabajos_solo_se_registra_en_el_modo_normal()
    {
        var (normal, _, _, _) = Crear();
        normal.AgregarTrabajosDelCarritoEnLaApi(soloEnModoNormal: true);
        var (restauracion, _, _, _) = Crear();
        restauracion.AgregarTrabajosDelCarritoEnLaApi(soloEnModoNormal: false);

        using var proveedorNormal = normal.BuildServiceProvider();
        using var proveedorRestauracion = restauracion.BuildServiceProvider();

        Assert.Contains(proveedorNormal.GetServices<IHostedService>(), s => s is ServicioDeTrabajosDelCarrito);
        Assert.DoesNotContain(proveedorRestauracion.GetServices<IHostedService>(), s => s is ServicioDeTrabajosDelCarrito);
        Assert.Equal(6, proveedorRestauracion.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeCarrito>>().Count);
    }

    [Fact]
    public async Task Cada_corrida_fija_el_actor_sistema_lo_limpia_al_terminar_y_registra_su_resultado_sin_denegaciones()
    {
        var (servicios, actor, registro, denegaciones) = Crear();
        servicios.AgregarTrabajosDelCarritoEnLaApi(soloEnModoNormal: false);
        using var proveedor = servicios.BuildServiceProvider();

        foreach (var trabajo in proveedor.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeCarrito>>())
        {
            await trabajo.EjecutarUnaVez();
        }

        // Las dependencias son dobles que lanzan: la corrida falla cerrada (se registra como fallo y no cae el servicio), pero el actor «sistema» fue aceptado (sin denegaciones) y se limpió.
        Assert.Equal(6, registro.Corridas.Count);
        Assert.All(registro.Corridas, c => Assert.Equal(ResultadoDeTrabajo.Fallo, c.Resultado));
        Assert.Equal(0, denegaciones.Registradas);
        Assert.Null(actor.Actual);
    }

    [Fact]
    public async Task Sin_el_alcance_de_sistema_los_casos_de_uso_rechazan_la_corrida_y_queda_denegada()
    {
        var (servicios, actor, _, denegaciones) = Crear();
        servicios.AgregarTrabajosDeFondoDelCarrito(_ => (corrida, cancelacion) => corrida(cancelacion));
        using var proveedor = servicios.BuildServiceProvider();
        var trabajo = proveedor.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeCarrito>>()[0];

        await trabajo.EjecutarUnaVez();

        Assert.Null(actor.Actual);
        Assert.True(denegaciones.Registradas >= 1);
    }
}
