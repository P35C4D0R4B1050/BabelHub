using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Npgsql;

namespace BabelHub.IntegrationTests.Plataforma;

/// <summary>
/// Alcance de datos por transacción del Inventario (inventario-fuentes T04, D09) con PostgreSQL real: <c>UnidadDeTrabajoPostgres</c> fija
/// <c>babelhub.alcance</c> según una lista blanca de actores, una política de filas de ejemplo (la misma forma que tendrá <c>stock</c>)
/// la lee con el rol real, y la elevación a «toda la red» vale solo mientras dura su bloque.
/// </summary>
public sealed class AlcanceDeDatosInventarioTests(PostgresDeRolesFixture bd) : IClassFixture<PostgresDeRolesFixture>, IAsyncLifetime
{
    private static readonly Guid FuenteA = Guid.Parse("aaaaaaaa-0000-4000-8000-00000000000a");
    private static readonly Guid FuenteB = Guid.Parse("bbbbbbbb-0000-4000-8000-00000000000b");
    private static readonly EsquemaDeModulo Alcance = EsquemaDeModulo.Crear("alcance");

    public async Task InitializeAsync()
    {
        // Xunit crea una instancia por prueba: el esquema se crea una sola vez y las filas se restablecen en cada una.
        await using var comando = bd.Administracion.CreateCommand($$"""
            DO $preparacion$
            BEGIN
                IF to_regclass('alcance.existencias') IS NULL THEN
                    CREATE ROLE rol_alcance NOLOGIN;
                    GRANT rol_alcance TO acceso;
                    CREATE SCHEMA alcance AUTHORIZATION propietario;
                    CREATE TABLE alcance.existencias (fuente_id uuid NOT NULL, edicion_id int NOT NULL, fisico int NOT NULL, PRIMARY KEY (fuente_id, edicion_id));
                    ALTER TABLE alcance.existencias OWNER TO propietario;
                    GRANT USAGE ON SCHEMA alcance TO rol_alcance;
                    GRANT SELECT, INSERT, UPDATE ON alcance.existencias TO rol_alcance;
                    ALTER TABLE alcance.existencias ENABLE ROW LEVEL SECURITY;
                    ALTER TABLE alcance.existencias FORCE ROW LEVEL SECURITY;
                    CREATE POLICY por_alcance ON alcance.existencias FOR ALL
                        USING (current_setting('{{AlcanceDeDatosDelActor.Parametro}}', true) = 'red' OR current_setting('{{AlcanceDeDatosDelActor.Parametro}}', true) = fuente_id::text)
                        WITH CHECK (current_setting('{{AlcanceDeDatosDelActor.Parametro}}', true) = 'red' OR current_setting('{{AlcanceDeDatosDelActor.Parametro}}', true) = fuente_id::text);
                END IF;
            END
            $preparacion$;
            DELETE FROM alcance.existencias;
            INSERT INTO alcance.existencias VALUES ('{{FuenteA}}', 1, 10), ('{{FuenteB}}', 1, 20);
            """);
        await comando.ExecuteNonQueryAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private static ActorActual Encargado(Guid sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal);

    private UnidadDeTrabajoPostgres Unidad(ActorActual? actor) => new(bd.Acceso, actor: new ActorFijo(actor));

    private static async Task<long> Contar(UnidadDeTrabajoPostgres unidad)
    {
        long cuenta = 0;
        await unidad.EjecutarComo(Alcance, async _ => cuenta = (long)(await Escalar(unidad, "SELECT count(*) FROM alcance.existencias"))!);
        return cuenta;
    }

    private static async Task<object?> Escalar(UnidadDeTrabajoPostgres unidad, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
        var valor = await comando.ExecuteScalarAsync();
        return valor is DBNull ? null : valor;
    }

    private static async Task<int> Ejecutar(UnidadDeTrabajoPostgres unidad, string sql)
    {
        await using var comando = new NpgsqlCommand(sql, (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
        return await comando.ExecuteNonQueryAsync();
    }

    // --- la lista blanca ---

    public static TheoryData<string, string> ActoresConAlcanceDeRed => new()
    {
        { "administrador", "red" },
        { "logistica", "red" },
        { "cliente", "red" },
        { "sistema", "red" },
        { "integracion", "red" },
    };

    private static ActorActual? ActorDe(string nombre) => nombre switch
    {
        "administrador" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral),
        "logistica" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica),
        "cliente" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente),
        "sistema" => ActorActual.DelSistema(),
        "integracion" => ActorActual.DeIntegracion("culqi"),
        "sin-actor" => null,
        "encargado-sin-sucursal" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal),
        "encargado-con-sucursal-vacia" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, Guid.Empty),
        "rol-desconocido" => ActorActual.DeUsuario(Guid.NewGuid(), "auditor-externo", FuenteA),
        "anonimo" => ActorActual.Anonimo(),
        _ => throw new ArgumentOutOfRangeException(nameof(nombre)),
    };

    [Theory]
    [MemberData(nameof(ActoresConAlcanceDeRed))]
    public void Para_ConUnActorDeLaRed_DevuelveRed(string nombre, string esperado) =>
        Assert.Equal(esperado, AlcanceDeDatosDelActor.Para(ActorDe(nombre)));

    [Fact]
    public void Para_ConUnEncargadoConSucursal_DevuelveLaSucursalEnMinusculasConGuiones() =>
        Assert.Equal("aaaaaaaa-0000-4000-8000-00000000000a", AlcanceDeDatosDelActor.Para(Encargado(FuenteA)));

    [Theory]
    [InlineData("sin-actor")]
    [InlineData("encargado-sin-sucursal")]
    [InlineData("encargado-con-sucursal-vacia")]
    [InlineData("rol-desconocido")]
    [InlineData("anonimo")]
    public void Para_ConCualquierOtroActor_NoDevuelveValor(string nombre) => Assert.Null(AlcanceDeDatosDelActor.Para(ActorDe(nombre)));

    // --- la política con el rol real ---

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Politica_ConUnEncargado_SoloVeLasFilasDeSuSucursal()
    {
        var unidad = Unidad(Encargado(FuenteA));

        var vistas = await unidad.Ejecutar(async token =>
        {
            long cuenta = 0;
            await unidad.EjecutarComo(Alcance, async _ => cuenta = (long)(await Escalar(unidad, "SELECT count(*) FROM alcance.existencias WHERE edicion_id = 1"))!, token);
            return cuenta;
        });

        Assert.Equal(1, vistas);
    }

    [Theory]
    [MemberData(nameof(ActoresConAlcanceDeRed))]
    public async Task Politica_ConUnActorDeLaRed_VeTodasLasFilas(string nombre, string esperado)
    {
        var unidad = Unidad(ActorDe(nombre));
        Assert.Equal("red", esperado);

        Assert.Equal(2, await unidad.Ejecutar(async token => await Contar(unidad)));
    }

    [Theory]
    [InlineData("sin-actor")]
    [InlineData("encargado-sin-sucursal")]
    [InlineData("encargado-con-sucursal-vacia")]
    [InlineData("rol-desconocido")]
    [InlineData("anonimo")]
    public async Task Politica_ConUnActorFueraDeLaListaBlanca_NoVeNingunaFila(string nombre)
    {
        var unidad = Unidad(ActorDe(nombre));

        var (cuenta, valor) = await unidad.Ejecutar(async token =>
            (await Contar(unidad), await Escalar(unidad, $"SELECT nullif(current_setting('{AlcanceDeDatosDelActor.Parametro}', true), '')")));

        Assert.Equal(0, cuenta);
        Assert.Null(valor);
    }

    [Fact]
    public async Task Politica_ConUnaUnidadSinActorInyectado_DenegaPorDefecto()
    {
        var unidad = new UnidadDeTrabajoPostgres(bd.Acceso);

        Assert.Equal(0, await unidad.Ejecutar(async token => await Contar(unidad)));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Escribir_UnaFilaDeOtraSucursal_LoRechazaLaPoliticaYSeTraduceADenegacion()
    {
        var unidad = Unidad(Encargado(FuenteA));

        var error = await Assert.ThrowsAsync<PostgresException>(() => unidad.Ejecutar(async token =>
        {
            await unidad.EjecutarComo(Alcance, _ => Ejecutar(unidad, $"INSERT INTO alcance.existencias VALUES ('{FuenteB}', 2, 5)"), token);
            return 1;
        }));
        Assert.Equal("42501", error.SqlState);
        Assert.True(RechazoDeLaPolitica.Es(error));

        await Assert.ThrowsAsync<AlcanceDeDatosDenegadoException>(() => RechazoDeLaPolitica.Traducir(() => unidad.Ejecutar(async token =>
        {
            await unidad.EjecutarComo(Alcance, _ => Ejecutar(unidad, $"INSERT INTO alcance.existencias VALUES ('{FuenteB}', 3, 5)"), token);
            return 1;
        })));
    }

    [Fact]
    public async Task Escribir_EnLaPropiaSucursal_Procede()
    {
        var unidad = Unidad(Encargado(FuenteA));

        var filas = await unidad.Ejecutar(async token =>
        {
            var afectadas = 0;
            await unidad.EjecutarComo(Alcance, async _ => afectadas = await Ejecutar(unidad, $"UPDATE alcance.existencias SET fisico = fisico + 1 WHERE fuente_id = '{FuenteA}' AND edicion_id = 1"), token);
            return afectadas;
        });

        Assert.Equal(1, filas);
    }

    [Fact]
    public async Task Actualizar_UnaFilaDeOtraSucursal_NoAfectaFilasYNoLanza()
    {
        var unidad = Unidad(Encargado(FuenteA));

        var filas = await unidad.Ejecutar(async token =>
        {
            var afectadas = -1;
            await unidad.EjecutarComo(Alcance, async _ => afectadas = await Ejecutar(unidad, $"UPDATE alcance.existencias SET fisico = 0 WHERE fuente_id = '{FuenteB}' AND edicion_id = 1"), token);
            return afectadas;
        });

        // La política oculta la fila: por eso el adaptador de stock (T18) comprueba con elevación si existe, para distinguir la denegación de la falta de stock.
        Assert.Equal(0, filas);
        Assert.Equal(20, await bd.Consultar<int>($"SELECT fisico FROM alcance.existencias WHERE fuente_id = '{FuenteB}' AND edicion_id = 1"));
    }

    [Fact]
    public async Task UnErrorDePermisoQueNoEsDeLaPolitica_NoSeTraduceADenegacion()
    {
        var unidad = Unidad(Encargado(FuenteA));

        var error = await Assert.ThrowsAsync<PostgresException>(() => RechazoDeLaPolitica.Traducir(() => unidad.Ejecutar(async token =>
        {
            // El rol del esquema no tiene DELETE sobre la tabla: es un 42501 de permiso, no de política.
            await unidad.EjecutarComo(Alcance, _ => Ejecutar(unidad, "DELETE FROM alcance.existencias"), token);
            return 1;
        })));

        Assert.Equal("42501", error.SqlState);
        Assert.False(RechazoDeLaPolitica.Es(error));
    }

    // --- sin fugas entre conexiones del grupo ---

    [Fact]
    public async Task ElValorDeUnaTransaccion_NoSeFiltraALaSiguienteDeLaMismaConexion()
    {
        var comoAdministrador = Unidad(ActorDe("administrador"));
        var pidAdministrador = await comoAdministrador.Ejecutar(async token =>
        {
            Assert.Equal(2, await Contar(comoAdministrador));
            return (int)(await Escalar(comoAdministrador, "SELECT pg_backend_pid()"))!;
        });

        // La misma conexión física del grupo atiende ahora a un actor sin valor: no hereda «red».
        var comoVisitante = Unidad(ActorActual.Anonimo());
        var pidVisitante = await comoVisitante.Ejecutar(async token =>
        {
            Assert.Equal(0, await Contar(comoVisitante));
            return (int)(await Escalar(comoVisitante, "SELECT pg_backend_pid()"))!;
        });
        Assert.Equal(pidAdministrador, pidVisitante); // si el grupo no reutiliza la conexión, la prueba no demuestra nada

        var comoEncargado = Unidad(Encargado(FuenteB));
        Assert.Equal(1, await comoEncargado.Ejecutar(async token => await Contar(comoEncargado)));
    }

    [Fact]
    public async Task LaLecturaCoherente_TambienFijaElAlcanceDelActor()
    {
        var unidad = Unidad(Encargado(FuenteB));

        Assert.Equal(1, await unidad.EjecutarLectura(async token => await Contar(unidad)));
    }

    // --- la elevación ---

    [Fact]
    public async Task ConAlcanceDeRed_VeTodasLasFilasSoloDentroDelBloque()
    {
        var unidad = Unidad(Encargado(FuenteA));
        var elevacion = (IElevacionDeAlcance)unidad;

        var (antes, dentro, despues) = await unidad.Ejecutar(async token =>
        {
            var primero = await Contar(unidad);
            var enElBloque = await elevacion.ConAlcanceDeRed(async interno => await Contar(unidad), token);
            return (primero, enElBloque, await Contar(unidad));
        });

        Assert.Equal(1, antes);
        Assert.Equal(2, dentro);
        Assert.Equal(1, despues);
    }

    [Fact]
    public async Task ConAlcanceDeRed_PermiteEscribirFilasDeOtrasSucursalesSoloDentroDelBloque()
    {
        var unidad = Unidad(Encargado(FuenteA));
        var elevacion = (IElevacionDeAlcance)unidad;

        await unidad.Ejecutar(async token =>
        {
            await elevacion.ConAlcanceDeRed(
                async interno =>
                {
                    await unidad.EjecutarComo(Alcance, _ => Ejecutar(unidad, $"INSERT INTO alcance.existencias VALUES ('{FuenteB}', 7, 3)"), interno);
                    return 1;
                },
                token);
            return 1;
        });
        Assert.Equal(3, await bd.Consultar<int>($"SELECT fisico FROM alcance.existencias WHERE fuente_id = '{FuenteB}' AND edicion_id = 7"));

        // Fuera del bloque el actor vuelve a su alcance y la política rechaza la misma escritura.
        var error = await Assert.ThrowsAsync<PostgresException>(() => unidad.Ejecutar(async token =>
        {
            await unidad.EjecutarComo(Alcance, _ => Ejecutar(unidad, $"INSERT INTO alcance.existencias VALUES ('{FuenteB}', 8, 3)"), token);
            return 1;
        }));
        Assert.True(RechazoDeLaPolitica.Es(error));
    }

    [Fact]
    public async Task ConAlcanceDeRed_AlSalirRestauraElValorDelActorAunqueElBloqueLance()
    {
        var unidad = Unidad(Encargado(FuenteA));
        var elevacion = (IElevacionDeAlcance)unidad;

        await unidad.Ejecutar(async token =>
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => elevacion.ConAlcanceDeRed<int>(_ => throw new InvalidOperationException("falla el bloque"), token));
            Assert.Equal(FuenteA.ToString("D"), await Escalar(unidad, $"SELECT current_setting('{AlcanceDeDatosDelActor.Parametro}', true)"));
            return 1;
        });
    }

    [Fact]
    public async Task ConAlcanceDeRed_ConUnaSentenciaQueAbortaLaTransaccion_PropagaElErrorOriginal()
    {
        var unidad = Unidad(Encargado(FuenteA));
        var elevacion = (IElevacionDeAlcance)unidad;

        var error = await Assert.ThrowsAsync<PostgresException>(() => unidad.Ejecutar(async token =>
        {
            await elevacion.ConAlcanceDeRed(
                async interno =>
                {
                    await Escalar(unidad, "SELECT 1 / 0");
                    return 1;
                },
                token);
            return 1;
        }));

        Assert.Equal("22012", error.SqlState); // división por cero, no el 25P02 de la restauración
    }

    [Fact]
    public async Task ConAlcanceDeRed_ParaUnActorSinValor_AlSalirVuelveADenegar()
    {
        var unidad = Unidad(ActorActual.Anonimo());
        var elevacion = (IElevacionDeAlcance)unidad;

        var (dentro, despues) = await unidad.Ejecutar(async token =>
        {
            var visto = await elevacion.ConAlcanceDeRed(async interno => await Contar(unidad), token);
            return (visto, await Contar(unidad));
        });

        Assert.Equal(2, dentro);
        Assert.Equal(0, despues);
    }

    [Fact]
    public async Task ConAlcanceDeRed_SinUnaTransaccionActiva_Lanza()
    {
        var elevacion = (IElevacionDeAlcance)Unidad(ActorActual.DelSistema());

        await Assert.ThrowsAsync<InvalidOperationException>(() => elevacion.ConAlcanceDeRed(_ => Task.FromResult(1)));
    }

    private sealed class ActorFijo(ActorActual? actor) : IActorActual
    {
        public ActorActual? Actual { get; } = actor;
    }
}
