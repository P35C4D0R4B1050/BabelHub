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
    public async Task UnTrabajoQueNuncaCorrio_NoTieneUltimaCorrida()
    {
        Assert.Null(await Registro().UltimaCorrida(Trabajo()));
    }

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
    public async Task UnaCorridaMasVieja_NoPisaALaMasNueva()
    {
        var nombre = Trabajo();
        var registro = Registro();
        await registro.Registrar(nombre, ResultadoDeTrabajo.Exito, Ahora.AddMinutes(5));

        await registro.Registrar(nombre, ResultadoDeTrabajo.Fallo, Ahora);

        Assert.Equal((Ahora.AddMinutes(5), ResultadoDeTrabajo.Exito), await registro.UltimaCorrida(nombre));
    }

    [Fact]
    public async Task LoGuardadoSobreviveAUnReinicio()
    {
        var nombre = Trabajo();
        await Registro().Registrar(nombre, ResultadoDeTrabajo.Exito, Ahora);

        var trasElReinicio = Registro();

        Assert.Equal((Ahora, ResultadoDeTrabajo.Exito), await trasElReinicio.UltimaCorrida(nombre));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Mayusculas")]
    [InlineData("con espacio")]
    [InlineData("falló: password=Sup3r")]
    [InlineData("1-empieza-con-numero")]
    public async Task UnNombreQueNoEsEstable_SeRechazaSinTocarLaBase(string nombre)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Registro().Registrar(nombre, ResultadoDeTrabajo.Exito, Ahora));
        await Assert.ThrowsAsync<ArgumentException>(() => Estados().Guardar(nombre, new EstadoDeUmbral(true, Ahora), CancellationToken.None));
    }

    [Fact]
    public async Task LaBaseRechazaUnResultadoQueNoEsElEnumeradoYUnNombreInestable()
    {
        await using var resultado = bd.Administracion.CreateCommand("INSERT INTO plataforma.trabajos_de_fondo VALUES ('prueba-uno', now(), 'se cayo por password=1234')");
        await using var nombre = bd.Administracion.CreateCommand("INSERT INTO plataforma.trabajos_de_fondo VALUES ('Con Espacio', now(), 'exito')");

        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => resultado.ExecuteNonQueryAsync())).SqlState);
        Assert.Equal("23514", (await Assert.ThrowsAsync<PostgresException>(() => nombre.ExecuteNonQueryAsync())).SqlState);
    }

    [Fact]
    public async Task ElRolDelEsquema_NoPuedeBorrarNiVaciarElHistorialDeCorridas()
    {
        var nombre = Trabajo();
        await Registro().Registrar(nombre, ResultadoDeTrabajo.Exito, Ahora);

        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var rol = new NpgsqlCommand("SET LOCAL ROLE rol_plataforma", conexion, transaccion))
        {
            await rol.ExecuteNonQueryAsync();
        }

        await using var borrar = new NpgsqlCommand($"DELETE FROM plataforma.trabajos_de_fondo WHERE nombre = '{nombre}'", conexion, transaccion);
        var error = await Assert.ThrowsAsync<PostgresException>(() => borrar.ExecuteNonQueryAsync());

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task ElRolDeAcceso_NoLeeLasTablasSinElRolDelEsquema()
    {
        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using var leer = new NpgsqlCommand("SELECT count(*) FROM plataforma.trabajos_de_fondo", conexion);

        var error = await Assert.ThrowsAsync<PostgresException>(() => leer.ExecuteScalarAsync());

        Assert.Equal("42501", error.SqlState);
    }

    [Fact]
    public async Task ElEstadoDeUnUmbral_SeGuardaSeActualizaYSoloAvanza()
    {
        const string Motivo = "umbral-de-prueba";
        var estados = Estados();
        Assert.Null(await estados.Obtener(Motivo, CancellationToken.None));

        await estados.Guardar(Motivo, new EstadoDeUmbral(true, Ahora), CancellationToken.None);
        await estados.Guardar(Motivo, new EstadoDeUmbral(false, Ahora.AddMinutes(1)), CancellationToken.None);
        await estados.Guardar(Motivo, new EstadoDeUmbral(true, Ahora.AddMinutes(-3)), CancellationToken.None);

        Assert.Equal(new EstadoDeUmbral(false, Ahora.AddMinutes(1)), await Estados().Obtener(Motivo, CancellationToken.None));
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
