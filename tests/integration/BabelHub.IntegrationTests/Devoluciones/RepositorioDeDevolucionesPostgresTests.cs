using System.Text.Json;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.Infrastructure.Devoluciones;
using Npgsql;

namespace BabelHub.IntegrationTests.Devoluciones;

/// <summary>
/// El esquema y el repositorio de Devoluciones sobre PostgreSQL real (T19 y T21; DEV-01, DEV-03, DEV-16, DEV-24, DEV-27): sin columna de cuenta, la máquina de estados y la redacción protegidas por disparadores, el cupo atómico en orden de línea,
/// el bloqueo por devolución con <c>SKIP LOCKED</c>, las consultas de candidatas de los trabajos, el permiso mínimo del rol y que un rechazo de la base nunca repita el motivo ni el texto.
/// </summary>
[Collection(ColeccionPostgresDeDevoluciones.Nombre)]
public sealed class RepositorioDeDevolucionesPostgresTests(PostgresDeDevolucionesFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    private static readonly Guid Personal = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private Task<T> En<T>(Func<CancellationToken, Task<T>> trabajo) => bd.Unidad.Ejecutar(trabajo);

    private static Devolucion Impreso(Guid? pedido = null, string sucursal = "lima-central", int lineas = 2, DateTimeOffset? creada = null, Guid? cliente = null, string motivo = "MOTIVO-SEMBRADO") =>
        Devolucion.Solicitar(
            pedido ?? Guid.NewGuid(),
            TipoDeDevolucion.Impreso,
            OrigenDeDevolucion.Solicitud,
            sucursal,
            TextoRedactado.Crear(motivo, 300)!,
            null,
            [.. Enumerable.Range(0, lineas).Select(i => new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), i + 1, 12.5m))],
            creada ?? Ahora,
            ResponsableDelCambio.Cliente(cliente ?? Guid.NewGuid()));

    private static Devolucion Reporte(Guid? pedido = null) =>
        Devolucion.Solicitar(
            pedido ?? Guid.NewGuid(),
            TipoDeDevolucion.Digital,
            OrigenDeDevolucion.Reporte,
            null,
            TextoRedactado.Crear("MOTIVO-SEMBRADO", 300)!,
            TextoRedactado.Crear("TEXTO-SEMBRADO", 2000)!,
            [new NuevaLineaDeDevolucion(Guid.NewGuid(), Guid.NewGuid(), 1, 30m, Guid.NewGuid())],
            Ahora,
            ResponsableDelCambio.Cliente(Guid.NewGuid()));

    private Task Agregar(Devolucion d) => En(async token =>
    {
        await bd.Devoluciones.Agregar(d, token);
        return 0;
    });

    private Task Guardar(Devolucion d) => En(async token =>
    {
        await bd.Devoluciones.Guardar(d, token);
        return 0;
    });

    private Task<Devolucion?> Obtener(Guid id) => En(token => bd.Devoluciones.Obtener(id, false, token));

    // ---------- El esquema ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public async Task El_esquema_no_tiene_ninguna_columna_de_cuenta_ni_de_usuario()
    {
        var columnas = new List<string>();
        await using var comando = bd.Administracion.CreateCommand("SELECT column_name FROM information_schema.columns WHERE table_schema = 'devoluciones'");
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            columnas.Add(lector.GetString(0));
        }

        Assert.NotEmpty(columnas);
        Assert.DoesNotContain(columnas, c => c.Contains("cuenta", StringComparison.Ordinal) || c.Contains("usuario", StringComparison.Ordinal) || c.Contains("cliente_id", StringComparison.Ordinal));
        // El único identificador de una persona es el del responsable de un cambio.
        Assert.Contains("responsable_id", columnas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Agregar_y_obtener_devuelven_todos_los_campos_y_el_historial_de_cada_linea()
    {
        var cliente = Guid.NewGuid();
        var devolucion = Impreso(cliente: cliente);
        await Agregar(devolucion);

        var leida = await Obtener(devolucion.Id);

        Assert.NotNull(leida);
        Assert.Equal(devolucion.PedidoId, leida.PedidoId);
        Assert.Equal(TipoDeDevolucion.Impreso, leida.Tipo);
        Assert.Equal(OrigenDeDevolucion.Solicitud, leida.Origen);
        Assert.Equal("lima-central", leida.CodigoDeSucursalReceptora);
        Assert.Equal("MOTIVO-SEMBRADO", leida.Motivo.Valor);
        Assert.False(leida.Motivo.EstaRedactado);
        Assert.Null(leida.Texto);
        Assert.Equal(devolucion.CreadaEn, leida.CreadaEn);
        Assert.Equal(2, leida.Lineas.Count);
        foreach (var original in devolucion.Lineas)
        {
            var linea = leida.Lineas.Single(l => l.LineaDePedidoId == original.LineaDePedidoId);
            Assert.Equal((original.Id, original.EdicionId, original.Cantidad, original.PrecioUnitario, original.Monto, original.Estado), (linea.Id, linea.EdicionId, linea.Cantidad, linea.PrecioUnitario, linea.Monto, linea.Estado));
            Assert.Equal(original.Cambios, linea.Cambios);
            Assert.Equal(ResponsableDelCambio.Cliente(cliente), linea.Cambios[0].Responsable);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-01")]
    public async Task Un_reporte_guarda_su_texto_y_la_referencia_al_acceso_y_un_digital_no_tiene_sucursal()
    {
        var reporte = Reporte();
        await Agregar(reporte);

        var leido = await Obtener(reporte.Id);

        Assert.Equal("TEXTO-SEMBRADO", leido!.Texto!.Valor);
        Assert.Null(leido.CodigoDeSucursalReceptora);
        Assert.Equal(reporte.Lineas[0].AccesoId, leido.Lineas[0].AccesoId);
        Assert.Equal(OrigenDeDevolucion.Reporte, leido.Origen);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Guardar_persiste_aprobada_recibida_y_reembolsada_con_su_historial_y_los_marcadores_de_reingreso()
    {
        var devolucion = Impreso();
        await Agregar(devolucion);
        var d = (await Obtener(devolucion.Id))!;
        d.Aprobar(Ahora.AddHours(1), ResponsableDelCambio.Personal(Personal));
        d.IniciarReingreso(Ahora.AddHours(2));
        d.MarcarReingresoIncierto(Ahora.AddHours(2));
        d.ConfirmarReingreso(Ahora.AddHours(3));
        d.Recibir(Ahora.AddHours(4), ResponsableDelCambio.Personal(Personal));
        var primera = d.Lineas[0].LineaDePedidoId;
        d.RegistrarReembolsoDeLinea(primera, Ahora.AddHours(4));
        d.Reembolsar(primera, Ahora.AddHours(5), ResponsableDelCambio.Sistema);
        d.RegistrarReembolsoDeLinea(d.Lineas[1].LineaDePedidoId, Ahora.AddHours(4));
        await Guardar(d);

        var leida = (await Obtener(devolucion.Id))!;

        Assert.Equal(Ahora.AddHours(2), leida.ReingresoIniciadoEn);
        Assert.Equal(Ahora.AddHours(2), leida.ReingresoIncertoEn);
        Assert.Equal(Ahora.AddHours(3), leida.ReingresoConfirmadoEn);
        var uno = leida.Lineas.Single(l => l.LineaDePedidoId == primera);
        Assert.Equal(EstadoDeLineaDeDevolucion.Reembolsada, uno.Estado);
        Assert.Equal(Ahora.AddHours(5), uno.ReembolsadaEn);
        Assert.Equal(4, uno.Cambios.Count);
        Assert.Equal(ResponsableDelCambio.Sistema, uno.Cambios[^1].Responsable);
        Assert.Equal(ResponsableDelCambio.Personal(Personal), uno.Cambios[1].Responsable);
        var otra = leida.Lineas.Single(l => l.LineaDePedidoId != primera);
        Assert.Equal(EstadoDeLineaDeDevolucion.Recibida, otra.Estado);
        Assert.Equal(Ahora.AddHours(4), otra.ReembolsoRegistradoEn);
        Assert.Equal(Ahora.AddHours(1), otra.AprobadaEn);
    }

    // ---------- Los disparadores y las restricciones ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task Una_linea_no_retrocede_ni_sale_de_un_estado_final_aunque_el_codigo_se_equivoque()
    {
        var devolucion = Impreso();
        await Agregar(devolucion);
        var rechazada = devolucion.Lineas[0].Id;
        var aprobada = devolucion.Lineas[1].Id;

        await bd.Ejecutar($"UPDATE devoluciones.lineas_de_devolucion SET estado = 'rechazada', estado_en = now() WHERE id = '{rechazada}'");
        await bd.Ejecutar($"UPDATE devoluciones.lineas_de_devolucion SET estado = 'aprobada', estado_en = now(), aprobada_en = now() WHERE id = '{aprobada}'");
        await bd.Ejecutar($"UPDATE devoluciones.lineas_de_devolucion SET estado = 'recibida', estado_en = now() WHERE id = '{aprobada}'");
        var desdeFinal = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE devoluciones.lineas_de_devolucion SET estado = 'aprobada' WHERE id = '{rechazada}'"));
        var retroceso = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE devoluciones.lineas_de_devolucion SET estado = 'aprobada' WHERE id = '{aprobada}'"));
        var aRechazada = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE devoluciones.lineas_de_devolucion SET estado = 'rechazada' WHERE id = '{aprobada}'"));

        Assert.All(new[] { desdeFinal.SqlState, retroceso.SqlState, aRechazada.SqlState }, e => Assert.Equal("23514", e));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-16")]
    public async Task El_marcador_de_reingreso_solo_se_limpia_sin_intento_incierto_ni_confirmacion()
    {
        var limpio = Impreso();
        var incierto = Impreso();
        foreach (var d in new[] { limpio, incierto })
        {
            d.Aprobar(Ahora.AddHours(1), ResponsableDelCambio.Personal(Personal));
            d.IniciarReingreso(Ahora.AddHours(2));
        }

        incierto.MarcarReingresoIncierto(Ahora.AddHours(2));
        await Agregar(limpio);
        await Agregar(incierto);
        await bd.Ejecutar($"UPDATE devoluciones.devoluciones SET reingreso_iniciado_en = NULL WHERE id = '{limpio.Id}'");
        var borrado = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE devoluciones.devoluciones SET reingreso_iniciado_en = NULL WHERE id = '{incierto.Id}'"));
        var incertoBorrado = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE devoluciones.devoluciones SET reingreso_incierto_en = NULL WHERE id = '{incierto.Id}'"));

        Assert.Equal("23514", borrado.SqlState);
        Assert.Equal("23514", incertoBorrado.SqlState);
        Assert.Null(await bd.Consultar<DateTime?>($"SELECT reingreso_iniciado_en FROM devoluciones.devoluciones WHERE id = '{limpio.Id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public async Task La_redaccion_reemplaza_el_texto_quita_al_cliente_y_el_acceso_y_nada_mas_se_puede_cambiar_en_el_motivo_ni_en_el_historial()
    {
        var cliente = Guid.NewGuid();
        var reporte = Reporte();
        var impreso = Impreso(cliente: cliente);
        await Agregar(reporte);
        await Agregar(impreso);
        impreso.Aprobar(Ahora.AddHours(1), ResponsableDelCambio.Personal(Personal));
        await Guardar(impreso);

        var d = (await Obtener(reporte.Id))!;
        d.Redactar(Ahora.AddDays(1), quitarElAcceso: true);
        await Guardar(d);
        var i = (await Obtener(impreso.Id))!;
        i.Redactar(Ahora.AddDays(1), quitarElAcceso: true);
        await Guardar(i);
        var leidoReporte = (await Obtener(reporte.Id))!;
        var leidoImpreso = (await Obtener(impreso.Id))!;

        Assert.True(leidoReporte.Motivo.EstaRedactado);
        Assert.Equal(TextoRedactado.Redactado, leidoReporte.Texto!.Valor);
        Assert.Null(leidoReporte.Lineas[0].AccesoId);
        Assert.DoesNotContain(leidoImpreso.Lineas.SelectMany(l => l.Cambios), c => c.Responsable is { Tipo: TipoDeResponsable.Cliente, Id: not null });
        Assert.Equal(ResponsableDelCambio.Personal(Personal), leidoImpreso.Lineas[0].Cambios[1].Responsable);
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM devoluciones.devoluciones WHERE motivo LIKE '%SEMBRADO%' OR texto LIKE '%SEMBRADO%'"));
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM devoluciones.cambios_de_linea WHERE responsable_id = '{cliente}'"));
        var motivo = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE devoluciones.devoluciones SET motivo = 'otro' WHERE id = '{impreso.Id}'"));
        var personal = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE devoluciones.cambios_de_linea SET responsable_id = NULL WHERE responsable_tipo = 'personal'"));
        var fecha = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE devoluciones.devoluciones SET redactada_en = now() WHERE id = '{impreso.Id}'"));
        Assert.All(new[] { motivo.SqlState, personal.SqlState, fecha.SqlState }, e => Assert.Equal("23514", e));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-31")]
    public async Task Un_rechazo_de_la_base_no_repite_el_motivo_ni_el_texto_ni_el_detalle_de_la_fila()
    {
        // Una devolución con fecha de redacción pero con el motivo sin redactar rompe la restricción: el Detail de PostgreSQL traería la fila completa.
        var rota = Devolucion.Rehidratar(
            Guid.NewGuid(),
            Guid.NewGuid(),
            TipoDeDevolucion.Digital,
            OrigenDeDevolucion.Reporte,
            null,
            TextoRedactado.Rehidratar("MOTIVO-SEMBRADO", redactado: false),
            TextoRedactado.Rehidratar("TEXTO-SEMBRADO", redactado: false),
            Ahora,
            Ahora.AddDays(1),
            null,
            null,
            null,
            [new LineaRehidratada(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1, 10m, 10m, null, [new CambioDeEstado(EstadoDeLineaDeDevolucion.Solicitada, Ahora, ResponsableDelCambio.Sistema)], null)]);

        var falla = await Assert.ThrowsAsync<FallaDeLaBaseDeDevolucionesException>(() => Agregar(rota));

        Assert.Equal("23514", falla.CodigoDeEstado);
        Assert.Equal("devoluciones_redactada_con_valor_fijo", falla.Restriccion);
        Assert.DoesNotContain("SEMBRADO", falla.Message + JsonSerializer.Serialize(falla.Data), StringComparison.Ordinal);
        Assert.Null(falla.InnerException);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-26")]
    public async Task El_rol_de_la_aplicacion_no_puede_borrar_ni_cambiar_el_pedido_el_tipo_o_el_motivo_original()
    {
        var devolucion = Impreso();
        await Agregar(devolucion);

        var borrar = await Assert.ThrowsAsync<FallaDeLaBaseDeDevolucionesException>(() => bd.AccesoAlEsquema.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("DELETE FROM devoluciones.devoluciones", conexion, transaccion);
                return await comando.ExecuteNonQueryAsync(token);
            },
            CancellationToken.None));
        var cambiar = await Assert.ThrowsAsync<FallaDeLaBaseDeDevolucionesException>(() => bd.AccesoAlEsquema.Ejecutar(
            async (conexion, transaccion, token) =>
            {
                await using var comando = new NpgsqlCommand("UPDATE devoluciones.devoluciones SET pedido_id = gen_random_uuid()", conexion, transaccion);
                return await comando.ExecuteNonQueryAsync(token);
            },
            CancellationToken.None));

        Assert.Equal("42501", borrar.CodigoDeEstado);
        Assert.Equal("42501", cambiar.CodigoDeEstado);
        Assert.NotNull(await Obtener(devolucion.Id));
    }

    // ---------- El cupo (DEV-03) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-03")]
    public async Task Veinte_pares_de_solicitudes_simultaneas_sobre_la_misma_unidad_dejan_pasar_solo_a_una()
    {
        for (var par = 0; par < 20; par++)
        {
            var linea = Guid.NewGuid();
            var pedido = Guid.NewGuid();

            var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => En(token => bd.Devoluciones.TomarCupos([new CupoPedido(linea, pedido, 3, 2)], token)))));

            Assert.Equal(1, resultados.Count(r => r is null));
            Assert.Equal(1, resultados.Count(r => r == linea));
            Assert.Equal(2, await bd.Consultar<int>($"SELECT cantidad_comprometida FROM devoluciones.cupos_de_linea WHERE linea_id = '{linea}'"));
        }
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-03")]
    public async Task Si_una_linea_no_alcanza_la_transaccion_del_llamador_revierte_lo_comprometido_en_las_anteriores()
    {
        var a = Guid.Parse("00000000-0000-0000-0000-00000000000a");
        var b = Guid.Parse("00000000-0000-0000-0000-00000000000b");
        var pedido = Guid.NewGuid();
        await En(token => bd.Devoluciones.TomarCupos([new CupoPedido(b, pedido, 1, 1)], token));

        Guid? faltante = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() => En<int>(async token =>
        {
            faltante = await bd.Devoluciones.TomarCupos([new CupoPedido(b, pedido, 1, 1), new CupoPedido(a, pedido, 2, 2)], token);
            throw new InvalidOperationException("el llamador revierte");
        }));

        Assert.Equal(b, faltante);
        Assert.Equal(0L, await bd.Consultar<long>($"SELECT count(*) FROM devoluciones.cupos_de_linea WHERE linea_id = '{a}'"));
        Assert.Equal(1, await bd.Consultar<int>($"SELECT cantidad_comprometida FROM devoluciones.cupos_de_linea WHERE linea_id = '{b}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-03")]
    public async Task Devolver_un_cupo_lo_libera_en_orden_de_linea_y_nunca_baja_de_cero()
    {
        var linea = Guid.NewGuid();
        await En(token => bd.Devoluciones.TomarCupos([new CupoPedido(linea, Guid.NewGuid(), 3, 2)], token));

        await En(async token =>
        {
            await bd.Devoluciones.DevolverCupos([(linea, 1)], token);
            return 0;
        });
        var libre = await En(token => bd.Devoluciones.TomarCupos([new CupoPedido(linea, Guid.NewGuid(), 3, 2)], token));
        await En(async token =>
        {
            await bd.Devoluciones.DevolverCupos([(linea, 9)], token);
            return 0;
        });

        Assert.Null(libre);
        Assert.Equal(0, await bd.Consultar<int>($"SELECT cantidad_comprometida FROM devoluciones.cupos_de_linea WHERE linea_id = '{linea}'"));
    }

    // ---------- El bloqueo por devolución (DEV-16) ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-15")]
    public async Task Skip_locked_salta_la_devolucion_que_otra_transaccion_tiene_bloqueada_y_la_toma_al_liberarse()
    {
        var devolucion = Impreso();
        await Agregar(devolucion);
        var tomada = new TaskCompletionSource();
        var soltar = new TaskCompletionSource();
        var primera = Task.Run(() => En(async token =>
        {
            await bd.Devoluciones.Obtener(devolucion.Id, true, token);
            tomada.SetResult();
            await soltar.Task;
            return 0;
        }));
        await tomada.Task;

        var mientras = await En(token => bd.Devoluciones.ObtenerParaActualizarSinEsperar(devolucion.Id, token));
        soltar.SetResult();
        await primera;
        var despues = await En(token => bd.Devoluciones.ObtenerParaActualizarSinEsperar(devolucion.Id, token));
        var inexistente = await En(token => bd.Devoluciones.ObtenerParaActualizarSinEsperar(Guid.NewGuid(), token));

        Assert.Null(mientras);
        Assert.NotNull(despues);
        Assert.Null(inexistente);
    }

    // ---------- Las consultas ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-12")]
    public async Task Los_listados_filtran_por_pedido_sucursal_y_tipo_y_paginan_de_la_mas_reciente_a_la_mas_antigua()
    {
        var pedido = Guid.NewGuid();
        var antigua = Impreso(pedido, creada: Ahora.AddDays(-2));
        var reciente = Impreso(pedido, "arequipa", creada: Ahora);
        var ajena = Impreso();
        var reporte = Reporte();
        foreach (var d in new[] { antigua, reciente, ajena, reporte })
        {
            await Agregar(d);
        }

        var delPedido = await En(token => bd.Devoluciones.ListarDePedidos([pedido], 1, 1, token));
        var segundaPagina = await En(token => bd.Devoluciones.ListarDePedidos([pedido], 2, 1, token));
        var deLima = await En(token => bd.Devoluciones.ListarDeLaSucursal("lima-central", 1, 10, token));
        var soloReportes = await En(token => bd.Devoluciones.ListarDeLaAdministracion(new FiltroDeLaAdministracion(null, true), 1, 10, token));
        var digitales = await En(token => bd.Devoluciones.ListarDeLaAdministracion(new FiltroDeLaAdministracion(TipoDeDevolucion.Digital, false), 1, 10, token));
        var todas = await En(token => bd.Devoluciones.ListarDeLaAdministracion(new FiltroDeLaAdministracion(null, false), 1, 10, token));

        Assert.Equal(2, delPedido.Total);
        Assert.Equal(reciente.Id, Assert.Single(delPedido.Devoluciones).Id);
        Assert.Equal(antigua.Id, Assert.Single(segundaPagina.Devoluciones).Id);
        Assert.Equal(new[] { antigua.Id, ajena.Id }.Order(), deLima.Devoluciones.Select(d => d.Id).Order());
        Assert.DoesNotContain(deLima.Devoluciones, d => d.Tipo != TipoDeDevolucion.Impreso || d.CodigoDeSucursalReceptora != "lima-central");
        Assert.Equal(reporte.Id, Assert.Single(soloReportes.Devoluciones).Id);
        Assert.Equal(reporte.Id, Assert.Single(digitales.Devoluciones).Id);
        Assert.Equal(4, todas.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public async Task Las_candidatas_de_la_revision_de_plazos_son_las_aprobadas_sin_marcador_de_hace_mas_de_lo_pedido()
    {
        var vieja = Impreso(creada: Ahora.AddDays(-30));
        var conMarcador = Impreso(creada: Ahora.AddDays(-30));
        var reciente = Impreso(creada: Ahora.AddDays(-30));
        var mixta = Impreso(creada: Ahora.AddDays(-30));
        foreach (var d in new[] { vieja, conMarcador, reciente, mixta })
        {
            await Agregar(d);
        }

        foreach (var (d, dias) in new[] { (vieja, 12), (conMarcador, 12), (reciente, 3) })
        {
            var guardada = (await Obtener(d.Id))!;
            guardada.Aprobar(Ahora.AddDays(-dias), ResponsableDelCambio.Personal(Personal));
            if (d == conMarcador)
            {
                guardada.IniciarReingreso(Ahora.AddDays(-11));
            }

            await Guardar(guardada);
        }

        var candidatas = await En(token => bd.Devoluciones.ObtenerConRecepcionVencida(Ahora.AddDays(-10), 50, token));
        var estancadas = await En(token => bd.Devoluciones.ObtenerConReingresoEstancado(Ahora.AddHours(-24), 50, token));

        Assert.Equal([vieja.Id], candidatas);
        Assert.Equal([conMarcador.Id], estancadas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-29")]
    public async Task Las_candidatas_de_la_depuracion_tienen_todas_las_lineas_finales_sin_redactar_y_el_ultimo_cambio_antes_de_la_cota()
    {
        var final = Impreso(creada: Ahora.AddYears(-3));
        var abierta = Impreso(creada: Ahora.AddYears(-3));
        var reciente = Impreso(creada: Ahora);
        var yaRedactada = Impreso(creada: Ahora.AddYears(-3));
        foreach (var d in new[] { final, abierta, reciente, yaRedactada })
        {
            await Agregar(d);
        }

        foreach (var d in new[] { final, reciente, yaRedactada })
        {
            var guardada = (await Obtener(d.Id))!;
            guardada.Rechazar(d.CreadaEn.AddHours(1), ResponsableDelCambio.Personal(Personal));
            await Guardar(guardada);
        }

        var redactada = (await Obtener(yaRedactada.Id))!;
        redactada.Redactar(Ahora, quitarElAcceso: false);
        await Guardar(redactada);

        var candidatas = await En(token => bd.Devoluciones.ObtenerVencidasParaDepurar(Ahora.AddYears(-2), 50, token));

        Assert.Equal([final.Id], candidatas);
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-20")]
    public async Task Los_reembolsos_pendientes_son_las_lineas_recibidas_de_un_impreso_y_aprobadas_de_un_digital()
    {
        var impreso = Impreso(lineas: 1);
        await Agregar(impreso);
        var d = (await Obtener(impreso.Id))!;
        d.Aprobar(Ahora.AddHours(1), ResponsableDelCambio.Personal(Personal));
        d.IniciarReingreso(Ahora.AddHours(2));
        d.ConfirmarReingreso(Ahora.AddHours(2));
        d.Recibir(Ahora.AddHours(3), ResponsableDelCambio.Personal(Personal));
        await Guardar(d);
        var aprobadoDigital = Reporte();
        await Agregar(aprobadoDigital);
        var r = (await Obtener(aprobadoDigital.Id))!;
        r.Aprobar(Ahora.AddHours(1), ResponsableDelCambio.Personal(Personal));
        await Guardar(r);
        await Agregar(Impreso());

        var pendientes = await En(token => bd.Devoluciones.ObtenerConReembolsoPendiente(50, token));

        Assert.Equal(2, pendientes.Count);
        Assert.Contains((impreso.Id, impreso.Lineas[0].LineaDePedidoId), pendientes);
        Assert.Contains((aprobadoDigital.Id, aprobadoDigital.Lineas[0].LineaDePedidoId), pendientes);
    }
}
