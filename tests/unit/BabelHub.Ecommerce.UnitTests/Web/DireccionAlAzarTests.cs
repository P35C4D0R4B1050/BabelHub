using BabelHub.Ecommerce.Domain.Web.Indice;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>La dirección al azar del índice infinito (web-indice-infinito T04; IND-03, AC-IND-02, D05): la forma <c>s{1–40}-p{1–4}-e{1–5}-v{1–32}-pg{1–410}</c>, siempre válida y de menos de 64 caracteres, con los extremos de cada rango.</summary>
public sealed class DireccionAlAzarTests
{
    /// <summary>Una función de enteros que siempre elige el mínimo (0) o el máximo (n - 1) de cada rango.</summary>
    private static int Minimo(int n) => 0;

    private static int Maximo(int n) => n - 1;

    [Fact]
    [Trait("Criterio", "AC-IND-02")]
    public void Con_los_minimos_de_cada_rango_la_forma_es_s1_p1_e1_v1_pg1()
    {
        Assert.Equal("s1-p1-e1-v1-pg1", DireccionAlAzar.Crear(Minimo));
    }

    [Theory]
    [Trait("Criterio", "AC-IND-02")]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(1000)]
    public void Siempre_es_una_direccion_valida_de_menos_de_64_caracteres(int semilla)
    {
        var azar = new Random(semilla);

        for (var i = 0; i < 500; i++)
        {
            var direccion = DireccionAlAzar.Crear(azar.Next);

            Assert.Equal(EstadoDeDireccion.Valida, DireccionDelIndice.Evaluar(direccion).Estado);
            Assert.True(direccion.Length < 64);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IND-02")]
    public void Una_funcion_fuera_de_rango_se_rechaza_en_lugar_de_producir_una_direccion_fuera_de_forma()
    {
        Assert.Throws<InvalidOperationException>(() => DireccionAlAzar.Crear(n => n));
        Assert.Throws<InvalidOperationException>(() => DireccionAlAzar.Crear(n => -1));
    }

    [Theory]
    [Trait("Criterio", "AC-IND-02")]
    [InlineData("s1-p1-e1-v1-pg1", "s1-p1-e1-v1-pg2")]
    [InlineData("s12-p3-e4-v5-pg409", "s12-p3-e4-v5-pg410")]
    [InlineData("s12-p3-e4-v5-pg410", "s12-p3-e4-v5-pg1")]
    public void Avanzar_la_pagina_da_una_direccion_distinta_de_la_misma_forma(string actual, string esperada)
    {
        var siguiente = DireccionAlAzar.AvanzarPagina(actual);

        Assert.Equal(esperada, siguiente);
        Assert.NotEqual(actual, siguiente);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-02")]
    [InlineData("abc")]
    [InlineData("s1-p1-e1-v1-pg0")]
    [InlineData("s1-p1-e1-v1-pg411")]
    [InlineData("s41-p1-e1-v1-pg1")]
    [InlineData("s1-p1-e1-v1-pg1\n")]
    public void Solo_una_direccion_de_la_forma_se_puede_avanzar_y_las_demas_nunca_coinciden_con_una_al_azar(string direccion)
    {
        Assert.False(DireccionAlAzar.TieneLaForma(direccion));
        Assert.Throws<ArgumentException>(() => DireccionAlAzar.AvanzarPagina(direccion));
    }
}
