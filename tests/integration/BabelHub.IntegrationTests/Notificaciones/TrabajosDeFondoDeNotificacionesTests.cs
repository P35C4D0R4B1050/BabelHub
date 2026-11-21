using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// Los trabajos de fondo de Notificaciones (transversal-notificaciones T14; NOT-04, AUD-31): corren como el actor «sistema», registran el resultado de cada corrida, alertan tras fallos seguidos, cancelan una corrida colgada y no
/// dejan caer el servicio. Sin base de datos: el ejecutor con dobles; los intervalos y los nombres, de la definición real.
/// </summary>
public sealed class TrabajosDeFondoDeNotificacionesTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.UtcDateTime.AddHours(-5));
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
        public List<string> Motivos { get; } = [];

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            Motivos.Add(alerta.Motivo);
            return Task.CompletedTask;
        }
    }

    private sealed class ActorEspia
    {
        public int Corridas { get; private set; }

        public Task Ejecutar(Func<CancellationToken, Task> corrida, CancellationToken cancelacion)
        {
            Corridas++;
            return corrida(cancelacion);
        }
    }

    private static (TrabajoDeFondoDeNotificaciones Trabajo, RegistroEspia Registro, AlertasEspia Alertas, ActorEspia Actor) Trabajo(Func<CancellationToken, Task> corrida, OpcionesDelTrabajoDeNotificaciones? opciones = null)
    {
        var (registro, alertas, actor) = (new RegistroEspia(), new AlertasEspia(), new ActorEspia());
        return (new TrabajoDeFondoDeNotificaciones("notificaciones-prueba", corrida, registro, actor.Ejecutar, alertas, new RelojFijo(Ahora), opciones ?? new OpcionesDelTrabajoDeNotificaciones(TimeSpan.FromMinutes(1))), registro, alertas, actor);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task LaCorrida_CorreComoSistema_YSuResultadoSeRegistra()
    {
        var (trabajo, registro, alertas, actor) = Trabajo(_ => Task.CompletedTask);

        await trabajo.EjecutarUnaVez();

        Assert.Equal(1, actor.Corridas);
        Assert.Equal([("notificaciones-prueba", ResultadoDeTrabajo.Exito)], registro.Corridas);
        Assert.Empty(alertas.Motivos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task TrasTresFallosSeguidos_AlertaUnaVezSinPublicarNadaPersonal_YUnExitoReiniciaLaCuenta()
    {
        var falla = true;
        var (trabajo, registro, alertas, _) = Trabajo(_ => falla ? throw new InvalidOperationException("n8n no responde") : Task.CompletedTask);

        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        Assert.Empty(alertas.Motivos);
        await trabajo.EjecutarUnaVez();
        Assert.Equal([TrabajoDeFondoDeNotificaciones.MotivoDelTrabajoDetenido], alertas.Motivos);

        falla = false;
        await trabajo.EjecutarUnaVez();
        falla = true;
        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();

        Assert.Single(alertas.Motivos);
        Assert.Equal([ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Exito, ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Fallo], registro.Corridas.Select(c => c.Resultado));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task UnaCorridaColgada_SeCancelaAlPlazo_YCuentaComoFallo_ParaPoderAlertar()
    {
        var (trabajo, registro, alertas, _) = Trabajo(ct => Task.Delay(Timeout.Infinite, ct), new OpcionesDelTrabajoDeNotificaciones(TimeSpan.FromMinutes(1), 1, TimeSpan.FromMilliseconds(50)));

        await trabajo.EjecutarUnaVez();

        Assert.Equal([("notificaciones-prueba", ResultadoDeTrabajo.Fallo)], registro.Corridas);
        Assert.Single(alertas.Motivos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task CorrerNoLanza_YSeDetieneConLaCancelacion_DespuesDeLaPrimeraCorrida()
    {
        using var origen = new CancellationTokenSource();
        var corridas = 0;
        var (trabajo, _, _, _) = Trabajo(_ =>
        {
            corridas++;
            origen.Cancel();
            return Task.CompletedTask;
        });

        await trabajo.Correr(origen.Token);

        Assert.Equal(1, corridas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task CorrerRepiteLaCorridaEnCadaIntervalo()
    {
        using var origen = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var corridas = 0;
        var (trabajo, _, _, _) = Trabajo(
            _ =>
            {
                if (++corridas == 3)
                {
                    origen.Cancel();
                }

                return Task.CompletedTask;
            },
            new OpcionesDelTrabajoDeNotificaciones(TimeSpan.FromMilliseconds(20)));

        await trabajo.Correr(origen.Token);

        Assert.Equal(3, corridas);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public void LosTrabajosDeNotificaciones_TienenNombresEstables_ElEnvioCada30SegundosPorLotesDe10_YLaPurgaCada6Horas()
    {
        var trabajos = TrabajosDeNotificaciones.Todos(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new RegistroEspia(), new ActorEspia().Ejecutar, new AlertasEspia(), new RelojFijo(Ahora));

        Assert.Equal([TrabajosDeNotificaciones.EnvioDeAvisos, TrabajosDeNotificaciones.PurgaDeAvisos], trabajos.Select(t => t.Nombre));
        Assert.Equal(TimeSpan.FromSeconds(30), TrabajosDeNotificaciones.OpcionesDelEnvio.Intervalo);
        Assert.Equal(TimeSpan.FromMinutes(10), TrabajosDeNotificaciones.OpcionesDelEnvio.PlazoDeLaCorrida);
        Assert.Equal(10, TrabajosDeNotificaciones.LotesPorCorrida);
        Assert.Equal(TimeSpan.FromHours(6), TrabajosDeNotificaciones.OpcionesDeLaPurga.Intervalo);
        Assert.Equal(1, TrabajosDeNotificaciones.OpcionesDeLaPurga.FallosSeguidosParaAlertar);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-04")]
    public async Task LaCorridaRealDelEnvio_SinLosServiciosDelModulo_FallaSinCaerElServicio_YQuedaRegistrada()
    {
        var registro = new RegistroEspia();
        var trabajos = TrabajosDeNotificaciones.Todos(new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), registro, new ActorEspia().Ejecutar, new AlertasEspia(), new RelojFijo(Ahora));

        await trabajos[0].EjecutarUnaVez();

        Assert.Equal([(TrabajosDeNotificaciones.EnvioDeAvisos, ResultadoDeTrabajo.Fallo)], registro.Corridas);
    }
}
