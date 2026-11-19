using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.Infrastructure.Envios;
using Npgsql;

namespace BabelHub.IntegrationTests.Envios;

/// <summary>
/// El esquema y el repositorio de Envíos sobre PostgreSQL real (T12; ENV-01, ENV-15, ENV-18, ENV-19): sin columnas de contacto, el único por línea sin abortar la transacción del llamador, el único por guía,
/// los <c>CHECK</c> y los disparadores, el permiso mínimo del rol, el bloqueo <c>FOR UPDATE</c> y la ida y vuelta de todos los campos.
/// </summary>
[Collection(ColeccionPostgresDeEnvios.Nombre)]
public sealed class RepositorioDeEnviosPostgresTests(PostgresDeEnviosFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Envio Nuevo(Guid? pedido = null, params Guid[] lineas) =>
        Envio.Crear(Guid.NewGuid(), pedido ?? Guid.NewGuid(), "lima-central", lineas.Length == 0 ? [Guid.NewGuid(), Guid.NewGuid()] : lineas, Ahora);

    private static Envio ConGuia(string numero = "GUIA-000123", string operador = "operador-a")
    {
        var envio = Nuevo();
        envio.TomarSolicitudDeGuia(Ahora, Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear(numero), operador, Ahora);
        return envio;
    }

    private Task<T> En<T>(Func<CancellationToken, Task<T>> trabajo) => bd.Unidad.Ejecutar(trabajo);

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public async Task El_esquema_no_tiene_ninguna_columna_de_contacto()
    {
        var columnas = new List<string>();
        await using var comando = bd.Administracion.CreateCommand("SELECT column_name FROM information_schema.columns WHERE table_schema = 'envios'");
        await using var lector = await comando.ExecuteReaderAsync();
        while (await lector.ReadAsync())
        {
            columnas.Add(lector.GetString(0));
        }

        Assert.NotEmpty(columnas);
        Assert.DoesNotContain(columnas, c => c.Contains("nombre", StringComparison.OrdinalIgnoreCase) || c.Contains("direccion", StringComparison.OrdinalIgnoreCase) || c.Contains("telefono", StringComparison.OrdinalIgnoreCase) || c.Contains("correo", StringComparison.OrdinalIgnoreCase) || c.Contains("contacto", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Un_envio_se_guarda_y_se_lee_con_todos_sus_campos_y_las_lineas_en_orden()
    {
        var lineas = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        var envio = Nuevo(null, lineas);
        envio.TomarSolicitudDeGuia(Ahora, Guid.NewGuid());
        envio.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", Ahora);
        envio.IniciarDespacho(Ahora.AddMinutes(1));
        envio.MarcarDespachoIncierto(Ahora.AddMinutes(2));
        envio.LiberarArrendamientoDelDespacho();
        envio.PasarAEnReparto(Ahora.AddMinutes(3));
        envio.RegistrarProgresoEnRepartoInformado(Ahora.AddMinutes(4));
        envio.MarcarEntregado(Ahora.AddHours(1));

        await En(async token => await bd.Envios.Agregar(envio, token));
        var leido = (await En(async token => await bd.Envios.Obtener(envio.Id, false, token)))!;

        Assert.Equal(envio.Id, leido.Id);
        Assert.Equal(envio.PedidoId, leido.PedidoId);
        Assert.Equal("lima-central", leido.CodigoDeFuente);
        Assert.Equal(EstadoDeEnvio.Entregado, leido.Estado);
        Assert.Equal("GUIA-000123", leido.NumeroDeGuia!.ToString());
        Assert.Equal("operador-a", leido.Operador);
        Assert.Equal(envio.ClaveDeSolicitud, leido.ClaveDeSolicitud);
        Assert.Equal(Ahora.AddMinutes(1), leido.DespachoIniciadoEn);
        Assert.Equal(Ahora.AddMinutes(2), leido.DespachoIncertoEn);
        Assert.Equal(Ahora.AddMinutes(3), leido.EnRepartoEn);
        Assert.Equal(Ahora.AddMinutes(4), leido.ProgresoEnRepartoInformadoEn);
        Assert.Equal(Ahora.AddHours(1), leido.EntregadoEn);
        Assert.Equal(lineas, leido.Lineas.Select(l => l.LineaId).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task El_unico_por_linea_se_traduce_sin_abortar_la_transaccion_del_llamador()
    {
        var comun = Guid.NewGuid();
        var primero = Nuevo(null, comun, Guid.NewGuid());
        var segundo = Nuevo(null, comun, Guid.NewGuid());
        var tercero = Nuevo();

        var resultados = await En(async token =>
        {
            var a = await bd.Envios.Agregar(primero, token);
            var b = await bd.Envios.Agregar(segundo, token); // viola el único (linea_id): se deshace solo este intento
            var c = await bd.Envios.Agregar(tercero, token); // la transacción del llamador sigue viva
            return (a, b, c);
        });

        Assert.Equal(ResultadoDeAgregadoDeEnvio.Agregado, resultados.a);
        Assert.Equal(ResultadoDeAgregadoDeEnvio.LineaRepetida, resultados.b);
        Assert.Equal(ResultadoDeAgregadoDeEnvio.Agregado, resultados.c);
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM envios.envios"));
        Assert.Equal(4L, await bd.Consultar<long>("SELECT count(*) FROM envios.envio_lineas"));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Dos_crear_simultaneos_sobre_las_mismas_lineas_dejan_un_solo_envio()
    {
        var lineas = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var pedido = Guid.NewGuid();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => En(async token => await bd.Envios.Agregar(Nuevo(pedido, lineas), token)))));

        Assert.Equal(1, resultados.Count(r => r == ResultadoDeAgregadoDeEnvio.Agregado));
        Assert.Equal(1, resultados.Count(r => r == ResultadoDeAgregadoDeEnvio.LineaRepetida));
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM envios.envios"));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-01")]
    public async Task Buscar_por_lineas_devuelve_los_envios_que_las_contienen()
    {
        var a = Nuevo();
        var b = Nuevo();
        await En(async token =>
        {
            await bd.Envios.Agregar(a, token);
            await bd.Envios.Agregar(b, token);
            return 0;
        });

        var encontrados = await En(async token => await bd.Envios.BuscarPorLineas([a.Lineas[0].LineaId, Guid.NewGuid()], token));

        Assert.Equal(a.Id, Assert.Single(encontrados).Id);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task El_mismo_numero_de_guia_de_un_operador_no_se_repite_y_se_traduce_a_guia_repetida()
    {
        var primero = ConGuia("GUIA-000123", "operador-a");
        var otro = Nuevo();
        otro.TomarSolicitudDeGuia(Ahora, Guid.NewGuid());
        otro.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-a", Ahora);
        var distintoOperador = Nuevo();
        distintoOperador.TomarSolicitudDeGuia(Ahora, Guid.NewGuid());
        distintoOperador.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000123"), "operador-b", Ahora);

        await En(async token =>
        {
            await bd.Envios.Agregar(primero, token);
            return 0;
        });

        await Assert.ThrowsAsync<GuiaRepetidaException>(() => En(async token =>
        {
            await bd.Envios.Agregar(otro, token);
            return 0;
        }));
        await En(async token =>
        {
            await bd.Envios.Agregar(distintoOperador, token);
            return 0;
        });
        Assert.Equal(2L, await bd.Consultar<long>("SELECT count(*) FROM envios.envios"));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task Guardar_un_numero_ya_usado_por_otro_envio_es_guia_repetida_y_no_un_error_de_la_base()
    {
        var uno = ConGuia("GUIA-000111");
        var dos = Nuevo();
        await En(async token =>
        {
            await bd.Envios.Agregar(uno, token);
            await bd.Envios.Agregar(dos, token);
            return 0;
        });
        dos.TomarSolicitudDeGuia(Ahora, Guid.NewGuid());
        dos.RegistrarGuia(NumeroDeGuia.Crear("GUIA-000111"), "operador-a", Ahora);

        await Assert.ThrowsAsync<GuiaRepetidaException>(() => En(async token =>
        {
            await bd.Envios.Guardar(dos, token);
            return 0;
        }));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-15")]
    public async Task El_numero_el_operador_y_la_clave_no_cambian_una_vez_fijados()
    {
        var envio = ConGuia();
        await En(async token => await bd.Envios.Agregar(envio, token));

        await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE envios.envios SET numero_de_guia = 'GUIA-999999' WHERE id = '{envio.Id}'"));
        await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE envios.envios SET operador = 'otro' WHERE id = '{envio.Id}'"));
        await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE envios.envios SET clave_de_solicitud = '{Guid.NewGuid()}' WHERE id = '{envio.Id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public async Task Los_check_rechazan_un_estado_incoherente_con_la_guia_o_las_fechas()
    {
        var envio = Nuevo();
        await En(async token => await bd.Envios.Agregar(envio, token));

        // En reparto sin guía y sin fecha.
        await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE envios.envios SET estado = 'en_reparto' WHERE id = '{envio.Id}'"));
        // Pendiente con el marcador de despacho.
        await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE envios.envios SET despacho_iniciado_en = now() WHERE id = '{envio.Id}'"));
        // Un número de guía con formato inválido.
        await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE envios.envios SET numero_de_guia = '------', operador = 'op', guia_generada_en = now(), estado = 'guia_generada' WHERE id = '{envio.Id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-03")]
    public async Task Un_envio_entregado_o_cancelado_no_cambia_de_estado()
    {
        var cancelado = Nuevo();
        cancelado.Cancelar(Ahora);
        await En(async token => await bd.Envios.Agregar(cancelado, token));

        await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE envios.envios SET estado = 'pendiente_de_guia', cancelado_en = NULL WHERE id = '{cancelado.Id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-17")]
    public async Task El_rol_de_envios_no_puede_borrar_ni_vaciar_las_tablas_ni_cambiar_el_pedido()
    {
        var envio = Nuevo();
        await En(async token => await bd.Envios.Agregar(envio, token));

        await Assert.ThrowsAsync<PostgresException>(() => ComoAcceso($"DELETE FROM envios.envios WHERE id = '{envio.Id}'"));
        await Assert.ThrowsAsync<PostgresException>(() => ComoAcceso("TRUNCATE envios.envios CASCADE"));
        await Assert.ThrowsAsync<PostgresException>(() => ComoAcceso($"UPDATE envios.envios SET pedido_id = '{Guid.NewGuid()}' WHERE id = '{envio.Id}'"));
        await Assert.ThrowsAsync<PostgresException>(() => ComoAcceso($"DELETE FROM envios.envio_lineas WHERE envio_id = '{envio.Id}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task Guardar_actualiza_el_estado_y_la_version_y_un_bloqueo_que_no_responde_es_no_disponible()
    {
        var envio = ConGuia();
        await En(async token => await bd.Envios.Agregar(envio, token));
        envio.IniciarDespacho(Ahora.AddMinutes(1));
        envio.PasarAEnReparto(Ahora.AddMinutes(2));

        await En(async token =>
        {
            await bd.Envios.Guardar(envio, token);
            return 0;
        });

        Assert.Equal("en_reparto", await bd.Consultar<string>($"SELECT estado FROM envios.envios WHERE id = '{envio.Id}'"));
        Assert.Equal(2, await bd.Consultar<int>($"SELECT version FROM envios.envios WHERE id = '{envio.Id}'"));

        // Otra conexión retiene el bloqueo de la fila: la lectura FOR UPDATE agota su lock_timeout y sale como «no disponible».
        await using var retenedor = await bd.Administracion.OpenConnectionAsync();
        await using var transaccion = await retenedor.BeginTransactionAsync();
        await using (var bloquear = new NpgsqlCommand($"SELECT 1 FROM envios.envios WHERE id = '{envio.Id}' FOR UPDATE", retenedor, transaccion))
        {
            await bloquear.ExecuteScalarAsync();
        }

        await Assert.ThrowsAsync<EnviosNoDisponiblesException>(() => En(async token => await bd.Envios.Obtener(envio.Id, true, token)));
        await transaccion.RollbackAsync();
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-19")]
    public async Task Los_envios_de_un_pedido_se_bloquean_en_orden_de_identificador()
    {
        var pedido = Guid.NewGuid();
        var envios = Enumerable.Range(0, 4).Select(_ => Nuevo(pedido)).ToList();
        await En(async token =>
        {
            foreach (var e in envios)
            {
                await bd.Envios.Agregar(e, token);
            }

            return 0;
        });

        var leidos = await En(async token => await bd.Envios.ObtenerDelPedido(pedido, true, token));

        Assert.Equal(envios.Select(e => e.Id).Order().ToArray(), leidos.Select(e => e.Id).ToArray());
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-08")]
    public async Task El_listado_pagina_filtra_por_estado_y_cuenta_el_total()
    {
        var pendientes = Enumerable.Range(0, 5).Select(i => Envio.Crear(Guid.NewGuid(), Guid.NewGuid(), "lima-central", [Guid.NewGuid()], Ahora.AddMinutes(i))).ToList();
        var conGuia = ConGuia();
        await En(async token =>
        {
            foreach (var e in pendientes)
            {
                await bd.Envios.Agregar(e, token);
            }

            await bd.Envios.Agregar(conGuia, token);
            return 0;
        });

        var pagina = await En(async token => await bd.Envios.Listar(EstadoDeEnvio.PendienteDeGuia, 2, 2, token));
        var todos = await En(async token => await bd.Envios.Listar(null, 1, 50, token));

        Assert.Equal(5, pagina.Total);
        Assert.Equal(2, pagina.Envios.Count);
        Assert.Equal(pendientes.OrderByDescending(e => e.CreadoEn).Skip(2).Take(2).Select(e => e.Id), pagina.Envios.Select(e => e.Id));
        Assert.Equal(6, todos.Total);
    }

    [Fact]
    [Trait("Criterio", "AC-ENV-07")]
    public async Task Las_consultas_de_los_trabajos_devuelven_lo_pendiente_en_orden()
    {
        var enReparto = ConGuia("GUIA-000001");
        enReparto.IniciarDespacho(Ahora);
        enReparto.PasarAEnReparto(Ahora.AddMinutes(1));
        var estancado = ConGuia("GUIA-000002");
        estancado.IniciarDespacho(Ahora.AddHours(-30));
        var cancelado = ConGuia("GUIA-000003");
        cancelado.Cancelar(Ahora.AddHours(-24).AddMinutes(-10));
        await En(async token =>
        {
            await bd.Envios.Agregar(enReparto, token);
            await bd.Envios.Agregar(estancado, token);
            await bd.Envios.Agregar(cancelado, token);
            return 0;
        });

        var pendientes = await En(async token => await bd.Envios.ObtenerConProgresoPendiente(10, token));
        var estancados = await En(async token => await bd.Envios.ObtenerConDespachoIniciadoAntesDe(Ahora.AddHours(-24), 10, token));
        var sinUso = await En(async token => await bd.Envios.ObtenerCanceladosConGuiaEntre(Ahora.AddHours(-25), Ahora.AddHours(-24), 10, token));

        Assert.Equal(enReparto.Id, Assert.Single(pendientes).Id);
        Assert.Equal(estancado.Id, Assert.Single(estancados).Id);
        Assert.Equal(cancelado.Id, Assert.Single(sinUso).Id);
    }

    private async Task ComoAcceso(string sql)
    {
        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var rol = new NpgsqlCommand("SET LOCAL ROLE \"rol_envios\"", conexion, transaccion))
        {
            await rol.ExecuteNonQueryAsync();
        }

        await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
        await comando.ExecuteNonQueryAsync();
        await transaccion.CommitAsync();
    }
}
