using System.Reflection;
using BabelHub.Ecommerce.Api.Devoluciones;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// T25: la Api hospeda los trabajos de fondo de Devoluciones (DEV-14, DEV-21, DEV-27; AC-DEV-13, AC-DEV-29) <b>solo en el modo normal</b>, con la fábrica del servicio (que recibe los trabajos) y con el actor «sistema» fijado alrededor de cada
/// corrida y limpiado al terminar, aunque falle.
/// </summary>
public sealed class TrabajosDeDevolucionesEnLaApiTests
{
    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora { get; } = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

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

    private sealed class AlertasEspia : IPublicadorDeAlertasTecnicas
    {
        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    public class DobleQueLanza : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? metodo, object?[]? argumentos) => throw new NotSupportedException();
    }

    private static T Doble<T>()
        where T : class => DispatchProxy.Create<T, DobleQueLanza>();

    private static (ServiceCollection Servicios, ActorDelFlujo Actor, RegistroEspia Registro) Crear()
    {
        var actor = new ActorDelFlujo();
        var registro = new RegistroEspia();
        var servicios = new ServiceCollection();
        servicios.AddSingleton<IActorActual>(actor);
        servicios.AddSingleton<IEstablecedorDeActor>(actor);
        servicios.AddSingleton<IRegistroDeTrabajosDeFondo>(registro);
        servicios.AddSingleton<IPublicadorDeAlertasTecnicas>(new AlertasEspia());
        servicios.AddSingleton<IReloj>(new RelojFijo());
        servicios.AddSingleton(Doble<IUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IConexionDeLaUnidadDeTrabajo>());
        servicios.AddSingleton(Doble<IDenegacionDeAcceso>());
        servicios.AddSingleton(Doble<IControlDeLimites>());
        servicios.AddSingleton(Doble<IAuditoria>());
        servicios.AddSingleton(Doble<INotificador>());
        servicios.AddSingleton(Doble<IEstadoDeCuenta>());
        servicios.AddSingleton(Doble<IMarcaDeCancelacionDeCuenta>());
        servicios.AddSingleton(Doble<IReembolsos>());
        servicios.AddSingleton(Doble<IAccesosDeBiblioteca>());
        servicios.AddSingleton(Doble<IAccesosParaDevoluciones>());
        servicios.AddSingleton(Doble<IPedidosParaDevoluciones>());
        servicios.AddSingleton(Doble<IClienteDelInventario>());
        servicios.AgregarDevoluciones(esProduccion: false);
        return (servicios, actor, registro);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public void El_servicio_que_corre_los_trabajos_solo_se_registra_en_el_modo_normal_y_recibe_los_tres_trabajos()
    {
        var (normal, _, _) = Crear();
        normal.AgregarTrabajosDeDevolucionesEnLaApi(soloEnModoNormal: true);
        var (restauracion, _, _) = Crear();
        restauracion.AgregarTrabajosDeDevolucionesEnLaApi(soloEnModoNormal: false);

        using var proveedorNormal = normal.BuildServiceProvider();
        using var proveedorRestauracion = restauracion.BuildServiceProvider();

        Assert.Contains(proveedorNormal.GetServices<IHostedService>(), s => s is ServicioDeTrabajosDeDevoluciones);
        Assert.DoesNotContain(proveedorRestauracion.GetServices<IHostedService>(), s => s is ServicioDeTrabajosDeDevoluciones);
        Assert.Equal(3, proveedorRestauracion.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeDevoluciones>>().Count);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    public async Task Cada_corrida_fija_el_actor_sistema_lo_limpia_al_terminar_aunque_falle_y_registra_su_resultado()
    {
        var (servicios, actor, registro) = Crear();
        servicios.AgregarTrabajosDeDevolucionesEnLaApi(soloEnModoNormal: false);
        using var proveedor = servicios.BuildServiceProvider();

        var trabajos = proveedor.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeDevoluciones>>();
        foreach (var trabajo in trabajos)
        {
            await trabajo.EjecutarUnaVez();
        }

        // Los repositorios y Pedidos son dobles que lanzan: cada corrida falla cerrado (se registra como fallo, no cae el servicio) y el actor se limpia.
        Assert.Equal(trabajos.Count, registro.Corridas.Count);
        Assert.All(registro.Corridas, c => Assert.Equal(ResultadoDeTrabajo.Fallo, c.Resultado));
        Assert.Null(actor.Actual);
    }
}
