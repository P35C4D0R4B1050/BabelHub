using System.Reflection;
using BabelHub.Ecommerce.Api.Envios;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Envios;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>T17: la Api hospeda los trabajos de fondo de Envíos (ENV-07, ENV-11; AC-ENV-07) <b>solo en el modo normal</b> y con el actor «sistema» fijado alrededor de cada corrida (y limpiado al terminar).</summary>
public sealed class TrabajosDeEnviosEnLaApiTests
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
        public int Publicadas { get; private set; }

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            Publicadas++;
            return Task.CompletedTask;
        }
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
        servicios.AgregarDoblesProvisionalesDePedidos(esProduccion: false);
        servicios.AgregarEnvios(esProduccion: false);
        return (servicios, actor, registro);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public void El_servicio_que_corre_los_trabajos_solo_se_registra_en_el_modo_normal()
    {
        var (normal, _, _) = Crear();
        normal.AgregarTrabajosDeEnviosEnLaApi(soloEnModoNormal: true);
        var (restauracion, _, _) = Crear();
        restauracion.AgregarTrabajosDeEnviosEnLaApi(soloEnModoNormal: false);

        using var proveedorNormal = normal.BuildServiceProvider();
        using var proveedorRestauracion = restauracion.BuildServiceProvider();

        Assert.Contains(proveedorNormal.GetServices<IHostedService>(), s => s is ServicioDeTrabajosDeEnvios);
        Assert.DoesNotContain(proveedorRestauracion.GetServices<IHostedService>(), s => s is ServicioDeTrabajosDeEnvios);
        Assert.Equal(2, proveedorRestauracion.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeEnvios>>().Count);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task Cada_corrida_fija_el_actor_sistema_lo_limpia_al_terminar_y_registra_su_resultado()
    {
        var (servicios, actor, registro) = Crear();
        servicios.AgregarTrabajosDeEnviosEnLaApi(soloEnModoNormal: false);
        using var proveedor = servicios.BuildServiceProvider();

        foreach (var trabajo in proveedor.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeEnvios>>())
        {
            await trabajo.EjecutarUnaVez();
        }

        // La base y Pedidos son dobles que lanzan: la corrida falla cerrada (se registra como fallo y no cae el servicio) y el actor se limpia.
        Assert.Equal(["envios-reintento-de-progreso", "envios-revision-de-estancados"], registro.Corridas.Select(c => c.Trabajo));
        Assert.All(registro.Corridas, c => Assert.Equal(ResultadoDeTrabajo.Fallo, c.Resultado));
        Assert.Null(actor.Actual);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task Con_un_actor_de_usuario_en_lugar_del_sistema_la_corrida_termina_en_fallo()
    {
        var (servicios, actor, registro) = Crear();
        servicios.AgregarTrabajosDeFondoDeEnvios(sp => async (corrida, ct) =>
        {
            actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), "personal-de-logistica", null));
            try
            {
                await corrida(ct);
            }
            finally
            {
                actor.Limpiar();
            }
        });
        using var proveedor = servicios.BuildServiceProvider();

        await proveedor.GetRequiredService<IReadOnlyList<TrabajoDeFondoDeEnvios>>()[1].EjecutarUnaVez();

        var corrida = Assert.Single(registro.Corridas);
        Assert.Equal(ResultadoDeTrabajo.Fallo, corrida.Resultado);
        Assert.Null(actor.Actual);
    }
}
