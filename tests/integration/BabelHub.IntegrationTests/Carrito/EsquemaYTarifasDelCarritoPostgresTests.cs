using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.Infrastructure.Carrito;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using IRepositorioDeTarifasDeEnvio = BabelHub.Ecommerce.Application.Carrito.Interno.IRepositorioDeTarifasDeEnvio;

namespace BabelHub.IntegrationTests.Carrito;

/// <summary>
/// T44: el esquema <c>carrito</c> de la venta por sucursal sobre PostgreSQL real: la migración repetible desde el anterior, los permisos por columna, las restricciones, las tarifas de envío y sus efectos en el paso al pago y en las compras
/// anteriores.
/// </summary>
[Collection(ColeccionPostgresDelCarrito.Nombre)]
public sealed class EsquemaYTarifasDelCarritoPostgresTests(PostgresDelCarritoFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private RepositorioDeTarifasDeEnvioPostgres Tarifas => new(bd.AccesoAlEsquema);

    private Task<CostoDeEnvio?> Fijar(string sucursal, decimal importe) =>
        bd.Unidad.Ejecutar(token => Tarifas.Fijar(sucursal, CostoDeEnvio.Crear(importe), Ahora, token));

    // ---------- La migración ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-39")]
    public async Task Una_base_anterior_se_migra_dos_veces_y_conserva_sus_lineas_sin_sucursal_con_identidad_estable()
    {
        var nombre = "anterior_" + Guid.NewGuid().ToString("N");
        await bd.Ejecutar($"CREATE DATABASE {nombre}");
        var cadena = new NpgsqlConnectionStringBuilder(bd.CadenaDeAdministracion) { Database = nombre, Pooling = false }.ConnectionString;
        await using var anterior = NpgsqlDataSource.Create(cadena);

        async Task Ejecutar(string sql)
        {
            await using var comando = anterior.CreateCommand(sql);
            await comando.ExecuteNonQueryAsync();
        }

        async Task<List<string>> Filas(string sql)
        {
            await using var comando = anterior.CreateCommand(sql);
            await using var lector = await comando.ExecuteReaderAsync();
            var filas = new List<string>();
            while (await lector.ReadAsync())
            {
                filas.Add(lector.GetString(0));
            }

            return filas;
        }

        await Ejecutar(EsquemaAnteriorDelCarrito.Sql + EsquemaAnteriorDelCarrito.Permisos);
        var (carrito, checkout, cuenta, impreso, digital, lineaDelCheckout) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await Ejecutar(
            $"""
            INSERT INTO carrito.carritos (id, cuenta_id, estado, version, creado_en, modificado_en) VALUES ('{carrito}', '{cuenta}', 'abierto', 1, now(), now());
            INSERT INTO carrito.lineas_de_carrito (carrito_id, edicion_id, posicion, cantidad, es_digital, agregada_en) VALUES
              ('{carrito}', '{impreso}', 0, 3, false, now()), ('{carrito}', '{digital}', 1, 1, true, now());
            INSERT INTO carrito.checkouts (id, cuenta_id, carrito_id, estado, paso, version, tipo_de_entrega, codigo_de_sucursal_de_recojo, creado_en, modificado_en)
              VALUES ('{checkout}', '{cuenta}', '{carrito}', 'pago_pendiente', 3, 1, 'recojo', 'sede-central', now(), now());
            INSERT INTO carrito.lineas_del_checkout (checkout_id, edicion_id, posicion, linea_id, es_digital, titulo, isbn, cantidad, precio_unitario)
              VALUES ('{checkout}', '{impreso}', 0, '{lineaDelCheckout}', false, 'Libro', '9780307474728', 3, 40);
            """);

        const string LasLineas = "SELECT concat_ws('|', edicion_id, linea_id, coalesce(codigo_de_sucursal, 'sin'), cantidad, es_digital) FROM carrito.lineas_de_carrito ORDER BY posicion";

        await Ejecutar(DefinicionDelCarrito.Sql + DefinicionDelCarrito.Permisos);
        var primera = await Filas(LasLineas);
        await Ejecutar(DefinicionDelCarrito.Sql + DefinicionDelCarrito.Permisos);
        var segunda = await Filas(LasLineas);

        Assert.Equal(primera, segunda);
        Assert.Equal(2, segunda.Count);
        Assert.StartsWith($"{impreso}|", segunda[0], StringComparison.Ordinal);
        Assert.EndsWith("|sin|3|f", segunda[0], StringComparison.Ordinal);
        Assert.EndsWith("|sin|1|t", segunda[1], StringComparison.Ordinal);
        Assert.Equal(2, segunda.Select(f => f.Split('|')[1]).Distinct().Count());
        Assert.Equal(
            [$"{lineaDelCheckout}|sin|sin"],
            await Filas("SELECT concat_ws('|', linea_id, coalesce(linea_de_carrito_id::text, 'sin'), coalesce(codigo_de_sucursal, 'sin')) FROM carrito.lineas_del_checkout"));
        Assert.Equal(
            ["lineas_de_carrito|carrito_id,linea_id", "lineas_del_checkout|checkout_id,linea_id"],
            await Filas(
                """
                SELECT c.relname || '|' || string_agg(a.attname, ',' ORDER BY array_position(i.indkey::int[], a.attnum::int))
                FROM pg_index i JOIN pg_class c ON c.oid = i.indrelid JOIN pg_namespace n ON n.oid = c.relnamespace
                  JOIN pg_attribute a ON a.attrelid = c.oid AND a.attnum = ANY (i.indkey)
                WHERE i.indisprimary AND n.nspname = 'carrito' AND c.relname IN ('lineas_de_carrito', 'lineas_del_checkout')
                GROUP BY c.relname ORDER BY c.relname
                """));

        // Ya migrada, admite el mismo libro en una sucursal junto a la línea anterior, y una sola línea digital por libro.
        await Ejecutar($"INSERT INTO carrito.lineas_de_carrito (carrito_id, linea_id, edicion_id, codigo_de_sucursal, posicion, cantidad, es_digital, agregada_en) VALUES ('{carrito}', gen_random_uuid(), '{impreso}', 'cusco', 2, 2, false, now())");
        var repetida = await Assert.ThrowsAsync<PostgresException>(() => Ejecutar($"INSERT INTO carrito.lineas_de_carrito (carrito_id, linea_id, edicion_id, posicion, cantidad, es_digital, agregada_en) VALUES ('{carrito}', gen_random_uuid(), '{digital}', 3, 1, true, now())"));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, repetida.SqlState);
        Assert.Empty(await Filas("SELECT codigo_de_sucursal FROM carrito.tarifas_de_envio"));
    }

    // ---------- Permisos y restricciones ----------

    private async Task<string?> ComoElRolDelCarrito(string sql)
    {
        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using var transaccion = await conexion.BeginTransactionAsync();
        await using (var rol = new NpgsqlCommand("SET LOCAL ROLE rol_carrito", conexion, transaccion))
        {
            await rol.ExecuteNonQueryAsync();
        }

        try
        {
            await using var comando = new NpgsqlCommand(sql, conexion, transaccion);
            await comando.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException excepcion)
        {
            return excepcion.SqlState;
        }
    }

    [Theory]
    [InlineData("UPDATE carrito.lineas_de_carrito SET codigo_de_sucursal = 'otra'")]
    [InlineData("UPDATE carrito.lineas_de_carrito SET linea_id = gen_random_uuid()")]
    [InlineData("UPDATE carrito.lineas_de_carrito SET edicion_id = gen_random_uuid()")]
    [InlineData("UPDATE carrito.lineas_del_checkout SET codigo_de_sucursal = 'otra'")]
    [InlineData("UPDATE carrito.tarifas_de_envio SET codigo_de_sucursal = 'otra'")]
    [InlineData("DELETE FROM carrito.tarifas_de_envio")]
    [InlineData("TRUNCATE carrito.tarifas_de_envio")]
    [Trait("Criterio", "AC-CAR-39")]
    public async Task El_rol_del_carrito_no_cambia_la_identidad_ni_la_sucursal_de_una_linea_ni_borra_tarifas(string sql) =>
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, await ComoElRolDelCarrito(sql));

    [Theory]
    [InlineData("INSERT INTO carrito.tarifas_de_envio VALUES ('cusco', -0.01, now())")]
    [InlineData("INSERT INTO carrito.tarifas_de_envio VALUES ('cusco', 500.01, now())")]
    [InlineData("INSERT INTO carrito.tarifas_de_envio VALUES ('', 10, now())")]
    [InlineData("INSERT INTO carrito.tarifas_de_envio VALUES (repeat('x', 41), 10, now())")]
    [Trait("Criterio", "AC-CAR-41")]
    public async Task Una_tarifa_fuera_de_rango_o_con_un_codigo_invalido_la_rechaza_la_base(string sql) =>
        Assert.Equal(PostgresErrorCodes.CheckViolation, await ComoElRolDelCarrito(sql));

    [Fact]
    [Trait("Criterio", "AC-CAR-35")]
    public async Task Una_linea_digital_no_lleva_sucursal_y_la_de_un_impreso_tiene_de_1_a_40_caracteres()
    {
        var carrito = Guid.NewGuid();
        await bd.Ejecutar($"INSERT INTO carrito.carritos (id, cuenta_id, estado, version, creado_en, modificado_en) VALUES ('{carrito}', gen_random_uuid(), 'abierto', 1, now(), now())");

        Task<string?> Insertar(string sucursal, bool digital) =>
            ComoElRolDelCarrito($"INSERT INTO carrito.lineas_de_carrito (carrito_id, linea_id, edicion_id, codigo_de_sucursal, posicion, cantidad, es_digital, agregada_en) VALUES ('{carrito}', gen_random_uuid(), gen_random_uuid(), {sucursal}, 0, 1, {(digital ? "true" : "false")}, now())");

        Assert.Equal(PostgresErrorCodes.CheckViolation, await Insertar("'cusco'", digital: true));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await Insertar("''", digital: false));
        Assert.Equal(PostgresErrorCodes.CheckViolation, await Insertar("repeat('x', 41)", digital: false));
        Assert.Null(await Insertar("repeat('x', 40)", digital: false));
        Assert.Null(await Insertar("NULL", digital: true));
    }

    // ---------- Las tarifas ----------

    [Fact]
    [Trait("Criterio", "AC-CAR-40")]
    public async Task Fijar_una_tarifa_devuelve_la_que_reemplaza_y_leer_trae_solo_las_propias()
    {
        Assert.Empty(await bd.Unidad.Ejecutar(token => Tarifas.Leer(token)));

        Assert.Null(await Fijar("cusco", 18.50m));
        Assert.Equal(18.50m, (await Fijar("cusco", 0m))!.Valor);
        Assert.Null(await Fijar("ayacucho", 500m));

        var leidas = await bd.Unidad.Ejecutar(token => Tarifas.Leer(token));
        Assert.Equal([("ayacucho", 500m), ("cusco", 0m)], leidas.OrderBy(t => t.Key, StringComparer.Ordinal).Select(t => (t.Key, t.Value.Valor)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-40")]
    public async Task Veinte_cambios_simultaneos_de_una_tarifa_se_serializan_y_cada_uno_recibe_la_que_de_verdad_reemplazo()
    {
        await Fijar("cusco", 100m);
        var puerta = new TaskCompletionSource();

        var cambios = Enumerable.Range(1, 20).Select(i => Task.Run(async () =>
        {
            await puerta.Task;
            return (Nuevo: (decimal)i, Anterior: (await Fijar("cusco", i))!.Valor);
        })).ToList();
        puerta.SetResult();
        var hechos = await Task.WhenAll(cambios);

        var final = (await bd.Unidad.Ejecutar(token => Tarifas.Leer(token)))["cusco"].Valor;
        // Una sola cadena: cada importe (el inicial y los 20 nuevos) se reemplaza una vez, salvo el que quedó.
        Assert.Equal(hechos.Select(h => h.Nuevo).Append(100m).Where(v => v != final).Order(), hechos.Select(h => h.Anterior).Order());
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-40")]
    public async Task La_administracion_cambia_el_costo_con_su_auditoria_y_si_no_se_puede_auditar_la_base_no_cambia()
    {
        await using var entorno = await EntornoDelCarritoConPostgres.Crear(bd);
        Task<Resultado<CostoDeEnvioDeSucursal>> Cambiar(decimal importe) =>
            entorno.ComoAdministracion(sp => sp.GetRequiredService<IAdministracionDeEnvios>().Cambiar(new CambiarCostoDeEnvioEntrada(AtajosDeCarrito.Sucursal, importe)));

        var cambiado = await Cambiar(18.50m);
        entorno.Auditoria.Caida = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => Cambiar(99m));
        await Assert.ThrowsAsync<InvalidOperationException>(() => entorno.ComoAdministracion(sp => sp.GetRequiredService<IAdministracionDeEnvios>().Cambiar(new CambiarCostoDeEnvioEntrada("cusco", 99m))));

        Assert.True(cambiado.EsExito, cambiado.Error?.Clave);
        Assert.Equal(["tarifa-de-envio"], entorno.Auditoria.Eventos);
        Assert.Equal(1L, await bd.Consultar<long>("SELECT count(*) FROM carrito.tarifas_de_envio"));
        Assert.Equal(18.50m, await bd.Consultar<decimal>($"SELECT importe FROM carrito.tarifas_de_envio WHERE codigo_de_sucursal = '{AtajosDeCarrito.Sucursal}'"));
        var costos = (await entorno.ComoAdministracion(sp => sp.GetRequiredService<IAdministracionDeEnvios>().Listar())).Valor;
        Assert.Equal((12m, 18.50m), (costos.Predeterminado, costos.Sucursales.Single(s => s.CodigoDeSucursal == AtajosDeCarrito.Sucursal).Importe));
    }

    // ---------- Efectos en la compra ----------

    private async Task<(EntornoDelCarritoConPostgres Entorno, Guid Cuenta, Guid Impreso)> ConImpreso(int cantidad)
    {
        var entorno = await EntornoDelCarritoConPostgres.Crear(bd);
        var impreso = entorno.Catalogo.Poner(FormatoDeEdicion.Impresa, 40m);
        entorno.Inventario.FijarStock(impreso, 10);
        entorno.Disponibilidad.Vendible(impreso, 10);
        var cuenta = EntornoDelCarritoConPostgres.NuevaCuenta();
        var agregado = await entorno.Agregar(cuenta, impreso, cantidad);
        Assert.True(agregado.EsExito, agregado.Error?.Clave);
        return (entorno, cuenta, impreso);
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-42")]
    public async Task Si_la_tarifa_cambia_antes_de_pagar_no_se_reserva_ni_se_cobra_con_el_total_anterior_y_el_nuevo_se_puede_confirmar()
    {
        var (entorno, cuenta, _) = await ConImpreso(3);
        await using var _e = entorno;
        var vista = await _e.PrepararCheckout(cuenta, domicilio: true);
        Assert.Equal(132m, vista.Total);

        await _e.Como(cuenta, async sp => await sp.GetRequiredService<IUnidadDeTrabajo>().Ejecutar(token => sp.GetRequiredService<IRepositorioDeTarifasDeEnvio>().Fijar(AtajosDeCarrito.Sucursal, CostoDeEnvio.Crear(18.50m), Ahora, token)));
        var conElAnterior = await _e.Pagar(cuenta, 132m);

        Assert.False(conElAnterior.EsExito);
        Assert.Equal("checkout-total-cambio", conElAnterior.Error!.Clave);
        Assert.Equal(0, _e.InventarioDeLasPruebas.Reservas);
        Assert.Equal(0, _e.Pagos.Creaciones);
        Assert.Equal(138.50m, (await _e.Como(cuenta, sp => sp.GetRequiredService<ICheckoutDelCliente>().Obtener())).Valor.Total);

        var conElNuevo = await _e.Pagar(cuenta, 138.50m);

        Assert.True(conElNuevo.EsExito, conElNuevo.Error?.Clave);
        Assert.Equal(18.50m, await bd.Consultar<decimal>($"SELECT envio_congelado FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'"));
    }

    [Fact]
    [Trait("Criterio", "AC-CAR-39")]
    public async Task Un_pago_pendiente_anterior_sin_sucursal_se_aprueba_y_quita_lo_comprado_por_libro()
    {
        var (entorno, cuenta, _) = await ConImpreso(2);
        await using var _e = entorno;
        await _e.PrepararCheckout(cuenta);
        var pago = await _e.Pagar(cuenta, 80m);
        Assert.True(pago.EsExito, pago.Error?.Clave);
        var pedido = pago.Valor.PedidoId!.Value;
        await _e.Como(cuenta, sp => sp.GetRequiredService<IProgresoDelCheckout>().PagoPendiente(pedido, _e.Reloj.Ahora.AddMinutes(30), _e.Reloj.Ahora.AddMinutes(15)));
        // Como quedó guardada una compra anterior a la venta por sucursal: sin sucursal y sin la línea del carrito.
        await bd.Ejecutar("UPDATE carrito.lineas_del_checkout SET linea_de_carrito_id = NULL, codigo_de_sucursal = NULL; UPDATE carrito.lineas_de_carrito SET codigo_de_sucursal = NULL");

        var aprobado = await _e.Como(cuenta, sp => sp.GetRequiredService<IProgresoDelCheckout>().PagoAprobado(pedido));

        Assert.Equal(ResultadoDelAvisoDePago.Aplicado, aprobado);
        Assert.Equal("pagado", await bd.Consultar<string>($"SELECT estado FROM carrito.checkouts WHERE cuenta_id = '{cuenta}'"));
        Assert.Equal(0L, await bd.Consultar<long>("SELECT count(*) FROM carrito.lineas_de_carrito"));
    }
}
