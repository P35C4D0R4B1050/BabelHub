using BabelHub.Ecommerce.Application.Pedidos.Interno;
using BabelHub.Ecommerce.Domain.Pedidos;
using BabelHub.Ecommerce.Infrastructure.Pedidos;
using Npgsql;

namespace BabelHub.IntegrationTests.Pedidos;

/// <summary>
/// T46 (PED-08, PED-43; AC-PED-37, AC-PED-39): el esquema <c>pedidos</c> anterior se actualiza de forma repetible y la lista del encargado selecciona y filtra por las líneas de su sucursal, en la consulta y antes de paginar.
/// </summary>
[Collection(ColeccionPostgresDePedidos.Nombre)]
public sealed class PedidosPorSucursalPostgresTests(PostgresDePedidosFixture bd) : IAsyncLifetime
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Las reglas que tenía el esquema antes de la venta por sucursal: una línea por edición y todo recojo con su sucursal.</summary>
    private const string EsquemaAnterior =
        """
        DROP INDEX IF EXISTS pedidos.lineas_una_digital_por_edicion;
        ALTER TABLE pedidos.lineas_de_pedido ADD CONSTRAINT lineas_una_por_edicion UNIQUE (pedido_id, edicion_id);
        ALTER TABLE pedidos.pedidos DROP CONSTRAINT pedidos_recojo_con_sucursal;
        ALTER TABLE pedidos.pedidos ADD CONSTRAINT pedidos_recojo_con_sucursal CHECK ((tipo_de_entrega = 'recojo') = (codigo_de_sucursal_de_recojo IS NOT NULL));
        """;

    public Task InitializeAsync() => bd.Limpiar();

    public Task DisposeAsync() => Task.CompletedTask;

    private static DatosDeLineaNueva Impresa(string fuente, Guid? edicion = null, bool traslado = false) =>
        new(Guid.NewGuid(), edicion ?? Guid.NewGuid(), false, "Ficciones", null, 1, 50m, 50m, fuente, traslado, traslado ? Guid.NewGuid() : null, null);

    private static DatosDeLineaNueva Digital(Guid? edicion = null) =>
        new(Guid.NewGuid(), edicion ?? Guid.NewGuid(), true, "El Aleph", null, 1, 20m, 20m, null, false, null, null);

    private static Pedido Crear(TipoDeEntregaDelPedido tipo, string? sucursalComun, params DatosDeLineaNueva[] lineas)
    {
        var domicilio = tipo == TipoDeEntregaDelPedido.Domicilio;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), NumeroDePedido.Generar(Random.Shared.Next).Valor, Guid.NewGuid(), tipo, sucursalComun, domicilio ? "ayacucho" : null, 0m, lineas.Sum(l => l.Importe),
            domicilio ? ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123") : null, new("t-1", Ahora), new("p-1", Ahora), lineas.Any(l => l.EsDigital) ? new("r-1", Ahora) : null, lineas, Ahora);
        var resultado = Pedido.Crear(datos);
        Assert.True(resultado.Creado, resultado.Rechazo?.ToString());
        return resultado.Pedido!;
    }

    private async Task Guardar(params Pedido[] pedidos)
    {
        foreach (var pedido in pedidos)
        {
            Assert.True(await bd.Pedidos.Agregar(pedido));
        }
    }

    private async Task<Guid[]> DeLaSucursal(string sucursal, EstadoDePedido? estado = null) =>
        [.. (await bd.Pedidos.Listar(new CriterioDeListado(null, sucursal, false, estado, 1, 200))).Pedidos.Select(p => p.Id).Order()];

    private static Guid[] Ids(params Pedido[] pedidos) => [.. pedidos.Select(p => p.Id).Order()];

    [Fact]
    [Trait("Criterio", "AC-PED-39")]
    public async Task El_esquema_anterior_se_actualiza_dos_veces_y_conserva_la_sucursal_comun_de_sus_pedidos()
    {
        await bd.Ejecutar(EsquemaAnterior);
        var edicion = Guid.NewGuid();
        var anterior = Crear(TipoDeEntregaDelPedido.Recojo, "ayacucho", Impresa("cusco", traslado: true), Impresa("ayacucho"));
        await Guardar(anterior);
        var porSucursal = Crear(TipoDeEntregaDelPedido.Recojo, null, Impresa("cusco", edicion), Impresa("ayacucho", edicion));
        var conElEsquemaAnterior = await Assert.ThrowsAsync<FallaDeLaBaseDePedidosException>(() => bd.Pedidos.Agregar(porSucursal));
        Assert.Equal(PostgresErrorCodes.CheckViolation, conElEsquemaAnterior.CodigoDeEstado);

        await bd.Ejecutar(DefinicionDePedidos.Sql);
        await bd.Ejecutar(DefinicionDePedidos.Sql);

        await Guardar(porSucursal);
        var leido = (await bd.Pedidos.Obtener(anterior.Id, conBloqueo: false))!;
        Assert.Equal(new[] { "ayacucho", "ayacucho" }, leido.Lineas.Select(leido.SucursalDeLaLinea));
        var nuevo = (await bd.Pedidos.Obtener(porSucursal.Id, conBloqueo: false))!;
        Assert.Null(nuevo.CodigoDeSucursalDeRecojo);
        Assert.Equal(new[] { "cusco", "ayacucho" }, nuevo.Lineas.Select(nuevo.SucursalDeLaLinea));
        Assert.Equal(Ids(anterior, porSucursal), await DeLaSucursal("ayacucho"));
        Assert.Equal(Ids(porSucursal), await DeLaSucursal("cusco"));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-39")]
    public async Task Una_edicion_digital_sigue_siendo_unica_y_solo_un_recojo_puede_llevar_sucursal_comun()
    {
        var edicion = Guid.NewGuid();
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, null, Impresa("cusco"), Digital(edicion));
        await Guardar(pedido);

        var repetida = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar(
            $"""
            INSERT INTO pedidos.lineas_de_pedido (id, pedido_id, edicion_id, posicion, es_digital, titulo, cantidad, precio_unitario, importe, estado, requiere_traslado, version)
            VALUES (gen_random_uuid(), '{pedido.Id}', '{edicion}', 9, true, 'El Aleph', 1, 20, 20, 'disponible', false, 1)
            """));
        var fueraDelRecojo = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE pedidos.pedidos SET tipo_de_entrega = 'digital', codigo_de_sucursal_de_recojo = 'cusco' WHERE id = '{pedido.Id}'"));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, repetida.SqlState);
        Assert.Equal(PostgresErrorCodes.CheckViolation, fueraDelRecojo.SqlState);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-37")]
    public async Task La_lista_del_encargado_selecciona_por_las_lineas_de_su_sucursal_y_filtra_por_su_estado_antes_de_paginar()
    {
        var deDos = Crear(TipoDeEntregaDelPedido.Recojo, null, Impresa("ayacucho"), Impresa("cusco"));
        deDos.PrepararLineas([deDos.Lineas[0].Id], Ahora);
        deDos.MarcarListas([deDos.Lineas[0].Id], Ahora);
        var deCusco = Crear(TipoDeEntregaDelPedido.Recojo, null, Impresa("cusco"), Digital());
        var anterior = Crear(TipoDeEntregaDelPedido.Recojo, "ayacucho", Impresa("cusco", traslado: true));
        var aDomicilio = Crear(TipoDeEntregaDelPedido.Domicilio, null, Impresa("ayacucho"));
        await Guardar(deDos, deCusco, anterior, aDomicilio);

        Assert.Equal(Ids(deDos, anterior), await DeLaSucursal("ayacucho"));
        Assert.Equal(Ids(deDos, deCusco), await DeLaSucursal("cusco"));
        Assert.Empty(await DeLaSucursal("lima"));
        Assert.Equal(Ids(deDos), await DeLaSucursal("ayacucho", EstadoDePedido.ListoParaRecojo));
        Assert.Equal(Ids(anterior), await DeLaSucursal("ayacucho", EstadoDePedido.Confirmado));
        Assert.Equal(Ids(deDos, deCusco), await DeLaSucursal("cusco", EstadoDePedido.Confirmado));

        var segundaPagina = await bd.Pedidos.Listar(new CriterioDeListado(null, "cusco", false, EstadoDePedido.Confirmado, 2, 1));
        Assert.Equal(2, segundaPagina.Total);
        Assert.Single(segundaPagina.Pedidos);
        Assert.NotEmpty(segundaPagina.Pedidos[0].Lineas);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-37")]
    public async Task El_estado_calculado_en_la_consulta_coincide_con_el_del_dominio_para_cada_par_de_estados_de_linea()
    {
        string[] estados = ["confirmado", "en-transito", "recibido", "en-preparacion", "listo", "entregado", "no-recogido", "anulado"];
        var pedidos = (from uno in estados from dos in estados select ConLineasEn(uno, dos)).ToArray();
        await Guardar(pedidos);

        foreach (var estado in Enum.GetValues<EstadoDePedido>())
        {
            var esperados = pedidos.Where(p => p.EstadoDeLasLineas([.. p.Lineas.Where(l => !l.EsDigital)]) == estado).ToArray();

            var listados = await DeLaSucursal("ayacucho", estado);

            Assert.True(Enumerable.SequenceEqual(Ids(esperados), listados), $"El filtro «{estado}» no coincide con el Dominio.");
        }
    }

    /// <summary>Un recojo anterior (admite líneas con traslado) con dos impresas en los estados pedidos y una digital, que el encargado no ve.</summary>
    private static Pedido ConLineasEn(string uno, string dos)
    {
        static bool ConTraslado(string estado) => estado is "en-transito" or "recibido";
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, "ayacucho", Impresa("ayacucho", traslado: ConTraslado(uno)), Impresa("ayacucho", traslado: ConTraslado(dos)), Digital());
        var pares = new[] { (pedido.Lineas[0].Id, Estado: uno), (pedido.Lineas[1].Id, Estado: dos) };
        var hacePlazos = Ahora.AddDays(-20);
        var vencidas = pares.Where(p => p.Estado == "no-recogido").Select(p => p.Id).ToArray();
        pedido.PrepararLineas(vencidas, hacePlazos);
        pedido.MarcarListas(vencidas, hacePlazos);
        pedido.MarcarNoRecogidas(Ahora, new HashSet<Guid>());
        foreach (var (id, estado) in pares)
        {
            switch (estado)
            {
                case "en-transito":
                    pedido.RegistrarTraslado(id, EstadoDeLineaImpresa.EnTransito, null, Ahora);
                    break;
                case "recibido":
                    pedido.RegistrarTraslado(id, EstadoDeLineaImpresa.Recibido, null, Ahora);
                    break;
                case "en-preparacion":
                    pedido.PrepararLineas([id], Ahora);
                    break;
                case "listo" or "entregado":
                    pedido.PrepararLineas([id], Ahora);
                    pedido.MarcarListas([id], Ahora);
                    if (estado == "entregado")
                    {
                        pedido.EntregarLineas([id], Ahora);
                    }

                    break;
                case "anulado":
                    pedido.AplicarAnulacion(new PlanDeAnulacion(OrigenDeLaAnulacion.Administracion, MotivoDeAnulacion.Otro, [id], [], 0m, 50m, []), Ahora);
                    break;
            }
        }

        return pedido;
    }

    // ---------- T52: el envío por sucursal del pedido y el de la anulación (AC-PED-42, AC-PED-43) ----------

    private static Pedido ConEnvios()
    {
        var lineas = new[] { Impresa("ayacucho"), Impresa("cusco") };
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), NumeroDePedido.Generar(Random.Shared.Next).Valor, Guid.NewGuid(), TipoDeEntregaDelPedido.Domicilio, null, "ayacucho", 48.50m, 148.50m,
            ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123"), new("t-1", Ahora), new("p-1", Ahora), null, lineas, Ahora, [new("ayacucho", 30m), new("cusco", 18.50m)]);
        return Pedido.Crear(datos).Pedido!;
    }

    private static Anulacion Anular(Pedido pedido) => Anulacion.Iniciar(
        Guid.NewGuid(), pedido.Id, pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora);

    [Fact]
    [Trait("Criterio", "AC-PED-43")]
    public async Task El_pedido_guarda_y_lee_el_envio_de_cada_sucursal_y_ninguno_donde_no_lo_hay()
    {
        var conDetalle = ConEnvios();
        var sinDetalle = Crear(TipoDeEntregaDelPedido.Recojo, null, Impresa("cusco"));
        await Guardar(conDetalle, sinDetalle);

        var leido = (await bd.Pedidos.Obtener(conDetalle.Id, conBloqueo: true))!;

        Assert.Equal(new EnvioDeSucursal[] { new("ayacucho", 30m), new("cusco", 18.50m) }, leido.EnviosPorSucursal);
        Assert.Equal(48.50m, leido.CostoDeEnvio);
        Assert.Null((await bd.Pedidos.Obtener(sinDetalle.Id, conBloqueo: false))!.EnviosPorSucursal);
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM pedidos.pedidos WHERE envios_por_sucursal IS NULL"));
        // Guardar un cambio de estado no toca el detalle, también leído desde un listado.
        await bd.Pedidos.Actualizar((await bd.Pedidos.Listar(new CriterioDeListado(conDetalle.CuentaId, null, false, null, 1, 10))).Pedidos.Single());
        Assert.Equal(2, (await bd.Pedidos.Obtener(conDetalle.Id, conBloqueo: false))!.EnviosPorSucursal!.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public async Task La_anulacion_guarda_el_envio_a_devolver_y_lo_actualiza_al_recalcular_el_plan()
    {
        var pedido = ConEnvios();
        pedido.PrepararLineas([pedido.Lineas[1].Id], Ahora);
        pedido.MarcarListas([pedido.Lineas[1].Id], Ahora);
        pedido.DespacharLineas([pedido.Lineas[1].Id], Ahora);
        await Guardar(pedido);
        await bd.Pedidos.AgregarAnulacion(Anular(pedido));

        var leida = Assert.Single(await bd.Pedidos.AnulacionesEnCurso(pedido.Id));
        Assert.Equal((30m, true, 80m), (leida.Envio, leida.IncluyeEnvio, leida.Monto));

        leida.AjustarAlPlan(new PlanDeAnulacion(leida.Origen, leida.Motivo, [pedido.Lineas[0].Id], [], 0m, 50m, []), Ahora);
        await bd.Pedidos.ActualizarAnulacion(leida);

        var ajustada = Assert.Single(await bd.Pedidos.AnulacionesEnCurso(pedido.Id));
        Assert.Equal((0m, false, 50m), (ajustada.Envio, ajustada.IncluyeEnvio, ajustada.Monto));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-43")]
    public async Task Las_columnas_del_envio_se_agregan_dos_veces_sobre_el_esquema_anterior_sin_rellenar_y_una_fila_anterior_se_lee_como_antes()
    {
        await bd.Ejecutar("ALTER TABLE pedidos.pedidos DROP COLUMN envios_por_sucursal; ALTER TABLE pedidos.anulaciones DROP COLUMN envio;");
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, null, Impresa("ayacucho"));
        await bd.Ejecutar(
            $"""
            INSERT INTO pedidos.pedidos (id, numero, cuenta_id, tipo_de_entrega, region_de_entrega, costo_de_envio, total, estado, creado_en, contacto_nombre, contacto_telefono, contacto_direccion, terminos_version, terminos_aceptado_en, privacidad_version, privacidad_aceptado_en, version)
            VALUES ('{pedido.Id}', 'ABCDEFGH23', gen_random_uuid(), 'domicilio', 'ayacucho', 12, 62, 'confirmado', now(), 'Ana', '987654321', 'Jr. Lima 123', 't-1', now(), 'p-1', now(), 1);
            INSERT INTO pedidos.anulaciones (id, pedido_id, origen, motivo, incluye_envio, hay_digitales, monto, estado, iniciada_por, proximo_intento_en, intentos, creada_en)
            VALUES (gen_random_uuid(), '{pedido.Id}', 'administracion', 'otro', true, false, 62, 'en_curso', gen_random_uuid(), now(), 0, now());
            """);

        for (var veces = 0; veces < 2; veces++)
        {
            await bd.Ejecutar(DefinicionDePedidos.Sql);
            await bd.Ejecutar(DefinicionDePedidos.Permisos);
        }

        var anterior = Assert.Single(await bd.Pedidos.AnulacionesEnCurso(pedido.Id));
        Assert.Equal((12m, true), (anterior.Envio, anterior.IncluyeEnvio));
        Assert.Equal(1, await bd.Consultar<long>("SELECT count(*) FROM pedidos.anulaciones WHERE envio IS NULL"));
        await bd.Pedidos.ActualizarAnulacion(anterior);
        Assert.Equal(12m, await bd.Consultar<decimal>("SELECT envio FROM pedidos.anulaciones"));
        await Guardar(ConEnvios());
    }

    [Theory]
    [InlineData("envio = -1, incluye_envio = false")]
    [InlineData("envio = 999, incluye_envio = true")]
    [InlineData("envio = 5, incluye_envio = false")]
    [InlineData("envio = 0, incluye_envio = true")]
    [Trait("Criterio", "AC-PED-42")]
    public async Task La_base_rechaza_un_envio_negativo_mayor_que_el_monto_o_incoherente_con_su_marca(string cambio)
    {
        var pedido = ConEnvios();
        await Guardar(pedido);
        await bd.Pedidos.AgregarAnulacion(Anular(pedido));

        var error = await Assert.ThrowsAsync<PostgresException>(() => bd.Ejecutar($"UPDATE pedidos.anulaciones SET {cambio}"));

        Assert.Equal(("anulaciones_envio_coherente", PostgresErrorCodes.CheckViolation), (error.ConstraintName, error.SqlState));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-43")]
    public async Task El_rol_de_pedidos_actualiza_el_envio_de_la_anulacion_pero_no_el_detalle_guardado_con_el_pedido()
    {
        await Guardar(ConEnvios());
        await using var conexion = await bd.Acceso.OpenConnectionAsync();
        await using (var rol = new NpgsqlCommand("SET ROLE rol_pedidos; UPDATE pedidos.anulaciones SET envio = 0 WHERE false", conexion))
        {
            await rol.ExecuteNonQueryAsync();
        }

        await using var cambio = new NpgsqlCommand("UPDATE pedidos.pedidos SET envios_por_sucursal = NULL WHERE false", conexion);
        var error = await Assert.ThrowsAsync<PostgresException>(() => cambio.ExecuteNonQueryAsync());

        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
    }
}
