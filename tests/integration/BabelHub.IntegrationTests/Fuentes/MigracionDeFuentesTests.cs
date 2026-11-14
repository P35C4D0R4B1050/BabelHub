using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Shared.Contratos;
using Npgsql;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>
/// T17: la migración del esquema <c>fuentes</c> con PostgreSQL real y <b>el rol real de la aplicación</b> (no un superusuario): las restricciones son la última defensa de las reglas
/// del Dominio y la política de filas de <c>stock</c> permite solo lo de la sucursal del actor, <b>deniega sin actor</b> (cero filas, sin error) y deja pasar a la red completa.
/// </summary>
public sealed class MigracionDeFuentesTests(PostgresDeFuentesFixture bd) : IClassFixture<PostgresDeFuentesFixture>, IAsyncLifetime
{
    private static readonly EsquemaDeModulo Esquema = EsquemaDeModulo.Crear(DefinicionDeFuentes.Esquema);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> SqlStateDe(Func<Task> operacion)
    {
        var error = await Assert.ThrowsAsync<PostgresException>(operacion);
        return error.SqlState;
    }

    private Task<string> FallaAlEjecutar(string sql) => SqlStateDe(() => bd.Ejecutar(sql));

    // --- la migración ---

    [Fact]
    [Trait("Criterio", "AC-FUE-04")]
    public async Task LaMigracionEsRepetible_YNoPierdeLosDatos()
    {
        var lima = await bd.AgregarFuente("lima");

        await bd.Ejecutar(DefinicionDeFuentes.Rol);
        await bd.Ejecutar(DefinicionDeFuentes.Sql);
        await bd.Ejecutar(DefinicionDeFuentes.Permisos);

        Assert.Equal(1L, await bd.Consultar<long>($"SELECT count(*) FROM fuentes.fuentes WHERE id = '{lima}'"));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM pg_policies WHERE schemaname = 'fuentes' AND tablename = 'stock'"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-04")]
    public async Task UnaFuenteNuevaAparecesinCambiarLaEstructura_YNoNecesitaFilasDeStock()
    {
        for (var i = 0; i < 6; i++)
        {
            await bd.AgregarFuente($"sucursal-{i}", $"Sucursal {i}", recepcion: i == 0);
        }

        Assert.Equal(6L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.stock"));
    }

    // --- stock ---

    [Theory]
    [Trait("Criterio", "AC-FUE-02")]
    [InlineData(2, 3)]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    public async Task ElCheckDeStock_RechazaUnEstadoImposible(int fisico, int reservado)
    {
        var fuente = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();

        var estado = await SqlStateDe(() => bd.PonerStock(fuente, edicion, fisico, reservado));

        Assert.Equal("23514", estado);
        Assert.Null(await bd.StockDe(fuente, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task ElCheckDeStock_AdmiteReservadoIgualAlFisico_ElCasoLimite()
    {
        var fuente = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();

        await bd.PonerStock(fuente, edicion, 5, 5);

        Assert.Equal((5, 5), await bd.StockDe(fuente, edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task UnaEdicionDigitalNuncaTieneStock_PorLaClaveCompuestaYElCheckDeFormato()
    {
        var fuente = await bd.AgregarFuente("lima");
        var digital = await bd.AgregarEdicion("digital");

        // La clave compuesta (edicion_id, formato) apunta a una ficha impresa: la digital no la cumple.
        var sinFicha = await FallaAlEjecutar($"INSERT INTO fuentes.stock (fuente_id, edicion_id, fisico, reservado) VALUES ('{fuente}', '{digital}', 1, 0)");
        // Declararse impresa en la fila tampoco ayuda: la ficha dice digital.
        var mintiendo = await FallaAlEjecutar($"INSERT INTO fuentes.stock (fuente_id, edicion_id, formato, fisico, reservado) VALUES ('{fuente}', '{digital}', 'impreso', 1, 0)");
        // Y una fila que se dice digital choca con el CHECK de formato.
        var comoDigital = await FallaAlEjecutar($"INSERT INTO fuentes.stock (fuente_id, edicion_id, formato, fisico, reservado) VALUES ('{fuente}', '{digital}', 'digital', 1, 0)");

        Assert.Equal("23503", sinFicha);
        Assert.Equal("23503", mintiendo);
        Assert.Equal("23514", comoDigital);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.stock"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task UnaEdicionSinFicha_NoPuedeTenerStock()
    {
        var fuente = await bd.AgregarFuente("lima");

        var estado = await FallaAlEjecutar($"INSERT INTO fuentes.stock (fuente_id, edicion_id, fisico, reservado) VALUES ('{fuente}', '{Guid.NewGuid()}', 1, 0)");

        Assert.Equal("23503", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-05")]
    public async Task CambiarLaFichaADigitalConStockExistente_FallaPorLaClaveCompuesta()
    {
        var fuente = await bd.AgregarFuente("lima");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(fuente, edicion, 3, 0);

        var estado = await FallaAlEjecutar($"UPDATE fuentes.ediciones SET formato = 'digital' WHERE edicion_id = '{edicion}'");

        Assert.Equal("23503", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-02")]
    public async Task UnaFilaDeStockNeedsUnaFuenteReal()
    {
        var edicion = await bd.AgregarEdicion();

        var estado = await FallaAlEjecutar($"INSERT INTO fuentes.stock (fuente_id, edicion_id, fisico, reservado) VALUES ('{Guid.NewGuid()}', '{edicion}', 1, 0)");

        Assert.Equal("23503", estado);
    }

    // --- fuentes ---

    [Fact]
    [Trait("Criterio", "AC-FUE-01")]
    public async Task UnCodigoOUnNombreRepetido_SeRechazaPorLaRestriccionUnica()
    {
        await bd.AgregarFuente("cusco", "Cusco");

        var codigo = await SqlStateDe(() => bd.AgregarFuente("cusco", "Otro nombre"));
        var nombre = await SqlStateDe(() => bd.AgregarFuente("cusco-2", "Cusco"));

        Assert.Equal("23505", codigo);
        Assert.Equal("23505", nombre);
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-01")]
    [InlineData("L")]
    [InlineData("Lima")]
    [InlineData("lima ")]
    [InlineData("un-codigo-demasiado-largo")]
    public async Task UnCodigoConFormaInvalida_SeRechazaPorElCheck(string codigo)
    {
        var estado = await SqlStateDe(() => bd.AgregarFuente(codigo, "Nombre " + codigo.Length));

        Assert.Equal("23514", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task DosFuentesDeRecepcion_SeRechazanPorElIndiceUnicoParcial_AunSaltandoseLaAplicacion()
    {
        await bd.AgregarFuente("lima", recepcion: true);
        await bd.AgregarFuente("cusco");

        var segunda = await SqlStateDe(() => bd.AgregarFuente("arequipa", recepcion: true));
        var porUpdate = await FallaAlEjecutar("UPDATE fuentes.fuentes SET es_recepcion = true WHERE codigo = 'cusco'");

        Assert.Equal("23505", segunda);
        Assert.Equal("23505", porUpdate);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes WHERE es_recepcion"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-16")]
    public async Task VariasFuentesSinRecepcionSonValidasParaLaBase_ElInvarianteLoDaLaAplicacion()
    {
        await bd.AgregarFuente("lima");
        await bd.AgregarFuente("cusco");

        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.fuentes WHERE es_recepcion"));
    }

    // --- rutas ---

    [Theory]
    [Trait("Criterio", "AC-FUE-14")]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task UnaRutaConPlazoNoPositivo_SeRechaza(int plazo)
    {
        var cusco = await bd.AgregarFuente("cusco");
        var ayacucho = await bd.AgregarFuente("ayacucho");

        var estado = await FallaAlEjecutar($"INSERT INTO fuentes.rutas (origen_id, destino_id, plazo_dias) VALUES ('{cusco}', '{ayacucho}', {plazo})");

        Assert.Equal("23514", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-14")]
    public async Task UnaRutaDeUnaFuenteASiMisma_SeRechaza()
    {
        var lima = await bd.AgregarFuente("lima");

        var estado = await FallaAlEjecutar($"INSERT INTO fuentes.rutas (origen_id, destino_id, plazo_dias) VALUES ('{lima}', '{lima}', 2)");

        Assert.Equal("23514", estado);
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-14")]
    public async Task UnaRutaConUnaFuenteInexistente_SeRechazaPorLaClaveForanea_YLaRutaEsDireccional()
    {
        var cusco = await bd.AgregarFuente("cusco");
        var ayacucho = await bd.AgregarFuente("ayacucho");

        var sinDestino = await FallaAlEjecutar($"INSERT INTO fuentes.rutas (origen_id, destino_id, plazo_dias) VALUES ('{cusco}', '{Guid.NewGuid()}', 2)");
        await bd.Ejecutar($"INSERT INTO fuentes.rutas (origen_id, destino_id, plazo_dias) VALUES ('{cusco}', '{ayacucho}', 2), ('{ayacucho}', '{cusco}', 4)");

        Assert.Equal("23503", sinDestino);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.rutas"));
    }

    // --- la política de filas, con el rol real de la aplicación ---

    private sealed record Escenario(Guid Ayacucho, Guid Cusco, Guid Edicion);

    private async Task<Escenario> StockEnDosFuentes()
    {
        var ayacucho = await bd.AgregarFuente("ayacucho");
        var cusco = await bd.AgregarFuente("cusco");
        var edicion = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, edicion, 10, 1);
        await bd.PonerStock(cusco, edicion, 20, 0);
        return new Escenario(ayacucho, cusco, edicion);
    }

    private async Task<long> ContarComo(ActorActual? actor)
    {
        var unidad = bd.Unidad(actor);
        long cuenta = 0;
        await unidad.Ejecutar(async token =>
        {
            await unidad.EjecutarComo(Esquema, async _ =>
            {
                await using var comando = new NpgsqlCommand("SELECT count(*) FROM fuentes.stock", (NpgsqlConnection)unidad.Conexion, (NpgsqlTransaction)unidad.Transaccion);
                cuenta = (long)(await comando.ExecuteScalarAsync(token))!;
            }, token);
            return 0;
        });
        return cuenta;
    }

    private static ActorActual Encargado(Guid sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal);

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Politica_UnEncargadoVeSoloElStockDeSuSucursal()
    {
        var e = await StockEnDosFuentes();

        Assert.Equal(1L, await ContarComo(Encargado(e.Ayacucho)));
        Assert.Equal(1L, await ContarComo(Encargado(e.Cusco)));
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [InlineData("administrador")]
    [InlineData("logistica")]
    [InlineData("cliente")]
    [InlineData("sistema")]
    [InlineData("integracion")]
    public async Task Politica_LaRedCompleta_VeTodoElStock(string quien)
    {
        await StockEnDosFuentes();
        ActorActual actor = quien switch
        {
            "administrador" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral),
            "logistica" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.PersonalDeLogistica),
            "cliente" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.Cliente),
            "sistema" => ActorActual.DelSistema(),
            _ => ActorActual.DeIntegracion("e-commerce"),
        };

        Assert.Equal(2L, await ContarComo(actor));
    }

    [Theory]
    [Trait("Criterio", "AC-FUE-06")]
    [InlineData("sin-actor")]
    [InlineData("visitante")]
    [InlineData("encargado-sin-sucursal")]
    [InlineData("rol-desconocido")]
    public async Task Politica_SinActorOSinAlcanceValido_DeniegaYDevuelveCeroFilasSinError(string quien)
    {
        await StockEnDosFuentes();
        ActorActual? actor = quien switch
        {
            "sin-actor" => null,
            "visitante" => ActorActual.Anonimo(),
            "encargado-sin-sucursal" => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal),
            _ => ActorActual.DeUsuario(Guid.NewGuid(), "auditor-externo", Guid.NewGuid()),
        };

        // Es exactamente el riesgo del hallazgo: no hay error, hay «cero filas», que parece «sin stock».
        Assert.Equal(0L, await ContarComo(actor));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Politica_UnaEscrituraDeStockSinActorOFueraDeAlcance_SeRechazaPorLaPolitica()
    {
        var e = await StockEnDosFuentes();

        foreach (var actor in new ActorActual?[] { null, Encargado(e.Ayacucho) })
        {
            var unidad = bd.Unidad(actor);
            var error = await Assert.ThrowsAsync<PostgresException>(() => unidad.Ejecutar(async token =>
            {
                await unidad.EjecutarComo(Esquema, async _ =>
                {
                    await using var comando = new NpgsqlCommand(
                        "INSERT INTO fuentes.stock (fuente_id, edicion_id, formato, fisico, reservado) VALUES (@f, @e, 'impreso', 0, 0)",
                        (NpgsqlConnection)unidad.Conexion,
                        (NpgsqlTransaction)unidad.Transaccion);
                    comando.Parameters.AddWithValue("f", e.Cusco);
                    comando.Parameters.AddWithValue("e", await EdicionNueva());
                    await comando.ExecuteNonQueryAsync(token);
                }, token);
                return 0;
            }));

            Assert.Equal("42501", error.SqlState);
            Assert.True(BabelHub.Inventario.Infrastructure.Plataforma.RechazoDeLaPolitica.Es(error));
        }
    }

    private async Task<Guid> EdicionNueva() => await bd.AgregarEdicion();

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Politica_UnUpdateFueraDeAlcance_NoLanzaYAfectaCeroFilas()
    {
        var e = await StockEnDosFuentes();
        var unidad = bd.Unidad(Encargado(e.Ayacucho));
        int afectadas = -1;

        await unidad.Ejecutar(async token =>
        {
            await unidad.EjecutarComo(Esquema, async _ =>
            {
                await using var comando = new NpgsqlCommand(
                    "UPDATE fuentes.stock SET fisico = fisico + 1 WHERE fuente_id = @f AND edicion_id = @e",
                    (NpgsqlConnection)unidad.Conexion,
                    (NpgsqlTransaction)unidad.Transaccion);
                comando.Parameters.AddWithValue("f", e.Cusco);
                comando.Parameters.AddWithValue("e", e.Edicion);
                afectadas = await comando.ExecuteNonQueryAsync(token);
            }, token);
            return 0;
        });

        Assert.Equal(0, afectadas);
        Assert.Equal((20, 0), await bd.StockDe(e.Cusco, e.Edicion));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task Politica_ElPropietarioDeLaTabla_TambienEstaSujetoPorElForce()
    {
        await StockEnDosFuentes();

        // El propietario no es un superusuario: con FORCE ROW LEVEL SECURITY la política también le aplica y, sin alcance, no ve nada.
        await using var conexion = await bd.Administracion.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var rol = new NpgsqlCommand("SET LOCAL ROLE propietario", conexion, transaccion))
        {
            await rol.ExecuteNonQueryAsync();
        }

        await using var cuenta = new NpgsqlCommand("SELECT count(*) FROM fuentes.stock", conexion, transaccion);
        Assert.Equal(0L, (long)(await cuenta.ExecuteScalarAsync())!);
    }

    // --- el permiso mínimo de rol_fuentes ---

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task ElRolDeAcceso_PorSiSoloNoLeeElEsquema_YElRolDelModuloNoBorraNiCreaTablas()
    {
        await StockEnDosFuentes();
        var actor = ActorActual.DelSistema();

        var sinRol = bd.Unidad(actor);
        var estadoSinRol = await Assert.ThrowsAsync<PostgresException>(() => sinRol.Ejecutar(async token =>
        {
            await using var comando = new NpgsqlCommand("SELECT count(*) FROM fuentes.stock", (NpgsqlConnection)sinRol.Conexion, (NpgsqlTransaction)sinRol.Transaccion);
            return await comando.ExecuteScalarAsync(token);
        }));

        var conRol = bd.Unidad(actor);
        var borrar = await Assert.ThrowsAsync<PostgresException>(() => conRol.Ejecutar(async token =>
        {
            await conRol.EjecutarComo(Esquema, async _ =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM fuentes.stock", (NpgsqlConnection)conRol.Conexion, (NpgsqlTransaction)conRol.Transaccion);
                await comando.ExecuteNonQueryAsync(token);
            }, token);
            return 0;
        }));

        var crear = bd.Unidad(actor);
        var creacion = await Assert.ThrowsAsync<PostgresException>(() => crear.Ejecutar(async token =>
        {
            await crear.EjecutarComo(Esquema, async _ =>
            {
                await using var comando = new NpgsqlCommand("CREATE TABLE fuentes.intrusa (id int)", (NpgsqlConnection)crear.Conexion, (NpgsqlTransaction)crear.Transaccion);
                await comando.ExecuteNonQueryAsync(token);
            }, token);
            return 0;
        }));

        Assert.Equal("42501", estadoSinRol.SqlState);
        Assert.Equal("42501", borrar.SqlState);
        Assert.Equal("42501", creacion.SqlState);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM fuentes.stock"));
    }

    [Fact]
    [Trait("Criterio", "AC-FUE-06")]
    public async Task ElRolDeAcceso_NoEsSuperusuarioNiSaltaLaSeguridadPorFilas_NiEsMiembroDelPropietario()
    {
        var superusuario = await bd.Consultar<bool>("SELECT rolsuper FROM pg_roles WHERE rolname = 'acceso'");
        var saltaRls = await bd.Consultar<bool>("SELECT rolbypassrls FROM pg_roles WHERE rolname = 'acceso'");
        var miembroDelPropietario = await bd.Consultar<bool>("SELECT pg_has_role('acceso', 'propietario', 'MEMBER')");

        Assert.False(superusuario);
        Assert.False(saltaRls);
        Assert.False(miembroDelPropietario);
    }
}
