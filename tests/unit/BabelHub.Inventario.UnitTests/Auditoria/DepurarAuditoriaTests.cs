using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.UnitTests.Plataforma;
using BabelHub.Inventario.UnitTests.Soporte;

namespace BabelHub.Inventario.UnitTests.Auditoria;

/// <summary>
/// La depuración diaria del Inventario (T19; AUD-19, AUD-31, AUD-32, AUD-36): llena <c>por_anonimizar</c> con las bajas de titular recibidas y las marca entregadas, anonimiza con
/// la función de la base, registra la ejecución con el actor «sistema» aunque sea cero y reintenta con esperas crecientes y alerta a las 24 horas.
/// </summary>
public sealed class DepurarAuditoriaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    private sealed class BajasFalsas : IBajasRecibidas
    {
        public List<Guid> Disponibles { get; } = [];

        public List<Guid> Entregadas { get; } = [];

        public Task<IReadOnlyList<Guid>> Pendientes(int maximo, CancellationToken cancelacion = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([.. Disponibles.Except(Entregadas).Take(maximo)]);

        public Task MarcarEntregadas(IReadOnlyCollection<Guid> identificadores, CancellationToken cancelacion = default)
        {
            Entregadas.AddRange(identificadores);
            return Task.CompletedTask;
        }
    }

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Reloj = new RelojFijo(Ahora);
            Depurar = new DepurarAuditoria(
                [new BajasRecibidasComoFuente(Bajas)],
                PorAnonimizar,
                Anonimizador,
                [],
                Auditoria,
                Estados,
                Bloqueo,
                Alertas,
                actor ?? ActorDePrueba.DelSistema(),
                Unidad,
                Reloj);
        }

        public RelojFijo Reloj { get; }

        public BajasFalsas Bajas { get; } = new();

        public PorAnonimizarEnMemoria PorAnonimizar { get; } = new();

        public AnonimizadorFalso Anonimizador { get; } = new();

        public AuditoriaEspia Auditoria { get; } = new();

        public EstadoDeDepuracionEnMemoria Estados { get; } = new();

        public BloqueoDeDepuracionFalso Bloqueo { get; } = new();

        public AlertasEspia Alertas { get; } = new();

        public UnidadDeTrabajoDeAuditoria Unidad { get; } = new();

        public DepurarAuditoria Depurar { get; }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public async Task LasBajasRecibidasSeCopianAPorAnonimizarSeMarcanEntregadasYLaDepuracionAnonimizaSuRastro()
    {
        var e = new Entorno();
        var baja = Guid.NewGuid();
        e.Bajas.Disponibles.Add(baja);
        e.Anonimizador.Respuestas.Enqueue(4);

        var resultado = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Ejecutada, resultado.Estado);
        Assert.Equal(4, resultado.RegistrosAnonimizados);
        Assert.Contains((TipoDePorAnonimizar.BajaDeTitular, baja), e.PorAnonimizar.Filas);
        Assert.Equal([baja], e.Bajas.Entregadas);
        Assert.Equal("4", Assert.Single(e.Auditoria.Solicitudes).Detalle["registrosAnonimizados"]);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public async Task UnaBajaRepetidaNoDuplicaNadaYLaEjecucionSeRegistraConCero()
    {
        var e = new Entorno();
        e.Bajas.Disponibles.Add(Guid.NewGuid());
        await e.Depurar.Ejecutar();

        var segunda = await e.Depurar.Ejecutar();

        Assert.Equal(0, segunda.IdentificadoresNuevos);
        Assert.Single(e.PorAnonimizar.Filas);
        Assert.Equal("0", e.Auditoria.Solicitudes[^1].Detalle["registrosAnonimizados"]);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task SinElActorSistemaLaDepuracionSeNiega()
    {
        foreach (var actor in new[] { ActorDePrueba.SinContexto(), ActorDePrueba.Anonimo(), ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeIntegracion("ecommerce") })
        {
            var e = new Entorno(actor);

            await Assert.ThrowsAsync<InvalidOperationException>(() => e.Depurar.Ejecutar());
            Assert.Equal(0, e.Anonimizador.Llamadas);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task UnFalloReintentaConEsperasCrecientes_ALasVeinticuatroHorasAlertaYOtraCorridaNoLaPisa()
    {
        var e = new Entorno();
        e.Anonimizador.Falla = new InvalidOperationException("Host=db;Password=Sup3r");

        var primera = await e.Depurar.Ejecutar();
        Assert.Equal(Ahora.AddMinutes(1), e.Estados.Estado.ProximoIntento);
        Assert.False(primera.AlertaPublicada);

        e.Estados.Estado = new EstadoDeLaDepuracion(Ahora.AddDays(-1), Ahora.AddHours(-24), 30, Ahora.AddMinutes(-1));
        var tardia = await e.Depurar.Ejecutar();
        Assert.True(tardia.AlertaPublicada);
        Assert.Equal([MotivosDeAlerta.DepuracionDiariaSinExito], e.Alertas.Tecnicas);

        e.Bloqueo.OtroLoTiene = true;
        Assert.Equal(EstadoDeLaCorridaDeDepuracion.OtraCorridaEnCurso, (await e.Depurar.Ejecutar()).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task SiNoSePuedeRegistrarLaEjecucionLaCorridaFallaYSeReintenta()
    {
        var e = new Entorno();
        e.Auditoria.Caida = true;

        var resultado = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Fallida, resultado.Estado);
        Assert.Equal(1, e.Estados.Estado.IntentosFallidos);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task ElPasoDeRestauracionCorreAlFinalSinEsperarYFallaSiLaDepuracionFalla()
    {
        var e = new Entorno();
        e.Estados.Estado = new EstadoDeLaDepuracion(null, Ahora.AddHours(-2), 3, Ahora.AddHours(5));
        var paso = new PasoDeRestauracionDeDepuracion(e.Depurar);

        Assert.Equal(OrdenDePasoDeRestauracion.Auditoria, paso.Orden);
        Assert.Equal(ResultadoDePaso.Correcto, await paso.Ejecutar());

        e.Anonimizador.Falla = new InvalidOperationException("falla");
        Assert.Equal(ResultadoDePaso.Fallido, await paso.Ejecutar());
    }
}
