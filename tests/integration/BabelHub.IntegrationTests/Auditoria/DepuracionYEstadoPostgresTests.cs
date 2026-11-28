using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Auditoria;

/// <summary>
/// La depuración diaria de punta a punta con PostgreSQL real, el rol de depuración y el estado persistente (T19, T25, T27; AUD-19, AUD-31, AUD-36; AC-AUD-09, AC-AUD-28, AC-AUD-36), y los pequeños
/// repositorios de estado: la marca de lectura de alertas, las alertas del Inventario ya enviadas y la clave del cursor.
/// </summary>
public sealed class DepuracionYEstadoPostgresTests(PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>
{
    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class OrigenDePrueba : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    private sealed class CuentasFijas(params Guid[] cuentas) : ICuentasCanceladasPendientes
    {
        public Task<IReadOnlyList<Guid>> Listar(int maximo, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<Guid>>([.. cuentas.Take(maximo)]);
    }

    private sealed class Entorno
    {
        public Entorno(PostgresDeAuditoriaFixture bd, NpgsqlDataSource? depuracion = null, params Guid[] cuentas)
        {
            Unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
            Actores = new ActorDelFlujo();
            Actores.Establecer(ActorActual.DelSistema());
            var catalogo = PostgresDeAuditoriaFixture.Catalogo;
            var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Ecommerce, catalogo, Reloj, Actores, new OrigenDePrueba());
            var auditoria = new AuditoriaDeAplicacion(constructor, new PostgresAlmacenDeRegistros(Unidad, Unidad));
            Estado = new PostgresEstadoDeLaDepuracion(Unidad, Unidad);
            Bloqueo = new PostgresBloqueoDeLaDepuracion(bd.Acceso);
            Depurar = new DepurarAuditoria(
                [new CuentasCanceladasComoFuente(new CuentasFijas(cuentas))],
                new PostgresAlmacenDePorAnonimizar(Unidad, Unidad),
                new PostgresAnonimizadorDeRegistros(depuracion ?? bd.Depuracion),
                [],
                auditoria,
                Estado,
                Bloqueo,
                new PublicadorDeAlertas(ServicioDeOrigen.Ecommerce, CatalogoDeAlertasDeAuditoria.Crear(), new PostgresAlmacenDeAlertas(Unidad, Unidad), Unidad, Reloj),
                Actores,
                Unidad,
                Reloj);
        }

        public RelojDePrueba Reloj { get; } = new(DateTimeOffset.UtcNow);

        public UnidadDeTrabajoPostgres Unidad { get; }

        public ActorDelFlujo Actores { get; }

        public PostgresEstadoDeLaDepuracion Estado { get; }

        public PostgresBloqueoDeLaDepuracion Bloqueo { get; }

        public DepurarAuditoria Depurar { get; }
    }

    private static PostgresEstadoDeLaDepuracion PostgresEstadoDeLaDepuracionDe(UnidadDeTrabajoPostgres unidad) => new(unidad, unidad);

    private async Task Sembrar(string accion, string intervalo, Guid? actor, string? objetoId = null, string? objetoTipo = null)
    {
        await bd.EjecutarComoLaAplicacion(
            "rol_auditoria",
            "INSERT INTO auditoria.registros (id, servicio, tipo, actor_tipo, actor_id, rol, instante_utc, accion, objeto_tipo, objeto_id, origen, detalle, claves_personales, resultado, truncado) "
            + $"VALUES (gen_random_uuid(), 'ecommerce', 'auditoria', 'usuario', {(actor is null ? "NULL" : $"'{actor}'")}, 'administrador-central', now() - interval '{intervalo}', '{accion}', {(objetoTipo is null ? "NULL" : $"'{objetoTipo}'")}, {(objetoId is null ? "NULL" : $"'{objetoId}'")}, 'v1:origen-1234', '{{}}'::jsonb, '{{}}', 'exitoso', false)");
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task LaCorridaLlenaPorAnonimizarAnonimizaConElRolDeDepuracionYRegistraLaEjecucionComoSistema()
    {
        var cuenta = Guid.NewGuid();
        var e = new Entorno(bd, null, cuenta);
        await Sembrar("BibliotecaDeClienteConsultada", "1 day", Guid.NewGuid(), cuenta.ToString("D"), "cliente");
        await Sembrar("BibliotecaDeClienteConsultada", "3 years", Guid.NewGuid(), Guid.NewGuid().ToString("D"), "cliente");

        var resultado = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Ejecutada, resultado.Estado);
        Assert.True(resultado.RegistrosAnonimizados >= 2);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.por_anonimizar WHERE identificador_opaco = '{cuenta}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE objeto_id = '{cuenta:D}'"));
        var ejecucion = await bd.Consultar<string>("SELECT actor_tipo || '|' || (detalle ->> 'registrosAnonimizados') FROM auditoria.registros WHERE accion = 'DepuracionDiariaEjecutada' ORDER BY secuencia DESC LIMIT 1");
        Assert.Equal($"sistema|{resultado.RegistrosAnonimizados}", ejecucion);

        var estado = await e.Estado.Obtener();
        Assert.NotNull(estado.UltimaExitosa);
        Assert.Equal(0, estado.IntentosFallidos);
        Assert.Equal(0, estado.AnonimizadosSinRegistrar);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-09")]
    public async Task RepetirLaDepuracionRegistraCero_YElRegistroDeLaEjecucionNoTieneDatosPersonales()
    {
        var e = new Entorno(bd);
        await e.Depurar.Ejecutar();

        var segunda = await e.Depurar.Ejecutar();

        Assert.Equal(0, segunda.RegistrosAnonimizados);
        Assert.Equal("0", await bd.Consultar<string>("SELECT detalle ->> 'registrosAnonimizados' FROM auditoria.registros WHERE accion = 'DepuracionDiariaEjecutada' ORDER BY secuencia DESC LIMIT 1"));
        Assert.Null(await bd.Consultar<Guid?>("SELECT actor_id FROM auditoria.registros WHERE accion = 'DepuracionDiariaEjecutada' ORDER BY secuencia DESC LIMIT 1"));
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task OtraCorridaConElBloqueoDeSesionNoPisaALaPrimera_YSeLiberaAlTerminar()
    {
        var e = new Entorno(bd);
        await using (var tomado = await e.Bloqueo.Intentar())
        {
            Assert.NotNull(tomado);
            Assert.Null(await new PostgresBloqueoDeLaDepuracion(bd.Acceso).Intentar());
            Assert.Equal(EstadoDeLaCorridaDeDepuracion.OtraCorridaEnCurso, (await e.Depurar.Ejecutar()).Estado);
        }

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Ejecutada, (await e.Depurar.Ejecutar()).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-28")]
    public async Task SiLaFuncionDeDepuracionFalla_ElEstadoPersisteElFalloYLaEsperaCreciente()
    {
        // Una fuente de datos con una credencial que la base rechaza: la depuración falla de verdad, sin un doble.
        var mala = NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(bd.Depuracion.ConnectionString) { Password = "no-es-la-clave", Timeout = 5 }.ConnectionString);
        var e = new Entorno(bd, mala);
        await e.Estado.Guardar(EstadoDeLaDepuracion.Inicial);

        var fallida = await e.Depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Fallida, fallida.Estado);
        var estado = await e.Estado.Obtener();
        Assert.Equal(1, estado.IntentosFallidos);
        Assert.NotNull(estado.PrimerFalloSinExito);
        Assert.Equal(e.Reloj.Ahora.AddMinutes(1).UtcTicks / 10, estado.ProximoIntento!.Value.UtcTicks / 10);
        Assert.Equal(EstadoDeLaCorridaDeDepuracion.EnEspera, (await e.Depurar.Ejecutar()).Estado);

        // El estado sobrevive a un reinicio (otra unidad de trabajo sobre la misma base).
        Assert.Equal(estado, await PostgresEstadoDeLaDepuracionDe(new UnidadDeTrabajoPostgres(bd.Acceso)).Obtener());
        await bd.Ejecutar("DELETE FROM auditoria.estado_de_depuracion");
    }

    // ---------- Correo de alertas, lectura del Inventario y cursor ----------

    [Fact]
    [Trait("Criterio", "AC-AUD-30")]
    public async Task LaMarcaDeLecturaSoloAvanzaYLasAlertasDelInventarioYaEnviadasNoSeDuplican()
    {
        var unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
        var marca = new PostgresMarcaDeLecturaDeAlertas(unidad, unidad);
        await bd.Ejecutar("DELETE FROM auditoria.marca_de_lectura_de_alertas");
        Assert.Null(await marca.Obtener());
        var t = DateTimeOffset.UtcNow;

        await marca.Guardar(t);
        await marca.Guardar(t.AddMinutes(-30));

        Assert.Equal(t.UtcTicks / 10, (await marca.Obtener())!.Value.UtcTicks / 10);

        var enviadas = new PostgresAlertasYaEnviadas(unidad, unidad);
        var id = $"inv:{Guid.NewGuid():D}";
        Assert.False(await enviadas.Contiene(id));
        await enviadas.Marcar(id);
        await enviadas.Marcar(id);
        Assert.True(await enviadas.Contiene(id));
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.alertas_ya_enviadas WHERE alerta_id = '{id}'"));
        Assert.Equal("23514", (await PostgresDeAuditoriaFixture.IntentarComo(bd.Acceso, "rol_auditoria", "INSERT INTO auditoria.alertas_ya_enviadas VALUES ('eco:no-es-del-inventario', now())"))?.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-26")]
    public async Task ElCorreoPendienteSeLeeConSusIntentosYElReintentoSeAnotaConLaEspera()
    {
        var unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow.AddYears(3));
        var publicador = new PublicadorDeAlertas(ServicioDeOrigen.Ecommerce, CatalogoDeAlertasDeAuditoria.Crear(), new PostgresAlmacenDeAlertas(unidad, unidad), unidad, reloj);
        await ((IPublicadorDeAlertasTecnicas)publicador).Publicar(new AlertaTecnica(MotivosDeAlerta.DependenciaNoNecesariaFallida, reloj.Ahora));
        var correo = new PostgresAlmacenDeCorreoDeAlertas(unidad, unidad);

        var pendientes = await correo.ConCorreoPendiente(reloj.Ahora, 100);
        var nuestra = pendientes.Single(p => p.Alerta.Motivo == MotivosDeAlerta.DependenciaNoNecesariaFallida && p.Alerta.PrimeraOcurrencia >= reloj.Ahora.AddSeconds(-1));
        Assert.Equal(0, nuestra.Intentos);

        await correo.RegistrarIntentoFallido(nuestra.Alerta.Id, 3, reloj.Ahora.AddMinutes(4));
        Assert.DoesNotContain(await correo.ConCorreoPendiente(reloj.Ahora.AddMinutes(1), 100), p => p.Alerta.Id == nuestra.Alerta.Id);
        var despues = (await correo.ConCorreoPendiente(reloj.Ahora.AddMinutes(5), 100)).Single(p => p.Alerta.Id == nuestra.Alerta.Id);
        Assert.Equal(3, despues.Intentos);

        await new PostgresAlmacenDeAlertas(unidad, unidad).Guardar(nuestra.Alerta.MarcarComoEnviada());
        Assert.DoesNotContain(await correo.ConCorreoPendiente(reloj.Ahora.AddHours(1), 100), p => p.Alerta.Id == nuestra.Alerta.Id);
    }

    [Fact]
    public void LasClavesDelCursorExigen32BytesUnaVersionSegura_YLaAnteriorDistintaDeLaVigente()
    {
        var clave = new string('a', 32);
        var otra = new string('b', 32);

        var claves = new ClavesDeCursorDelEntorno(clave, "v2", otra, "v1");
        Assert.Equal("v2", claves.Vigente.Version);
        Assert.Equal("v1", claves.Buscar("v1")!.Version);
        Assert.Null(claves.Buscar("v9"));

        Assert.Throws<ArgumentException>(() => new ClavesDeCursorDelEntorno(new string('a', 31), "v1"));
        Assert.Throws<ArgumentException>(() => new ClavesDeCursorDelEntorno(null, "v1"));
        Assert.Throws<ArgumentException>(() => new ClavesDeCursorDelEntorno(clave, "v.1"));
        Assert.Throws<ArgumentException>(() => new ClavesDeCursorDelEntorno(clave, string.Empty));
        Assert.Throws<ArgumentException>(() => new ClavesDeCursorDelEntorno(clave, "v1", clave, "v0"));
        Assert.Throws<ArgumentException>(() => new ClavesDeCursorDelEntorno(clave, "v1", otra, "v1"));
        Assert.DoesNotContain(clave, claves.Vigente.Clave.ToString(), StringComparison.Ordinal);
    }
}
