using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.UnitTests.Reservas;

/// <summary>T04: <c>SeleccionPorCercania</c> (SEL-02, SEL-23, SEL-24, SEL-25): recojo, domicilio, región sin fuente, plazos iguales, ruta sin definir y una línea nunca dividida.</summary>
public sealed class SeleccionPorCercaniaTests
{
    private static readonly SeleccionPorCercania Estrategia = new();

    private static CandidataDeFuente Fuente(string codigo, int vendible, int? plazo = null, bool destino = false, bool region = false) =>
        new(Guid.NewGuid(), codigo, vendible, destino, region, plazo);

    private static Seleccion Recojo(int cantidad, params CandidataDeFuente[] candidatas) =>
        Estrategia.Seleccionar(new SolicitudDeSeleccion(TipoDeEntrega.Recojo, cantidad, candidatas));

    private static Seleccion Domicilio(int cantidad, params CandidataDeFuente[] candidatas) =>
        Estrategia.Seleccionar(new SolicitudDeSeleccion(TipoDeEntrega.Domicilio, cantidad, candidatas));

    [Fact]
    [Trait("Criterio", "AC-SEL-10")]
    public void La_estrategia_predeterminada_se_llama_cercania_y_cumple_el_contrato_sustituible()
    {
        IEstrategiaDeSeleccion estrategia = new SeleccionPorCercania();

        Assert.Equal("cercania", estrategia.Nombre);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-02")]
    public void Recojo_con_vendible_en_la_sucursal_elegida_la_selecciona_sin_traslado()
    {
        var resultado = Recojo(1, Fuente("ayacucho", 2, 0, destino: true), Fuente("cusco", 9, 2));

        Assert.Equal(("ayacucho", false, (int?)null), (resultado.Codigo, resultado.ImplicaTraslado, resultado.PlazoEnDias));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-02")]
    public void Recojo_sin_vendible_en_la_sucursal_selecciona_la_de_menor_plazo_y_pide_traslado()
    {
        var resultado = Recojo(1, Fuente("ayacucho", 0, 0, destino: true), Fuente("cusco", 3, 2), Fuente("lima", 8, 4));

        Assert.Equal(("cusco", true, (int?)2), (resultado.Codigo, resultado.ImplicaTraslado, resultado.PlazoEnDias));
        Assert.Equal(11, resultado.DisponibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-23")]
    public void Recojo_la_ruta_sin_definir_se_trata_como_la_menos_cercana_aunque_tenga_mas_stock()
    {
        var resultado = Recojo(1, Fuente("ayacucho", 0, 0, destino: true), Fuente("lima", 50, null), Fuente("cusco", 1, 4));

        Assert.Equal(("cusco", (int?)4), (resultado.Codigo, resultado.PlazoEnDias));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-34")]
    public void Recojo_solo_con_rutas_sin_definir_elige_mayor_vendible_y_el_traslado_queda_a_confirmar()
    {
        var resultado = Recojo(1, Fuente("ayacucho", 0, 0, destino: true), Fuente("lima", 3, null), Fuente("cusco", 7, null));

        Assert.Equal(("cusco", true, (int?)null), (resultado.Codigo, resultado.ImplicaTraslado, resultado.PlazoEnDias));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-23")]
    public void Recojo_con_plazos_iguales_elige_el_mayor_vendible_y_luego_el_codigo_menor()
    {
        var porVendible = Recojo(1, Fuente("ayacucho", 0, 0, destino: true), Fuente("cusco", 3, 2), Fuente("lima", 8, 2));
        var porCodigo = Recojo(1, Fuente("ayacucho", 0, 0, destino: true), Fuente("lima", 5, 2), Fuente("cusco", 5, 2));

        Assert.Equal("lima", porVendible.Codigo);
        Assert.Equal("cusco", porCodigo.Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-34")]
    public void Una_linea_de_cinco_nunca_se_divide_entre_dos_fuentes_y_si_ninguna_tiene_cinco_falta_stock()
    {
        var resultado = Recojo(5, Fuente("ayacucho", 3, 0, destino: true), Fuente("cusco", 4, 2));

        Assert.False(resultado.HayStock);
        Assert.Null(resultado.FuenteId);
        Assert.Equal(7, resultado.DisponibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-34")]
    public void Una_ruta_definida_gana_a_una_sin_definir_cuando_las_dos_tienen_stock()
    {
        var resultado = Recojo(1, Fuente("ayacucho", 0, 0, destino: true), Fuente("arequipa", 9, null), Fuente("lima", 2, 4));

        Assert.Equal("lima", resultado.Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-23")]
    public void Domicilio_con_vendible_en_la_region_elige_la_fuente_de_la_region_sin_traslado()
    {
        var resultado = Domicilio(1, Fuente("cusco", 4, 0, region: true), Fuente("lima", 40, 3));

        Assert.Equal(("cusco", false, (int?)null), (resultado.Codigo, resultado.ImplicaTraslado, resultado.PlazoEnDias));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-23")]
    public void Domicilio_con_varias_fuentes_en_la_region_elige_mayor_vendible_y_luego_codigo_menor_antes_que_las_de_otras_regiones()
    {
        var porVendible = Domicilio(1, Fuente("cusco-norte", 4, 0, region: true), Fuente("cusco-sur", 6, 0, region: true), Fuente("lima", 99, 1));
        var porCodigo = Domicilio(1, Fuente("cusco-sur", 6, 0, region: true), Fuente("cusco-norte", 6, 0, region: true));

        Assert.Equal("cusco-sur", porVendible.Codigo);
        Assert.Equal("cusco-norte", porCodigo.Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-33")]
    public void Domicilio_sin_vendible_en_la_region_elige_la_de_menor_plazo_hacia_ella_y_no_genera_traslado()
    {
        var resultado = Domicilio(1, Fuente("cusco", 0, 0, region: true), Fuente("lima", 8, 3), Fuente("arequipa", 2, 1));

        Assert.Equal(("arequipa", false, (int?)null), (resultado.Codigo, resultado.ImplicaTraslado, resultado.PlazoEnDias));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-25")]
    public void Domicilio_en_una_region_sin_fuente_elige_el_mayor_vendible_y_con_empate_el_codigo_menor()
    {
        var mayor = Domicilio(1, Fuente("lima", 5), Fuente("cusco", 8));
        var empate = Domicilio(1, Fuente("lima", 5), Fuente("cusco", 5));

        Assert.Equal("cusco", mayor.Codigo);
        Assert.Equal("cusco", empate.Codigo);
        Assert.False(empate.ImplicaTraslado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-25")]
    public void Domicilio_con_dos_fuentes_fuera_de_la_region_y_plazos_iguales_aplica_vendible_y_codigo()
    {
        var resultado = Domicilio(1, Fuente("lima", 5, 2), Fuente("arequipa", 5, 2), Fuente("piura", 9, 2));

        Assert.Equal("piura", resultado.Codigo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-05")]
    public void Sin_candidatas_con_stock_suficiente_falta_stock_con_lo_que_hay_en_la_red()
    {
        var sinCandidatas = Domicilio(2);
        var sinAlcanzar = Domicilio(10, Fuente("lima", 4), Fuente("cusco", 5));

        Assert.False(sinCandidatas.HayStock);
        Assert.Equal(0, sinCandidatas.DisponibleEnLaRed);
        Assert.False(sinAlcanzar.HayStock);
        Assert.Equal(9, sinAlcanzar.DisponibleEnLaRed);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-05")]
    public void La_cantidad_debe_ser_positiva()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Recojo(0, Fuente("lima", 1, 0, destino: true)));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-10")]
    public void Otra_estrategia_se_sustituye_sin_cambiar_el_contrato()
    {
        IEstrategiaDeSeleccion siempreLaMayor = new SiempreLaDeMayorVendible();
        var solicitud = new SolicitudDeSeleccion(TipoDeEntrega.Recojo, 1, [Fuente("ayacucho", 5, 0, destino: true), Fuente("lima", 50, 4)]);

        Assert.Equal("ayacucho", Estrategia.Seleccionar(solicitud).Codigo);
        Assert.Equal("lima", siempreLaMayor.Seleccionar(solicitud).Codigo);
    }

    private sealed class SiempreLaDeMayorVendible : IEstrategiaDeSeleccion
    {
        public string Nombre => "mayor-vendible";

        public Seleccion Seleccionar(SolicitudDeSeleccion solicitud)
        {
            var elegida = solicitud.Candidatas.OrderByDescending(c => c.Vendible).First();
            return new Seleccion(elegida.FuenteId, elegida.Codigo, !elegida.EsElDestino, elegida.PlazoHaciaElDestinoEnDias, 0);
        }
    }
}
