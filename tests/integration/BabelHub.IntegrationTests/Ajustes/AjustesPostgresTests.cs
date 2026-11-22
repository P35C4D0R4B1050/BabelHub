using BabelHub.Inventario.Application.Ajustes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.IntegrationTests.Reservas;
using BabelHub.Shared.Contratos;
using Npgsql;

namespace BabelHub.IntegrationTests.Ajustes;

/// <summary>
/// T10, T11, T12 y T15 de <c>inventario-ajustes</c> con PostgreSQL real, el rol real de la aplicación y el contenedor de producción: las restricciones y la inmutabilidad del esquema, la política de filas, los escenarios con stock
/// real (AJU-01 a AJU-12, AJU-15, AJU-18) y las solicitudes simultáneas reales (una sola aplicada, el vendible nunca negativo, cadena anterior/posterior correcta, una sola suma por devolución).
/// </summary>
public sealed class AjustesPostgresTests(PostgresDeReservasFixture bd) : IClassFixture<PostgresDeReservasFixture>, IAsyncLifetime
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(120);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static ActorActual Admin() => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.AdministradorCentral);

    private static ActorActual Encargado(Guid sucursal) => ActorActual.DeUsuario(Guid.NewGuid(), RolesDeUsuario.EncargadoDeSucursal, sucursal);

    private static string Clave() => Guid.NewGuid().ToString("N");

    private static RegistrarAjusteManualEntrada Merma(string fuente, Guid edicion, int cantidad, string? nota = null, string? clave = null, string motivo = "merma") =>
        new(fuente, edicion, motivo, cantidad, null, nota, clave ?? Clave());

    private static RegistrarAjusteManualEntrada Conteo(string fuente, Guid edicion, int conteo, string? clave = null) =>
        new(fuente, edicion, "correccion-de-conteo", null, conteo, null, clave ?? Clave());

    private static RegistrarIngresoDeMercaderiaEntrada Ingreso(Guid edicion, int cantidad, string? referencia = null, string? nota = null, string? clave = null) =>
        new(edicion, null, cantidad, referencia, nota, clave ?? Clave());

    private Task<Resultado<AjusteRegistrado>> Ajustar(PilaDeReservas pila, ActorActual actor, RegistrarAjusteManualEntrada entrada) =>
        pila.Ejecutar<RegistrarAjusteManualEntrada, AjusteRegistrado>(actor, entrada);

    private async Task<(Guid Lima, Guid Cusco, Guid Ayacucho)> Fuentes()
    {
        var lima = await bd.AgregarFuente("lima", recepcion: true);
        return (lima, await bd.AgregarFuente("cusco"), await bd.AgregarFuente("ayacucho"));
    }

    private async Task<long> Interbloqueos() => await bd.Consultar<long>("SELECT deadlocks FROM pg_stat_database WHERE datname = current_database()");

    private static async Task<T[]> Simultaneas<T>(params Func<Task<T>>[] trabajos)
    {
        var compuerta = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tareas = trabajos.Select(t => Task.Run(async () =>
        {
            await compuerta.Task;
            return await t();
        })).ToList();
        compuerta.SetResult();
        return await Task.WhenAll(tareas).WaitAsync(Limite);
    }

    private async Task<NpgsqlException?> IntentarComoLaAplicacion(string sql, string alcance = "red")
    {
        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        try
        {
            await using (var rol = new NpgsqlCommand("SET LOCAL ROLE rol_ajustes", conexion, transaccion))
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

    // ---------- T10: restricciones, inmutabilidad y política de filas ----------

    [Theory]
    [Trait("Criterio", "AC-AJU-03")]
    [InlineData("merma", 5, 7, "NULL", "NULL", "NULL")]
    [InlineData("merma", 5, 5, "NULL", "NULL", "NULL")]
    [InlineData("ingreso-de-mercaderia", 5, 3, "NULL", "NULL", "NULL")]
    [InlineData("correccion-de-conteo", 5, 7, "6", "NULL", "NULL")]
    [InlineData("correccion-de-conteo", 5, 5, "5", "NULL", "NULL")]
    [InlineData("devolucion", 5, 6, "NULL", "NULL", "NULL")]
    [InlineData("merma", 5, 4, "NULL", "gen_random_uuid()", "gen_random_uuid()")]
    [InlineData("regalo", 5, 4, "NULL", "NULL", "NULL")]
    [InlineData("merma", 5, -1, "NULL", "NULL", "NULL")]
    [InlineData("ingreso-de-mercaderia", 5, 1000001, "NULL", "NULL", "NULL")]
    public async Task LasRestriccionesDeLaTablaRechazanLoQueElDominioYaRechaza(string motivo, int anterior, int posterior, string conteo, string devolucion, string pedido)
    {
        var (lima, _, _) = await Fuentes();

        var error = await Assert.ThrowsAnyAsync<PostgresException>(() => bd.Ejecutar(
            $"INSERT INTO ajustes.ajustes (id, fuente_id, edicion_id, motivo, cantidad_anterior, cantidad_posterior, conteo_real, usuario_id, fecha, devolucion_id, pedido_id) VALUES (gen_random_uuid(), '{lima}', gen_random_uuid(), '{motivo}', {anterior}, {posterior}, {conteo}, gen_random_uuid(), now(), {devolucion}, {pedido})"));

        Assert.Equal("23514", error.SqlState);
    }
    [Trait("Criterio", "AC-AJU-03")]

    [Fact]
    [Trait("Criterio", "AC-AJU-15")]
    public async Task LasRestriccionesDelIngresoYDeLaIdempotencia()
    {
        var (lima, _, _) = await Fuentes();
        string Ingreso(string cantidad, string referencia, string nota) =>
            $"INSERT INTO ajustes.ingresos (id, fuente_id, edicion_id, cantidad, referencia, nota, usuario_id, fecha, ajuste_id) VALUES (gen_random_uuid(), '{lima}', gen_random_uuid(), {cantidad}, {referencia}, {nota}, gen_random_uuid(), now(), gen_random_uuid())";

        await bd.Ejecutar(Ingreso("5000", "'GR-001/A.2 b'", "NULL"));
        foreach (var invalida in new[] { Ingreso("0", "NULL", "NULL"), Ingreso("5001", "NULL", "NULL"), Ingreso("5", "'guía;'", "NULL"), Ingreso("5", $"'{new string('A', 61)}'", "NULL"), Ingreso("5", "NULL", $"'{new string('n', 201)}'") })
        {
            Assert.Equal("23514", (await Assert.ThrowsAnyAsync<PostgresException>(() => bd.Ejecutar(invalida))).SqlState);
        }

        Assert.Equal("23514", (await Assert.ThrowsAnyAsync<PostgresException>(() => bd.Ejecutar(
            $"INSERT INTO ajustes.operaciones_idempotentes (usuario_id, operacion, clave, huella_de_carga, resultado) VALUES (gen_random_uuid(), 'ajuste', 'corta', '{new string('a', 64)}', '{{}}')"))).SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task LaDevolucionYLaEdicionSonUnicas()
    {
        var (lima, _, _) = await Fuentes();
        var devolucion = Guid.NewGuid();
        var edicion = Guid.NewGuid();
        string Insertar() =>
            $"INSERT INTO ajustes.ajustes (id, fuente_id, edicion_id, motivo, cantidad_anterior, cantidad_posterior, usuario_id, fecha, devolucion_id, pedido_id) VALUES (gen_random_uuid(), '{lima}', '{edicion}', 'devolucion', 1, 2, gen_random_uuid(), now(), '{devolucion}', gen_random_uuid())";

        await bd.Ejecutar(Insertar());

        Assert.Equal("23505", (await Assert.ThrowsAnyAsync<PostgresException>(() => bd.Ejecutar(Insertar()))).SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-17")]
    public async Task LaAplicacionNoPuedeModificarNiBorrarNiVaciarAjustesNiIngresos_NiTocarLasFuncionesDeDepuracion()
    {
        var (lima, _, _) = await Fuentes();
        await bd.Ejecutar($"INSERT INTO ajustes.ajustes (id, fuente_id, edicion_id, motivo, cantidad_anterior, cantidad_posterior, usuario_id, nota, fecha) VALUES (gen_random_uuid(), '{lima}', gen_random_uuid(), 'merma', 5, 4, gen_random_uuid(), 'n', now() - interval '3 years')");
        await bd.Ejecutar($"INSERT INTO ajustes.ingresos (id, fuente_id, edicion_id, cantidad, usuario_id, nota, fecha, ajuste_id) VALUES (gen_random_uuid(), '{lima}', gen_random_uuid(), 5, gen_random_uuid(), 'n', now() - interval '3 years', gen_random_uuid())");

        foreach (var sql in new[]
        {
            "UPDATE ajustes.ajustes SET nota = NULL",
            "UPDATE ajustes.ajustes SET usuario_id = NULL",
            "UPDATE ajustes.ajustes SET cantidad_posterior = 0",
            "DELETE FROM ajustes.ajustes",
            "TRUNCATE ajustes.ajustes",
            "UPDATE ajustes.ingresos SET nota = NULL",
            "DELETE FROM ajustes.ingresos",
            "TRUNCATE ajustes.ingresos",
            "DELETE FROM ajustes.operaciones_idempotentes",
            "SELECT ajustes.anonimizar_registros()",
            "SELECT ajustes.purgar_operaciones()",
        })
        {
            var error = await IntentarComoLaAplicacion(sql);
            Assert.True(error is PostgresException { SqlState: "42501" }, $"{sql}: {error?.Message ?? "se permitió"}");
        }

        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes WHERE nota = 'n' AND usuario_id IS NOT NULL"));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-08")]
    public async Task LaPoliticaDeFilasSoloDejaVerYEscribirLaSucursalDeLaFilaOLaRed_YSinValorDeniega()
    {
        var (lima, cusco, _) = await Fuentes();
        await bd.Ejecutar($"INSERT INTO ajustes.ajustes (id, fuente_id, edicion_id, motivo, cantidad_anterior, cantidad_posterior, usuario_id, fecha) VALUES (gen_random_uuid(), '{cusco}', gen_random_uuid(), 'merma', 5, 4, gen_random_uuid(), now())");

        async Task<long> Ver(string alcance)
        {
            await using var conexion = await bd.Acceso.OpenConnectionAsync();
            await using var transaccion = await conexion.BeginTransactionAsync();
            await using (var rol = new NpgsqlCommand("SET LOCAL ROLE rol_ajustes", conexion, transaccion))
            {
                await rol.ExecuteNonQueryAsync();
            }

            await using (var fijar = new NpgsqlCommand("SELECT set_config('babelhub.alcance', @a, true)", conexion, transaccion))
            {
                fijar.Parameters.AddWithValue("a", alcance);
                await fijar.ExecuteScalarAsync();
            }

            await using var cuenta = new NpgsqlCommand("SELECT count(*) FROM ajustes.ajustes", conexion, transaccion);
            return (long)(await cuenta.ExecuteScalarAsync())!;
        }

        Assert.Equal((1L, 1L, 0L, 0L), (await Ver("red"), await Ver(cusco.ToString()), await Ver(lima.ToString()), await Ver(string.Empty)));

        var escribirEnOtra = await IntentarComoLaAplicacion($"INSERT INTO ajustes.ajustes (id, fuente_id, edicion_id, motivo, cantidad_anterior, cantidad_posterior, usuario_id, fecha) VALUES (gen_random_uuid(), '{cusco}', gen_random_uuid(), 'merma', 5, 4, gen_random_uuid(), now())", lima.ToString());
        Assert.True(escribirEnOtra is PostgresException { SqlState: "42501" }, escribirEnOtra?.Message);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-17")]
    public async Task LaMigracionEsRepetibleYLosPermisosDeLaAplicacionSonSoloInsertarYLeer()
    {
        await bd.Ejecutar(BabelHub.Inventario.Infrastructure.Ajustes.DefinicionDeAjustes.Roles + BabelHub.Inventario.Infrastructure.Ajustes.DefinicionDeAjustes.Sql + BabelHub.Inventario.Infrastructure.Ajustes.DefinicionDeAjustes.FuncionesDeDepuracion);

        var privilegios = await bd.Consultar<string>("SELECT string_agg(DISTINCT privilege_type, ',' ORDER BY privilege_type) FROM information_schema.role_table_grants WHERE grantee = 'rol_ajustes' AND table_schema = 'ajustes' AND table_name IN ('ajustes', 'ingresos')");

        Assert.Equal("INSERT,SELECT", privilegios);
    }

    // ---------- T15: escenarios con stock real ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-01")]
    public async Task UnaMermaDelEncargadoDeAyacucho_BajaElFisicoDeNueveASiete_ConLaNotaYUnaAuditoria()
    {
        var (_, _, ayacucho) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, libro, 9, 0);
        using var pila = new PilaDeReservas(bd);
        var encargado = Encargado(ayacucho);

        var resultado = await Ajustar(pila, encargado, Merma("ayacucho", libro, 2, "caja mojada"));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal((7, 0), await bd.StockDe(ayacucho, libro));
        var fila = await bd.Consultar<string>($"SELECT motivo || '|' || cantidad_anterior || '|' || cantidad_posterior || '|' || nota || '|' || usuario_id || '|' || fuente_id FROM ajustes.ajustes WHERE id = '{resultado.Valor.AjusteId}'");
        Assert.Equal($"merma|9|7|caja mojada|{encargado.UsuarioId}|{ayacucho}", fila);
        var auditada = Assert.Single(pila.Auditoria.Registros, r => r.Accion == "AjusteRegistrado");
        Assert.Equal(("9", "7"), (auditada.Detalle["cantidadAnterior"], auditada.Detalle["cantidadPosterior"]));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-12")]
    public async Task ElAdministradorCentralAjustaEnCusco_YLaAuditoriaIndicaLaSucursal()
    {
        var (_, cusco, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);

        var resultado = await Ajustar(pila, Admin(), Merma("cusco", libro, 1));

        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        Assert.Equal(cusco, Assert.Single(pila.Auditoria.Registros, r => r.Accion == "AjusteRegistrado").Fuente);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-05")]
    public async Task ConteoConFisicoYReservadoCinco_SieteSubeElFisico_CuatroSeRechazaConLosDosPedidos_CincoSeAcepta()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var cliente = PilaDeReservas.Cliente();
        var uno = PilaDeReservas.ARecojo("lima", [PilaDeReservas.Linea(libro, 3)]);
        var dos = PilaDeReservas.ARecojo("lima", [PilaDeReservas.Linea(libro, 2)]);
        Assert.True((await pila.Reservar(cliente, uno)).EsExito);
        Assert.True((await pila.Reservar(PilaDeReservas.Cliente(), dos)).EsExito);

        var subir = await Ajustar(pila, Admin(), Conteo("lima", libro, 7));
        var rechazado = await Ajustar(pila, Admin(), Conteo("lima", libro, 4));
        var igual = await Ajustar(pila, Admin(), Conteo("lima", libro, 5));

        Assert.True(subir.EsExito, subir.Error?.Clave);
        Assert.Equal("conteo-menor-que-reservas", rechazado.Error!.Clave);
        Assert.True(DetalleDeConteoMenorQueReservas.TryLeer(rechazado.Error.Errores, out var pedidos, out var total));
        Assert.Equal(new[] { uno.PedidoId, dos.PedidoId }.Order(), pedidos.Order());
        Assert.Equal(2, total);
        Assert.True(igual.EsExito, igual.Error?.Clave);
        Assert.Equal((5, 5), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-07")]
    public async Task UnConteoIgualAlFisicoSeRechazaSinCambios_YConLaAuditoriaCaidaNoSeAplicaNada()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 9, 0);
        using var pila = new PilaDeReservas(bd);

        var igual = await Ajustar(pila, Admin(), Conteo("lima", libro, 9));
        pila.Auditoria.Falla = true;
        await Assert.ThrowsAsync<BabelHub.Inventario.Application.Auditoria.AuditoriaNoDisponibleException>(() => Ajustar(pila, Admin(), Merma("lima", libro, 1)));

        Assert.Equal("sin-cambios", igual.Error!.Clave);
        Assert.Equal((9, 0), await bd.StockDe(lima, libro));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes"));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public async Task IngresosDeDiezYCinco_LlevanElFisicoADiezYQuince_ConReferenciaYAuditoria_YUnEncargadoSeRechaza()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        using var pila = new PilaDeReservas(bd);

        var primero = await pila.Ejecutar<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado>(Admin(), Ingreso(libro, 10, "GR-001"));
        var segundo = await pila.Ejecutar<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado>(Admin(), Ingreso(libro, 5));
        var encargado = await pila.Ejecutar<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado>(Encargado(lima), Ingreso(libro, 5));

        Assert.Equal((0, 10, 10, 15), (primero.Valor.FisicoAnterior, primero.Valor.FisicoPosterior, segundo.Valor.FisicoAnterior, segundo.Valor.FisicoPosterior));
        Assert.Equal((15, 0), await bd.StockDe(lima, libro));
        Assert.Equal("GR-001", await bd.Consultar<string>($"SELECT referencia FROM ajustes.ingresos WHERE id = '{primero.Valor.IngresoId}'"));
        Assert.Equal(2, pila.Auditoria.Contar("IngresoDeMercaderiaRegistrado"));
        Assert.Equal(CategoriaDeError.Prohibido, encargado.Error!.Categoria);
        Assert.Equal(["fuera-de-rol"], pila.Denegaciones.Motivos);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task ElReingresoSumaUnaVezConLosDatosDeLaDevolucion_ElRepetidoNoSuma_YLasUnidadesPorPedidoSumanLasSucursales()
    {
        var (lima, cusco, ayacucho) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(ayacucho, libro, 3, 0);
        using var pila = new PilaDeReservas(bd);
        var devolucion = Guid.NewGuid();
        var pedido = Guid.NewGuid();
        var entrada = new ReingresarPorDevolucionEntrada(devolucion, pedido, "ayacucho", [new(libro, 1)]);

        var primero = await pila.Ejecutar<ReingresarPorDevolucionEntrada, ReingresoRegistrado>(Encargado(ayacucho), entrada);
        var repetido = await pila.Ejecutar<ReingresarPorDevolucionEntrada, ReingresoRegistrado>(Encargado(ayacucho), entrada);
        var otraFuente = await pila.Ejecutar<ReingresarPorDevolucionEntrada, ReingresoRegistrado>(Admin(), entrada with { CodigoDeFuente = "cusco" });
        var otroPedido = await pila.Ejecutar<ReingresarPorDevolucionEntrada, ReingresoRegistrado>(Admin(), entrada with { PedidoId = Guid.NewGuid() });
        var enCusco = await pila.Ejecutar<ReingresarPorDevolucionEntrada, ReingresoRegistrado>(Admin(), new(Guid.NewGuid(), pedido, "cusco", [new(libro, 4)]));
        _ = lima;

        Assert.False(primero.Valor.YaAplicado);
        Assert.True(repetido.Valor.YaAplicado);
        Assert.Equal("idempotencia-en-conflicto", otraFuente.Error!.Clave);
        Assert.Equal("idempotencia-en-conflicto", otroPedido.Error!.Clave);
        Assert.Equal((4, 0), await bd.StockDe(ayacucho, libro));
        Assert.Equal((4, 0), await bd.StockDe(cusco, libro));
        Assert.Equal(2, pila.Auditoria.Contar("ReingresoPorDevolucion"));
        Assert.True(enCusco.EsExito);

        // La clase de lectura (con la elevación a la red) suma lo devuelto en todas las sucursales.
        var reingresadas = await pila.EnUnAmbito(
            ActorActual.DeIntegracion("operacion-de-usuario"),
            sp => Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IUnidadDeTrabajo>(sp).Ejecutar(async token =>
                await Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<BabelHub.Inventario.Application.Fuentes.IReingresosPorDevolucion>(sp).PorPedidos([pedido], token)));
        Assert.Equal([new BabelHub.Inventario.Application.Fuentes.UnidadesReingresadas(pedido, libro, 5)], reingresadas);
    }

    // ---------- T12: concurrencia ----------

    [Fact]
    [Trait("Criterio", "AC-AJU-11")]
    public async Task DosMermasDeTresSobreUnVendibleDeCinco_AplicanUnaSola_ElVendibleNuncaEsNegativo_SinInterbloqueos()
    {
        var (_, cusco, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(cusco, libro, 5, 0);
        using var pila = new PilaDeReservas(bd);
        var antes = await Interbloqueos();

        var resultados = await Simultaneas(
            () => Ajustar(pila, Encargado(cusco), Merma("cusco", libro, 3)),
            () => Ajustar(pila, Encargado(cusco), Merma("cusco", libro, 3)));

        Assert.Equal(1, resultados.Count(r => r.EsExito));
        Assert.Equal("stock-insuficiente", resultados.Single(r => !r.EsExito).Error!.Clave);
        Assert.Equal((2, 0), await bd.StockDe(cusco, libro));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes"));
        await Task.Delay(TimeSpan.FromSeconds(2));
        Assert.Equal(0L, await Interbloqueos() - antes);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-10")]
    public async Task DosIngresosSimultaneosSobreUnaParejaNueva_DejanLaCadenaCeroDiezYDiezQuince()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        using var pila = new PilaDeReservas(bd);

        var resultados = await Simultaneas(
            () => pila.Ejecutar<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado>(Admin(), Ingreso(libro, 10)),
            () => pila.Ejecutar<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado>(Admin(), Ingreso(libro, 5)));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        var cadena = resultados.Select(r => (r.Valor.FisicoAnterior, r.Valor.FisicoPosterior)).OrderBy(c => c.FisicoAnterior).ToList();
        Assert.Equal(0, cadena[0].FisicoAnterior);
        Assert.Equal(cadena[0].FisicoPosterior, cadena[1].FisicoAnterior);
        Assert.Equal((15, 0), await bd.StockDe(lima, libro));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-05")]
    public async Task DosCorreccionesDeConteoSimultaneasSobreUnaParejaSinFila_AplicanUnaYLaOtraVeElResultado()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        using var pila = new PilaDeReservas(bd);

        var resultados = await Simultaneas(
            () => Ajustar(pila, Admin(), Conteo("lima", libro, 4)),
            () => Ajustar(pila, Admin(), Conteo("lima", libro, 6)));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        var cadena = resultados.Select(r => (r.Valor.FisicoAnterior, r.Valor.FisicoPosterior)).OrderBy(c => c.FisicoAnterior).ToList();
        Assert.Equal(0, cadena[0].FisicoAnterior);
        Assert.Equal(cadena[0].FisicoPosterior, cadena[1].FisicoAnterior);
        Assert.Equal(cadena[1].FisicoPosterior, (await bd.StockDe(lima, libro))!.Value.Fisico);
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-09")]
    public async Task DosReingresosSimultaneosDeLaMismaDevolucion_SumanUnaSolaVez()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 3, 0);
        using var pila = new PilaDeReservas(bd);
        var entrada = new ReingresarPorDevolucionEntrada(Guid.NewGuid(), Guid.NewGuid(), "lima", [new(libro, 2)]);

        var resultados = await Simultaneas(
            () => pila.Ejecutar<ReingresarPorDevolucionEntrada, ReingresoRegistrado>(Admin(), entrada),
            () => pila.Ejecutar<ReingresarPorDevolucionEntrada, ReingresoRegistrado>(Admin(), entrada),
            () => pila.Ejecutar<ReingresarPorDevolucionEntrada, ReingresoRegistrado>(Admin(), entrada));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Equal(1, resultados.Count(r => !r.Valor.YaAplicado));
        Assert.Equal((5, 0), await bd.StockDe(lima, libro));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes WHERE motivo = 'devolucion'"));
    }

    [Fact]
    [Trait("Criterio", "AC-AJU-18")]
    public async Task ElMismoAjusteConLaMismaClaveEnParalelo_SeAplicaUnaSolaVez()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 20, 0);
        using var pila = new PilaDeReservas(bd);
        var usuario = Admin();
        var entrada = Merma("lima", libro, 2, "n", Clave());

        var resultados = await Simultaneas(
            () => Ajustar(pila, usuario, entrada),
            () => Ajustar(pila, usuario, entrada),
            () => Ajustar(pila, usuario, entrada),
            () => Ajustar(pila, usuario, entrada));

        Assert.All(resultados, r => Assert.True(r.EsExito, r.Error?.Clave));
        Assert.Single(resultados.Select(r => r.Valor.AjusteId).Distinct());
        Assert.Equal((18, 0), await bd.StockDe(lima, libro));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes"));
    }


    [Fact]
    [Trait("Criterio", "AC-AJU-18")]
    public async Task LaMismaClaveEnParaleloConCargasDistintas_SeAplicaUnaSolaYLasDemasRecibenElOriginalOElConflicto()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 20, 0);
        using var pila = new PilaDeReservas(bd);
        var usuario = Admin();
        var clave = Clave();
        var dos = Merma("lima", libro, 2, "n", clave);
        var tres = Merma("lima", libro, 3, "n", clave);

        var resultados = await Simultaneas(
            () => Ajustar(pila, usuario, dos),
            () => Ajustar(pila, usuario, tres),
            () => Ajustar(pila, usuario, dos),
            () => Ajustar(pila, usuario, tres));

        var exitos = resultados.Where(r => r.EsExito).ToList();
        Assert.NotEmpty(exitos);
        Assert.Single(exitos.Select(r => r.Valor.AjusteId).Distinct());
        Assert.All(resultados.Where(r => !r.EsExito), r => Assert.Equal("idempotencia-en-conflicto", r.Error!.Clave));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM ajustes.ajustes"));
        var fisico = (await bd.StockDe(lima, libro))!.Value.Fisico;
        Assert.True(fisico is 18 or 17, $"físico {fisico}");
    }
    [Fact]
    [Trait("Criterio", "AC-AJU-18")]
    public async Task LaClaveSeGuardaPorUsuarioYOperacion_ElIngresoYElAjusteConLaMismaClaveSonIndependientes()
    {
        var (lima, _, _) = await Fuentes();
        var libro = await bd.AgregarEdicion();
        await bd.PonerStock(lima, libro, 20, 0);
        using var pila = new PilaDeReservas(bd);
        var usuario = Admin();
        var clave = Clave();

        var ajuste = await Ajustar(pila, usuario, Merma("lima", libro, 2, null, clave));
        var ingreso = await pila.Ejecutar<RegistrarIngresoDeMercaderiaEntrada, IngresoRegistrado>(usuario, Ingreso(libro, 5, null, null, clave));
        var otraCarga = await Ajustar(pila, usuario, Merma("lima", libro, 3, null, clave));

        Assert.True(ajuste.EsExito && ingreso.EsExito);
        Assert.Equal("idempotencia-en-conflicto", otraCarga.Error!.Clave);
        Assert.Equal((23, 0), await bd.StockDe(lima, libro));
    }
}
