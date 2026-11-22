using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Notificaciones.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Notificaciones;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Notificaciones;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Auditoria;
using BabelHub.Shared.Contratos;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.IntegrationTests.Notificaciones;

/// <summary>
/// La desactivación de los avisos de un número con la <b>auditoría real</b> sobre PostgreSQL (T24 de <c>transversal-notificaciones</c>, hallazgo D4; AC-NOT-13, NOT-13): la entrada <c>AvisosDeNumeroDesactivados</c> queda en
/// <c>auditoria.registros</c> en la misma transacción, con solo la huella parcial y la causa (jamás el número, en ninguna forma), la repetición no audita dos veces, la reactivación se audita aparte y, si la auditoría no se puede escribir,
/// la desactivación no se aplica (el número sigue recibiendo avisos y la huella no queda guardada).
/// </summary>
public sealed class DesactivacionDeNumerosConAuditoriaRealTests(PostgresDeAuditoriaFixture bd) : IClassFixture<PostgresDeAuditoriaFixture>, IAsyncLifetime
{
    private const string Numero = "+51 987 654 321";

    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private static readonly byte[] ClaveDelNumero = System.Text.Encoding.UTF8.GetBytes(new string('n', 16) + "0123456789abcdefghij");

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Ahora { get; } = new(2026, 11, 21, 15, 0, 0, TimeSpan.Zero);

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class SinPlantillas : IPlantillasDeRuta
    {
        public IReadOnlyCollection<string> Todas => [];
    }

    private sealed class OrigenFijo : IOrigenDeSolicitud
    {
        public OrigenDeSolicitud Actual => OrigenDeSolicitud.Desconocido;
    }

    /// <summary>Un proceso del E-commerce con Auditoría y Notificaciones reales sobre la misma base; la auditoría puede «caerse» con un disparador que la rechaza.</summary>
    private sealed class Proceso : IAsyncDisposable
    {
        private readonly ServiceProvider _proveedor;

