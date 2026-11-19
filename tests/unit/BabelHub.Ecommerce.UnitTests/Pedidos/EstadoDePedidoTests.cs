using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T02 (PED-20, PED-30, AC-PED-12, AC-PED-21): el estado del pedido se deriva de sus líneas con una función pura.</summary>
public sealed class EstadoDePedidoTests
{
    private static EstadoDePedido De(EstadoDeLineaImpresa[] impresas, params EstadoDeLineaDigital[] digitales) => DerivacionDelEstadoDelPedido.Derivar(impresas, digitales);

    [Fact]
    public void El_pedido_esta_anulado_solo_si_todas_sus_lineas_lo_estan()
    {
        Assert.Equal(EstadoDePedido.Anulado, De([EstadoDeLineaImpresa.Anulado, EstadoDeLineaImpresa.Anulado], EstadoDeLineaDigital.Anulado));
        Assert.Equal(EstadoDePedido.Anulado, De([EstadoDeLineaImpresa.Anulado]));
        Assert.Equal(EstadoDePedido.Anulado, De([], EstadoDeLineaDigital.Anulado));
        Assert.NotEqual(EstadoDePedido.Anulado, De([EstadoDeLineaImpresa.Anulado], EstadoDeLineaDigital.Disponible));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-21")]
    public void Una_linea_anulada_no_impide_que_el_pedido_quede_entregado_cuando_las_demas_se_entregan()
    {
        Assert.Equal(EstadoDePedido.Entregado, De([EstadoDeLineaImpresa.Anulado, EstadoDeLineaImpresa.Entregado]));
        Assert.Equal(EstadoDePedido.Entregado, De([EstadoDeLineaImpresa.Entregado, EstadoDeLineaImpresa.Entregado], EstadoDeLineaDigital.Disponible));
    }

    [Fact]
    public void Un_pedido_solo_digital_con_el_acceso_concedido_esta_entregado()
    {
        Assert.Equal(EstadoDePedido.Entregado, De([], EstadoDeLineaDigital.Disponible, EstadoDeLineaDigital.Disponible));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-12")]
    public void Un_digital_disponible_con_un_impreso_en_transito_figura_en_transito()
    {
        Assert.Equal(EstadoDePedido.EnTransito, De([EstadoDeLineaImpresa.EnTransito], EstadoDeLineaDigital.Disponible));
    }

    [Fact]
    public void Sin_otro_caso_manda_la_linea_impresa_menos_avanzada_y_se_ignoran_las_anuladas()
    {
        Assert.Equal(EstadoDePedido.Confirmado, De([EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.Entregado]));
        Assert.Equal(EstadoDePedido.EnTransito, De([EstadoDeLineaImpresa.EnTransito, EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.Anulado]));
        Assert.Equal(EstadoDePedido.Recibido, De([EstadoDeLineaImpresa.Recibido, EstadoDeLineaImpresa.EnPreparacion]));
        Assert.Equal(EstadoDePedido.EnPreparacion, De([EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.ListoParaRecojo]));
        Assert.Equal(EstadoDePedido.ListoParaRecojo, De([EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.Entregado]));
        Assert.Equal(EstadoDePedido.Despachado, De([EstadoDeLineaImpresa.Despachado, EstadoDeLineaImpresa.Entregado]));
    }

    [Fact]
    public void Un_pedido_con_una_linea_no_recogida_y_las_demas_entregadas_o_no_recogidas_esta_no_recogido()
    {
        Assert.Equal(EstadoDePedido.NoRecogido, De([EstadoDeLineaImpresa.NoRecogido, EstadoDeLineaImpresa.Entregado]));
        Assert.Equal(EstadoDePedido.NoRecogido, De([EstadoDeLineaImpresa.NoRecogido, EstadoDeLineaImpresa.NoRecogido]));
        Assert.Equal(EstadoDePedido.NoRecogido, De([EstadoDeLineaImpresa.NoRecogido, EstadoDeLineaImpresa.Anulado], EstadoDeLineaDigital.Disponible));
    }

    [Fact]
    public void Si_otra_linea_sigue_pendiente_el_pedido_no_es_no_recogido_sino_el_de_la_menos_avanzada()
    {
        Assert.Equal(EstadoDePedido.EnPreparacion, De([EstadoDeLineaImpresa.NoRecogido, EstadoDeLineaImpresa.EnPreparacion]));
        Assert.Equal(EstadoDePedido.ListoParaRecojo, De([EstadoDeLineaImpresa.NoRecogido, EstadoDeLineaImpresa.ListoParaRecojo]));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-12")]
    public void El_filtro_por_estado_ve_solo_los_pedidos_derivados_a_ese_estado()
    {
        var pedidos = new (string Nombre, EstadoDeLineaImpresa[] Impresas, EstadoDeLineaDigital[] Digitales)[]
        {
            ("transito", [EstadoDeLineaImpresa.EnTransito], [EstadoDeLineaDigital.Disponible]),
            ("confirmado", [EstadoDeLineaImpresa.Confirmado], []),
            ("entregado", [EstadoDeLineaImpresa.Entregado], []),
        };

        var enTransito = pedidos.Where(p => DerivacionDelEstadoDelPedido.Derivar(p.Impresas, p.Digitales) == EstadoDePedido.EnTransito).Select(p => p.Nombre);

        Assert.Equal(["transito"], enTransito);
    }

    [Fact]
    public void Un_pedido_sin_ninguna_linea_no_se_deriva()
    {
        Assert.Throws<ArgumentException>(() => De([]));
    }

    [Fact]
    public void Los_estados_finales_son_entregado_y_anulado_y_dan_la_fecha_del_estado_final()
    {
        Assert.True(EstadoDePedido.Entregado.EsFinal());
        Assert.True(EstadoDePedido.Anulado.EsFinal());
        Assert.False(EstadoDePedido.NoRecogido.EsFinal());
        Assert.False(EstadoDePedido.EnPreparacion.EsFinal());
    }
}
