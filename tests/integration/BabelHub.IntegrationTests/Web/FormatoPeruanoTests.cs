using BabelHub.Ecommerce.Api.Web.Formato;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Importes en soles y fechas con formato peruano (WEB-14, docs/06 §08: «S/ 1 234,50», día/mes/año y hora de Lima).</summary>
public sealed class FormatoPeruanoTests
{
    private const string Nb = " ";

    [Fact]
    [Trait("Criterio", "AC-WEB-10")]
    public void Un_importe_de_1234_5_aparece_en_soles_con_el_formato_peruano() =>
        Assert.Equal($"S/{Nb}1{Nb}234,50", FormatoPeruano.Soles(1234.5m));

    [Theory]
    [InlineData(0, "S/ 0,00")]
    [InlineData(12, "S/ 12,00")]
    [InlineData(111.9, "S/ 111,90")]
    [InlineData(999.995, "S/ 1 000,00")]
    [InlineData(1234567.891, "S/ 1 234 567,89")]
    [InlineData(-5.5, "-S/ 5,50")]
    [Trait("Criterio", "AC-WEB-10")]
    public void Los_importes_llevan_siempre_dos_decimales_y_separador_de_miles(double importe, string esperado) =>
        Assert.Equal(esperado, FormatoPeruano.Soles((decimal)importe));

    [Fact]
    [Trait("Criterio", "AC-WEB-10")]
    public void El_formato_no_depende_de_la_cultura_del_servidor()
    {
        var actual = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("en-US");
            Assert.Equal($"S/{Nb}1{Nb}234,50", FormatoPeruano.Soles(1234.5m));
            Assert.Equal("14/11/2026", FormatoPeruano.Fecha(new DateOnly(2026, 11, 14)));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = actual;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-WEB-10")]
    public void Las_fechas_son_dia_mes_anio_y_la_hora_es_la_de_Lima()
    {
        // 2026-11-14 20:42 UTC es 15:42 en Lima (UTC-5, sin horario de verano).
        var instante = new DateTimeOffset(2026, 11, 14, 20, 42, 0, TimeSpan.Zero);

        Assert.Equal("14/11/2026", FormatoPeruano.Fecha(new DateOnly(2026, 11, 14)));
        Assert.Equal("14/11/2026 15:42", FormatoPeruano.FechaYHora(instante));
        Assert.Equal("15:42", FormatoPeruano.Hora(instante));
    }
}
