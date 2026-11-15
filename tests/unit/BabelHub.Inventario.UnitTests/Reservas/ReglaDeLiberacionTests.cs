using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T03: la matriz motivo × estado de las reglas de liberación (D15) y el tope de 3 reemplazos por pedido (SEL-36, SEL-37).</summary>
public sealed class ReglaDeLiberacionTests
{
    public enum Esperado
    {
        Libera,
        Anula,
        Invalido,
        SinCambios,
    }

    private static LineaDeReserva LineaEn(string estado) => estado switch
    {
        "pendiente" => ReservaDePedidoTests.Linea(EstadoDeLinea.Pendiente),
        "confirmada" => ReservaDePedidoTests.Linea(EstadoDeLinea.Confirmada),
        "cerrada-traslado" => ReservaDePedidoTests.Linea(EstadoDeLinea.Cerrada, CausaDeCierre.Traslado),
        "cerrada-entrega" => ReservaDePedidoTests.Linea(EstadoDeLinea.Cerrada, CausaDeCierre.Entrega),
        "cerrada-reparto" => ReservaDePedidoTests.Linea(EstadoDeLinea.Cerrada, CausaDeCierre.Reparto),
        "liberada" => ReservaDePedidoTests.Linea(EstadoDeLinea.Liberada),
        "expirada" => ReservaDePedidoTests.Linea(EstadoDeLinea.Expirada),
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    private static Esperado Clasificar(DecisionDeLiberacion decision) =>
        decision.EstadoInvalido ? Esperado.Invalido
        : decision.ALiberar.Count > 0 ? Esperado.Libera
        : decision.AAnular.Count > 0 ? Esperado.Anula
        : Esperado.SinCambios;

    public static TheoryData<MotivoDeLiberacion, string, Esperado> Matriz()
    {
        var datos = new TheoryData<MotivoDeLiberacion, string, Esperado>();

        // Estos motivos solo liberan pendientes: un pedido confirmado (o cerrado) se rechaza sin cambios y sin cancelar nada.
        foreach (var motivo in new[] { MotivoDeLiberacion.CheckoutCancelado, MotivoDeLiberacion.PagoVencido, MotivoDeLiberacion.Reemplazo })
        {
            datos.Add(motivo, "pendiente", Esperado.Libera);
            datos.Add(motivo, "confirmada", Esperado.Invalido);
            datos.Add(motivo, "cerrada-traslado", Esperado.Invalido);
            datos.Add(motivo, "cerrada-entrega", Esperado.Invalido);
            datos.Add(motivo, "cerrada-reparto", Esperado.Invalido);
            datos.Add(motivo, "liberada", Esperado.SinCambios);
            datos.Add(motivo, "expirada", Esperado.SinCambios);
        }

        // Los motivos que anulan el pedido liberan pendientes y confirmadas, marcan la cerrada por traslado y rechazan la entregada o en reparto.
        foreach (var motivo in new[] { MotivoDeLiberacion.PedidoAnulado, MotivoDeLiberacion.CanceladoPorElCliente, MotivoDeLiberacion.FallaPosteriorAlPago })
        {
            datos.Add(motivo, "pendiente", Esperado.Libera);
            datos.Add(motivo, "confirmada", Esperado.Libera);
            datos.Add(motivo, "cerrada-traslado", Esperado.Anula);
            datos.Add(motivo, "cerrada-entrega", Esperado.Invalido);
            datos.Add(motivo, "cerrada-reparto", Esperado.Invalido);
            datos.Add(motivo, "liberada", Esperado.SinCambios);
            datos.Add(motivo, "expirada", Esperado.SinCambios);
        }

        // No recogido: solo confirmadas de un pedido de recojo.
        datos.Add(MotivoDeLiberacion.NoRecogido, "pendiente", Esperado.Invalido);
        datos.Add(MotivoDeLiberacion.NoRecogido, "confirmada", Esperado.Libera);
        datos.Add(MotivoDeLiberacion.NoRecogido, "cerrada-traslado", Esperado.Anula);
        datos.Add(MotivoDeLiberacion.NoRecogido, "cerrada-entrega", Esperado.Invalido);
        datos.Add(MotivoDeLiberacion.NoRecogido, "cerrada-reparto", Esperado.Invalido);
        datos.Add(MotivoDeLiberacion.NoRecogido, "liberada", Esperado.SinCambios);
        datos.Add(MotivoDeLiberacion.NoRecogido, "expirada", Esperado.SinCambios);
        return datos;
    }

    [Theory]
    [MemberData(nameof(Matriz))]
    [Trait("Criterio", "AC-SEL-07")]
    public void La_matriz_motivo_por_estado_de_D15(MotivoDeLiberacion motivo, string estado, Esperado esperado)
    {
        var decision = ReglaDeLiberacion.Decidir(motivo, TipoDeEntrega.Recojo, [LineaEn(estado)]);

        Assert.Equal(esperado, Clasificar(decision));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-07")]
    [InlineData(MotivoDeLiberacion.CheckoutCancelado, false)]
    [InlineData(MotivoDeLiberacion.PagoVencido, false)]
    [InlineData(MotivoDeLiberacion.Reemplazo, false)]
    [InlineData(MotivoDeLiberacion.PedidoAnulado, true)]
    [InlineData(MotivoDeLiberacion.CanceladoPorElCliente, true)]
    [InlineData(MotivoDeLiberacion.FallaPosteriorAlPago, true)]
    [InlineData(MotivoDeLiberacion.NoRecogido, true)]
    public void Solo_los_motivos_que_anulan_el_pedido_invocan_los_traslados_y_marcan_la_anulacion(MotivoDeLiberacion motivo, bool anula)
    {
        Assert.Equal(anula, ReglaDeLiberacion.AnulaElPedido(motivo));
        Assert.Equal(anula, ReglaDeLiberacion.Decidir(motivo, TipoDeEntrega.Recojo, [LineaEn("liberada")]).AnulaElPedido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-22")]
    public void No_recogido_en_un_pedido_a_domicilio_se_rechaza_y_no_toca_nada()
    {
        var decision = ReglaDeLiberacion.Decidir(MotivoDeLiberacion.NoRecogido, TipoDeEntrega.Domicilio, [LineaEn("confirmada")]);

        Assert.True(decision.EstadoInvalido);
        Assert.Empty(decision.ALiberar);
        Assert.Empty(decision.AAnular);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public void Anular_con_una_mezcla_libera_lo_liberable_y_marca_la_cerrada_por_traslado_aunque_haya_una_entregada()
    {
        var pendiente = LineaEn("pendiente");
        var confirmada = LineaEn("confirmada");
        var traslado = LineaEn("cerrada-traslado");
        var entregada = LineaEn("cerrada-entrega");

        var decision = ReglaDeLiberacion.Decidir(MotivoDeLiberacion.PedidoAnulado, TipoDeEntrega.Recojo, [pendiente, confirmada, traslado, entregada]);

        Assert.False(decision.EstadoInvalido);
        Assert.Equal([pendiente, confirmada], decision.ALiberar);
        Assert.Equal([traslado], decision.AAnular);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-07")]
    public void Una_linea_cerrada_por_traslado_ya_anulada_no_se_vuelve_a_marcar_y_repetir_no_cambia_nada()
    {
        var traslado = LineaEn("cerrada-traslado");
        traslado.Anular(DateTimeOffset.UnixEpoch);

        var decision = ReglaDeLiberacion.Decidir(MotivoDeLiberacion.PedidoAnulado, TipoDeEntrega.Recojo, [traslado]);

        Assert.True(decision.SinCambios);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-17")]
    public void Cancelar_el_checkout_con_un_pedido_pendiente_libera_todas_sus_pendientes_y_no_anula_el_pedido()
    {
        var a = LineaEn("pendiente");
        var b = LineaEn("pendiente");

        var decision = ReglaDeLiberacion.Decidir(MotivoDeLiberacion.CheckoutCancelado, TipoDeEntrega.Domicilio, [a, b]);

        Assert.Equal([a, b], decision.ALiberar);
        Assert.False(decision.AnulaElPedido);
        Assert.Empty(decision.AAnular);
    }

    // --- el contador de reemplazos ---

    [Theory]
    [Trait("Criterio", "AC-SEL-37")]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(-1, false)]
    public void El_pedido_admite_hasta_tres_reemplazos(int actuales, bool admite)
    {
        Assert.Equal(admite, ContadorDeReemplazos.Admite(actuales));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-38")]
    public void Cada_reemplazo_aceptado_suma_uno_y_el_cuarto_consecutivo_no_se_admite()
    {
        var cuenta = 0;
        for (var i = 0; i < ContadorDeReemplazos.Maximo; i++)
        {
            cuenta = ContadorDeReemplazos.Siguiente(cuenta);
        }

        Assert.Equal(3, cuenta);
        Assert.False(ContadorDeReemplazos.Admite(cuenta));
        Assert.Throws<InvalidOperationException>(() => ContadorDeReemplazos.Siguiente(cuenta));
    }
}
