using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Auditoria;

/// <summary>
/// La depuración diaria (T19; AUD-11, AUD-19, AUD-31, AUD-36): llena <c>por_anonimizar</c> desde las cuentas canceladas, anonimiza con la función de la base y con la de
/// Identidad, registra la ejecución con el actor «sistema» aunque sea cero, no se pisa con otra corrida, reintenta con esperas crecientes y alerta a las 24 horas.
/// </summary>
public sealed class DepurarAuditoriaTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 8, 0, 0, TimeSpan.Zero);

    private sealed class Entorno
    {
        public Entorno(ActorDePrueba? actor = null)
        {
            Reloj = new RelojFijo(Ahora);
            Depurar = new DepurarAuditoria(
                [Cuentas],
                PorAnonimizar,
                Anonimizador,
                [Identidad],
                Auditoria,
                Estados,
                Bloqueo,
                Alertas,
                actor ?? ActorDePrueba.DelSistema(),
                Unidad,
                Reloj);
        }

        public RelojFijo Reloj { get; }

        public FuenteDeIdentificadoresFalsa Cuentas { get; } = new(TipoDePorAnonimizar.CuentaCancelada);

        public PorAnonimizarEnMemoria PorAnonimizar { get; } = new();

        public AnonimizadorFalso Anonimizador { get; } = new();

        public DepuradorAdicionalFalso Identidad { get; } = new(3);

        public AuditoriaEspia Auditoria { get; } = new();

        public EstadoDeDepuracionEnMemoria Estados { get; } = new();

        public BloqueoDeDepuracionFalso Bloqueo { get; } = new();

        public AlertasEspia Alertas { get; } = new();

        public UnidadDeTrabajoDeAuditoria Unidad { get; } = new();

        public DepurarAuditoria Depurar { get; }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task LaCorridaLlenaPorAnonimizarAnonimizaConLasDosFuncionesYRegistraLaEjecucionConElTotal()
    {
        var e = new Entorno();
        var cuenta = Guid.NewGuid();
        e.Cuentas.Disponibles.Add(cuenta);
        e.Anonimizador.Respuestas.Enqueue(9);

        var resultado = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Ejecutada, resultado.Estado);
        Assert.Equal(12, resultado.RegistrosAnonimizados);
        Assert.Equal(1, resultado.IdentificadoresNuevos);
        Assert.Contains((TipoDePorAnonimizar.CuentaCancelada, cuenta), e.PorAnonimizar.Filas);
        var solicitud = Assert.Single(e.Auditoria.Solicitudes);
        Assert.Equal(AccionesAuditables.DepuracionDiariaEjecutada, solicitud.Accion);
        Assert.Equal("12", solicitud.Detalle["registrosAnonimizados"]);
        Assert.Equal(Ahora, e.Estados.Estado.UltimaExitosa);
        Assert.Equal(1, e.Unidad.Ejecuciones);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task RepetirLaDepuracionElMismoDia_NoCambiaNadaYRegistraCero()
    {
        var e = new Entorno();
        e.Cuentas.Disponibles.Add(Guid.NewGuid());
        e.Anonimizador.Respuestas.Enqueue(5);
        await e.Depurar.Ejecutar();
        var identidad = new DepuradorAdicionalFalso(0);
        var segunda = new DepurarAuditoria([e.Cuentas], e.PorAnonimizar, e.Anonimizador, [identidad], e.Auditoria, e.Estados, e.Bloqueo, e.Alertas, ActorDePrueba.DelSistema(), e.Unidad, e.Reloj);

        var resultado = await segunda.Ejecutar();

        Assert.Equal(0, resultado.RegistrosAnonimizados);
        Assert.Equal(0, resultado.IdentificadoresNuevos);
        Assert.Single(e.PorAnonimizar.Filas);
        Assert.Equal(["8", "0"], e.Auditoria.Solicitudes.Select(s => s.Detalle["registrosAnonimizados"]));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task SinCuentasNuevasLaEjecucionSeRegistraConCero()
    {
        var e = new Entorno();
        var sinIdentidad = new DepurarAuditoria([e.Cuentas], e.PorAnonimizar, e.Anonimizador, [], e.Auditoria, e.Estados, e.Bloqueo, e.Alertas, ActorDePrueba.DelSistema(), e.Unidad, e.Reloj);

        await sinIdentidad.Ejecutar();

        Assert.Equal("0", Assert.Single(e.Auditoria.Solicitudes).Detalle["registrosAnonimizados"]);
        Assert.Empty(e.PorAnonimizar.Lotes);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-36")]
    public async Task LaDepuracionSeNiegaACorrerSinElActorSistema()
    {
        foreach (var actor in new[] { ActorDePrueba.SinContexto(), ActorDePrueba.Anonimo(), ActorDePrueba.DeAdministradorCentral(), ActorDePrueba.DeIntegracion("x") })
        {
            var e = new Entorno(actor);

            await Assert.ThrowsAsync<InvalidOperationException>(() => e.Depurar.Ejecutar());
            await Assert.ThrowsAsync<InvalidOperationException>(() => e.Depurar.EjecutarAntesDeAbrir());
            Assert.Equal(0, e.Anonimizador.Llamadas);
            Assert.Equal(0, e.Bloqueo.Tomados);
        }
    }

    [Fact]
    public async Task LosIdentificadoresSeCopianPorLotesDeComoMaximoMilYLaCorridaSiguienteContinua()
    {
        var e = new Entorno();
        e.Cuentas.Disponibles.AddRange(Enumerable.Range(0, 1500).Select(_ => Guid.NewGuid()));

        var primera = await e.Depurar.Ejecutar();
        var segunda = await e.Depurar.Ejecutar();

        Assert.Equal(1000, primera.IdentificadoresNuevos);
        Assert.Equal(500, segunda.IdentificadoresNuevos);
        Assert.Equal(1500, e.PorAnonimizar.Filas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task OtraCorridaConElBloqueo_NoHaceNadaYElBloqueoSiempreSeLibera()
    {
        var e = new Entorno();
        e.Bloqueo.OtroLoTiene = true;

        var ocupada = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.OtraCorridaEnCurso, ocupada.Estado);
        Assert.Equal(0, e.Anonimizador.Llamadas);

        e.Bloqueo.OtroLoTiene = false;
        e.Anonimizador.Falla = new InvalidOperationException("la base no responde");
        await e.Depurar.Ejecutar();
        Assert.Equal(e.Bloqueo.Tomados, e.Bloqueo.Liberados);
    }

    // ---------- Fallos y reintentos ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task UnFalloReintentaConEsperasCrecientesYNoAlertaAntesDeLas24Horas()
    {
        var e = new Entorno();
        e.Anonimizador.Falla = new InvalidOperationException("Host=db;Password=Sup3r");
        var esperadas = new[] { 1, 2, 4, 8, 16, 32, 60, 60 };

        for (var n = 0; n < esperadas.Length; n++)
        {
            var resultado = await e.Depurar.Ejecutar();

            Assert.Equal(EstadoDeLaCorridaDeDepuracion.Fallida, resultado.Estado);
            Assert.False(resultado.AlertaPublicada);
            Assert.Equal(n + 1, e.Estados.Estado.IntentosFallidos);
            Assert.Equal(e.Reloj.Ahora + TimeSpan.FromMinutes(esperadas[n]), e.Estados.Estado.ProximoIntento);
            e.Reloj.Ahora = e.Estados.Estado.ProximoIntento!.Value;
        }

        Assert.Empty(e.Alertas.Tecnicas);
        Assert.Equal(Ahora, e.Estados.Estado.PrimerFalloSinExito);
        Assert.Null(e.Estados.Estado.UltimaExitosa);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task ElIntentoAntesDeLaEsperaNoHaceNada()
    {
        var e = new Entorno();
        e.Anonimizador.Falla = new InvalidOperationException("falla");
        await e.Depurar.Ejecutar();
        var llamadas = e.Anonimizador.Llamadas;
        e.Reloj.Ahora = Ahora.AddSeconds(30);

        var espera = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.EnEspera, espera.Estado);
        Assert.Equal(llamadas, e.Anonimizador.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task SiPasan24HorasDesdeElPrimerFalloSinUnaEjecucionExitosa_SePublicaLaAlertaTecnica()
    {
        var e = new Entorno();
        e.Estados.Estado = new EstadoDeLaDepuracion(Ahora.AddDays(-1), Ahora.AddHours(-24), 30, Ahora.AddMinutes(-1));
        e.Anonimizador.Falla = new InvalidOperationException("falla");

        var resultado = await e.Depurar.Ejecutar();

        Assert.True(resultado.AlertaPublicada);
        Assert.Equal([MotivosDeAlerta.DepuracionDiariaSinExito], e.Alertas.Tecnicas);

        var antes = new Entorno();
        antes.Estados.Estado = new EstadoDeLaDepuracion(Ahora.AddDays(-1), Ahora.AddHours(-23).AddMinutes(-59), 30, Ahora.AddMinutes(-1));
        antes.Anonimizador.Falla = new InvalidOperationException("falla");
        Assert.False((await antes.Depurar.Ejecutar()).AlertaPublicada);
        Assert.Empty(antes.Alertas.Tecnicas);
    }

    [Fact]
    public async Task UnaEjecucionExitosaReiniciaElEstadoDeFallos()
    {
        var e = new Entorno();
        e.Estados.Estado = new EstadoDeLaDepuracion(Ahora.AddDays(-1), Ahora.AddHours(-3), 6, Ahora.AddMinutes(-1));

        var resultado = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Ejecutada, resultado.Estado);
        Assert.Equal(new EstadoDeLaDepuracion(Ahora, null, 0, null), e.Estados.Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-36")]
    public async Task SiNoSePuedeRegistrarLaEjecucion_LaCorridaFalla_YLaAnonimizacionSeHizoDentroDeLaMismaUnidadDeTrabajo()
    {
        var e = new Entorno();
        e.Auditoria.Caida = true;
        e.Anonimizador.Respuestas.Enqueue(7);

        var resultado = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Fallida, resultado.Estado);
        Assert.Equal(1, e.Unidad.Ejecuciones);
        Assert.Equal(1, e.Estados.Estado.IntentosFallidos);
        Assert.Null(e.Estados.Estado.UltimaExitosa);
    }

    [Fact]
    public async Task UnaFuenteOUnaDepuracionAdicionalQueFallan_FallanLaCorrida()
    {
        var porFuente = new Entorno();
        porFuente.Cuentas.Falla = new InvalidOperationException("identidad no responde");
        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Fallida, (await porFuente.Depurar.Ejecutar()).Estado);

        var porIdentidad = new Entorno();
        porIdentidad.Identidad.Falla = new InvalidOperationException("sin función");
        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Fallida, (await porIdentidad.Depurar.Ejecutar()).Estado);
        Assert.Empty(porIdentidad.Auditoria.Solicitudes);
    }

    [Fact]
    public async Task SiNoSePuedeAnotarElFalloOPublicarLaAlerta_NoSeInterrumpeNada()
    {
        var e = new Entorno();
        e.Anonimizador.Falla = new InvalidOperationException("falla");
        var depurar = new DepurarAuditoria([e.Cuentas], e.PorAnonimizar, e.Anonimizador, [], e.Auditoria, new EstadoQueFalla(e.Estados), e.Bloqueo, new AlertasQueFallan(), ActorDePrueba.DelSistema(), e.Unidad, e.Reloj);
        e.Estados.Estado = new EstadoDeLaDepuracion(null, Ahora.AddDays(-2), 50, null);

        var resultado = await depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Fallida, resultado.Estado);
        Assert.True(resultado.AlertaPublicada);
    }

    private sealed class EstadoQueFalla(EstadoDeDepuracionEnMemoria interno) : IEstadoDeLaDepuracion
    {
        public Task<EstadoDeLaDepuracion> Obtener(CancellationToken cancelacion = default) => interno.Obtener(cancelacion);

        public Task Guardar(EstadoDeLaDepuracion estado, CancellationToken cancelacion = default) => Task.FromException(new InvalidOperationException("sin base"));
    }

    private sealed class AlertasQueFallan : IAlertas
    {
        public Task PublicarTecnica(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => Task.FromException(new InvalidOperationException("sin base"));

        public Task PublicarDeSeguridad(CodigoDeAlerta motivo, CancellationToken cancelacion = default) => Task.FromException(new InvalidOperationException("sin base"));
    }

    [Fact]
    public void LasEsperasDeLosFallosDuplicanHastaUnaHora()
    {
        Assert.Equal(TimeSpan.FromMinutes(1), DepurarAuditoria.EsperaTrasFallar(1));
        Assert.Equal(TimeSpan.FromMinutes(60), DepurarAuditoria.EsperaTrasFallar(7));
        Assert.Equal(TimeSpan.FromMinutes(60), DepurarAuditoria.EsperaTrasFallar(1000));
        Assert.Throws<ArgumentOutOfRangeException>(() => DepurarAuditoria.EsperaTrasFallar(0));
    }

    // ---------- Paso de restauración ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task ElPasoDeRestauracionCorreDespuesDeIdentidadYDeLaWebYAntesDeAbrirNoEsperaYFallaSiLaDepuracionFalla()
    {
        var e = new Entorno();
        e.Estados.Estado = new EstadoDeLaDepuracion(null, Ahora.AddHours(-2), 3, Ahora.AddHours(5));
        var paso = new PasoDeRestauracionDeDepuracion(e.Depurar);

        Assert.Equal(OrdenDePasoDeRestauracion.Auditoria, paso.Orden);
        Assert.True(paso.Orden > OrdenDePasoDeRestauracion.Web);
        Assert.Equal("auditoria-depurar", paso.Nombre);
        Assert.Equal(ResultadoDePaso.Correcto, await paso.Ejecutar());
        Assert.Equal(1, e.Anonimizador.Llamadas);

        e.Anonimizador.Falla = new InvalidOperationException("falla");
        Assert.Equal(ResultadoDePaso.Fallido, await paso.Ejecutar());

        var ejecutor = new EjecutorDePasosDeRestauracion([paso]);
        var informe = await ejecutor.Ejecutar();
        Assert.False(informe.PermiteAbrir);
        Assert.Equal("auditoria-depurar", informe.PasoFallido);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task ElPasoVuelveALlenarPorAnonimizarDeLasCuentasCanceladasYNoLasReaplicaDeNuevo()
    {
        var e = new Entorno();
        var cuenta = Guid.NewGuid();
        e.Cuentas.Disponibles.Add(cuenta);
        var paso = new PasoDeRestauracionDeDepuracion(e.Depurar);

        await paso.Ejecutar();

        Assert.Contains((TipoDePorAnonimizar.CuentaCancelada, cuenta), e.PorAnonimizar.Filas);
        Assert.Equal(1, e.Anonimizador.Llamadas);
        Assert.Equal(1, e.Identidad.Llamadas);
    }

    private sealed class CuentasFalsas(params Guid[] cuentas) : ICuentasCanceladasPendientes
    {
        public Task<IReadOnlyList<Guid>> Listar(int maximo, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<Guid>>([.. cuentas.Take(maximo)]);
    }

    private sealed class DepuradorDeIdentidadFalso : IDepuradorDeRegistrosAislados
    {
        public Task<int> Depurar(CancellationToken cancelacion = default) => Task.FromResult(7);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task LosAdaptadoresDeIdentidadDelegan_LasCuentasCanceladasYLaDepuracionDeIdentidadRegistros()
    {
        var cuenta = Guid.NewGuid();
        var fuente = new CuentasCanceladasComoFuente(new CuentasFalsas(cuenta, Guid.NewGuid()));

        Assert.Equal(TipoDePorAnonimizar.CuentaCancelada, fuente.Tipo);
        Assert.Equal(cuenta, Assert.Single(await fuente.Pendientes(1)));
        await fuente.MarcarEntregados([cuenta]);
        Assert.Equal(7, await new DepuradorDeRegistrosDeIdentidad(new DepuradorDeIdentidadFalso()).Depurar());
    }
}
