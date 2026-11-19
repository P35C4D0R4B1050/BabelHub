using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T05 (PED-13, PED-16, PED-17, PED-18, PED-22, PED-23, PED-27; AC-PED-15, 16, 18, 20, 22): qué se anula, qué se reembolsa y cuándo el cliente puede cancelar.</summary>
public sealed class AnulacionDePedidoTests
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);
    private static readonly Guid Cuenta = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static DatosDeLineaNueva Impresa(string titulo, bool traslado = false, decimal precio = 50m, string fuente = "LIM") =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, titulo, "9780000000001", 1, precio, precio, fuente, traslado, traslado ? Guid.NewGuid() : null, null);

    private static DatosDeLineaNueva Digital(string titulo = "Digital", decimal precio = 20m) =>
        new(Guid.NewGuid(), Guid.NewGuid(), true, titulo, null, 1, precio, precio, null, false, null, null);

    private static Pedido Crear(TipoDeEntregaDelPedido tipo, params DatosDeLineaNueva[] lineas)
    {
        var contacto = tipo == TipoDeEntregaDelPedido.Domicilio ? ContactoDeEntrega.Crear("Ana", "987654321", "Jr. Lima 123") : null;
        var envio = tipo == TipoDeEntregaDelPedido.Domicilio ? 12m : 0m;
        var datos = new DatosDePedidoNuevo(
            Guid.NewGuid(), "ABCD234567", Cuenta, tipo, tipo == TipoDeEntregaDelPedido.Recojo ? "LIM" : null, tipo == TipoDeEntregaDelPedido.Domicilio ? "ayacucho" : null, envio, lineas.Sum(l => l.Importe) + envio, contacto,
            new AceptacionDeTexto("t-1", Inicio), new AceptacionDeTexto("p-1", Inicio), lineas.Any(l => l.EsDigital) ? new AceptacionDeTexto("r-1", Inicio) : null, lineas, Inicio);
        var resultado = Pedido.Crear(datos);
        Assert.True(resultado.Creado, resultado.Rechazo?.ToString());
        return resultado.Pedido!;
    }

    private static LineaDePedido De(Pedido pedido, string titulo) => pedido.Lineas.Single(l => l.Titulo == titulo);

    [Fact]
    [Trait("Criterio", "AC-PED-11")]
    public void Anular_un_pedido_con_un_impreso_confirmado_y_un_digital_descargado_anula_todo_y_reembolsa_el_total_con_el_envio()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Impreso"), Digital());

        var resultado = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.PagoDuplicado);

        var plan = resultado.Plan!;
        Assert.Equal([De(pedido, "Impreso").Id], plan.LineasImpresas);
        Assert.Equal([De(pedido, "Digital").Id], plan.LineasDigitales);
        Assert.True(plan.IncluyeEnvio);
        Assert.Equal(82m, plan.Monto);
        Assert.Equal(pedido.Total, plan.Monto);
        Assert.Equal(OrigenDeLaAnulacion.Administracion, plan.Origen);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-18")]
    public void Anular_un_pedido_con_un_impreso_entregado_o_solo_digital_se_rechaza_porque_corresponde_una_devolucion()
    {
        var entregado = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        var linea = De(entregado, "Uno");
        entregado.PrepararLineas([linea.Id], Inicio);
        entregado.MarcarListas([linea.Id], Inicio);
        entregado.EntregarLineas([linea.Id], Inicio);
        var soloDigital = Crear(TipoDeEntregaDelPedido.Digital, Digital());

        Assert.Equal(RechazoDeAnulacion.NoQuedaImpresaPorSalir, entregado.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Rechazo);
        Assert.Equal(RechazoDeAnulacion.NoQuedaImpresaPorSalir, soloDigital.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Rechazo);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-20")]
    public void Si_un_impreso_ya_salio_se_anula_solo_lo_que_no_salio_se_conservan_las_digitales_y_no_se_reembolsa_el_envio()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Cusco", fuente: "CUS"), Impresa("Ayacucho", fuente: "AYA"), Digital());
        var cusco = De(pedido, "Cusco");
        var ayacucho = De(pedido, "Ayacucho");
        pedido.PrepararLineas([cusco.Id, ayacucho.Id], Inicio);
        pedido.MarcarListas([cusco.Id], Inicio);
        pedido.DespacharLineas([cusco.Id], Inicio);

        var plan = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!;

        Assert.Equal([ayacucho.Id], plan.LineasImpresas);
        Assert.Empty(plan.LineasDigitales);
        Assert.False(plan.IncluyeEnvio);
        Assert.Equal(50m, plan.Monto);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-22")]
    public void Se_anulan_las_lineas_en_preparacion_listas_y_no_recogidas_si_ninguna_salio()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Prep"), Impresa("Lista"), Impresa("NoRec"));
        var prep = De(pedido, "Prep");
        var lista = De(pedido, "Lista");
        var noRec = De(pedido, "NoRec");
        pedido.PrepararLineas([prep.Id, lista.Id, noRec.Id], Inicio);
        pedido.MarcarListas([lista.Id, noRec.Id], Inicio);
        pedido.MarcarNoRecogidas(Inicio.AddDays(16), new HashSet<Guid> { lista.Id });
        Assert.Equal(EstadoDeLineaImpresa.NoRecogido, noRec.EstadoImpreso);

        var plan = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.NoRecogido).Plan!;
        var aplicadas = pedido.AplicarAnulacion(plan, Inicio.AddDays(17));

        Assert.Equal(3, plan.LineasImpresas.Count);
        Assert.Equal(3, aplicadas);
        Assert.Equal(EstadoDePedido.Anulado, pedido.Estado);
        Assert.Equal(Inicio.AddDays(17), pedido.FechaEstadoFinal);
        Assert.All(pedido.Lineas, l => Assert.Equal(EstadoDeLineaImpresa.Anulado, l.EstadoImpreso));
        Assert.Equal(Inicio.AddDays(17), noRec.AnuladaEn);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-21")]
    public void Tras_anular_lo_que_no_salio_la_linea_que_se_entrega_deja_el_pedido_entregado()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Cusco"), Impresa("Ayacucho"));
        var cusco = De(pedido, "Cusco");
        var ayacucho = De(pedido, "Ayacucho");
        pedido.PrepararLineas([cusco.Id, ayacucho.Id], Inicio);
        pedido.MarcarListas([cusco.Id], Inicio);
        pedido.DespacharLineas([cusco.Id], Inicio);
        pedido.AplicarAnulacion(pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!, Inicio.AddHours(1));

        pedido.EntregarLineas([cusco.Id], Inicio.AddDays(2));

        Assert.Equal(EstadoDePedido.Entregado, pedido.Estado);
    }

    [Fact]
    public void Si_un_conflicto_revela_que_una_linea_ya_salio_el_plan_se_recalcula_sin_ella_sin_el_envio_y_sin_las_digitales()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Uno"), Impresa("Dos"), Digital());
        var uno = De(pedido, "Uno");
        var original = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Plan!;
        Assert.True(original.IncluyeEnvio);
        Assert.Single(original.LineasDigitales);

        var recalculado = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro, new HashSet<Guid> { uno.Id }).Plan!;

        Assert.Equal([De(pedido, "Dos").Id], recalculado.LineasImpresas);
        Assert.False(recalculado.IncluyeEnvio);
        Assert.Empty(recalculado.LineasDigitales);
        Assert.Equal(50m, recalculado.Monto);
    }

    [Fact]
    public void Si_todas_las_lineas_pendientes_ya_salieron_el_recalculo_las_deja_sin_anulacion()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Uno"));

        var resultado = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro, new HashSet<Guid> { pedido.Lineas[0].Id });

        Assert.Equal(RechazoDeAnulacion.NoQuedaImpresaPorSalir, resultado.Rechazo);
    }

    [Fact]
    public void Un_motivo_fuera_de_la_lista_cerrada_se_rechaza_y_las_digitales_no_se_anulan_si_un_impreso_ya_salio_aunque_el_plan_las_traiga()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Cusco"), Impresa("Ayacucho"), Digital());
        var cusco = De(pedido, "Cusco");
        pedido.PrepararLineas([cusco.Id], Inicio);
        pedido.MarcarListas([cusco.Id], Inicio);
        pedido.DespacharLineas([cusco.Id], Inicio);
        var digital = De(pedido, "Digital");
        var plan = new PlanDeAnulacion(OrigenDeLaAnulacion.Administracion, MotivoDeAnulacion.Otro, [De(pedido, "Ayacucho").Id], [digital.Id], false, 70m, []);

        pedido.AplicarAnulacion(plan, Inicio);

        Assert.Equal(RechazoDeAnulacion.MotivoNoCorresponde, pedido.PlanearAnulacionDeLaAdministracion((MotivoDeAnulacion)99).Rechazo);
        Assert.Equal(EstadoDeLineaDigital.Disponible, digital.EstadoDigital);
    }

    [Fact]
    public void La_administracion_no_usa_el_motivo_del_cliente_y_un_pedido_ya_anulado_no_se_anula_de_nuevo()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        Assert.Equal(RechazoDeAnulacion.MotivoNoCorresponde, pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.CanceladoPorElCliente).Rechazo);
        pedido.AplicarAnulacion(pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.PagoDuplicado).Plan!, Inicio);

        Assert.Equal(RechazoDeAnulacion.PedidoYaAnulado, pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.Otro).Rechazo);
        Assert.Equal(RechazoDeAnulacion.PedidoYaAnulado, pedido.PlanearCancelacionDelCliente().Rechazo);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-15")]
    public void El_cliente_cancela_un_pedido_de_impresos_todos_confirmados_aunque_tengan_un_traslado_aprobado_y_se_reembolsa_el_total()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Domicilio, Impresa("Uno", traslado: true), Impresa("Dos"));

        var plan = pedido.PlanearCancelacionDelCliente().Plan!;

        Assert.Equal(OrigenDeLaAnulacion.Cliente, plan.Origen);
        Assert.Equal(MotivoDeAnulacion.CanceladoPorElCliente, plan.Motivo);
        Assert.Equal(2, plan.LineasImpresas.Count);
        Assert.Empty(plan.LineasDigitales);
        Assert.True(plan.IncluyeEnvio);
        Assert.Equal(pedido.Total, plan.Monto);
        Assert.Equal([De(pedido, "Uno").TrasladoId!.Value], plan.TrasladosACancelar);
    }

    [Fact]
    [Trait("Criterio", "AC-PED-16")]
    public void El_cliente_no_cancela_un_pedido_con_un_impreso_en_preparacion_o_en_transito_ni_con_un_digital()
    {
        var enPreparacion = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        enPreparacion.PrepararLineas([enPreparacion.Lineas[0].Id], Inicio);
        var enTransito = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno", traslado: true));
        enTransito.Lineas[0].PasarA(EstadoDeLineaImpresa.EnTransito, Inicio);
        var conDigital = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"), Digital());

        Assert.Equal(RechazoDeAnulacion.ImpresoYaEnCamino, enPreparacion.PlanearCancelacionDelCliente().Rechazo);
        Assert.Equal(RechazoDeAnulacion.ImpresoYaEnCamino, enTransito.PlanearCancelacionDelCliente().Rechazo);
        Assert.Equal(RechazoDeAnulacion.IncluyeDigital, conDigital.PlanearCancelacionDelCliente().Rechazo);
    }

    [Fact]
    public void El_motivo_es_de_lista_cerrada_con_codigo_estable_y_sin_nota_libre()
    {
        Assert.Equal(["pago-duplicado", "no-recogido", "otro", "cancelado-por-el-cliente"], Enum.GetValues<MotivoDeAnulacion>().Select(MotivosDeAnulacion.Codigo));
        Assert.True(MotivosDeAnulacion.TryParse("pago-duplicado", out var motivo));
        Assert.Equal(MotivoDeAnulacion.PagoDuplicado, motivo);
        Assert.All(new[] { "", "Pago-Duplicado", "texto libre", "pago duplicado", " otro" }, texto => Assert.False(MotivosDeAnulacion.TryParse(texto, out _)));
        Assert.False(MotivosDeAnulacion.EsDeLaAdministracion(MotivoDeAnulacion.CanceladoPorElCliente));
        Assert.True(MotivosDeAnulacion.EsDeLaAdministracion(MotivoDeAnulacion.NoRecogido));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-17")]
    public void Tras_cancelar_la_linea_ya_no_se_prepara_y_una_doble_cancelacion_no_cambia_nada()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"));
        var linea = De(pedido, "Uno");
        var plan = pedido.PlanearCancelacionDelCliente().Plan!;
        Assert.Equal(1, pedido.AplicarAnulacion(plan, Inicio));

        var preparar = pedido.PrepararLineas([linea.Id], Inicio.AddMinutes(1));
        var doble = pedido.AplicarAnulacion(plan, Inicio.AddMinutes(2));

        Assert.Equal(ResultadoDeOperacionDeLinea.EstadoInvalido, Assert.Single(preparar).Resultado);
        Assert.Equal(0, doble);
        Assert.Equal(RechazoDeAnulacion.PedidoYaAnulado, pedido.PlanearCancelacionDelCliente().Rechazo);
    }

    [Fact]
    public void Aplicar_la_anulacion_anula_las_digitales_y_recalcula_el_pedido_y_repetirla_no_cambia_nada()
    {
        var pedido = Crear(TipoDeEntregaDelPedido.Recojo, Impresa("Uno"), Digital());
        var plan = pedido.PlanearAnulacionDeLaAdministracion(MotivoDeAnulacion.PagoDuplicado).Plan!;

        var primera = pedido.AplicarAnulacion(plan, Inicio.AddHours(1));
        var repetida = pedido.AplicarAnulacion(plan, Inicio.AddHours(2));

        Assert.Equal(2, primera);
        Assert.Equal(0, repetida);
        Assert.Equal(EstadoDeLineaDigital.Anulado, De(pedido, "Digital").EstadoDigital);
        Assert.Equal(EstadoDePedido.Anulado, pedido.Estado);
        Assert.Equal(Inicio.AddHours(1), pedido.FechaEstadoFinal);
    }
}
