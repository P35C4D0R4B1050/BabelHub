using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class UnidadDeTrabajoPostgresTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>
{
    private static readonly EsquemaDeModulo Identidad = EsquemaDeModulo.Crear("identidad");
    private static readonly EsquemaDeModulo Pedidos = EsquemaDeModulo.Crear("pedidos");

    private UnidadDeTrabajoPostgres Unidad() => new(bd.Acceso);

    private static async Task Sql(UnidadDeTrabajoPostgres unidad, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
        await comando.ExecuteNonQueryAsync();
    }

    private static async Task<T?> Escalar<T>(UnidadDeTrabajoPostgres unidad, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
        var valor = await comando.ExecuteScalarAsync();
        return valor is null or DBNull ? default : (T)valor;
    }

    private async Task<long> ContarPedidos(int id) => await bd.Consultar<long>($"SELECT count(*) FROM pedidos.pedidos WHERE id = {id}");

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnTrabajoQueTermina_ConfirmaLosEfectos()
    {
        var unidad = Unidad();

        await unidad.Ejecutar<int>(async token =>
        {
            await unidad.EjecutarComo(Pedidos, _ => Sql(unidad, "INSERT INTO pedidos.pedidos VALUES (101, 1)"), token);
            return 1;
        });

        Assert.Equal(1, await ContarPedidos(101));
        Assert.False(unidad.HayTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnErrorInesperadoAMitad_NoDejaEfectosParciales()
    {
        var unidad = Unidad();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.Ejecutar<int>(async token =>
        {
            await unidad.EjecutarComo(Pedidos, _ => Sql(unidad, "INSERT INTO pedidos.pedidos VALUES (102, 1)"), token);
            throw new InvalidOperationException("se cayó a la mitad");
        }));

        Assert.Equal(0, await ContarPedidos(102));
        Assert.False(unidad.HayTransaccion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task TransaccionPorCasoDeUso_ConUnFalloDeNegocioTrasEscribir_RevierteEnPostgreSQL()
    {
        var unidad = Unidad();
        var casoDeUso = new TransaccionPorCasoDeUso<int, string>(new CasoDeUsoQueEscribeYFalla(unidad), unidad);

        var resultado = await casoDeUso.Ejecutar(103);

        Assert.False(resultado.EsExito);
        Assert.Equal("stock-insuficiente", resultado.Error!.Clave);
        Assert.Equal(0, await ContarPedidos(103));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_ConUnaOperacionEntreModulos_EsUnaSolaTransaccionQueSeConfirmaORevierteJunta()
    {
        var unidad = Unidad();

        await unidad.Ejecutar<int>(async token =>
        {
            await unidad.EjecutarComo(Identidad, _ => Sql(unidad, "INSERT INTO identidad.usuarios VALUES (201, 'a@correo.pe')"), token);
            await unidad.EjecutarComo(Pedidos, _ => Sql(unidad, "INSERT INTO pedidos.pedidos VALUES (201, 201)"), token);
            return 1;
        });
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM identidad.usuarios WHERE id = 201"));
        Assert.Equal(1, await ContarPedidos(201));

        await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.Ejecutar<int>(async token =>
        {
            await unidad.EjecutarComo(Identidad, _ => Sql(unidad, "INSERT INTO identidad.usuarios VALUES (202, 'b@correo.pe')"), token);
            await unidad.EjecutarComo(Pedidos, _ => Sql(unidad, "INSERT INTO pedidos.pedidos VALUES (202, 202)"), token);
            throw new InvalidOperationException("falla la segunda parte");
        }));
        Assert.Equal(0, await bd.Consultar<long>("SELECT count(*) FROM identidad.usuarios WHERE id = 202"));
        Assert.Equal(0, await ContarPedidos(202));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-04")]
    public async Task Ejecutar_AnidadoSeUneALaTransaccionDeAfuera()
    {
        var unidad = Unidad();

        await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.Ejecutar<int>(async token =>
        {
            await unidad.Ejecutar<int>(async interna =>
            {
                await unidad.EjecutarComo(Pedidos, _ => Sql(unidad, "INSERT INTO pedidos.pedidos VALUES (104, 1)"), interna);
                return 1;
            }, token);
            throw new InvalidOperationException("falla la de afuera");
        }));

        Assert.Equal(0, await ContarPedidos(104));
    }

    [Fact]
    public async Task EjecutarComo_SinUnaTransaccionActiva_Lanza()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Unidad().EjecutarComo(Pedidos, _ => Task.CompletedTask));
        Assert.Throws<InvalidOperationException>(() => _ = Unidad().Conexion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task EjecutarComo_ElEsquemaDeIdentidad_EsIlegibleSinEjecutarComoYDesdeOtroEsquema()
    {
        var unidad = Unidad();

        // Con el rol de acceso solo (NOINHERIT) no hay ningún permiso sobre los esquemas, ni siquiera dentro de la unidad de trabajo.
        var sinRol = await Assert.ThrowsAsync<PostgresException>(() => unidad.Ejecutar<int>(async _ =>
        {
            await Escalar<long>(unidad, "SELECT count(*) FROM identidad.usuarios");
            return 1;
        }));
        Assert.Equal("42501", sinRol.SqlState);

        await LeerIdentidadFallaDesde(unidad, null);
        await LeerIdentidadFallaDesde(unidad, Pedidos);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-03")]
    public async Task EjecutarComo_ElModuloDeIdentidad_LeeSuEsquemaYAlSalirElRolVuelveAlDeAcceso()
    {
        var unidad = Unidad();

        await unidad.Ejecutar<int>(async token =>
        {
            Assert.Equal("acceso", await Escalar<string>(unidad, "SELECT current_user"));
            long cuenta = 0;
            await unidad.EjecutarComo(Identidad, async _ =>
            {
                Assert.Equal("rol_identidad", await Escalar<string>(unidad, "SELECT current_user"));
                cuenta = await Escalar<long>(unidad, "SELECT count(*) FROM identidad.usuarios");
            }, token);

            Assert.True(cuenta >= 1);
            Assert.Equal("acceso", await Escalar<string>(unidad, "SELECT current_user"));
            Assert.Equal("acceso", await Escalar<string>(unidad, "SELECT session_user"));
            return 1;
        });
    }

    [Fact]
    public async Task EjecutarComo_ConUnBloqueQueLanza_RestauraElRolYNoEnmascaraElError()
    {
        var unidad = Unidad();

        await unidad.Ejecutar<int>(async token =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => unidad.EjecutarComo(Pedidos, _ => throw new InvalidOperationException("falla el bloque"), token));
            Assert.Equal("acceso", await Escalar<string>(unidad, "SELECT current_user"));
            return 1;
        });
    }

    [Fact]
    public async Task EjecutarComo_ConUnaSentenciaQueAbortaLaTransaccion_PropagaElErrorOriginal()
    {
        var unidad = Unidad();

        var error = await Assert.ThrowsAsync<PostgresException>(() => unidad.Ejecutar<int>(async token =>
        {
            await unidad.EjecutarComo(Pedidos, _ => Sql(unidad, "INSERT INTO pedidos.pedidos VALUES (1000000, NULL)"), token);
            return 1;
        }));

        Assert.Equal("23502", error.SqlState); // not_null_violation, no el 25P02 de la restauración
    }

    [Fact]
    public async Task EjecutarComo_Anidado_VuelveAlRolDelBloqueQueLoContiene()
    {
        var unidad = Unidad();

        await unidad.Ejecutar<int>(async token =>
        {
            await unidad.EjecutarComo(Pedidos, async externo =>
            {
                await unidad.EjecutarComo(Identidad, async interno =>
                {
                    Assert.Equal("rol_identidad", await Escalar<string>(unidad, "SELECT current_user"));
                }, externo);
                Assert.Equal("rol_pedidos", await Escalar<string>(unidad, "SELECT current_user"));
            }, token);
            Assert.Equal("acceso", await Escalar<string>(unidad, "SELECT current_user"));
            return 1;
        });
    }

    [Fact]
    public async Task EjecutarComo_ConUnRolDeEsquemaQueNoExiste_Falla()
    {
        var unidad = Unidad();

        var error = await Assert.ThrowsAsync<PostgresException>(() => unidad.Ejecutar<int>(async token =>
        {
            await unidad.EjecutarComo(EsquemaDeModulo.Crear("inexistente"), _ => Task.CompletedTask, token);
            return 1;
        }));

        Assert.Equal("22023", error.SqlState); // invalid_parameter_value: no existe el rol
    }

    [Fact]
    public async Task ElRolDeAcceso_EsNoInheritYSoloMiembroDeLosRolesDeEsquema()
    {
        var hereda = await bd.Consultar<bool>("SELECT rolinherit FROM pg_roles WHERE rolname = 'acceso'");
        var miembros = await bd.Consultar<string>(
            "SELECT string_agg(r.rolname, ',' ORDER BY r.rolname) FROM pg_auth_members m JOIN pg_roles r ON r.oid = m.roleid JOIN pg_roles u ON u.oid = m.member WHERE u.rolname = 'acceso'");

        Assert.False(hereda);
        Assert.Equal("rol_identidad,rol_pedidos,rol_plataforma", miembros);
        Assert.DoesNotContain("depuracion", miembros, StringComparison.Ordinal);
        Assert.DoesNotContain("propietario", miembros, StringComparison.Ordinal);
    }

    private async Task LeerIdentidadFallaDesde(UnidadDeTrabajoPostgres unidad, EsquemaDeModulo? desde)
    {
        if (desde is null)
        {
            await using var conexion = await bd.Acceso.OpenConnectionAsync();
            await using var comando = new NpgsqlCommand("SELECT count(*) FROM identidad.usuarios", conexion);
            var error = await Assert.ThrowsAsync<PostgresException>(() => comando.ExecuteScalarAsync());
            Assert.Equal("42501", error.SqlState);
            return;
        }

        var denegado = await Assert.ThrowsAsync<PostgresException>(() => unidad.Ejecutar<int>(async token =>
        {
            await unidad.EjecutarComo(desde, _ => Escalar<long>(unidad, "SELECT count(*) FROM identidad.usuarios"), token);
            return 1;
        }));
        Assert.Equal("42501", denegado.SqlState);
    }

    private sealed class CasoDeUsoQueEscribeYFalla(UnidadDeTrabajoPostgres unidad) : ICasoDeUso<int, string>
    {
        public async Task<Resultado<string>> Ejecutar(int entrada, CancellationToken cancelacion = default)
        {
            await unidad.EjecutarComo(EsquemaDeModulo.Crear("pedidos"), _ => Sql(unidad, $"INSERT INTO pedidos.pedidos VALUES ({entrada}, 1)"), cancelacion);
            throw new ExcepcionDeNegocio("stock-insuficiente");
        }
    }
}
