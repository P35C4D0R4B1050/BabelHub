using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Auditoria;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.IntegrationTests.Limites;

/// <summary>
/// El límite de ingresos fallidos con el motor real de Redis y <c>Auditoria</c> real sobre PostgreSQL (T27b de <c>transversal-seguridad</c>; AC-SEG-08; SEG-08): 50 rechazos de un origen en una misma ventana de 5 minutos alineada al
/// reloj dejan <b>un solo evento</b> con el contador en 50, escrito al cerrar la ventana (con la dirección completa ya borrada), y un reinicio del proceso a mitad de la ventana ni pierde ni duplica rechazos. El reloj de la aplicación es
/// controlable (el de Redis, no): el cierre ocurre cuando el reloj pasa el fin de la ventana más el margen de 10 segundos.
/// </summary>
[Collection(RedisDeContadoresCollection.Nombre)]
public sealed class LimitesConAuditoriaRealTests(RedisDeContadoresFixture redis, PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>
{
    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private sealed class RelojControlable(DateTimeOffset inicio) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class OrigenFijo(OrigenDeSolicitud origen) : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => origen;
    }

    /// <summary>Un «proceso»: sus propios servicios sobre la misma base y el mismo Redis, que se descarta para simular un reinicio.</summary>
    private sealed class Proceso : IAsyncDisposable
    {
        private readonly ServiceProvider _proveedor;

        public Proceso(RedisDeContadoresFixture redis, PostgresDeAuditoriaFixture bd, RelojControlable reloj, OrigenDeSolicitud origen)
        {
            var actor = new ActorDelFlujo();
            var servicios = new ServiceCollection();
            servicios.AddLogging();
            servicios.AddSingleton(bd.Acceso);
            servicios.AddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>()));
            servicios.AddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
            servicios.AddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
            servicios.AddSingleton<IActorActual>(actor);
            servicios.AddSingleton<IEstablecedorDeActor>(actor);
            servicios.AddSingleton<IReloj>(reloj);
            servicios.AddScoped<IOrigenDeSolicitud>(_ => new OrigenFijo(origen));
            servicios.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas));
            servicios.AgregarModuloAuditoria(new OpcionesDelModuloDeAuditoria(bd.Depuracion, new ClavesDeCursorDelEntorno(new string('k', 40), "v1")));
            servicios.AddScoped<IControlDeLimites>(p => new EvaluarLimite(
                p.GetRequiredService<CatalogoDePoliticas>(), redis.Motor(), null, p.GetRequiredService<IOrigenDeSolicitud>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
            _proveedor = servicios.BuildServiceProvider();
            Actor = actor;
        }

        public ActorDelFlujo Actor { get; }

        /// <summary>Un intento de ingreso fallido: reserva el cupo (que cuenta) y devuelve si se rechazó.</summary>
        public async Task<bool> Intentar(HuellaDeSujeto cuenta)
        {
            using var alcance = _proveedor.CreateScope();
            var resultado = await alcance.ServiceProvider.GetRequiredService<IControlDeLimites>().Reservar(PoliticasDeLimite.IngresosFallidos, huellaDelLlamador: cuenta);
            return !resultado.EsPermitido;
        }

        public async Task Cerrar()
        {
            Actor.Establecer(ActorActual.DelSistema());
            try
            {
                using var alcance = _proveedor.CreateScope();
                await alcance.ServiceProvider.GetRequiredService<CerrarVentanasDeLimite>().Ejecutar();
            }
            finally
            {
                Actor.Limpiar();
            }
        }

        public ValueTask DisposeAsync() => _proveedor.DisposeAsync();
    }

    private static int _ventanas = Random.Shared.Next(1, 50_000) * 10;

    private static DateTimeOffset InicioDeVentana()
    {
        // Cada prueba usa su propia ventana: cerrar una ventana cierra todas sus filas y una ventana cerrada no acumula más.
        var instante = DateTimeOffset.UtcNow.AddYears(1).AddMinutes(Interlocked.Add(ref _ventanas, 10));
        return new DateTimeOffset(instante.Year, instante.Month, instante.Day, instante.Hour, instante.Minute - (instante.Minute % 5), 0, TimeSpan.Zero);
    }

    private static (OrigenDeSolicitud Origen, HuellaDeSujeto Cuenta, CalculadorDeHuellaHmac Huellas) Sujeto()
    {
        var huellas = new CalculadorDeHuellaHmac(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella)));
        var direccion = $"198.51.{Random.Shared.Next(1, 255)}.{Random.Shared.Next(1, 255)}";
        return (new OrigenDeSolicitud(huellas.DeOrigen(direccion), direccion), huellas.DeSujeto($"lector-{Guid.NewGuid():N}@correo.pe", "cuenta-de-ingreso"), huellas);
    }

    private async Task<long> Eventos(OrigenDeSolicitud origen) =>
        await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'LimiteExcedido' AND (origen = '{origen.Direccion}' OR origen = '{origen.Huella.Valor}')");

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task CincuentaRechazosDeUnOrigenEnUnaVentana_DejanUnSoloEventoConElContadorEnCincuenta()
    {
        var (origen, cuenta, _) = Sujeto();
        var inicio = InicioDeVentana();
        var reloj = new RelojControlable(inicio.AddSeconds(20));
        await using var proceso = new Proceso(redis, bd, reloj, origen);

        var rechazados = 0;
        for (var i = 0; i < 55; i++)
        {
            reloj.Ahora = inicio.AddSeconds(20 + (i * 4) % 270);
            rechazados += await proceso.Intentar(cuenta) ? 1 : 0;
        }

        Assert.Equal(50, rechazados);
        var huella = origen.Huella.Valor;
        Assert.Equal(0L, await Eventos(origen));
        Assert.Equal(50L, await bd.Consultar<long>($"SELECT COALESCE(sum(rechazos), 0) FROM auditoria.acumulado_de_limites WHERE huella = '{huella}' AND ventana_inicio = '{inicio:O}'"));

        // Pasado el fin de la ventana más el margen, el cierre escribe un solo evento y borra la dirección completa.
        reloj.Ahora = inicio.AddMinutes(5).AddSeconds(11);
        await proceso.Cerrar();
        await proceso.Cerrar();

        Assert.Equal(1L, await Eventos(origen));
        Assert.Equal("50", await bd.Consultar<string>($"SELECT detalle->>'totalDeRechazos' FROM auditoria.registros WHERE accion = 'LimiteExcedido' AND origen = '{origen.Direccion}'"));
        Assert.Null(await bd.Consultar<string>($"SELECT direccion_completa FROM auditoria.acumulado_de_limites WHERE huella = '{huella}' AND ventana_inicio = '{inicio:O}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-08")]
    public async Task UnReinicioATresCuartosDeLaVentana_NoPierdeNiDuplicaRechazos()
    {
        var (origen, cuenta, _) = Sujeto();
        var inicio = InicioDeVentana();
        var reloj = new RelojControlable(inicio.AddSeconds(30));
        var rechazados = 0;

        await using (var antes = new Proceso(redis, bd, reloj, origen))
        {
            for (var i = 0; i < 5 + 20; i++)
            {
                rechazados += await antes.Intentar(cuenta) ? 1 : 0;
            }
        }

        // El proceso se reinicia: servicios nuevos sobre la misma base y el mismo Redis; el reloj sigue dentro de la misma ventana.
        reloj.Ahora = inicio.AddSeconds(200);
        await using var despues = new Proceso(redis, bd, reloj, origen);
        for (var i = 0; i < 30; i++)
        {
            rechazados += await despues.Intentar(cuenta) ? 1 : 0;
        }

        Assert.Equal(50, rechazados);
        var huella = origen.Huella.Valor;
        Assert.Equal(50L, await bd.Consultar<long>($"SELECT COALESCE(sum(rechazos), 0) FROM auditoria.acumulado_de_limites WHERE huella = '{huella}' AND ventana_inicio = '{inicio:O}'"));

        // Dos procesos que cierran a la vez (uno recién arrancado) escriben un solo evento.
        reloj.Ahora = inicio.AddMinutes(5).AddSeconds(11);
        await using var otro = new Proceso(redis, bd, reloj, origen);
        await Task.WhenAll(despues.Cerrar(), otro.Cerrar());

        Assert.Equal(1L, await Eventos(origen));
        Assert.Equal("50", await bd.Consultar<string>($"SELECT detalle->>'totalDeRechazos' FROM auditoria.registros WHERE accion = 'LimiteExcedido' AND origen = '{origen.Direccion}'"));
    }
}
