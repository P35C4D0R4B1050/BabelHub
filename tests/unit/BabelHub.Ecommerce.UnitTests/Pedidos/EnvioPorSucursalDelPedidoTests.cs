using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>
/// T50 (PED-22, PED-44, PED-45; AC-PED-20, AC-PED-42): el pedido guarda el envío de cada sucursal y una anulación devuelve el de la sucursal que no llegó a enviar, una sola vez, sin contar sucursales sin líneas.
/// </summary>
public sealed class EnvioPorSucursalDelPedidoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

    private static DatosDeLineaNueva Impresa(string fuente, decimal precio = 50m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, "Libro", null, 1, precio, precio, fuente, false, null, null);

    private static EnvioDeSucursal Envio(string sucursal, decimal importe) => new(sucursal, importe);

    private static readonly EnvioDeSucursal[] TresEnvios = [new("ayacucho", 30m), new("cusco", 18.50m), new("huanta", 25m)];

    private static DatosDePedidoNuevo Datos(TipoDeEntregaDelPedido tipo, decimal envio, IReadOnlyList<EnvioDeSucursal>? porSucursal, params DatosDeLineaNueva[] lineas)
    {
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123") : null;
        return new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234567", Guid.NewGuid(), tipo, null, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, envio, lineas.Sum(l => l.Importe) + envio, contacto,
            new("t-1", Ahora), new("p-1", Ahora), lineas.Any(l => l.EsDigital) ? new("r-1", Ahora) : null, lineas, Ahora, porSucursal);
    }

    private static Pedido Domicilio(IReadOnlyList<EnvioDeSucursal>? porSucursal, decimal envio, params DatosDeLineaNueva[] lineas)
    {
        var resultado = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Domicilio, envio, porSucursal, lineas));
        Assert.True(resultado.Creado, resultado.Rechazo?.ToString());
        return resultado.Pedido!;
    }

    /// <summary>Ayacucho, dos líneas de Cusco y Huanta: la compra de AC-PED-42.</summary>
    private static Pedido TresSucursales() => Domicilio(TresEnvios, 73.50m, Impresa("ayacucho"), Impresa("cusco", 40m), Impresa("cusco", 45m), Impresa("huanta", 20m));

    private static void Preparar(Pedido pedido, params LineaDePedido[] lineas) => pedido.PrepararLineas([.. lineas.Select(l => l.Id)], Ahora);

    private static void Despachar(Pedido pedido, params LineaDePedido[] lineas)
    {
        Preparar(pedido, lineas);
        pedido.MarcarListas([.. lineas.Select(l => l.Id)], Ahora);
        pedido.DespacharLineas([.. lineas.Select(l => l.Id)], Ahora);
    }

    private static PlanDeAnulacion Planear(Pedido pedido, params LineaDePedido[] queSalieron)
    {
        var resultado = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro, queSalieron.Length == 0 ? null : queSalieron.Select(l => l.Id).ToHashSet());
        Assert.True(resultado.EsValido, resultado.Rechazo?.ToString());
        return resultado.Plan!;
    }

    // ---------- La copia con el pedido (PED-44) ----------

    [Fact]
    [Trait("Criterio", "AC-PED-43")]
    public void El_pedido_guarda_el_envio_de_cada_sucursal_tal_como_llega_tambien_con_importe_cero()
    {
        var pedido = Domicilio([Envio("ayacucho", 30m), Envio("cusco", 0m)], 30m, Impresa("ayacucho"), Impresa("cusco"));

        Assert.Equal(new[] { Envio("ayacucho", 30m), Envio("cusco", 0m) }, pedido.EnviosPorSucursal);
        Assert.Equal(30m, pedido.CostoDeEnvio);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-43")]
    public void Un_pedido_sin_detalle_de_envios_se_crea_como_antes()
    {
        Assert.Null(Domicilio(null, 30m, Impresa("ayacucho")).EnviosPorSucursal);
    }

    public static TheoryData<string, EnvioDeSucursal[]> DetallesIncoherentes => new()
    {
        { "vacío", [] },
        { "sucursal repetida", [new("ayacucho", 10m), new("ayacucho", 20m)] },
        { "importe negativo", [new("ayacucho", 40m), new("cusco", -10m)] },
        { "no suma el costo", [new("ayacucho", 29.99m)] },
        { "sin código", [new(" ", 30m)] },
    };

    [Theory]
    [MemberData(nameof(DetallesIncoherentes))]
    [Trait("Criterio", "AC-PED-43")]
    public void Un_detalle_de_envios_incoherente_no_crea_el_pedido(string caso, EnvioDeSucursal[] porSucursal)
    {
        var resultado = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Domicilio, 30m, porSucursal, Impresa("ayacucho")));

        Assert.True(resultado.Rechazo == RechazoDeCrearPedido.EnvioPorSucursalIncoherente, caso);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-43")]
    public void Fuera_del_domicilio_no_hay_detalle_de_envios()
    {
        var recojo = Pedido.Crear(Datos(TipoDeEntregaDelPedido.Recojo, 0m, [Envio("ayacucho", 0m)], Impresa("ayacucho")));

        Assert.Equal(RechazoDeCrearPedido.EnvioPorSucursalIncoherente, recojo.Rechazo);
    }

    // ---------- Qué envío se devuelve (PED-22, PED-45) ----------

    [Fact]
    [Trait("Criterio", "AC-PED-20")]
    public void Con_Cusco_en_reparto_se_anula_Ayacucho_y_se_devuelve_su_envio_y_no_el_de_Cusco()
    {
        var pedido = Domicilio([Envio("ayacucho", 30m), Envio("cusco", 18.50m)], 48.50m, Impresa("ayacucho"), Impresa("cusco", 40m));
        var (ayacucho, cusco) = (pedido.Lineas[0], pedido.Lineas[1]);
        Despachar(pedido, cusco);
        Preparar(pedido, ayacucho);

        var plan = Planear(pedido);

        Assert.Equal([ayacucho.Id], plan.LineasImpresas);
        Assert.Equal((30m, true, 80m), (plan.Envio, plan.IncluyeEnvio, plan.Monto));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public void Una_sucursal_que_envio_una_parte_no_recupera_su_envio_y_las_que_no_enviaron_si()
    {
        var pedido = TresSucursales();
        Despachar(pedido, pedido.Lineas[1]);

        var plan = Planear(pedido);

        Assert.Equal([pedido.Lineas[0].Id, pedido.Lineas[2].Id, pedido.Lineas[3].Id], plan.LineasImpresas);
        Assert.Equal((55m, 50m + 45m + 20m + 55m), (plan.Envio, plan.Monto));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public void Si_una_linea_sale_antes_de_aplicarse_la_anulacion_el_envio_de_su_sucursal_tampoco_se_devuelve()
    {
        var pedido = TresSucursales();
        Despachar(pedido, pedido.Lineas[1]);

        var plan = Planear(pedido, pedido.Lineas[3]);

        Assert.Equal([pedido.Lineas[0].Id, pedido.Lineas[2].Id], plan.LineasImpresas);
        Assert.Equal((30m, 50m + 45m + 30m), (plan.Envio, plan.Monto));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public void Sin_nada_salido_se_devuelve_todo_el_costo_tambien_el_de_una_sucursal_sin_lineas()
    {
        var pedido = Domicilio([Envio("ayacucho", 30m), Envio("sin-lineas", 10m)], 40m, Impresa("ayacucho"));

        Assert.Equal((40m, 90m), (Planear(pedido).Envio, Planear(pedido).Monto));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public void Con_algo_salido_el_importe_de_una_sucursal_sin_lineas_no_se_devuelve()
    {
        var pedido = Domicilio([Envio("ayacucho", 30m), Envio("cusco", 18.50m), Envio("sin-lineas", 10m)], 58.50m, Impresa("ayacucho"), Impresa("cusco"));
        Despachar(pedido, pedido.Lineas[1]);

        Assert.Equal(30m, Planear(pedido).Envio);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public void Si_todas_las_sucursales_enviaron_algo_no_se_devuelve_envio()
    {
        var pedido = Domicilio([Envio("cusco", 18.50m)], 18.50m, Impresa("cusco"), Impresa("cusco", 40m));
        Despachar(pedido, pedido.Lineas[0]);

        var plan = Planear(pedido);

        Assert.Equal((0m, false, 40m), (plan.Envio, plan.IncluyeEnvio, plan.Monto));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public void Una_sucursal_ya_anulada_por_una_operacion_anterior_no_se_devuelve_otra_vez()
    {
        var pedido = TresSucursales();
        var ayacucho = pedido.Lineas[0];
        var primera = Planear(pedido) with { LineasImpresas = [ayacucho.Id] };
        Assert.Equal(1, pedido.AplicarAnulacion(primera, Ahora));

        var segunda = Planear(pedido);

        Assert.Equal(43.50m, segunda.Envio);
    }

    [Theory]
    [InlineData(true, 0)]
    [InlineData(false, 30)]
    [Trait("Criterio", "AC-PED-42")]
    public void Un_pedido_sin_detalle_devuelve_todo_si_nada_salio_y_nada_si_algo_salio(bool algoSalio, int esperado)
    {
        var pedido = Domicilio(null, 30m, Impresa("ayacucho"), Impresa("cusco"));
        if (algoSalio)
        {
            Despachar(pedido, pedido.Lineas[1]);
        }

        Assert.Equal(esperado, Planear(pedido).Envio);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-40")]
    public void La_cancelacion_del_cliente_sigue_devolviendo_todo_el_envio()
    {
        var plan = TresSucursales().PlanearCancelacionDelCliente().Plan!;

        Assert.Equal((73.50m, true, 155m + 73.50m), (plan.Envio, plan.IncluyeEnvio, plan.Monto));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-42")]
    public void La_anulacion_guarda_el_envio_del_plan_y_lo_cambia_al_recalcularlo()
    {
        var pedido = TresSucursales();
        Despachar(pedido, pedido.Lineas[1]);
        var anulacion = Anulacion.Iniciar(Guid.NewGuid(), pedido.Id, Planear(pedido), new IniciadorDeOperacion(Guid.NewGuid(), "administrador-central", null), Ahora);
        Assert.Equal((55m, true), (anulacion.Envio, anulacion.IncluyeEnvio));

        anulacion.AjustarAlPlan(Planear(pedido, pedido.Lineas[0], pedido.Lineas[3]), Ahora);

        Assert.Equal((0m, false, 45m), (anulacion.Envio, anulacion.IncluyeEnvio, anulacion.Monto));
    }
}
