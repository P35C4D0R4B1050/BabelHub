using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Auditoria;
using BabelHub.Inventario.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.AuditoriaInventario;

/// <summary>
/// Los trabajos de fondo del módulo (T27; AUD-28, AUD-29, AUD-31, AUD-41; AC-AUD-25, AC-AUD-26, AC-AUD-28): corren como «sistema» fijado explícitamente, registran su corrida, avisan tras fallos
/// seguidos, la depuración corre una vez cada 24 horas y el apagado ordenado los detiene.
/// </summary>
public sealed class TrabajosDeFondoDeAuditoriaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class RegistroEspia : IRegistroDeTrabajosDeFondo
    {
        public List<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas { get; } = [];

        public bool Falla { get; set; }

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            Corridas.Add((trabajo, resultado));
            return Falla ? Task.FromException(new InvalidOperationException("sin base")) : Task.CompletedTask;
        }
    }

    private sealed class AlertasEspia : IAlertas
    {
        public List<string> Tecnicas { get; } = [];

        public bool Falla { get; set; }

        public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default)
        {
            Tecnicas.Add(motivo.Valor);
            return Falla ? Task.FromException(new InvalidOperationException("sin base")) : Task.CompletedTask;
        }

        public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class Entorno
    {
        public Entorno(Func<ActorActual?, CancellationToken, Task>? corrida = null, TimeSpan? intervalo = null)
        {
            Trabajo = new TrabajoDeFondoDeAuditoria(
                "auditoria-prueba",
                async ct =>
                {
                    ActoresVistos.Add(Actores.Actual);
                    if (corrida is not null)
                    {
                        await corrida(Actores.Actual, ct);
                    }
                },
                Registro,
                Actores,
                Alertas,
                Reloj,
                new OpcionesDelTrabajoDeAuditoria(intervalo ?? TimeSpan.FromMinutes(5)));
        }

        public ActorDelFlujo Actores { get; } = new();

        public List<ActorActual?> ActoresVistos { get; } = [];

        public RegistroEspia Registro { get; } = new();

        public AlertasEspia Alertas { get; } = new();

        public RelojDePrueba Reloj { get; } = new(Ahora);

        public TrabajoDeFondoDeAuditoria Trabajo { get; }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task ElTrabajoCorreComoSistemaFijadoExplicitamenteYLimpiaElActorAlTerminar()
    {
        var e = new Entorno();
        Assert.Null(e.Actores.Actual);

        await e.Trabajo.EjecutarUnaVez();

        var visto = Assert.Single(e.ActoresVistos);
        Assert.Equal(TipoDeActor.Sistema, visto!.Tipo);
        Assert.Null(e.Actores.Actual);
        Assert.Equal([("auditoria-prueba", ResultadoDeTrabajo.Exito)], e.Registro.Corridas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task UnaCorridaQueFallaSeRegistraComoFalloYNoDejaCaerElServicio_NiSeQuedaConElActor()
    {
        var e = new Entorno((_, _) => throw new InvalidOperationException("Host=db;Password=Sup3r"));

        await e.Trabajo.EjecutarUnaVez();

        Assert.Equal([("auditoria-prueba", ResultadoDeTrabajo.Fallo)], e.Registro.Corridas);
        Assert.Null(e.Actores.Actual);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-31")]
    public async Task TrasTresFallosSeguidosSePublicaUnaAlertaTecnicaUnaSolaVez_YUnaCorridaExitosaReiniciaLaCuenta()
    {
        var falla = true;
        var e = new Entorno((_, _) => falla ? throw new InvalidOperationException("falla") : Task.CompletedTask);

        for (var i = 0; i < 5; i++)
        {
            await e.Trabajo.EjecutarUnaVez();
        }

        Assert.Equal([MotivosDeAlerta.TrabajoDeFondoDetenido], e.Alertas.Tecnicas);

        falla = false;
        await e.Trabajo.EjecutarUnaVez();
        falla = true;
        for (var i = 0; i < 3; i++)
        {
            await e.Trabajo.EjecutarUnaVez();
        }

        Assert.Equal(2, e.Alertas.Tecnicas.Count);
    }

    [Fact]
    public async Task SiNoSePuedeRegistrarLaCorridaOPublicarLaAlerta_ElTrabajoSigue()
    {
        var e = new Entorno((_, _) => throw new InvalidOperationException("falla"));
        e.Registro.Falla = true;
        e.Alertas.Falla = true;

        for (var i = 0; i < 4; i++)
        {
            await e.Trabajo.EjecutarUnaVez();
        }

        Assert.Equal(4, e.Registro.Corridas.Count);
    }

    [Fact]
    public async Task LaCancelacionDelApagadoSePropagaYNoSeCuentaComoFallo()
    {
        var e = new Entorno(async (_, ct) => await Task.Delay(Timeout.Infinite, ct));
        using var fuente = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => e.Trabajo.EjecutarUnaVez(fuente.Token));

        Assert.Empty(e.Registro.Corridas);
        Assert.Null(e.Actores.Actual);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task CorrerEjecutaAlEmpezarYEnCadaIntervaloYTerminaAlCancelar()
    {
        var e = new Entorno(intervalo: TimeSpan.FromMilliseconds(40));
        using var fuente = new CancellationTokenSource();

        var corrida = e.Trabajo.Correr(fuente.Token);
        await Task.Delay(400);
        await fuente.CancelAsync();
        await corrida;

        Assert.True(e.Registro.Corridas.Count >= 3, $"corridas: {e.Registro.Corridas.Count}");
    }

    [Fact]
    public async Task ElServicioCorreTodosLosTrabajosYSeDetieneAlApagar()
    {
        var a = new Entorno(intervalo: TimeSpan.FromMilliseconds(40));
        var b = new Entorno(intervalo: TimeSpan.FromMilliseconds(40));
        var servicio = new ServicioDeTrabajosDeAuditoria([a.Trabajo, b.Trabajo]);

        await servicio.StartAsync(CancellationToken.None);
        await Task.Delay(300);
        await servicio.StopAsync(CancellationToken.None);

        Assert.True(a.Registro.Corridas.Count >= 2);
        Assert.True(b.Registro.Corridas.Count >= 2);
    }

    // ---------- Depuración diaria: una vez cada 24 horas ----------

    private sealed class AnonimizadorContado : IAnonimizadorDeRegistros
    {
        public int Llamadas { get; private set; }

        public bool Falla { get; set; }

        public Task<int> Anonimizar(CancellationToken cancelacion = default)
        {
            Llamadas++;
            return Falla ? Task.FromException<int>(new InvalidOperationException("falla")) : Task.FromResult(0);
        }
    }

    private sealed class PorAnonimizarVacio : IAlmacenDePorAnonimizar
    {
        public Task<int> Agregar(TipoDePorAnonimizar tipo, IReadOnlyCollection<Guid> identificadores, DateTimeOffset registradoEn, CancellationToken cancelacion = default) => Task.FromResult(0);
    }

    private sealed class AuditoriaContada : IAuditoria
    {
        public int Registros { get; private set; }

        public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
        {
            Registros++;
            return Task.CompletedTask;
        }
    }

    private sealed class EstadoEnMemoria : IEstadoDeLaDepuracion
    {
        public EstadoDeLaDepuracion Estado { get; set; } = EstadoDeLaDepuracion.Inicial;

        public Task<EstadoDeLaDepuracion> Obtener(CancellationToken cancelacion = default) => Task.FromResult(Estado);

        public Task Guardar(EstadoDeLaDepuracion estado, CancellationToken cancelacion = default)
        {
            Estado = estado;
            return Task.CompletedTask;
        }
    }

    private sealed class BloqueoLibre : IBloqueoDeLaDepuracion
    {
        public Task<IAsyncDisposable?> Intentar(CancellationToken cancelacion = default) => Task.FromResult<IAsyncDisposable?>(new Nada());

        private sealed class Nada : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class UnidadSimple : IUnidadDeTrabajo
    {
        public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default) => await trabajo(cancelacion);

        public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
    }

    private sealed class EntornoDeDepuracion
    {
        public EntornoDeDepuracion()
        {
            var depurar = new DepurarAuditoria([], new PorAnonimizarVacio(), Anonimizador, [], Auditoria, Estado, new BloqueoLibre(), Alertas, Actores, new UnidadSimple(), Reloj);
            Trabajo = TrabajosDeAuditoria.Depuracion(depurar, Estado, Registro, Actores, Alertas, Reloj);
        }

        public ActorDelFlujo Actores { get; } = new();

        public RelojDePrueba Reloj { get; } = new(Ahora);

        public AnonimizadorContado Anonimizador { get; } = new();

        public AuditoriaContada Auditoria { get; } = new();

        public EstadoEnMemoria Estado { get; } = new();

        public RegistroEspia Registro { get; } = new();

        public AlertasEspia Alertas { get; } = new();

        public TrabajoDeFondoDeAuditoria Trabajo { get; }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task LaDepuracionCorreLaPrimeraVez_NoAntesDe24HorasDeLaUltimaExitosa_YSiDespues()
    {
        var e = new EntornoDeDepuracion();

        await e.Trabajo.EjecutarUnaVez();
        Assert.Equal(1, e.Anonimizador.Llamadas);
        Assert.Equal(1, e.Auditoria.Registros);

        e.Reloj.Ahora = Ahora.AddHours(23).AddMinutes(59);
        await e.Trabajo.EjecutarUnaVez();
        Assert.Equal(1, e.Anonimizador.Llamadas);

        e.Reloj.Ahora = Ahora.AddHours(24);
        await e.Trabajo.EjecutarUnaVez();
        Assert.Equal(2, e.Anonimizador.Llamadas);
        Assert.Equal([TrabajosDeAuditoria.DepuracionDiaria, TrabajosDeAuditoria.DepuracionDiaria, TrabajosDeAuditoria.DepuracionDiaria], e.Registro.Corridas.Select(c => c.Trabajo));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task UnaDepuracionQueFallaSeRegistraComoFallo_ConLaEsperaCrecienteEnElEstado()
    {
        var e = new EntornoDeDepuracion();
        e.Anonimizador.Falla = true;

        await e.Trabajo.EjecutarUnaVez();

        Assert.Equal([(TrabajosDeAuditoria.DepuracionDiaria, ResultadoDeTrabajo.Fallo)], e.Registro.Corridas);
        Assert.Equal(1, e.Estado.Estado.IntentosFallidos);
        Assert.Equal(Ahora.AddMinutes(1), e.Estado.Estado.ProximoIntento);

        // Antes de la espera el trabajo vuelve a pasar por la depuración, que no hace nada (EnEspera) y la corrida cuenta como exitosa.
        e.Reloj.Ahora = Ahora.AddSeconds(30);
        await e.Trabajo.EjecutarUnaVez();
        Assert.Equal(1, e.Anonimizador.Llamadas);

        // Tras la espera, reintenta y, si funciona, reinicia el estado.
        e.Anonimizador.Falla = false;
        e.Reloj.Ahora = Ahora.AddMinutes(2);
        await e.Trabajo.EjecutarUnaVez();
        Assert.Equal(2, e.Anonimizador.Llamadas);
        Assert.Equal(0, e.Estado.Estado.IntentosFallidos);
    }

    [Fact]
    public void LosIntervalosSonLosDelPlan_ElCorreoMenorQueLaVentanaDeAgrupacion()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), TrabajosDeAuditoria.IntervaloDelCierre.Intervalo);
        Assert.Equal(TimeSpan.FromMinutes(5), TrabajosDeAuditoria.IntervaloDelCorreo.Intervalo);
        Assert.True(TrabajosDeAuditoria.IntervaloDelCorreo.Intervalo < TimeSpan.FromMinutes(15));
        Assert.Equal(TimeSpan.FromHours(24), TrabajosDeAuditoria.PeriodoDeLaDepuracion);
    }
}
