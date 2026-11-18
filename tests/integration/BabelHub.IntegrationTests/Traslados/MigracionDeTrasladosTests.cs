using BabelHub.IntegrationTests.Reservas;
using BabelHub.Inventario.Infrastructure.Traslados;
using Npgsql;

namespace BabelHub.IntegrationTests.Traslados;

/// <summary>
/// T12: la migración del esquema <c>traslados</c> con PostgreSQL real y <b>el rol real de la aplicación</b> (no un superusuario): las restricciones son la última defensa de las reglas del Dominio, el único parcial por pedido
/// hace idempotente la generación, el buzón es único por evento y sucursal, la política de filas deja ver solo lo del alcance, y <c>rol_traslados</c> nunca borra un traslado ni cambia su origen, destino, cantidad o pedido.
/// </summary>
public sealed class MigracionDeTrasladosTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static async Task<string> SqlStateDe(Func<Task> operacion) => (await Assert.ThrowsAsync<PostgresException>(operacion)).SqlState;

    private static string Traslado(
        Guid? id = null, Guid? origen = null, Guid? destino = null, int cantidad = 3, string estado = "solicitado", Guid? pedido = null, bool anulado = false, string? fecha = "NULL", bool porConfirmar = false,
        string? motivo = null, string? guia = null, string? cerrado = null, string? despachado = null, string? recibido = null, Guid? edicion = null)
    {
        var cerradoEn = cerrado ?? (estado is "recibido" or "rechazado" or "cancelado" ? "now()" : "NULL");
        var despachadoEn = despachado ?? (estado is "en-transito" or "recibido" ? "now()" : "NULL");
        var recibidoEn = recibido ?? (estado == "recibido" ? "now()" : "NULL");
        return $"""
            INSERT INTO traslados.traslados (id, edicion_id, origen_id, destino_id, cantidad, estado, motivo, pedido_id, pedido_anulado, fecha_estimada, estimada_por_confirmar, guia, solicitado_en, despachado_en, recibido_en, cerrado_en)
            VALUES ('{id ?? Guid.NewGuid()}', '{edicion ?? Guid.NewGuid()}', '{origen ?? Guid.NewGuid()}', '{destino ?? Guid.NewGuid()}', {cantidad}, '{estado}', {(motivo is null ? "NULL" : $"'{motivo}'")}, {(pedido is null ? "NULL" : $"'{pedido}'")}, {anulado.ToString().ToLowerInvariant()}, {fecha ?? "NULL"}, {porConfirmar.ToString().ToLowerInvariant()}, {(guia is null ? "NULL" : $"'{guia}'")}, now(), {despachadoEn}, {recibidoEn}, {cerradoEn})
            """;
    }

    private static string Mensaje(string clave, string sucursal = "cusco", string tipo = "aviso", string estado = "pendiente", string? enviadoEn = null, string? fallo = null) =>
        $"""
        INSERT INTO traslados.mensajes_salientes (id, tipo, clave_de_evento, sucursal_codigo, carga, estado, proxima_fecha, creado_en, enviado_en, motivo_de_fallo)
        VALUES (gen_random_uuid(), '{tipo}', '{clave}', '{sucursal}', '{Carga}'::jsonb, '{estado}', now(), now(), {enviadoEn ?? (estado == "enviado" ? "now()" : "NULL")}, {(fallo is null ? "NULL" : $"'{fallo}'")})
        """;

    private const string Carga = "{\"estado\":\"aprobado\"}";

    private static string Clave(string estado = "aprobado") => $"traslado:{Guid.NewGuid():D}:{estado}";

    private async Task<NpgsqlException?> ComoLaAplicacion(string sql, string alcance = "red")
    {
        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        try
        {
            await using (var rol = new NpgsqlCommand("SET LOCAL ROLE rol_traslados", conexion, transaccion))
            {
                await rol.ExecuteNonQueryAsync();
            }

            await using (var fijar = new NpgsqlCommand("SELECT set_config('babelhub.alcance', @a, true)", conexion, transaccion))
            {
                fijar.Parameters.AddWithValue("a", alcance);
                await fijar.ExecuteScalarAsync();
            }

            await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
            await comando.ExecuteNonQueryAsync();
            await transaccion.CommitAsync();
            return null;
        }
        catch (NpgsqlException ex)
        {
            return ex;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task LaMigracionEsRepetible_YNoPierdeLosDatos()
    {
        await bd.Ejecutar(Traslado());
        await bd.Ejecutar(Mensaje(Clave()));

        await bd.Ejecutar(DefinicionDeTraslados.Rol);
        await bd.Ejecutar(DefinicionDeTraslados.Sql);
        await bd.Ejecutar(DefinicionDeTraslados.Permisos);

        Assert.Equal((1L, 1L), (await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"), await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes")));
    }

    // ---------- traslados: restricciones ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-19")]
    public async Task ElOrigenDebeSerDistintoDelDestino_YLaCantidadPositiva()
    {
        var mismo = Guid.NewGuid();

        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(origen: mismo, destino: mismo))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(cantidad: 0))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(cantidad: -2))));
        await bd.Ejecutar(Traslado(cantidad: 1));
    }

    [Fact]
    public async Task ElEstadoEsDeLaListaCerrada()
    {
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "perdido"))));
        foreach (var estado in new[] { "solicitado", "aprobado", "en-transito", "recibido", "rechazado", "cancelado" })
        {
            await bd.Ejecutar(Traslado(estado: estado));
        }

        Assert.Equal(6L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task UnTrasladoVinculadoNuncaEstaRechazado_ElDeReposicionSi()
    {
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "rechazado", pedido: Guid.NewGuid()))));
        await bd.Ejecutar(Traslado(estado: "rechazado"));
        await bd.Ejecutar(Traslado(estado: "cancelado", pedido: Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-18")]
    public async Task LaMarcaDePedidoAnuladoSoloExisteEnUnVinculado()
    {
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "en-transito", anulado: true))));
        await bd.Ejecutar(Traslado(estado: "en-transito", anulado: true, pedido: Guid.NewGuid()));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task LaEstimacionSoloExisteEnUnVinculado_YPorConfirmarNoLlevaFecha()
    {
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(fecha: "current_date"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(porConfirmar: true))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(pedido: Guid.NewGuid(), fecha: "current_date", porConfirmar: true))));
        await bd.Ejecutar(Traslado(pedido: Guid.NewGuid(), fecha: "current_date + 2"));
        await bd.Ejecutar(Traslado(pedido: Guid.NewGuid(), porConfirmar: true));
    }

    [Fact]
    public async Task LosTextosTienenTope_200ElMotivoY40LaGuia()
    {
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(motivo: new string('m', 201)))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "aprobado", guia: new string('G', 41)))));
        await bd.Ejecutar(Traslado(motivo: new string('m', 200)));
        await bd.Ejecutar(Traslado(estado: "aprobado", guia: new string('G', 40)));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-02")]
    public async Task LasFechasSonCoherentesConElEstado()
    {
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "en-transito", despachado: "NULL"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "recibido", recibido: "NULL"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "recibido", despachado: "NULL"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "recibido", cerrado: "NULL"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "cancelado", cerrado: "NULL"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Traslado(estado: "solicitado", cerrado: "now()"))));
    }

    [Fact]
    public async Task ElNumeroLoGeneraLaBase_Creciente_YUnico()
    {
        await bd.Ejecutar(Traslado());
        await bd.Ejecutar(Traslado());

        Assert.Equal(1L, await bd.Consultar<long>("SELECT max(numero) - min(numero) FROM traslados.traslados"));
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(DISTINCT numero) FROM traslados.traslados"));
        Assert.Equal("428C9", await SqlStateDe(() => bd.Ejecutar("UPDATE traslados.traslados SET numero = 99 WHERE numero = 1")));
    }

    // ---------- único parcial por pedido ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task ElPedidoTieneUnSoloTrasladoPorEdicionYOrigenMientrasNoSeCancele()
    {
        var (pedido, edicion, origen) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await bd.Ejecutar(Traslado(pedido: pedido, edicion: edicion, origen: origen));

        Assert.Equal("23505", await SqlStateDe(() => bd.Ejecutar(Traslado(pedido: pedido, edicion: edicion, origen: origen))));
        await bd.Ejecutar(Traslado(pedido: pedido, edicion: edicion, origen: Guid.NewGuid()));
        await bd.Ejecutar(Traslado(pedido: pedido, edicion: Guid.NewGuid(), origen: origen));
        await bd.Ejecutar(Traslado(pedido: Guid.NewGuid(), edicion: edicion, origen: origen));
        await bd.Ejecutar("UPDATE traslados.traslados SET estado = 'cancelado', cerrado_en = now() WHERE pedido_id = '" + pedido + "' AND edicion_id = '" + edicion + "' AND origen_id = '" + origen + "'");
        await bd.Ejecutar(Traslado(pedido: pedido, edicion: edicion, origen: origen));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task LasReposicionesSinPedidoNoChocanEntreSi()
    {
        var (edicion, origen, destino) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        await bd.Ejecutar(Traslado(edicion: edicion, origen: origen, destino: destino));
        await bd.Ejecutar(Traslado(edicion: edicion, origen: origen, destino: destino));

        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados"));
    }

    // ---------- el buzón ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElBuzonEsUnicoPorEventoSucursalYTipo_YElDuplicadoConOnConflictNoAborta()
    {
        var clave = Clave();
        await bd.Ejecutar(Mensaje(clave, "cusco", "aviso"));

        Assert.Equal("23505", await SqlStateDe(() => bd.Ejecutar(Mensaje(clave, "cusco", "aviso"))));
        await bd.Ejecutar(Mensaje(clave, "ayacucho", "aviso"));
        await bd.Ejecutar(Mensaje(clave, "cusco", "estado-de-pedido"));
        await bd.Ejecutar(Mensaje(clave, "cusco", "aviso") + " ON CONFLICT DO NOTHING");

        Assert.Equal(3L, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes"));
    }

    [Theory]
    [Trait("Criterio", "AC-TRA-08")]
    [InlineData("traslado:no-es-un-guid:aprobado", "cusco", "aviso", "pendiente")]
    [InlineData("traslado:00000000-0000-0000-0000-000000000001:APROBADO", "cusco", "aviso", "pendiente")]
    [InlineData("pedido:00000000-0000-0000-0000-000000000001:aprobado", "cusco", "aviso", "pendiente")]
    [InlineData("traslado:00000000-0000-0000-0000-000000000001:aprobado", "", "aviso", "pendiente")]
    [InlineData("traslado:00000000-0000-0000-0000-000000000001:aprobado", "Cusco Centro", "aviso", "pendiente")]
    [InlineData("traslado:00000000-0000-0000-0000-000000000001:aprobado", "cusco", "correo", "pendiente")]
    [InlineData("traslado:00000000-0000-0000-0000-000000000001:aprobado", "cusco", "aviso", "perdido")]
    public async Task ElBuzonRechazaClavesSucursalesTiposYEstadosFueraDeLaLista(string clave, string sucursal, string tipo, string estado) =>
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Mensaje(clave, sucursal, tipo, estado))));

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElBuzonExigeCoherenciaEntreEstadoFechaDeEnvioYMotivoDeFallo()
    {
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Mensaje(Clave(), estado: "enviado", enviadoEn: "NULL"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Mensaje(Clave(), estado: "pendiente", enviadoEn: "now()"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Mensaje(Clave(), estado: "pendiente", fallo: "permanente"))));
        Assert.Equal("23514", await SqlStateDe(() => bd.Ejecutar(Mensaje(Clave(), estado: "fallido", fallo: "otro"))));
        await bd.Ejecutar(Mensaje(Clave(), estado: "enviado"));
        await bd.Ejecutar(Mensaje(Clave(), estado: "fallido", fallo: "credencial"));
    }

    // ---------- permisos y política de filas con el rol real ----------

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task ElRolDeLaAplicacionNoBorraTrasladosNiCambiaSuOrigenDestinoCantidadOPedido_PeroSiBorraDelBuzon()
    {
        var (origen, destino) = (Guid.NewGuid(), Guid.NewGuid());
        var id = Guid.NewGuid();
        await bd.Ejecutar(Traslado(id: id, origen: origen, destino: destino));
        await bd.Ejecutar(Mensaje(Clave()));

        var borrar = await ComoLaAplicacion("DELETE FROM traslados.traslados");
        var cantidad = await ComoLaAplicacion("UPDATE traslados.traslados SET cantidad = 99");
        var origenNuevo = await ComoLaAplicacion($"UPDATE traslados.traslados SET origen_id = '{Guid.NewGuid()}'");
        var pedido = await ComoLaAplicacion($"UPDATE traslados.traslados SET pedido_id = '{Guid.NewGuid()}'");
        var vaciar = await ComoLaAplicacion("TRUNCATE traslados.traslados");
        var estado = await ComoLaAplicacion("UPDATE traslados.traslados SET estado = 'aprobado', aprobado_en = now(), version = version + 1");
        var purga = await ComoLaAplicacion("DELETE FROM traslados.mensajes_salientes");

        Assert.All(new[] { borrar, cantidad, origenNuevo, pedido, vaciar }, e => Assert.True(e is PostgresException { SqlState: "42501" }, e?.Message));
        Assert.Null(estado);
        Assert.Null(purga);
        Assert.Equal(("aprobado", 0L), (await bd.Consultar<string>($"SELECT estado FROM traslados.traslados WHERE id = '{id}'") ?? string.Empty, await bd.Consultar<long>("SELECT count(*) FROM traslados.mensajes_salientes")));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-03")]
    public async Task LaPoliticaDeFilasDejaVerYEscribirSoloLoDelOrigenOElDestinoOLaRed_YSinValorDeniega()
    {
        var (cusco, ayacucho, lima) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await bd.Ejecutar(Traslado(origen: cusco, destino: ayacucho));

        async Task<long> Ver(string alcance)
        {
            await using var conexion = await bd.Acceso.OpenConnectionAsync();
            await using var transaccion = await conexion.BeginTransactionAsync();
            await using (var rol = new NpgsqlCommand("SET LOCAL ROLE rol_traslados", conexion, transaccion))
            {
                await rol.ExecuteNonQueryAsync();
            }

            await using (var fijar = new NpgsqlCommand("SELECT set_config('babelhub.alcance', @a, true)", conexion, transaccion))
            {
                fijar.Parameters.AddWithValue("a", alcance);
                await fijar.ExecuteScalarAsync();
            }

            await using var cuenta = new NpgsqlCommand("SELECT count(*) FROM traslados.traslados", conexion, transaccion);
            return (long)(await cuenta.ExecuteScalarAsync())!;
        }

        Assert.Equal((1L, 1L, 1L, 0L, 0L), (await Ver("red"), await Ver(cusco.ToString()), await Ver(ayacucho.ToString()), await Ver(lima.ToString()), await Ver(string.Empty)));

        var escribirEnOtra = await ComoLaAplicacion(Traslado(origen: cusco, destino: ayacucho), lima.ToString());
        var escribirEnLaPropia = await ComoLaAplicacion(Traslado(origen: cusco, destino: ayacucho), ayacucho.ToString());
        var actualizarAjeno = await ComoLaAplicacion("UPDATE traslados.traslados SET version = version + 1", lima.ToString());

        Assert.True(escribirEnOtra is PostgresException { SqlState: "42501" }, escribirEnOtra?.Message);
        Assert.Null(escribirEnLaPropia);
        Assert.Null(actualizarAjeno);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM traslados.traslados WHERE version > 0"));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-20")]
    public async Task ElNumeroSeGeneraAlInsertarConElRolDeLaAplicacion_SinPermisoSobreLaSecuencia()
    {
        var error = await ComoLaAplicacion("INSERT INTO traslados.traslados (id, edicion_id, origen_id, destino_id, cantidad, estado, solicitado_en) VALUES (gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), gen_random_uuid(), 2, 'solicitado', now())");

        Assert.Null(error);
        Assert.True(await bd.Consultar<long>("SELECT numero FROM traslados.traslados") > 0);
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-14")]
    public async Task ElRolSinIniciarSesionNoEsPropietarioNiTieneMasPrivilegiosQueLosDeclarados()
    {
        var privilegios = await bd.Consultar<string>("SELECT string_agg(DISTINCT privilege_type, ',' ORDER BY privilege_type) FROM information_schema.role_table_grants WHERE grantee = 'rol_traslados' AND table_schema = 'traslados' AND table_name = 'traslados'");
        var propietario = await bd.Consultar<string>("SELECT tableowner FROM pg_tables WHERE schemaname = 'traslados' AND tablename = 'traslados'");
        var inicia = await bd.Consultar<bool>("SELECT rolcanlogin FROM pg_roles WHERE rolname = 'rol_traslados'");

        Assert.Equal(("INSERT,SELECT", "propietario", false), (privilegios, propietario, inicia));
    }

    [Fact]
    [Trait("Criterio", "AC-TRA-08")]
    public async Task ElRolDeLaAplicacionSoloActualizaLasColumnasDeEntregaDelBuzon_NuncaLaCargaLaClaveElTipoNiLaSucursal()
    {
        await bd.Ejecutar(Mensaje(Clave()));

        var carga = await ComoLaAplicacion("UPDATE traslados.mensajes_salientes SET carga = '{\"estado\":\"otro\"}'::jsonb");
        var clave = await ComoLaAplicacion($"UPDATE traslados.mensajes_salientes SET clave_de_evento = 'traslado:{Guid.NewGuid():D}:recibido'");
        var sucursal = await ComoLaAplicacion("UPDATE traslados.mensajes_salientes SET sucursal_codigo = 'lima'");
        var entrega = await ComoLaAplicacion("UPDATE traslados.mensajes_salientes SET estado = 'enviado', intentos = intentos + 1, enviado_en = now()");

        Assert.All(new[] { carga, clave, sucursal }, e => Assert.True(e is PostgresException { SqlState: "42501" }, e?.Message));
        Assert.Null(entrega);
        Assert.Equal("enviado", await bd.Consultar<string>("SELECT estado FROM traslados.mensajes_salientes"));
    }
}
