using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.UnitTests.Pedidos;

/// <summary>T01 (PED-02, PED-19, PED-30, AC-PED-02): la máquina de estados de una línea impresa y de una digital, y su orden.</summary>
public sealed class EstadosDeLineaTests
{
    private static readonly ContextoDeLinea ConTraslado = new(RequiereTraslado: true, EsDeRecojo: true);
    private static readonly ContextoDeLinea SinTrasladoRecojo = new(RequiereTraslado: false, EsDeRecojo: true);
    private static readonly ContextoDeLinea Domicilio = new(RequiereTraslado: false, EsDeRecojo: false);

    [Fact]
    [Trait("Criterio", "AC-PED-02")]
    public void Una_linea_confirmada_no_pasa_directamente_a_entregada()
    {
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.Entregado, SinTrasladoRecojo));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.ListoParaRecojo, SinTrasladoRecojo));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.Despachado, Domicilio));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-03")]
    public void El_ciclo_de_una_linea_con_traslado_pasa_por_transito_recibida_y_preparacion_y_sin_traslado_salta_directo_a_preparacion()
    {
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.EnTransito, ConTraslado));
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.EnTransito, EstadoDeLineaImpresa.Recibido, ConTraslado));
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Recibido, EstadoDeLineaImpresa.EnPreparacion, ConTraslado));
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.EnPreparacion, SinTrasladoRecojo));

        // Una línea con traslado no se prepara sin recibirse y una sin traslado no pasa por el tránsito (PED-10, PED-19).
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.EnPreparacion, ConTraslado));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.EnTransito, EstadoDeLineaImpresa.EnPreparacion, ConTraslado));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.EnTransito, SinTrasladoRecojo));
    }

    [Fact]
    public void En_recojo_la_preparacion_termina_en_listo_para_recojo_y_en_domicilio_en_despachado_nunca_al_reves()
    {
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.ListoParaRecojo, SinTrasladoRecojo));
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.Despachado, Domicilio));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.Despachado, SinTrasladoRecojo));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.ListoParaRecojo, Domicilio));
    }

    [Fact]
    public void Se_entrega_desde_listo_para_recojo_o_desde_despachado_y_un_no_recogido_tambien_se_entrega()
    {
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.Entregado, SinTrasladoRecojo));
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Despachado, EstadoDeLineaImpresa.Entregado, Domicilio));
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.NoRecogido, EstadoDeLineaImpresa.Entregado, SinTrasladoRecojo));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.Entregado, SinTrasladoRecojo));
    }

    [Fact]
    public void Solo_una_linea_lista_para_recojo_de_un_pedido_de_recojo_pasa_a_no_recogida()
    {
        Assert.True(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.NoRecogido, SinTrasladoRecojo));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.NoRecogido, Domicilio));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.EnPreparacion, EstadoDeLineaImpresa.NoRecogido, SinTrasladoRecojo));
    }

    [Theory]
    [InlineData(EstadoDeLineaImpresa.Confirmado, true)]
    [InlineData(EstadoDeLineaImpresa.EnTransito, true)]
    [InlineData(EstadoDeLineaImpresa.Recibido, true)]
    [InlineData(EstadoDeLineaImpresa.EnPreparacion, true)]
    [InlineData(EstadoDeLineaImpresa.ListoParaRecojo, true)]
    [InlineData(EstadoDeLineaImpresa.NoRecogido, true)]
    [InlineData(EstadoDeLineaImpresa.Despachado, false)]
    [InlineData(EstadoDeLineaImpresa.Entregado, false)]
    [InlineData(EstadoDeLineaImpresa.Anulado, false)]
    public void Se_anula_lo_que_no_salio_y_nada_mas(EstadoDeLineaImpresa desde, bool seAnula)
    {
        Assert.Equal(seAnula, MaquinaDeEstadosDeLinea.Permite(desde, EstadoDeLineaImpresa.Anulado, ConTraslado));
        Assert.Equal(seAnula, desde.SeAnula());
    }

    [Fact]
    [Trait("Criterio", "AC-PED-02")]
    public void Entregado_y_anulado_son_terminales_y_una_devolucion_posterior_no_cambia_el_estado()
    {
        foreach (var hacia in Enum.GetValues<EstadoDeLineaImpresa>())
        {
            Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Entregado, hacia, Domicilio));
            Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Anulado, hacia, Domicilio));
        }

        Assert.True(EstadoDeLineaImpresa.Entregado.EsTerminal());
        Assert.True(EstadoDeLineaImpresa.Anulado.EsTerminal());
        Assert.False(EstadoDeLineaImpresa.NoRecogido.EsTerminal());
    }

    [Fact]
    public void Un_estado_no_pasa_a_si_mismo_ni_retrocede()
    {
        foreach (var estado in Enum.GetValues<EstadoDeLineaImpresa>())
        {
            Assert.False(MaquinaDeEstadosDeLinea.Permite(estado, estado, ConTraslado));
        }

        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.Recibido, EstadoDeLineaImpresa.EnTransito, ConTraslado));
        Assert.False(MaquinaDeEstadosDeLinea.Permite(EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.EnPreparacion, SinTrasladoRecojo));
    }

    [Fact]
    [Trait("Criterio", "AC-PED-12")]
    public void El_orden_de_una_linea_impresa_es_confirmado_transito_recibido_preparacion_listo_o_despachado_y_entregado()
    {
        Assert.Equal(
            [1, 2, 3, 4, 5, 5, 6],
            new[]
            {
                EstadoDeLineaImpresa.Confirmado, EstadoDeLineaImpresa.EnTransito, EstadoDeLineaImpresa.Recibido, EstadoDeLineaImpresa.EnPreparacion,
                EstadoDeLineaImpresa.ListoParaRecojo, EstadoDeLineaImpresa.Despachado, EstadoDeLineaImpresa.Entregado,
            }.Select(e => e.Orden()));
        Assert.Null(EstadoDeLineaImpresa.Anulado.OrdenONulo());
        Assert.Equal(5, EstadoDeLineaImpresa.NoRecogido.Orden());
    }

    [Fact]
    public void Una_linea_digital_nace_disponible_y_solo_puede_anularse()
    {
        Assert.True(MaquinaDeEstadosDeLinea.PermiteDigital(EstadoDeLineaDigital.Disponible, EstadoDeLineaDigital.Anulado));
        Assert.False(MaquinaDeEstadosDeLinea.PermiteDigital(EstadoDeLineaDigital.Anulado, EstadoDeLineaDigital.Disponible));
        Assert.False(MaquinaDeEstadosDeLinea.PermiteDigital(EstadoDeLineaDigital.Disponible, EstadoDeLineaDigital.Disponible));
    }
}
