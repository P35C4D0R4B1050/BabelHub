using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// T01 de <c>inventario-seleccion-fuente</c> con PostgreSQL real y el rol real: la baja de un titular sustituye el sujeto de las claves de los eventos de límite sin cambiar el conteo, es
/// repetible, no toca otras claves y el permiso del rol alcanza solo a la columna <c>clave</c>.
/// </summary>
public sealed class SustitucionDeSujetoDeLimitesTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>
{
    private readonly UnidadDeTrabajoPostgres _unidad = new(bd.Acceso);
    private readonly UnidadDeTrabajoPostgres _independiente = new(bd.Acceso);

    private static readonly EsquemaDeModulo Plataforma = EsquemaDeModulo.Crear("plataforma");

    private MotorDeLimitesPostgres Motor() => new(_unidad, _independiente);

    private static string Nuevo() => Guid.NewGuid().ToString("N")[..10];

    private async Task Reservar(MotorDeLimitesPostgres motor, PoliticaDeLimite politica, string contador, int veces)
    {
        for (var i = 0; i < veces; i++)
        {
            await _unidad.Ejecutar(async token =>
            {
                Assert.True((await motor.Reservar(politica, contador, null, null, token)).EsPermitido);
                return 0;
            });
        }
    }

    private Task<long> Contar(string clave) => bd.Consultar<long>($"SELECT count(*) FROM plataforma.eventos_limite WHERE clave = '{clave}'");

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task Sustituir_CambiaElSujetoDeLasClavesDeUnaPoliticaPorUsuario_ElConteoSigueIgual_YLaRepeticionNoCambiaNada()
    {
        var motor = Motor();
        var politica = PoliticasDeSeguridad.ReservasPorHoraPorCliente;
        var original = Nuevo();
        var anonimo = "anon-" + Nuevo();
        await Reservar(motor, politica, $"u:s:{original}", 3);

        var primera = await motor.Sustituir(original, anonimo);
        var repetida = await motor.Sustituir(original, anonimo);

        Assert.Equal(3, primera);
        Assert.Equal(0, repetida);
        Assert.Equal(0L, await Contar($"{politica.Nombre}|u:s:{original}"));
        Assert.Equal(3L, await Contar($"{politica.Nombre}|u:s:{anonimo}"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task Sustituir_NoTocaLosEventosDelOrigen_NiLosDeUnSujetoQueSoloComparteElFinal()
    {
        var motor = Motor();
        var porCliente = PoliticasDeSeguridad.ReservasPorHoraPorCliente;
        var porOrigen = PoliticasDeSeguridad.ReservasPorHoraPorOrigen;
        var original = Nuevo();
        var parecido = "x" + original;
        var huellaDeOrigen = "o:" + Nuevo();
        await Reservar(motor, porCliente, $"u:s:{original}", 1);
        await Reservar(motor, porCliente, $"u:s:{parecido}", 2);
        await Reservar(motor, porOrigen, huellaDeOrigen, 4);

        await motor.Sustituir(original, "anon-" + Nuevo());

        // El conteo por origen sigue incluyendo esas reservas: sus claves llevan la huella del origen y no el sujeto (SEL-19, SEL-35).
        Assert.Equal(4L, await Contar($"{porOrigen.Nombre}|{huellaDeOrigen}"));
        Assert.Equal(2L, await Contar($"{porCliente.Nombre}|u:s:{parecido}"));
        Assert.Equal(0L, await Contar($"{porCliente.Nombre}|u:s:{original}"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task Sustituir_ConUnSujetoSinEventos_NoCambiaNadaNiFalla()
    {
        Assert.Equal(0, await Motor().Sustituir(Nuevo(), "anon-" + Nuevo()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public async Task ElRolDelEsquema_SoloPuedeActualizarLaColumnaClave()
    {
        var motor = Motor();
        await Reservar(motor, PoliticasDeSeguridad.ReservasPorHoraPorCliente, $"u:s:{Nuevo()}", 1);

        async Task<string> Actualizar(string asignacion)
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => _unidad.Ejecutar(async token =>
            {
                await _unidad.EjecutarComo(Plataforma, async interno =>
                {
                    await using var comando = new NpgsqlCommand($"UPDATE plataforma.eventos_limite SET {asignacion}", (NpgsqlConnection)_unidad.Conexion, (NpgsqlTransaction)_unidad.Transaccion);
                    await comando.ExecuteNonQueryAsync(interno);
                }, token);
                return 0;
            }));
            return error.SqlState;
        }

        Assert.Equal("42501", await Actualizar("instante = now()"));
        Assert.Equal("42501", await Actualizar("id = gen_random_uuid()"));
    }
}
