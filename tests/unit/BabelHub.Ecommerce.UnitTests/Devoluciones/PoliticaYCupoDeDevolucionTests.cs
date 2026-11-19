using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>Los plazos con sus bordes en instantes (Lima no tiene horario de verano), el monto de una línea y el cupo de unidades (T02; DEV-02, DEV-03, DEV-04, DEV-05, DEV-06, DEV-08, DEV-14, DEV-15, DEV-18, DEV-19).</summary>
public sealed class PoliticaYCupoDeDevolucionTests
{
    private static readonly DateTimeOffset Base = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    // ---------- Plazos ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-02")]
    [Trait("Criterio", "AC-DEV-14")]
    public void Un_impreso_entregado_hace_15_dias_exactos_se_admite_y_a_15_dias_y_una_hora_no()
    {
        Assert.True(PoliticaDeDevolucion.SolicitudDeImpresoEnPlazo(Base, Base.AddDays(15)));
        Assert.False(PoliticaDeDevolucion.SolicitudDeImpresoEnPlazo(Base, Base.AddDays(15).AddHours(1)));
        Assert.False(PoliticaDeDevolucion.SolicitudDeImpresoEnPlazo(Base, Base.AddDays(15).AddTicks(1)));
        Assert.True(PoliticaDeDevolucion.SolicitudDeImpresoEnPlazo(Base, Base.AddDays(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-13")]
    public void La_recepcion_vence_a_mas_de_10_dias_y_a_10_dias_exactos_no()
    {
        Assert.False(PoliticaDeDevolucion.RecepcionVencida(Base, Base.AddDays(10)));
        Assert.True(PoliticaDeDevolucion.RecepcionVencida(Base, Base.AddDays(10).AddHours(1)));
        Assert.True(PoliticaDeDevolucion.RecepcionVencida(Base, Base.AddDays(10).AddTicks(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-22")]
    public void El_reembolso_automatico_admite_14_dias_exactos_y_no_mas()
    {
        Assert.True(PoliticaDeDevolucion.ReembolsoAutomaticoEnPlazo(Base, Base.AddDays(14)));
        Assert.False(PoliticaDeDevolucion.ReembolsoAutomaticoEnPlazo(Base, Base.AddDays(14).AddHours(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-24")]
    public void El_reporte_admite_30_dias_exactos_y_no_mas()
    {
        Assert.True(PoliticaDeDevolucion.ReporteEnPlazo(Base, Base.AddDays(30)));
        Assert.False(PoliticaDeDevolucion.ReporteEnPlazo(Base, Base.AddDays(30).AddHours(1)));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-14")]
    public void Los_plazos_se_cuentan_en_instantes_sin_importar_el_desfase_con_que_llegan()
    {
        var enLima = new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.FromHours(-5));

        Assert.True(PoliticaDeDevolucion.SolicitudDeImpresoEnPlazo(enLima, Base.AddDays(15)));
        Assert.False(PoliticaDeDevolucion.SolicitudDeImpresoEnPlazo(enLima, Base.AddDays(15).AddMinutes(1)));
    }

    // ---------- Monto ----------

    [Theory]
    [Trait("Criterio", "AC-DEV-22")]
    [InlineData("129.90", 3, "389.70")]
    [InlineData("10.005", 1, "10.01")]
    [InlineData("0.333", 3, "1.00")]
    [InlineData("49.00", 1, "49.00")]
    public void El_monto_es_el_precio_unitario_por_la_cantidad_redondeado_al_centimo(string precio, int cantidad, string esperado)
    {
        Assert.Equal(decimal.Parse(esperado, System.Globalization.CultureInfo.InvariantCulture), PoliticaDeDevolucion.Monto(decimal.Parse(precio, System.Globalization.CultureInfo.InvariantCulture), cantidad));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-22")]
    public void Un_precio_negativo_o_una_cantidad_menor_que_uno_no_tienen_monto()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PoliticaDeDevolucion.Monto(-1m, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PoliticaDeDevolucion.Monto(10m, 0));
    }

    [Theory]
    [Trait("Criterio", "AC-DEV-22")]
    [InlineData("50.00", "100.00", "40.00", "50.00")]
    [InlineData("50.00", "100.00", "70.00", "30.00")]
    [InlineData("50.00", "100.00", "100.00", "0.00")]
    [InlineData("50.00", "100.00", "120.00", "0.00")]
    public void El_ultimo_reembolso_parcial_se_ajusta_al_saldo_de_la_linea_y_nunca_la_supera(string monto, string importe, string yaReembolsado, string esperado)
    {
        var d = (string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(d(esperado), PoliticaDeDevolucion.AjustarAlSaldo(d(monto), d(importe), d(yaReembolsado)));
    }

    // ---------- Cupo ----------

    [Fact]
    [Trait("Criterio", "AC-DEV-03")]
    public void El_cupo_compromete_unidades_hasta_lo_comprado_y_rechaza_el_exceso_sin_cambios()
    {
        var cupo = CupoDeLinea.Crear(Guid.NewGuid(), 3);

        Assert.True(cupo.Comprometer(2));
        Assert.False(cupo.Comprometer(2));
        Assert.Equal(2, cupo.CantidadComprometida);
        Assert.True(cupo.Comprometer(1));
        Assert.Equal(0, cupo.Disponible);
        Assert.False(cupo.Comprometer(1));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-18")]
    public void Una_linea_rechazada_devuelve_su_cantidad_y_se_puede_volver_a_pedir()
    {
        var cupo = CupoDeLinea.Crear(Guid.NewGuid(), 3);
        cupo.Comprometer(3);

        Assert.False(cupo.Comprometer(1));
        Assert.True(cupo.Devolver(3));
        Assert.Equal(3, cupo.Disponible);
        Assert.True(cupo.Comprometer(3));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-18")]
    public void Nunca_se_devuelve_mas_de_lo_comprometido()
    {
        var cupo = CupoDeLinea.Crear(Guid.NewGuid(), 3);
        cupo.Comprometer(1);

        Assert.False(cupo.Devolver(2));
        Assert.Equal(1, cupo.CantidadComprometida);
        Assert.Throws<ArgumentOutOfRangeException>(() => cupo.Comprometer(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => cupo.Devolver(0));
    }

    [Fact]
    [Trait("Criterio", "AC-DEV-03")]
    public void Un_cupo_guardado_incoherente_se_rechaza_al_leerlo()
    {
        Assert.Equal(2, CupoDeLinea.Rehidratar(Guid.NewGuid(), 3, 2).CantidadComprometida);
        Assert.Throws<ArgumentException>(() => CupoDeLinea.Rehidratar(Guid.NewGuid(), 3, 4));
        Assert.Throws<ArgumentException>(() => CupoDeLinea.Rehidratar(Guid.NewGuid(), 3, -1));
        Assert.Throws<ArgumentException>(() => CupoDeLinea.Rehidratar(Guid.Empty, 3, 0));
        Assert.Throws<ArgumentException>(() => CupoDeLinea.Crear(Guid.Empty, 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => CupoDeLinea.Crear(Guid.NewGuid(), 0));
    }

    [Fact]
    public void Dos_solicitudes_simultaneas_sobre_la_misma_unidad_solo_comprometen_a_una_con_el_cupo_protegido_por_un_candado()
    {
        var cupo = CupoDeLinea.Crear(Guid.NewGuid(), 1);
        var exitos = 0;

        Parallel.For(0, 20, _ =>
        {
            lock (cupo)
            {
                if (cupo.Comprometer(1))
                {
                    Interlocked.Increment(ref exitos);
                }
            }
        });

        // La atomicidad real es del UPDATE condicional de la infraestructura (T21); aquí se fija la regla.
        Assert.Equal(1, exitos);
    }
}
