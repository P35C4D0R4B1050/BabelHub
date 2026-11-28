using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class TrabajoDeUmbralesDeAlertaTests
{
    private sealed class RelojDePrueba : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class ContadorDePrueba : IContadorDeEventosDeSeguridad
    {
        public Dictionary<string, int> Conteos { get; } = [];

        public bool Falla { get; set; }

        public Task<int> Contar(string motivo, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default) =>
            Falla ? throw new InvalidOperationException("auditoría caída") : Task.FromResult(CancelarSiCorresponde(cancelacion, Conteos.GetValueOrDefault(motivo)));

        private static int CancelarSiCorresponde(CancellationToken cancelacion, int valor)
        {
            cancelacion.ThrowIfCancellationRequested();
            return valor;
        }
    }

    private sealed class AlertasDeSeguridadDePrueba(IActorActual actor) : IPublicadorDeAlertasDeSeguridad
    {
        public List<(AlertaDeSeguridad Alerta, TipoDeActor? Actor)> Publicadas { get; } = [];

        public Task Publicar(AlertaDeSeguridad alerta, CancellationToken cancelacion = default)
        {
            Publicadas.Add((alerta, actor.Actual?.Tipo));
            return Task.CompletedTask;
        }
    }

    private sealed class AlertasTecnicasDePrueba : IPublicadorDeAlertasTecnicas
    {
        public List<AlertaTecnica> Publicadas { get; } = [];

        public bool Falla { get; set; }

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("sin auditoría");
            }

            Publicadas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    private sealed class RegistroDePrueba : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado, DateTimeOffset Instante)> Corridas { get; } = [];

        public bool Falla { get; set; }

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            if (Falla)
            {
                throw new InvalidOperationException("sin base de datos");
            }

            Corridas.Add((trabajo, resultado, instante));
            return Task.CompletedTask;
        }
    }

    private readonly RelojDePrueba _reloj = new();
    private readonly ContadorDePrueba _contador = new();
    private readonly AlertasTecnicasDePrueba _tecnicas = new();
    private readonly RegistroDePrueba _registro = new();
    private readonly ActorDelFlujo _actor = new();
    private readonly AlertasDeSeguridadDePrueba _alertas;

    public TrabajoDeUmbralesDeAlertaTests()
    {
        _alertas = new AlertasDeSeguridadDePrueba(_actor);
    }

    private TrabajoDeUmbralesDeAlerta Trabajo(OpcionesDelTrabajoDeUmbrales? opciones = null, IReadOnlyList<UmbralDeAlerta>? umbrales = null)
    {
        var evaluar = new EvaluarUmbralesDeAlerta(umbrales ?? UmbralesDeAlerta.Todos, _contador, _alertas, new EstadoDeUmbralesEnMemoria(), _reloj);
        return new TrabajoDeUmbralesDeAlerta(evaluar, _registro, _tecnicas, _actor, _reloj, opciones);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task EjecutarUnaVez_ConDiezAccesosDenegados_NoAlertaYConOnceSiComoActorSistema()
    {
        var trabajo = Trabajo();
        _contador.Conteos["accesos-denegados"] = 10;
        await trabajo.EjecutarUnaVez();
        Assert.Empty(_alertas.Publicadas);

        _contador.Conteos["accesos-denegados"] = 11;
        _reloj.Ahora += TimeSpan.FromMinutes(1);
        await trabajo.EjecutarUnaVez();

        var (alerta, actor) = Assert.Single(_alertas.Publicadas);
        Assert.Equal("accesos-denegados", alerta.Motivo);
        Assert.Equal(11, alerta.Total);
        Assert.Equal(TipoDeActor.Sistema, actor);
        Assert.Null(_actor.Actual);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task EjecutarUnaVez_ConVeinteYVeintiunIngresosFallidos_AlertaSoloConVeintiuno()
    {
        var trabajo = Trabajo();
        _contador.Conteos["ingresos-fallidos"] = 20;
        await trabajo.EjecutarUnaVez();
        Assert.Empty(_alertas.Publicadas);

        _contador.Conteos["ingresos-fallidos"] = 21;
        _reloj.Ahora += TimeSpan.FromMinutes(1);
        await trabajo.EjecutarUnaVez();

        Assert.Equal("ingresos-fallidos", Assert.Single(_alertas.Publicadas).Alerta.Motivo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task EjecutarUnaVez_ConUnSoloUmbralConfigurado_NoEvaluaIngresosFallidos()
    {
        var trabajo = Trabajo(umbrales: [UmbralesDeAlerta.AccesosDenegados]);
        _contador.Conteos["ingresos-fallidos"] = 100;
        _contador.Conteos["accesos-denegados"] = 5;

        await trabajo.EjecutarUnaVez();

        Assert.Empty(_alertas.Publicadas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task EjecutarUnaVez_MientrasElUmbralSigueSuperado_NoRepiteLaAlertaMinutoTrasMinuto()
    {
        var trabajo = Trabajo();
        _contador.Conteos["accesos-denegados"] = 30;

        for (var minuto = 0; minuto < 5; minuto++)
        {
            await trabajo.EjecutarUnaVez();
            _reloj.Ahora += TimeSpan.FromMinutes(1);
        }

        Assert.Single(_alertas.Publicadas);
        Assert.Equal(5, _registro.Corridas.Count(c => c.Resultado == ResultadoDeTrabajo.Exito));
    }

    [Fact]
    public async Task EjecutarUnaVez_RegistraCadaCorridaConSuNombreResultadoEInstante()
    {
        var trabajo = Trabajo();

        await trabajo.EjecutarUnaVez();

        var corrida = Assert.Single(_registro.Corridas);
        Assert.Equal("evaluar-umbrales-de-alerta", corrida.Trabajo);
        Assert.Equal(ResultadoDeTrabajo.Exito, corrida.Resultado);
        Assert.Equal(_reloj.Ahora, corrida.Instante);
    }

    [Fact]
    public async Task EjecutarUnaVez_ConUnaCorridaQueFalla_LaRegistraSinLanzarYSeguraLaSiguiente()
    {
        var trabajo = Trabajo();
        _contador.Falla = true;

        await trabajo.EjecutarUnaVez();
        _contador.Falla = false;
        _contador.Conteos["accesos-denegados"] = 12;
        _reloj.Ahora += TimeSpan.FromMinutes(1);
        await trabajo.EjecutarUnaVez();

        Assert.Equal([ResultadoDeTrabajo.Fallo, ResultadoDeTrabajo.Exito], _registro.Corridas.Select(c => c.Resultado));
        Assert.Single(_alertas.Publicadas);
        Assert.Null(_actor.Actual);
    }

    [Fact]
    public async Task EjecutarUnaVez_ConCincoFallosSeguidos_PublicaUnaAlertaTecnicaUnaSolaVezPorRacha()
    {
        var trabajo = Trabajo();
        _contador.Falla = true;

        for (var i = 0; i < 8; i++)
        {
            _reloj.Ahora += TimeSpan.FromMinutes(1);
            await trabajo.EjecutarUnaVez();
        }

        var alerta = Assert.Single(_tecnicas.Publicadas);
        Assert.Equal(TrabajoDeUmbralesDeAlerta.MotivoDeAlerta, alerta.Motivo);

        // Un éxito reinicia la racha: otros cinco fallos seguidos vuelven a alertar.
        _contador.Falla = false;
        _reloj.Ahora += TimeSpan.FromMinutes(1);
        await trabajo.EjecutarUnaVez();
        _contador.Falla = true;
        for (var i = 0; i < 5; i++)
        {
            _reloj.Ahora += TimeSpan.FromMinutes(1);
            await trabajo.EjecutarUnaVez();
        }

        Assert.Equal(2, _tecnicas.Publicadas.Count);
    }

    [Fact]
    public async Task EjecutarUnaVez_ConElRegistroYLaAlertaCaidos_NoInterrumpeElTrabajo()
    {
        var trabajo = Trabajo(new OpcionesDelTrabajoDeUmbrales { FallosSeguidosParaAlertar = 1 });
        _contador.Falla = true;
        _registro.Falla = true;
        _tecnicas.Falla = true;

        await trabajo.EjecutarUnaVez();

        Assert.Empty(_registro.Corridas);
        Assert.Empty(_tecnicas.Publicadas);
    }

    [Fact]
    public async Task EjecutarUnaVez_ConLaCancelacionDeQuienLoEjecuta_SePropaga()
    {
        var trabajo = Trabajo();
        using var fuente = new CancellationTokenSource();
        await fuente.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => trabajo.EjecutarUnaVez(fuente.Token));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task Correr_EvaluaAlEmpezarYEnCadaIntervaloYTerminaAlCancelar()
    {
        var trabajo = Trabajo(new OpcionesDelTrabajoDeUmbrales { Intervalo = TimeSpan.FromMilliseconds(60) });
        _contador.Conteos["accesos-denegados"] = 15;
        using var fuente = new CancellationTokenSource();

        var corrida = trabajo.Correr(fuente.Token);
        await Task.Delay(500);
        await fuente.CancelAsync();
        await corrida.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.InRange(_registro.Corridas.Count, 3, 12);
        Assert.Single(_alertas.Publicadas);
        Assert.True(corrida.IsCompletedSuccessfully);
    }
}
