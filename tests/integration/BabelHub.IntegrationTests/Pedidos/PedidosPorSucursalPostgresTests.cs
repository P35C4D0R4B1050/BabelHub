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
}
