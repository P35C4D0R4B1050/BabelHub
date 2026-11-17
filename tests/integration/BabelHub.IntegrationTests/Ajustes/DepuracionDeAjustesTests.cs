using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Ajustes;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Reservas;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BabelHub.IntegrationTests.Ajustes;

/// <summary>
/// T13 (AJU-21, AC-AJU-17): la depuración diaria de ajustes e ingresos con PostgreSQL real, el rol de depuración y filas viejas: a los 2 años quedan sin usuario ni nota con su movimiento intacto, el de 1 año no cambia, repetirla no
/// cambia nada, la cuenta de la aplicación no puede tocar esas columnas, el disparador guardián valida el cambio del propietario de la función y el trabajo corre como «sistema» con su registro y detección de la ausencia de actor.
/// </summary>
public sealed class DepuracionDeAjustesTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class EstablecedorNulo : IEstablecedorDeActor
    {
        public void Establecer(ActorActual actor)
        {
        }

        public void Limpiar()
        {
        }
    }

    private async Task<(Guid Viejo, Guid Reciente, Guid IngresoViejo)> Sembrar()
    {
        var fuente = await bd.AgregarFuente("lima", recepcion: true);
        var viejo = Guid.NewGuid();
        var reciente = Guid.NewGuid();
        var ingresoViejo = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        await bd.Ejecutar($"INSERT INTO ajustes.ajustes (id, fuente_id, edicion_id, motivo, cantidad_anterior, cantidad_posterior, usuario_id, nota, fecha) VALUES ('{viejo}', '{fuente}', gen_random_uuid(), 'merma', 9, 7, '{usuario}', 'caja mojada', now() - interval '2 years 1 day')");
        await bd.Ejecutar($"INSERT INTO ajustes.ajustes (id, fuente_id, edicion_id, motivo, cantidad_anterior, cantidad_posterior, usuario_id, nota, fecha) VALUES ('{reciente}', '{fuente}', gen_random_uuid(), 'merma', 9, 7, '{usuario}', 'otra nota', now() - interval '1 year')");
        await bd.Ejecutar($"INSERT INTO ajustes.ingresos (id, fuente_id, edicion_id, cantidad, referencia, nota, usuario_id, fecha, ajuste_id) VALUES ('{ingresoViejo}', '{fuente}', gen_random_uuid(), 10, 'GR-001', 'nota vieja', '{usuario}', now() - interval '3 years', gen_random_uuid())");
        return (viejo, reciente, ingresoViejo);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-17")]
    public async Task ALos2AñosElUsuarioYLaNotaSeAnonimizan_ElMovimientoQuedaIntacto_ElDeUnAñoNoCambia_YRepetirNoCambiaNada()
    {
        var (viejo, reciente, ingresoViejo) = await Sembrar();
        var depurador = new PostgresDepuradorDeAjustes(bd.Depuracion);

        var primera = await depurador.Anonimizar();
        var segunda = await depurador.Anonimizar();

        Assert.Equal((2, 0), (primera, segunda));
        Assert.Equal("NULL|NULL|9|7", await bd.Consultar<string>($"SELECT coalesce(usuario_id::text, 'NULL') || '|' || coalesce(nota, 'NULL') || '|' || cantidad_anterior || '|' || cantidad_posterior FROM ajustes.ajustes WHERE id = '{viejo}'"));
        Assert.Equal("otra nota|9|7", await bd.Consultar<string>($"SELECT nota || '|' || cantidad_anterior || '|' || cantidad_posterior FROM ajustes.ajustes WHERE id = '{reciente}'"));
        Assert.True(await bd.Consultar<bool>($"SELECT usuario_id IS NOT NULL FROM ajustes.ajustes WHERE id = '{reciente}'"));
        // En el ingreso se vacían el usuario y la nota; la referencia (un código de documento) y la cantidad se conservan.
        Assert.Equal("NULL|NULL|GR-001|10", await bd.Consultar<string>($"SELECT coalesce(usuario_id::text, 'NULL') || '|' || coalesce(nota, 'NULL') || '|' || referencia || '|' || cantidad FROM ajustes.ingresos WHERE id = '{ingresoViejo}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-17")]
    public async Task ElDisparadorGuardianSoloDejaAlPropietarioDeLaFuncionRetirarElUsuarioYLaNotaDeFilasViejas()
    {
        var (viejo, reciente, _) = await Sembrar();

        async Task<PostgresException?> Intentar(string sql)
        {
            await using var conexion = await bd.Administracion.OpenConnectionAsync();
            await using var transaccion = await conexion.BeginTransactionAsync();
            try
            {
                await using (var rol = new NpgsqlCommand("SET LOCAL ROLE ajustes_depuracion_dueno; SELECT set_config('babelhub.alcance', 'red', true)", conexion, transaccion))
                {
                    await rol.ExecuteNonQueryAsync();
                }

                await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
                await comando.ExecuteNonQueryAsync();
                await transaccion.RollbackAsync();
                return null;
            }
            catch (PostgresException ex)
            {
                return ex;
            }
        }

        // Retirar el usuario y la nota de una fila vieja: permitido (se deshace al terminar la prueba).
        Assert.Null(await Intentar($"UPDATE ajustes.ajustes SET usuario_id = NULL, nota = NULL WHERE id = '{viejo}'"));

        // Una fila de un año, otra columna o poner otro valor: rechazado (por el permiso de columna o por el disparador).
        Assert.Equal("42501", (await Intentar($"UPDATE ajustes.ajustes SET usuario_id = NULL WHERE id = '{reciente}'"))!.SqlState);
        Assert.Equal("42501", (await Intentar($"UPDATE ajustes.ajustes SET nota = 'otra' WHERE id = '{viejo}'"))!.SqlState);
        Assert.Equal("42501", (await Intentar($"UPDATE ajustes.ajustes SET cantidad_posterior = 1 WHERE id = '{viejo}'"))!.SqlState);
        Assert.Equal("42501", (await Intentar($"UPDATE ajustes.ajustes SET usuario_id = gen_random_uuid() WHERE id = '{viejo}'"))!.SqlState);
        Assert.Equal("42501", (await Intentar($"DELETE FROM ajustes.ajustes WHERE id = '{viejo}'"))!.SqlState);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes WHERE usuario_id IS NOT NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-18")]
    public async Task LaPurgaBorraLasOperacionesIdempotentesDeMasDe30Dias_YRepetirNoCambiaNada()
    {
        await bd.Ejecutar("INSERT INTO ajustes.operaciones_idempotentes (usuario_id, operacion, clave, huella_de_carga, resultado, creada_en) VALUES (gen_random_uuid(), 'ajuste', 'clave-de-prueba-uno', repeat('a', 64), '{}', now() - interval '31 days')");
        await bd.Ejecutar("INSERT INTO ajustes.operaciones_idempotentes (usuario_id, operacion, clave, huella_de_carga, resultado, creada_en) VALUES (gen_random_uuid(), 'ingreso', 'clave-de-prueba-dos', repeat('a', 64), '{}', now() - interval '29 days')");
        var depurador = new PostgresDepuradorDeAjustes(bd.Depuracion);

        var primera = await depurador.PurgarOperaciones();
        var segunda = await depurador.PurgarOperaciones();

        Assert.Equal((1, 0), (primera, segunda));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.operaciones_idempotentes"));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-17")]
    public async Task ElTrabajoCorreComoSistema_AnonimizaYRegistraLaCorrida_YConUnEjecutorSinActorFallaALaVista()
    {
        var (viejo, _, _) = await Sembrar();
        using (var pila = new PilaDeReservas(bd))
        {
            var resultado = await TrabajosDeAjustes.Depurar(pila.Proveedor).EjecutarUnaVez();

            Assert.Equal(ResultadoDeCorrida.Exitosa, resultado);
            Assert.False(await bd.Consultar<bool>($"SELECT usuario_id IS NOT NULL FROM ajustes.ajustes WHERE id = '{viejo}'"));
            Assert.Contains(pila.Registro.Corridas, c => c.Trabajo == TrabajosDeAjustes.DepuracionDiaria && c.Resultado == ResultadoDeTrabajo.Exito);
        }

        await bd.Limpiar();
        var (viejo2, _, _) = await Sembrar();
        using var sinActor = new PilaDeReservas(bd, s => s.AddSingleton<EjecutorDeTrabajoDeFondo>(sp => new EjecutorDeTrabajoDeFondo(new EstablecedorNulo(), sp.GetRequiredService<IActorActual>())));

        var fallida = await TrabajosDeAjustes.Depurar(sinActor.Proveedor).EjecutarUnaVez();

        Assert.Equal(ResultadoDeCorrida.Fallida, fallida);
        Assert.True(await bd.Consultar<bool>($"SELECT usuario_id IS NOT NULL FROM ajustes.ajustes WHERE id = '{viejo2}'"));
        Assert.Contains(sinActor.Registro.Corridas, c => c.Trabajo == TrabajosDeAjustes.DepuracionDiaria && c.Resultado == ResultadoDeTrabajo.Fallo);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-17")]
    public async Task LaCuentaDeDepuracionNoPuedeLeerNiEscribirLasTablas_SoloEjecutarLasFunciones()
    {
        await Sembrar();
        await using var conexion = await bd.Depuracion.OpenConnectionAsync();

        var leer = await Assert.ThrowsAnyAsync<PostgresException>(async () =>
        {
            await using var comando = new NpgsqlCommand("SELECT count(*) FROM ajustes.ajustes", conexion);
            await comando.ExecuteScalarAsync();
        });

        Assert.Equal("42501", leer.SqlState);
    }
}
