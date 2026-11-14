using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Infrastructure.Auditoria;
using BabelHub.Inventario.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.AuditoriaInventario;

/// <summary>
/// Lo propio del Inventario contra PostgreSQL real (T19, T20, T25; AUD-08, AUD-19, AUD-32; AC-AUD-07, AC-AUD-29): la depuración con las bajas de titular recibidas, la auditoría de la baja con el actor
/// «integración» y el historial de ajustes y traslados de una sucursal como origen o destino.
/// </summary>
public sealed class DepuracionYHistorialInventarioTests(PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>
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

    private sealed class BajasFalsas(params Guid[] bajas) : IBajasRecibidas
    {
        public List<Guid> Entregadas { get; } = [];

        public Task<IReadOnlyList<Guid>> Pendientes(int maximo, CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<Guid>>([.. bajas.Except(Entregadas).Take(maximo)]);

        public Task MarcarEntregadas(IReadOnlyCollection<Guid> identificadores, CancellationToken cancelacion = default)
        {
            Entregadas.AddRange(identificadores);
            return Task.CompletedTask;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-29")]
    public async Task LaBajaDeUnTitularSeAuditaComoIntegracionYLaDepuracionSiguienteAnonimizaSuRastro()
    {
        var titular = Guid.NewGuid();
        var unidad = new UnidadDeTrabajoPostgres(bd.Acceso);
        var reloj = new RelojDePrueba(DateTimeOffset.UtcNow);
        var actores = new ActorDelFlujo();
        actores.Establecer(ActorActual.DeIntegracion(IntegracionesDeAuditoria.BajaDeTitular));
        var constructor = new ConstructorDeRegistros(ServicioDeOrigen.Inventario, PostgresDeAuditoriaFixture.Catalogo, reloj, actores, new OrigenDePrueba());
        var auditoria = new AuditoriaDeAplicacion(constructor, new PostgresAlmacenDeRegistros(unidad, unidad));
        var baja = BajaDeTitularRecibida.Validar([BajaDeTitularRecibida.CampoDelIdentificador], titular.ToString("D")).Valor;

        await unidad.Ejecutar(async ct =>
        {
            await new AuditoriaDeBajasRecibidas(auditoria, actores).Registrar(baja, esNueva: true, ct);
            return 0;
        });
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'BajaDeTitularProcesada' AND objeto_id = '{titular:D}' AND actor_tipo = 'integracion'"));

        // La repetición no escribe una segunda auditoría.
        await unidad.Ejecutar(async ct =>
        {
            await new AuditoriaDeBajasRecibidas(auditoria, actores).Registrar(baja, esNueva: false, ct);
            return 0;
        });
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'BajaDeTitularProcesada' AND objeto_id = '{titular:D}'"));

        // La depuración, con la baja como fuente de por_anonimizar y el actor «sistema», anonimiza el objeto del registro.
        actores.Establecer(ActorActual.DelSistema());
        var bajas = new BajasFalsas(titular);
        var depurar = new DepurarAuditoria(
            [new BajasRecibidasComoFuente(bajas)],
            new PostgresAlmacenDePorAnonimizar(unidad, unidad),
            new PostgresAnonimizadorDeRegistros(bd.Depuracion),
            [],
            auditoria,
            new PostgresEstadoDeLaDepuracion(unidad, unidad),
            new PostgresBloqueoDeLaDepuracion(bd.Acceso),
            new PublicadorDeAlertas(ServicioDeOrigen.Inventario, CatalogoDeAlertasDeAuditoria.Crear(), new PostgresAlmacenDeAlertas(unidad, unidad), unidad, reloj),
            actores,
            unidad,
            reloj);

        var resultado = await depurar.Ejecutar();

        Assert.Equal(EstadoDeLaCorridaDeDepuracion.Ejecutada, resultado.Estado);
        Assert.Equal([titular], bajas.Entregadas);
        Assert.Equal("baja_de_titular", await bd.Consultar<string>($"SELECT tipo FROM auditoria.por_anonimizar WHERE identificador_opaco = '{titular}'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE objeto_id = '{titular:D}'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'BajaDeTitularProcesada' AND objeto_id IS NULL AND anonimizado_en IS NOT NULL AND actor_tipo = 'integracion'"));
    }

    // ---------- Historial de una sucursal (AUD-08) ----------

    private static PostgresHistorialLocal HistorialDe(UnidadDeTrabajoPostgres unidad) => new(unidad, unidad);

    private async Task Sembrar(string accion, string? origen, string? destino, string intervalo)
    {
        await bd.EjecutarComoLaAplicacion(
            "rol_auditoria",
            "INSERT INTO auditoria.registros (id, servicio, tipo, actor_tipo, actor_id, rol, instante_utc, accion, sucursal_origen, sucursal_destino, detalle, claves_personales, resultado, truncado) "
            + $"VALUES (gen_random_uuid(), 'inventario', 'auditoria', 'usuario', '{Guid.NewGuid()}', 'encargado-de-sucursal', now() - interval '{intervalo}', '{accion}', {(origen is null ? "NULL" : $"'{origen}'")}, {(destino is null ? "NULL" : $"'{destino}'")}, '{{}}'::jsonb, '{{}}', 'exitoso', false)");
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task ElHistorialDeUnaSucursalTraeLosAjustesYLosTrasladosEnQueEsOrigenODestino_Y_SoloEsos()
    {
        var ayacucho = Guid.NewGuid().ToString("D");
        var cusco = Guid.NewGuid().ToString("D");
        var marca = Random.Shared.Next(2000, 4000);
        await Sembrar("AjusteRegistrado", ayacucho, null, $"{marca} days");
        await Sembrar("AjusteRegistrado", cusco, null, $"{marca} days");
        await Sembrar("TrasladoSolicitado", cusco, ayacucho, $"{marca} days 1 hour");
        await Sembrar("TrasladoRecibido", ayacucho, cusco, $"{marca} days 2 hours");
        await Sembrar("PrecioDeEdicionCambiado", ayacucho, null, $"{marca} days");
        var historial = HistorialDe(new UnidadDeTrabajoPostgres(bd.Acceso));

        var pagina = await historial.Consultar(Guid.Parse(ayacucho), null);

        Assert.Equal(["AjusteRegistrado", "TrasladoSolicitado", "TrasladoRecibido"], pagina.Registros.Select(r => r.Accion));
        Assert.All(pagina.Registros, r => Assert.True(r.SucursalOrigen == ayacucho || r.SucursalDestino == ayacucho));
        Assert.DoesNotContain(pagina.Registros, r => r.Accion == "PrecioDeEdicionCambiado");
        Assert.NotNull(pagina.CursorDeLaPagina);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-07")]
    public async Task ElHistorialPaginaDeVeinteEnVeinteSinRepetirNiOmitirYNoMuestraLaSucursalAjena()
    {
        var sucursal = Guid.NewGuid().ToString("D");
        var otra = Guid.NewGuid().ToString("D");
        for (var i = 0; i < 25; i++)
        {
            await Sembrar("AjusteRegistrado", sucursal, null, $"{5000 + i} minutes");
        }

        await Sembrar("AjusteRegistrado", otra, null, "5000 minutes");
        var historial = HistorialDe(new UnidadDeTrabajoPostgres(bd.Acceso));

        var primera = await historial.Consultar(Guid.Parse(sucursal), null);
        var segunda = await historial.Consultar(Guid.Parse(sucursal), primera.SiguienteCursor);

        Assert.Equal(20, primera.Registros.Count);
        Assert.Equal(5, segunda.Registros.Count);
        Assert.Null(segunda.SiguienteCursor);
        Assert.Empty(primera.Registros.Select(r => r.Id).Intersect(segunda.Registros.Select(r => r.Id)));
        Assert.All(primera.Registros.Concat(segunda.Registros), r => Assert.Equal(sucursal, r.SucursalOrigen));
    }
}
