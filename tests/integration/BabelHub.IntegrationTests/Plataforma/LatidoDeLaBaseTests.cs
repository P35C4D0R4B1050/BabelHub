using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// El latido de la base y su rol mínimo sobre PostgreSQL real (PLA-15, AC-PLA-15): cada latido actualiza la única fila y genera WAL
/// aunque la base no tenga más actividad, y el rol <c>plataforma_latido</c> no puede hacer otra cosa que eso.
/// </summary>
public sealed class LatidoDeLaBaseTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>
{
    private LatidoDeLaBase Latido() => new(bd.Latido);

    private async Task<PostgresException> ErrorDelRol(string sql)
    {
        await using var conexion = await bd.Latido.OpenConnectionAsync();
        await using var comando = new NpgsqlCommand(sql, conexion);
        return await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteNonQueryAsync());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-15")]
    public async Task CadaLatidoAvanzaElNumeroYElInstanteDeLaUnicaFila()
    {
        var latido = Latido();
        var antes = await bd.Consultar<long>("SELECT numero FROM plataforma.latido WHERE id = 1");
        var instanteAntes = await bd.Consultar<DateTime>("SELECT instante FROM plataforma.latido WHERE id = 1");

        var primero = await latido.Latir();
        var segundo = await latido.Latir();

        Assert.Equal(antes + 1, primero);
        Assert.Equal(antes + 2, segundo);
        Assert.True(await bd.Consultar<DateTime>("SELECT instante FROM plataforma.latido WHERE id = 1") > instanteAntes);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM plataforma.latido"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-15")]
    public async Task CadaLatidoGeneraWalNuevoAunqueLaBaseNoTengaOtraActividad()
    {
        var antes = await bd.Consultar<string>("SELECT pg_current_wal_insert_lsn()::text");

        await Latido().Latir();

        // La distancia en bytes entre la posición anterior y la actual: si el latido no escribiera, sería cero.
        var bytes = await bd.Consultar<decimal>($"SELECT pg_wal_lsn_diff(pg_current_wal_insert_lsn(), '{antes}'::pg_lsn)");
        Assert.True(bytes > 0, "El latido no generó WAL nuevo.");
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-15")]
    public async Task DosLatidosSimultaneos_NoSeEstorbanNiCreanOtrasFilas()
    {
        // El rol admite dos conexiones a la vez (el límite es parte de su mínimo privilegio): dos latidos por ronda.
        var latido = Latido();
        var antes = await bd.Consultar<long>("SELECT numero FROM plataforma.latido WHERE id = 1");
        var numeros = new List<long>();

        for (var ronda = 0; ronda < 5; ronda++)
        {
            numeros.AddRange(await Task.WhenAll(latido.Latir(), latido.Latir()));
        }

        Assert.Equal(Enumerable.Range(1, 10).Select(i => antes + i), numeros.Order());
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM plataforma.latido"));
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-15")]
    public async Task LaTablaSoloAdmiteLaFilaUno()
    {
        await using var otra = bd.Administracion.CreateCommand("INSERT INTO plataforma.latido (id, instante, numero) VALUES (2, now(), 0)");

        var error = await Assert.ThrowsAsync<PostgresException>(() => otra.ExecuteNonQueryAsync());

        Assert.Equal("23514", error.SqlState);
    }

    [Theory]
    [Trait("Criterio", "AC-PLA-15")]
    [InlineData("INSERT INTO plataforma.latido (id, instante, numero) VALUES (1, now(), 0)")]
    [InlineData("DELETE FROM plataforma.latido")]
    [InlineData("TRUNCATE plataforma.latido")]
    [InlineData("UPDATE plataforma.latido SET id = 1")]
    [InlineData("SELECT count(*) FROM plataforma.trabajos_de_fondo")]
    [InlineData("SELECT count(*) FROM plataforma.entradas_procesadas")]
    [InlineData("SELECT count(*) FROM identidad.usuarios")]
    [InlineData("CREATE TABLE plataforma.otra (x int)")]
    [InlineData("CREATE TABLE public.otra (x int)")]
    [InlineData("CREATE ROLE intruso")]
    [InlineData("SET ROLE rol_identidad")]
    [InlineData("SET ROLE rol_plataforma")]
    public async Task ElRolDelLatidoNoPuedeHacerNadaMasQueActualizarLaFila(string sql)
    {
        var error = await ErrorDelRol(sql);

        Assert.True(error.SqlState is "42501" or "0LP01", $"{sql}: {error.SqlState} {error.MessageText}");
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-15")]
    public async Task ElRolDelLatidoNoTieneAtributosDeAdministracionYTieneLimites()
    {
        var atributos = await bd.Consultar<string>(
            "SELECT concat_ws(',', rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolbypassrls, rolinherit, rolconnlimit) FROM pg_roles WHERE rolname = 'plataforma_latido'");

        Assert.Equal("f,f,f,f,f,f,2", atributos);
        var plazos = await bd.Consultar<string>("SELECT array_to_string(rolconfig, ';') FROM pg_roles WHERE rolname = 'plataforma_latido'");
        Assert.Contains("statement_timeout=5s", plazos, StringComparison.Ordinal);
        Assert.Contains("lock_timeout=2s", plazos, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LaDefinicionSePuedeAplicarDosVecesSinCambiarElEstado()
    {
        var antes = await bd.Consultar<long>("SELECT numero FROM plataforma.latido WHERE id = 1");

        await using var otraVez = bd.Administracion.CreateCommand(DefinicionDeLatido.Sql + DefinicionDeLatido.Rol);
        await otraVez.ExecuteNonQueryAsync();

        Assert.Equal(antes, await bd.Consultar<long>("SELECT numero FROM plataforma.latido WHERE id = 1"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM plataforma.latido"));
    }

    [Fact]
    public async Task SiLaFilaNoExiste_ElLatidoLoDiceEnVezDeCallar()
    {
        await using var vacio = bd.Administracion.CreateCommand("DELETE FROM plataforma.latido");
        await vacio.ExecuteNonQueryAsync();
        try
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Latido().Latir());
        }
        finally
        {
            await using var restaurar = bd.Administracion.CreateCommand("INSERT INTO plataforma.latido (id, instante, numero) VALUES (1, now(), 0)");
            await restaurar.ExecuteNonQueryAsync();
        }
    }
}
