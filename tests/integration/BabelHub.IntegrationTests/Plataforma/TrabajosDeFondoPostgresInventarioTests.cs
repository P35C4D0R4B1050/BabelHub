using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// El registro de trabajos de fondo y el estado de los umbrales sobre PostgreSQL real, con el rol del esquema <c>plataforma</c> y su
/// permiso mínimo: una fila por trabajo, resultado enumerado, una corrida vieja no pisa a una más nueva, y lo guardado sobrevive a un
/// reinicio (otra unidad de trabajo sobre la misma base).
/// </summary>
public sealed class TrabajosDeFondoPostgresInventarioTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);

    private sealed class RelojDePrueba(DateTimeOffset ahora) : IReloj
    {
        public DateTimeOffset Ahora { get; set; } = ahora;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }

    private sealed class ContadorFijo(int total) : IContadorDeEventosDeSeguridad
    {
        public Task<int> Contar(string motivo, DateTimeOffset desde, DateTimeOffset hasta, CancellationToken cancelacion = default) => Task.FromResult(total);
    }

    private sealed class AlertasEspia : IPublicadorDeAlertasDeSeguridad
    {
        public List<AlertaDeSeguridad> Alertas { get; } = [];

        public Task Publicar(AlertaDeSeguridad alerta, CancellationToken cancelacion = default)
        {
            Alertas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    // Cada instancia de «proceso» tiene su propia unidad de trabajo sobre la misma base.
    private RegistroDeTrabajosDeFondoPostgres Registro() => new(new UnidadDeTrabajoPostgres(bd.Acceso));

    private EstadoDeUmbralesPostgres Estados() => new(new UnidadDeTrabajoPostgres(bd.Acceso));

    private static string Trabajo() => "trabajo-" + Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task RegistrarGuardaLaUltimaCorridaYSuResultadoEnumerado_UnaFilaPorTrabajo()
    {
        var nombre = Trabajo();
        var registro = Registro();

        await registro.Registrar(nombre, ResultadoDeTrabajo.Exito, Ahora);
        await registro.Registrar(nombre, ResultadoDeTrabajo.Fallo, Ahora.AddMinutes(1));

        var ultima = await registro.UltimaCorrida(nombre);
        Assert.Equal((Ahora.AddMinutes(1), ResultadoDeTrabajo.Fallo), ultima);
        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM plataforma.trabajos_de_fondo WHERE nombre = '{nombre}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-09")]
    public async Task TrasUnReinicio_UnUmbralQueSigueSuperadoNoVuelveAAlertar()
    {
        var alertas = new AlertasEspia();
        var reloj = new RelojDePrueba(Ahora);
        var motivo = UmbralesDeAlerta.AccesosDenegados;
        // Una base limpia para este umbral: el estado puede haber quedado de otra prueba.
        await bd.Consultar<object>($"DELETE FROM plataforma.estado_de_umbrales WHERE motivo = '{motivo.Motivo}'");

        EvaluarUmbralesDeAlerta Proceso() => new([motivo], new ContadorFijo(11), alertas, Estados(), reloj);

        await Proceso().Ejecutar();
        reloj.Ahora = Ahora.AddMinutes(1);
        await Proceso().Ejecutar(); // otro proceso, tras un reinicio: el estado viene de la base

        Assert.Single(alertas.Alertas);
        Assert.Equal(new EstadoDeUmbral(true, Ahora.AddMinutes(1)), await Estados().Obtener(motivo.Motivo, CancellationToken.None));
    }
}
