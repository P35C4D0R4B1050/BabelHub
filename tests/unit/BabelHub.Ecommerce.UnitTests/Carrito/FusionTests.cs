using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>
/// T02: <see cref="Fusion"/> es una función pura (CAR-24): suma las cantidades de una misma edición hasta el máximo disponible, deja las digitales en una, descarta las que la cuenta ya posee o dejaron de venderse, aplica los topes y
/// devuelve los ajustes con su motivo cerrado. Nunca quita ni reduce lo que la cuenta ya tenía.
/// </summary>
public sealed class FusionTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static Guid Edicion(int n) => new(n, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);

    private static LineaDeCarrito Impresa(int edicion, int cantidad) => LineaDeCarrito.Crear(Edicion(edicion), esDigital: false, Cantidad.Crear(cantidad), Ahora);

    private static LineaDeCarrito DigitalDe(int edicion) => LineaDeCarrito.Crear(Edicion(edicion), esDigital: true, Cantidad.Uno, Ahora);

    private static EdicionParaFusion Vendible(int edicion, int? maximo = null, bool digital = false) => new(Edicion(edicion), digital, EsVendible: true, maximo);

    private static IReadOnlyDictionary<Guid, EdicionParaFusion> Ediciones(params EdicionParaFusion[] ediciones) => ediciones.ToDictionary(e => e.EdicionId);

    private static ResultadoDeFusion Fusionar(IEnumerable<LineaDeCarrito> cuenta, IEnumerable<LineaDeCarrito> visitante, IReadOnlyDictionary<Guid, EdicionParaFusion> ediciones, params int[] poseidas) =>
        Fusion.Fusionar([.. cuenta], [.. visitante], ediciones, poseidas.Select(Edicion).ToHashSet());

    [Fact]
    [Trait("Criterio", "AC-CAR-22")]
    public void El_impreso_de_las_dos_partes_queda_en_el_maximo_disponible_y_el_digital_poseido_se_descarta_con_aviso_de_ambos()
    {
        var resultado = Fusionar(
            cuenta: [Impresa(1, 1)],
            visitante: [Impresa(1, 1), DigitalDe(2)],
            Ediciones(Vendible(1, maximo: 1), Vendible(2, digital: true)),
            poseidas: 2);

        var linea = Assert.Single(resultado.Lineas);
        Assert.Equal((Edicion(1), 1), (linea.EdicionId, linea.Cantidad.Valor));
        Assert.Equal(
            [(Edicion(1), MotivoDeAjusteDeFusion.RecortadaAlMaximo, 1), (Edicion(2), MotivoDeAjusteDeFusion.YaPoseida, 0)],
            resultado.Ajustes.Select(a => (a.EdicionId, a.Motivo, a.CantidadFinal)));
    }

    [Fact]
    public void Las_cantidades_de_una_misma_edicion_se_suman_si_el_maximo_lo_permite()
    {
        var resultado = Fusionar([Impresa(1, 2)], [Impresa(1, 3)], Ediciones(Vendible(1, maximo: 10)));

        Assert.Equal(5, Assert.Single(resultado.Lineas).Cantidad.Valor);
        Assert.Empty(resultado.Ajustes);
    }

    [Fact]
    public void Sin_maximo_conocido_porque_el_catalogo_no_pudo_confirmarlo_se_suma_sin_recortar()
    {
        var resultado = Fusionar([Impresa(1, 2)], [Impresa(1, 3)], Ediciones(Vendible(1, maximo: null)));

        Assert.Equal(5, Assert.Single(resultado.Lineas).Cantidad.Valor);
        Assert.Empty(resultado.Ajustes);
    }

    [Fact]
    public void Una_edicion_solo_del_visitante_se_agrega_y_una_solo_de_la_cuenta_no_se_toca()
    {
        var resultado = Fusionar([Impresa(1, 4)], [Impresa(2, 2)], Ediciones(Vendible(2, maximo: 5)));

        Assert.Equal([(Edicion(1), 4), (Edicion(2), 2)], resultado.Lineas.Select(l => (l.EdicionId, l.Cantidad.Valor)));
        Assert.Empty(resultado.Ajustes);
    }

    [Fact]
    public void Una_edicion_solo_del_visitante_se_recorta_al_maximo_disponible_y_sin_stock_se_descarta()
    {
        var resultado = Fusionar([], [Impresa(1, 5), Impresa(2, 2)], Ediciones(Vendible(1, maximo: 3), Vendible(2, maximo: 0)));

        var linea = Assert.Single(resultado.Lineas);
        Assert.Equal((Edicion(1), 3), (linea.EdicionId, linea.Cantidad.Valor));
        Assert.Equal(
            [(Edicion(1), MotivoDeAjusteDeFusion.RecortadaAlMaximo, 3), (Edicion(2), MotivoDeAjusteDeFusion.SinStock, 0)],
            resultado.Ajustes.Select(a => (a.EdicionId, a.Motivo, a.CantidadFinal)));
    }

    [Fact]
    public void Lo_que_la_cuenta_ya_tenia_no_se_reduce_aunque_el_maximo_sea_menor()
    {
        var resultado = Fusionar([Impresa(1, 4)], [Impresa(1, 2)], Ediciones(Vendible(1, maximo: 3)));

        Assert.Equal(4, Assert.Single(resultado.Lineas).Cantidad.Valor);
        var ajuste = Assert.Single(resultado.Ajustes);
        Assert.Equal((MotivoDeAjusteDeFusion.RecortadaAlMaximo, 4), (ajuste.Motivo, ajuste.CantidadFinal));
    }

    [Fact]
    public void Una_digital_de_las_dos_partes_o_una_digital_ya_en_la_cuenta_queda_en_una_unidad_con_aviso()
    {
        var resultado = Fusionar([DigitalDe(1)], [DigitalDe(1), DigitalDe(2)], Ediciones(Vendible(1, digital: true), Vendible(2, digital: true)));

        Assert.Equal([(Edicion(1), 1), (Edicion(2), 1)], resultado.Lineas.Select(l => (l.EdicionId, l.Cantidad.Valor)));
        var ajuste = Assert.Single(resultado.Ajustes);
        Assert.Equal((Edicion(1), MotivoDeAjusteDeFusion.DigitalAUnaUnidad, 1), (ajuste.EdicionId, ajuste.Motivo, ajuste.CantidadFinal));
    }

    [Fact]
    public void Una_edicion_que_dejo_de_venderse_o_que_no_se_conoce_se_descarta_con_su_motivo()
    {
        var retirada = new EdicionParaFusion(Edicion(1), EsDigital: false, EsVendible: false, MaximoDisponible: null);

        var resultado = Fusionar([], [Impresa(1, 2), Impresa(2, 1)], Ediciones(retirada));

        Assert.Empty(resultado.Lineas);
        Assert.Equal(
            [(Edicion(1), MotivoDeAjusteDeFusion.NoVendible, 0), (Edicion(2), MotivoDeAjusteDeFusion.NoVendible, 0)],
            resultado.Ajustes.Select(a => (a.EdicionId, a.Motivo, a.CantidadFinal)));
    }

    [Fact]
    public void Una_edicion_que_dejo_de_venderse_no_quita_la_linea_que_la_cuenta_ya_tenia()
    {
        var retirada = new EdicionParaFusion(Edicion(1), EsDigital: false, EsVendible: false, MaximoDisponible: null);

        var resultado = Fusionar([Impresa(1, 2)], [Impresa(1, 1)], Ediciones(retirada));

        Assert.Equal(2, Assert.Single(resultado.Lineas).Cantidad.Valor);
        Assert.Equal(MotivoDeAjusteDeFusion.NoVendible, Assert.Single(resultado.Ajustes).Motivo);
    }

    [Fact]
    public void Una_suma_de_mas_de_20_unidades_se_recorta_al_tope_con_su_motivo()
    {
        var resultado = Fusionar([Impresa(1, 15)], [Impresa(1, 15)], Ediciones(Vendible(1, maximo: null)));

        var ajuste = Assert.Single(resultado.Ajustes);
        Assert.Equal(20, Assert.Single(resultado.Lineas).Cantidad.Valor);
        Assert.Equal((MotivoDeAjusteDeFusion.TopeDeUnidades, 20), (ajuste.Motivo, ajuste.CantidadFinal));
    }

    [Fact]
    public void El_tope_de_30_lineas_descarta_las_ultimas_del_visitante_y_nunca_las_de_la_cuenta()
    {
        var cuenta = Enumerable.Range(1, 29).Select(i => Impresa(i, 1)).ToList();
        var visitante = new[] { Impresa(100, 1), Impresa(101, 1), Impresa(102, 1) };
        var ediciones = Ediciones(Vendible(100), Vendible(101), Vendible(102));

        var resultado = Fusionar(cuenta, visitante, ediciones);

        Assert.Equal(CarritoDeDominio.MaximoDeLineas, resultado.Lineas.Count);
        Assert.Equal(Edicion(100), resultado.Lineas[^1].EdicionId);
        Assert.All(cuenta, l => Assert.Contains(resultado.Lineas, r => r.EdicionId == l.EdicionId));
        Assert.Equal(
            [(Edicion(101), MotivoDeAjusteDeFusion.TopeDeLineas, 0), (Edicion(102), MotivoDeAjusteDeFusion.TopeDeLineas, 0)],
            resultado.Ajustes.Select(a => (a.EdicionId, a.Motivo, a.CantidadFinal)));
    }

    [Fact]
    public void Una_fusion_sin_lineas_del_visitante_devuelve_las_de_la_cuenta_sin_ajustes()
    {
        var resultado = Fusionar([Impresa(1, 2), DigitalDe(2)], [], Ediciones());

        Assert.Equal([(Edicion(1), 2), (Edicion(2), 1)], resultado.Lineas.Select(l => (l.EdicionId, l.Cantidad.Valor)));
        Assert.Empty(resultado.Ajustes);
    }

    [Fact]
    public void Fusionar_es_una_funcion_pura_y_dos_veces_con_lo_mismo_da_lo_mismo()
    {
        var cuenta = new[] { Impresa(1, 2) };
        var visitante = new[] { Impresa(1, 1), Impresa(2, 3) };
        var ediciones = Ediciones(Vendible(1, maximo: 2), Vendible(2, maximo: 9));

        var primera = Fusionar(cuenta, visitante, ediciones);
        var segunda = Fusionar(cuenta, visitante, ediciones);

        Assert.Equal(primera.Lineas.Select(l => (l.EdicionId, l.Cantidad.Valor)), segunda.Lineas.Select(l => (l.EdicionId, l.Cantidad.Valor)));
        Assert.Equal(primera.Ajustes, segunda.Ajustes);
    }

    [Fact]
    public void Aplicar_la_fusion_deja_en_el_carrito_de_la_cuenta_las_lineas_resultantes_y_conserva_la_fecha_de_las_que_ya_estaban()
    {
        var cuenta = CarritoDeDominio.Crear(Guid.NewGuid(), PropietarioDelCarrito.DeCuenta(Guid.NewGuid()), Ahora);
        cuenta.Agregar(Edicion(1), esDigital: false, cantidad: 1, Ahora);
        var visitante = new[] { Impresa(1, 1), Impresa(2, 2) };
        var resultado = Fusion.Fusionar(cuenta.Lineas, visitante, Ediciones(Vendible(1, maximo: 5), Vendible(2, maximo: 5)), new HashSet<Guid>());
        var version = cuenta.Version;

        cuenta.AplicarFusion(resultado, Ahora.AddMinutes(5));

        Assert.Equal([(Edicion(1), 2), (Edicion(2), 2)], cuenta.Lineas.Select(l => (l.EdicionId, l.Cantidad.Valor)));
        Assert.Equal(Ahora, cuenta.Lineas.Single(l => l.EdicionId == Edicion(1)).AgregadaEn);
        Assert.Equal(Ahora.AddMinutes(5), cuenta.Lineas.Single(l => l.EdicionId == Edicion(2)).AgregadaEn);
        Assert.Equal(version + 1, cuenta.Version);
    }
}
