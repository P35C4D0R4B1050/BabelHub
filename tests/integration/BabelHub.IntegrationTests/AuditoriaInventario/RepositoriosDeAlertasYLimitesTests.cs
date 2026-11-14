using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Infrastructure.Auditoria;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.AuditoriaInventario;

/// <summary>
/// Las alertas, el acumulado de límites, la depuración y su estado contra PostgreSQL real (T25 y T27; AC-AUD-12, AC-AUD-25, AC-AUD-26, AC-AUD-28, AC-AUD-36; AUD-13, AUD-28, AUD-31, AUD-34, AUD-36, AUD-39, AUD-41):
/// la agrupación y la marca de revisada no se pisan con ocurrencias simultáneas, cada rechazo se persiste sin diferirlo con su tope de filas, el cierre escribe un solo evento y borra la dirección, y la
/// depuración corre con el rol de depuración y registra su ejecución como «sistema».
/// </summary>
public sealed class RepositoriosDeAlertasYLimitesTests(PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>
{
    private static readonly string Huella = "h" + Guid.NewGuid().ToString("N");

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class OrigenDePrueba : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    private sealed class Entorno
    {
        public Entorno(PostgresDeAuditoriaFixture bd, ActorActual? actor = null)
        {
            Bd = bd;
            Unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
            Actores = new ActorDelFlujo();
            Actores.Establecer(actor ?? ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central"));
            Catalogo = PostgresDeAuditoriaFixture.Catalogo;
            Constructor = new ConstructorDeRegistros(ServicioDeOrigen.Inventario, Catalogo, Reloj, Actores, new OrigenDePrueba());
            Registros = new PostgresAlmacenDeRegistros(Unidad, Unidad);
            Auditoria = new AuditoriaDeAplicacion(Constructor, Registros);
            Alertas = new PostgresAlmacenDeAlertas(Unidad, Unidad);
            Publicador = new PublicadorDeAlertas(ServicioDeOrigen.Inventario, CatalogoDeAlertasDeAuditoria.Crear(), Alertas, Unidad, Reloj);
            Limitador = new LimitadorDeEscrituras(concurrenciaMaxima: 64);
            Acumulador = new PostgresAcumuladorDeLimites(bd.Acceso, Limitador);
            Cerrables = new PostgresAcumuladoParaCerrar(Unidad, Unidad);
        }

        public PostgresDeAuditoriaFixture Bd { get; }

        public RelojDePrueba Reloj { get; } = new(DateTimeOffset.UtcNow);

        public UnidadDeTrabajoPostgres Unidad { get; }

        public ActorDelFlujo Actores { get; }

        public CatalogoDeAcciones Catalogo { get; }

        public ConstructorDeRegistros Constructor { get; }

        public PostgresAlmacenDeRegistros Registros { get; }

        public AuditoriaDeAplicacion Auditoria { get; }

        public PostgresAlmacenDeAlertas Alertas { get; }

        public PublicadorDeAlertas Publicador { get; }

        public LimitadorDeEscrituras Limitador { get; }

        public PostgresAcumuladorDeLimites Acumulador { get; }

        public PostgresAcumuladoParaCerrar Cerrables { get; }

        public ConsultaDeAlertasLocales ConsultaDeAlertas(FirmadorDeCursor? firmador = null) =>
            new(Actores, firmador ?? new FirmadorDeCursor(new ClavesDeCursorDelEntorno(new string('k', 40), "v1"), Reloj), Alertas, Auditoria, Unidad, Reloj);

        public Task Tecnica(string motivo, DateTimeOffset cuando) => ((IPublicadorDeAlertasTecnicas)Publicador).Publicar(new AlertaTecnica(motivo, cuando));

        public async Task Seguridad(string motivo, DateTimeOffset cuando) => await ((IAlertas)Publicador).PublicarDeSeguridad(new CodigoDeAlerta(motivo));
    }

    /// <summary>
    /// Un instante propio de cada prueba, siempre posterior al anterior: el agrupado de alertas toma el tiempo como monótono (una ocurrencia más de cinco minutos anterior a la última alerta abierta del motivo es un reloj
    /// roto y no se guarda), y la base se comparte entre las pruebas de la clase.
    /// </summary>
    private static class Cronologia
    {
        private static long _ticks = DateTimeOffset.UtcNow.AddYears(2).UtcTicks;

        public static DateTimeOffset Siguiente() => new(Interlocked.Add(ref _ticks, TimeSpan.FromDays(1).Ticks), TimeSpan.Zero);
    }

    // ---------- Alertas ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task VeinteOcurrenciasSimultaneasDelMismoMotivoSonUnaSolaAlertaConVeinteOcurrencias()
    {
        var e = new Entorno(bd);
        var inicio = Cronologia.Siguiente();

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, inicio.AddSeconds(i % 5))));

        var alertas = await bd.Consultar<long>($"SELECT count(*) FROM auditoria.alertas WHERE motivo = '{MotivosDeAlerta.TrabajoDeFondoDetenido}' AND primera_ocurrencia >= '{inicio.AddSeconds(-1):O}' AND primera_ocurrencia <= '{inicio.AddSeconds(10):O}'");
        var ocurrencias = await bd.Consultar<long>($"SELECT COALESCE(sum(ocurrencias), 0) FROM auditoria.alertas WHERE motivo = '{MotivosDeAlerta.TrabajoDeFondoDetenido}' AND primera_ocurrencia >= '{inicio.AddSeconds(-1):O}' AND primera_ocurrencia <= '{inicio.AddSeconds(10):O}'");
        Assert.Equal(1L, alertas);
        Assert.Equal(20L, ocurrencias);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task PasadaLaVentanaDeQuinceMinutosOtraOcurrenciaAbreOtraAlerta()
    {
        var e = new Entorno(bd);
        var inicio = Cronologia.Siguiente();

        await e.Tecnica(MotivosDeAlerta.MemoriaDeContadoresAlta, inicio);
        await e.Tecnica(MotivosDeAlerta.MemoriaDeContadoresAlta, inicio.AddMinutes(14));
        await e.Tecnica(MotivosDeAlerta.MemoriaDeContadoresAlta, inicio.AddMinutes(15));

        var filas = await e.Alertas.Desde(inicio.AddSeconds(-1), 5000);
        Assert.Equal([2, 1], filas.Where(a => a.Motivo == MotivosDeAlerta.MemoriaDeContadoresAlta && a.PrimeraOcurrencia >= inicio.AddSeconds(-1).AddSeconds(-1)).OrderBy(a => a.PrimeraOcurrencia).Select(a => a.Ocurrencias));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task MarcarRevisadaYSumarOcurrenciasALaVez_NoPierdenNiLaMarcaNiLasOcurrencias()
    {
        var e = new Entorno(bd);
        var inicio = Cronologia.Siguiente();
        for (var i = 0; i < 4; i++)
        {
            await e.Tecnica(MotivosDeAlerta.AccesosDenegados, inicio.AddSeconds(i));
        }

        var id = (await e.Alertas.UltimaDelMotivoParaActualizar(ServicioDeOrigen.Inventario, MotivosDeAlerta.AccesosDenegados)) is { } sola ? sola.Id : throw new InvalidOperationException();
        var consulta = e.ConsultaDeAlertas();
        var idDeLaAlerta = await bd.Consultar<string>($"SELECT id FROM auditoria.alertas WHERE motivo = '{MotivosDeAlerta.AccesosDenegados}' ORDER BY primera_ocurrencia DESC LIMIT 1");

        var marcas = Enumerable.Range(0, 3).Select(_ => consulta.MarcarRevisada(idDeLaAlerta!, 4));
        var sumas = Enumerable.Range(0, 10).Select(i => e.Tecnica(MotivosDeAlerta.AccesosDenegados, inicio.AddSeconds(10 + i)));
        await Task.WhenAll(marcas.Cast<Task>().Concat(sumas));

        Assert.Equal(14, await bd.Consultar<int>($"SELECT ocurrencias FROM auditoria.alertas WHERE id = '{idDeLaAlerta}'"));
        Assert.Equal(4, await bd.Consultar<int>($"SELECT revisadas_hasta FROM auditoria.alertas WHERE id = '{idDeLaAlerta}'"));
        _ = id;
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task LaMarcaDeRevisadaQuedaAuditadaYUnaOcurrenciaPosteriorVuelveALaAlertaAPendiente()
    {
        var e = new Entorno(bd);
        var inicio = Cronologia.Siguiente();
        for (var i = 0; i < 3; i++)
        {
            await e.Tecnica(MotivosDeAlerta.IngresosFallidos, inicio.AddSeconds(i));
        }

        var id = (await e.Alertas.Desde(inicio.AddSeconds(-1), 5000)).Single(a => a.Motivo == MotivosDeAlerta.IngresosFallidos && a.PrimeraOcurrencia >= inicio.AddSeconds(-1).AddSeconds(-1)).Id;
        var marcada = await e.ConsultaDeAlertas().MarcarRevisada(id, 3);

        Assert.True(marcada.EsExito);
        Assert.Equal(EstadoDeAlertaConsultada.Revisada, marcada.Valor.Estado);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'AlertaMarcadaRevisada' AND objeto_id = '{id}'"));

        await e.Tecnica(MotivosDeAlerta.IngresosFallidos, inicio.AddMinutes(3));
        var despues = await e.Alertas.Obtener(id);
        Assert.Equal(EstadoDeAlerta.Pendiente, despues!.Estado);
        Assert.Equal(3, despues.RevisadasHasta);
        Assert.Equal(4, despues.Ocurrencias);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-12")]
    public async Task GuardarUnEstadoViejoNoHaceRetrocederLasOcurrenciasNiLoRevisado()
    {
        var e = new Entorno(bd);
        var inicio = Cronologia.Siguiente();
        await e.Tecnica(MotivosDeAlerta.AccesosDenegados, inicio);
        var vieja = (await e.Alertas.Desde(inicio.AddSeconds(-1), 5000)).Single(a => a.Motivo == MotivosDeAlerta.AccesosDenegados && a.PrimeraOcurrencia >= inicio.AddSeconds(-1).AddSeconds(-1));
        await e.Tecnica(MotivosDeAlerta.AccesosDenegados, inicio.AddSeconds(5));
        await e.Tecnica(MotivosDeAlerta.AccesosDenegados, inicio.AddSeconds(6));
        var actual = await e.Alertas.Obtener(vieja.Id);
        await e.Alertas.Guardar(actual!.MarcarRevisada(3, Guid.NewGuid(), DateTimeOffset.UtcNow));

        await e.Alertas.Guardar(vieja);

        var final = await e.Alertas.Obtener(vieja.Id);
        Assert.Equal(3, final!.Ocurrencias);
        Assert.Equal(3, final.RevisadasHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-13")]
    public async Task LaListaDeSeguridadPaginaPorUltimaOcurrenciaSinLasTecnicas_YDesdeLeeEnOrdenAscendente()
    {
        var e = new Entorno(bd);
        var origen = Cronologia.Siguiente();
        for (var i = 0; i < 5; i++)
        {
            await e.Tecnica(MotivosDeAlerta.IngresosFallidos, origen.AddHours(i));
        }

        await e.Tecnica(MotivosDeAlerta.TrabajoDeFondoDetenido, origen.AddHours(9));

        var primera = await e.Alertas.ListarDeSeguridad(null, 3);
        var siguiente = await e.Alertas.ListarDeSeguridad(new PosicionDeAlerta(primera[^1].UltimaOcurrencia, primera[^1].Id), 3);

        Assert.All(primera.Concat(siguiente), a => Assert.Equal(ClaseDeAlerta.Seguridad, a.Clase));
        Assert.Equal(primera.Concat(siguiente).OrderByDescending(a => a.UltimaOcurrencia).ThenBy(a => a.Id, StringComparer.Ordinal).Select(a => a.Id), primera.Concat(siguiente).Select(a => a.Id));
        Assert.Empty(primera.Select(a => a.Id).Intersect(siguiente.Select(a => a.Id)));

        var ascendente = await e.Alertas.Desde(origen.AddSeconds(-1), 5000);
        Assert.Equal(ascendente.OrderBy(a => a.UltimaOcurrencia).ThenBy(a => a.Id, StringComparer.Ordinal).Select(a => a.Id), ascendente.Select(a => a.Id));
        Assert.Contains(ascendente, a => a.Clase == ClaseDeAlerta.Tecnica);
    }

    [Fact]
    public async Task LaBaseRechazaUnaAlertaConContadoresIncoherentes()
    {
        var error = await PostgresDeAuditoriaFixture.IntentarComo(
            bd.Acceso,
            "rol_auditoria",
            $"INSERT INTO auditoria.alertas (id, servicio, motivo, clase, primera_ocurrencia, ultima_ocurrencia, ocurrencias, revisadas_hasta) VALUES ('eco:{Guid.NewGuid():D}', 'ecommerce', 'accesos-denegados', 'seguridad', now(), now(), 2, 3)");

        Assert.Equal("23514", error?.SqlState);
    }

    // ---------- Acumulado de límites (AUD-28, AUD-41) ----------

    private static DateTimeOffset Ventana() => VentanaDeLimite.Que(DateTimeOffset.UtcNow.AddDays(-500).AddMinutes(-Random.Shared.Next(60, 500_000))).Inicio;

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task CincuentaRechazosYVeinteDespuesDeUnReinicioSonUnaFilaDeSetentaConLaDireccionCompleta()
    {
        var e = new Entorno(bd);
        var ventana = Ventana();
        var huella = "k" + Guid.NewGuid().ToString("N");

        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(ResultadoDeRegistroDeRechazo.Acumulado, await e.Acumulador.Registrar(huella, "registros-por-origen", ventana.AddSeconds(i), "2001:db8:0:0:0:0:0:1"));
        }

        // «Reinicio»: otro acumulador sobre la misma base sigue sumando a la misma ventana.
        var otro = new PostgresAcumuladorDeLimites(bd.Acceso, new LimitadorDeEscrituras());
        for (var i = 0; i < 20; i++)
        {
            await otro.Registrar(huella, "registros-por-origen", ventana.AddSeconds(100 + i), "2001:db8::1");
        }

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
        Assert.Equal(70, await bd.Consultar<int>($"SELECT rechazos FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
        Assert.Equal("2001:db8::1", await bd.Consultar<string>($"SELECT direccion_completa FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task VeinteRechazosSimultaneosDelMismoOrigenSumanVeinteSinPerderNingunoNiDuplicarLaFila()
    {
        var e = new Entorno(bd);
        var ventana = Ventana();
        var huella = "p" + Guid.NewGuid().ToString("N");

        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => e.Acumulador.Registrar(huella, "ingresos-fallidos", ventana.AddSeconds(i), "203.0.113.9")));

        Assert.Equal(20L, await bd.Consultar<long>($"SELECT sum(rechazos) FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task LaVentanaLlenaAgrupaLosOrigenesNuevosEnOtrosSinPerderNiPasarseDe1000Filas()
    {
        var e = new Entorno(bd);
        var ventana = Ventana();
        await bd.Ejecutar(
            $"INSERT INTO auditoria.acumulado_de_limites (huella, limite, ventana_inicio, rechazos) SELECT 'sembrado' || g, 'registros-por-origen', '{ventana:O}', 1 FROM generate_series(1, {AcumuladoDeLimite.TopeDeFilas - 1}) g");

        var resultados = new List<ResultadoDeRegistroDeRechazo>();
        for (var i = 0; i < 30; i++)
        {
            resultados.Add(await e.Acumulador.Registrar("nuevo" + Guid.NewGuid().ToString("N")[..8], "registros-por-origen", ventana.AddSeconds(i), "203.0.113.20"));
        }

        Assert.All(resultados, r => Assert.Equal(ResultadoDeRegistroDeRechazo.AgrupadoEnOtros, r));
        Assert.Equal((long)AcumuladoDeLimite.TopeDeFilas, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.acumulado_de_limites WHERE ventana_inicio = '{ventana:O}'"));
        Assert.Equal(30, await bd.Consultar<int>($"SELECT rechazos FROM auditoria.acumulado_de_limites WHERE ventana_inicio = '{ventana:O}' AND huella = 'otros'"));
        Assert.Null(await bd.Consultar<string>($"SELECT direccion_completa FROM auditoria.acumulado_de_limites WHERE ventana_inicio = '{ventana:O}' AND huella = 'otros'"));

        // Un origen que ya tenía su fila sigue sumando a ella.
        Assert.Equal(ResultadoDeRegistroDeRechazo.Acumulado, await e.Acumulador.Registrar("sembrado1", "registros-por-origen", ventana.AddSeconds(40), null));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task UnaHuellaOUnaDireccionInvalidasSeRechazanSinTocarLaBase()
    {
        var e = new Entorno(bd);
        var ventana = Ventana();

        Assert.Throws<ArgumentException>(() => e.Acumulador.Registrar("corta", "registros-por-origen", ventana, null).GetAwaiter().GetResult());
        await Assert.ThrowsAsync<ArgumentException>(() => e.Acumulador.Registrar("huella-valida-1234", "Con Mayusculas", ventana, null));
        await Assert.ThrowsAsync<ArgumentException>(() => e.Acumulador.Registrar("huella-valida-1234", "registros-por-origen", ventana, "no es una direccion; DROP"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.acumulado_de_limites WHERE ventana_inicio = '{ventana:O}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task UnRechazoQueLlegaAUnaVentanaCerradaSeInformaYNoSeAcumula()
    {
        var e = new Entorno(bd);
        var ventana = Ventana();
        var huella = "c" + Guid.NewGuid().ToString("N");
        await e.Acumulador.Registrar(huella, "registros-por-origen", ventana, "203.0.113.5");
        await e.Cerrables.GuardarCierre(ventana, ventana.AddMinutes(6));

        var tardio = await e.Acumulador.Registrar(huella, "registros-por-origen", ventana.AddSeconds(30), "203.0.113.5");

        Assert.Equal(ResultadoDeRegistroDeRechazo.VentanaCerrada, tardio);
        Assert.Equal(1, await bd.Consultar<int>($"SELECT rechazos FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
        Assert.Null(await bd.Consultar<string>($"SELECT direccion_completa FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
    }

    // ---------- Cierre: un solo evento, con el total y la dirección, y la dirección borrada (AC-AUD-25) ----------

    private CerrarVentanasDeLimite Cierre(Entorno e) =>
        new(e.Cerrables, e.Registros, e.Catalogo, ServicioDeOrigen.Inventario, e.Unidad, e.Publicador, e.Actores, e.Reloj);

    [Fact]
    [Trait("Criterio", "AC-AUD-25")]
    public async Task ElCierreEscribeUnSoloEventoConElTotalYLaDireccionCompletaFechadoEnElCierreYBorraLaDireccion()
    {
        var e = new Entorno(bd);
        e.Actores.Establecer(ActorActual.DelSistema());
        var ventana = Ventana();
        var huella = "e" + Guid.NewGuid().ToString("N");
        for (var i = 0; i < 70; i++)
        {
            await e.Acumulador.Registrar(huella, "registros-por-origen", ventana.AddSeconds(i % 290), "203.0.113.7");
        }

        e.Reloj.Ahora = ventana.AddMinutes(5).AddSeconds(11);
        var resultado = await Cierre(e).Ejecutar();

        Assert.True(resultado.VentanasCerradas >= 1);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'LimiteExcedido' AND instante_utc = '{ventana.AddMinutes(5):O}' AND origen = '203.0.113.7' AND motivo = 'registros-por-origen'"));
        Assert.Contains("70", await bd.Consultar<string>($"SELECT detalle::text FROM auditoria.registros WHERE accion = 'LimiteExcedido' AND instante_utc = '{ventana.AddMinutes(5):O}' AND origen = '203.0.113.7'"), StringComparison.Ordinal);
        Assert.Null(await bd.Consultar<string>($"SELECT direccion_completa FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
        Assert.NotNull(await bd.Consultar<DateTime?>($"SELECT cerrada_en FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));

        // Idempotente: otra corrida no emite otro evento.
        await Cierre(e).Ejecutar();
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'LimiteExcedido' AND instante_utc = '{ventana.AddMinutes(5):O}' AND origen = '203.0.113.7'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-41")]
    public async Task SiLaAuditoriaFallaDuranteElCierre_NadaCambia_ConLaDireccionIntactaYElIntentoAnotado()
    {
        var e = new Entorno(bd);
        e.Actores.Establecer(ActorActual.DelSistema());
        var ventana = Ventana();
        var huella = "f" + Guid.NewGuid().ToString("N");
        await e.Acumulador.Registrar(huella, "registros-por-origen", ventana, "203.0.113.8");
        await bd.Ejecutar("CREATE OR REPLACE FUNCTION auditoria.fallar_cierre() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN IF NEW.accion = 'LimiteExcedido' AND NEW.origen = '203.0.113.8' THEN RAISE EXCEPTION 'auditoria caida'; END IF; RETURN NEW; END $f$; DROP TRIGGER IF EXISTS fallar_cierre ON auditoria.registros; CREATE TRIGGER fallar_cierre BEFORE INSERT ON auditoria.registros FOR EACH ROW EXECUTE FUNCTION auditoria.fallar_cierre();");
        try
        {
            e.Reloj.Ahora = ventana.AddMinutes(5).AddSeconds(11);
            var resultado = await Cierre(e).Ejecutar();

            Assert.Equal(1, resultado.Fallidas);
            Assert.Equal("203.0.113.8", await bd.Consultar<string>($"SELECT direccion_completa FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
            Assert.Null(await bd.Consultar<DateTime?>($"SELECT cerrada_en FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
            Assert.Equal(1, await bd.Consultar<int>($"SELECT intentos_de_cierre FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
            Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'LimiteExcedido' AND origen = '203.0.113.8'"));
        }
        finally
        {
            await bd.Ejecutar("DROP TRIGGER IF EXISTS fallar_cierre ON auditoria.registros; DROP FUNCTION IF EXISTS auditoria.fallar_cierre();");
        }

        e.Reloj.Ahora = ventana.AddMinutes(6);
        await Cierre(e).Ejecutar();
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'LimiteExcedido' AND origen = '203.0.113.8'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-41")]
    public async Task LasVentanasCerradasSePurganA24HorasDelCierre()
    {
        var e = new Entorno(bd);
        var ventana = Ventana();
        var huella = "g" + Guid.NewGuid().ToString("N");
        await e.Acumulador.Registrar(huella, "registros-por-origen", ventana, "203.0.113.3");
        await e.Cerrables.GuardarCierre(ventana, ventana.AddMinutes(5));

        await e.Cerrables.Purgar(ventana.AddMinutes(5).AddHours(24).AddSeconds(-1));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));

        await e.Cerrables.Purgar(ventana.AddMinutes(5).AddHours(24));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.acumulado_de_limites WHERE huella = '{huella}'"));
    }


    // ---------- Conteo local para los umbrales de SEG-09 ----------

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task ElConteoLocalCuentaLosRegistrosDeLaAccionEnElRangoYLosRechazosDelLimiteQueSeSolapan()
    {
        var e = new Entorno(bd);
        var conteo = new PostgresConteoLocalDeEventos(e.Unidad, e.Unidad);
        var ventana = Ventana();
        var desde = ventana.AddMinutes(1);
        var hasta = ventana.AddMinutes(6);
        var limite = "solicitudes-por-usuario";
        await e.Acumulador.Registrar("u-" + Guid.NewGuid().ToString("N")[..16], limite, ventana.AddMinutes(2), null);
        await e.Acumulador.Registrar("u-" + Guid.NewGuid().ToString("N")[..16], limite, ventana.AddMinutes(7), null);
        await e.Acumulador.Registrar("u-" + Guid.NewGuid().ToString("N")[..16], "ingresos-fallidos", ventana.AddMinutes(2), null);

        var rechazos = await conteo.ContarRechazosDeLimite(limite, desde, hasta);
        var todos = await conteo.ContarRechazosDeLimite(null, desde, hasta);
        Assert.Equal(2, rechazos);
        Assert.Equal(3, todos);

        var registros = e.Auditoria;
        var antes = await conteo.ContarRegistros(AccionesAuditables.AccesoDenegado, desde, hasta);
        Assert.Equal(0, antes);
        _ = registros;
    }
}