        public Proceso(PostgresDeAuditoriaFixture bd, ActorActual actor)
        {
            var flujo = new ActorDelFlujo();
            flujo.Establecer(actor);
            var servicios = new ServiceCollection();
            servicios.AddLogging();
            servicios.AddSingleton(bd.Acceso);
            servicios.AddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>()));
            servicios.AddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
            servicios.AddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
            servicios.AddSingleton<IActorActual>(flujo);
            servicios.AddSingleton<IEstablecedorDeActor>(flujo);
            servicios.AddSingleton<IReloj>(new RelojFijo());
            servicios.AddSingleton<IPlantillasDeRuta, SinPlantillas>();
            servicios.AddSingleton(p => new ResolutorDeRutas(p.GetRequiredService<IPlantillasDeRuta>()));
            servicios.AddScoped<IOrigenDeSolicitud>(_ => new OrigenFijo());
            servicios.AddSingleton<ICalculadorDeHuella>(new CalculadorDeHuellaHmac(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella))));
            servicios.AddSingleton(new OpcionesDeHuellaDeNumero(1, ClaveDelNumero));
            servicios.AddSingleton<IHuellasDeNumeros>(sp => new CalculadorDeHuellaDeNumero(sp.GetRequiredService<OpcionesDeHuellaDeNumero>(), sp.GetRequiredService<ICalculadorDeHuella>()));
            servicios.AgregarModuloAuditoria(new OpcionesDelModuloDeAuditoria(bd.Depuracion, new ClavesDeCursorDelEntorno(new string('k', 40), "v1")));
            servicios.AddScoped<AccesoAlEsquemaDeNotificaciones>();
            servicios.AddScoped<IRepositorioDeNumerosDesactivados, RepositorioDeNumerosDesactivadosPostgres>();
            servicios.AddScoped<IDesactivacionDeNumeros, DesactivarAvisosDeUnNumero>();
            _proveedor = servicios.BuildServiceProvider();
        }

        public async Task<Ecommerce.Application.Plataforma.Resultado<ConfirmacionDeNotificaciones>> Desactivar(string numero = Numero)
        {
            using var alcance = _proveedor.CreateScope();
            return await alcance.ServiceProvider.GetRequiredService<IDesactivacionDeNumeros>().Desactivar(numero, "a pedido del titular");
        }

        public async Task<Ecommerce.Application.Plataforma.Resultado<ConfirmacionDeNotificaciones>> Reactivar(string numero = Numero)
        {
            using var alcance = _proveedor.CreateScope();
            return await alcance.ServiceProvider.GetRequiredService<IDesactivacionDeNumeros>().Reactivar(numero);
        }

        public ValueTask DisposeAsync() => _proveedor.DisposeAsync();
    }

    public async Task InitializeAsync()
    {
        await bd.Ejecutar(
            "DO $r$ BEGIN IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'rol_notificaciones') THEN CREATE ROLE rol_notificaciones NOLOGIN; END IF; END $r$;"
            + DefinicionDeNotificaciones.Sql
            + DefinicionDeNotificaciones.Permisos
            + "GRANT rol_notificaciones TO acceso;");
        await Limpiar();
    }

    public async Task DisposeAsync()
    {
        await bd.Ejecutar("DROP TRIGGER IF EXISTS auditoria_caida ON auditoria.registros; DROP FUNCTION IF EXISTS auditoria.rechazar_para_la_prueba();");
    }

    private async Task Limpiar() => await bd.Ejecutar("DELETE FROM notificaciones.numeros_desactivados;");

    private static ActorActual Administrador() => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral);

    private Task<long> Entradas(string accion = "AvisosDeNumeroDesactivados") =>
        bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = '{accion}'")!;

    private async Task<long> Desactivados() => await bd.Consultar<long>("SELECT count(*) FROM notificaciones.numeros_desactivados");

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task Desactivar_DejaUnaEntradaDeAuditoriaConLaHuellaParcialYLaCausa_SinElNumero_YRepetirNoAuditaDeNuevo()
    {
        await using var proceso = new Proceso(bd, Administrador());
        var antes = await Entradas();

        Assert.True((await proceso.Desactivar()).EsExito);
        Assert.True((await proceso.Desactivar("987654321")).EsExito);

        Assert.Equal(1, await Desactivados());
        Assert.Equal(antes + 1, await Entradas());
        var fila = await bd.Consultar<string>(
            "SELECT objeto_tipo || '|' || objeto_id || '|' || (detalle->>'causa') FROM auditoria.registros WHERE accion = 'AvisosDeNumeroDesactivados' ORDER BY id DESC LIMIT 1");
        Assert.NotNull(fila);
        var partes = fila.Split('|');
        Assert.Equal("numero", partes[0]);
        Assert.Equal(DesactivarAvisosDeUnNumero.CausaDeLaDesactivacion, partes[2]);

        // La entrada guarda la huella parcial (no el número, escrito de ninguna forma) y la huella completa solo vive en su tabla.
        var completa = await bd.Consultar<string>("SELECT huella_numero FROM notificaciones.numeros_desactivados LIMIT 1");
        Assert.NotNull(completa);
        Assert.NotEqual(completa, partes[1]);
        Assert.StartsWith(partes[1], completa, StringComparison.Ordinal);
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM auditoria.registros WHERE accion = 'AvisosDeNumeroDesactivados' AND (detalle::text ~ '987654321' OR detalle::text ~ '[+]51' OR objeto_id ~ '[0-9]{9}')"));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task Reactivar_SeAuditaAparte_YSoloSiHabiaUnaDesactivacion()
    {
        await using var proceso = new Proceso(bd, Administrador());
        var antes = await Entradas();

        Assert.True((await proceso.Reactivar()).EsExito);
        Assert.Equal(antes, await Entradas());

        Assert.True((await proceso.Desactivar()).EsExito);
        Assert.True((await proceso.Reactivar("987 654 321")).EsExito);

        Assert.Equal(0, await Desactivados());
        Assert.Equal(antes + 2, await Entradas());
        Assert.Equal(1, await bd.Consultar<long>($"SELECT count(*) FROM auditoria.registros WHERE accion = 'AvisosDeNumeroDesactivados' AND detalle->>'causa' = '{DesactivarAvisosDeUnNumero.CausaDeLaReactivacion}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task SiLaAuditoriaNoSePuedeEscribir_LaDesactivacionNoSeAplica_YSePuedeRepetirCuandoVuelve()
    {
        await using var proceso = new Proceso(bd, Administrador());
        await bd.Ejecutar(
            """
            CREATE FUNCTION auditoria.rechazar_para_la_prueba() RETURNS trigger LANGUAGE plpgsql AS $f$ BEGIN RAISE EXCEPTION 'auditoria caida' USING ERRCODE = '53300'; END $f$;
            CREATE TRIGGER auditoria_caida BEFORE INSERT ON auditoria.registros FOR EACH ROW EXECUTE FUNCTION auditoria.rechazar_para_la_prueba();
            """);
        var antes = await Entradas();

        await Assert.ThrowsAnyAsync<Exception>(() => proceso.Desactivar());

        Assert.Equal(0, await Desactivados());
        Assert.Equal(antes, await Entradas());

        await bd.Ejecutar("DROP TRIGGER auditoria_caida ON auditoria.registros; DROP FUNCTION auditoria.rechazar_para_la_prueba();");
        Assert.True((await proceso.Desactivar()).EsExito);
        Assert.Equal(1, await Desactivados());
        Assert.Equal(antes + 1, await Entradas());
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-13")]
    public async Task UnActorQueNoEsAdministradorCentral_NoDesactivaNiAudita()
    {
        await using var proceso = new Proceso(bd, ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, Guid.NewGuid()));
        var antes = await Entradas();

        var resultado = await proceso.Desactivar();

        Assert.False(resultado.EsExito);
        Assert.Equal(0, await Desactivados());
        Assert.Equal(antes, await Entradas());
    }
}
