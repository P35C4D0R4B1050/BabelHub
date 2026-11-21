using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Domain.Web.Indice;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>
/// El generador del índice infinito (web-indice-infinito T03; IND-01, IND-02, IND-06, AC-IND-01, D02): FNV-1a de 64 bits que siembra un SplitMix64, 20 líneas de 70 símbolos de un alfabeto de 30. Cada pieza se comprueba con vectores de referencia
/// publicados, y un <b>patrón de oro</b> (calculado aparte, con otra implementación) falla si alguien cambia el algoritmo: cambiaría todas las páginas ya compartidas.
/// </summary>
public sealed class GeneradorDePaginasDelIndiceTests
{
    private const string Alfabeto = "abcdefghijklmnñopqrstuvwxyz ,.";

    [Theory]
    [Trait("Criterio", "AC-IND-01")]
    [InlineData("", 0xcbf29ce484222325UL)]
    [InlineData("a", 0xaf63dc4c8601ec8cUL)]
    [InlineData("foobar", 0x85944171f73967e8UL)]
    public void FNV1a_de_64_bits_coincide_con_los_vectores_publicados(string texto, ulong esperado)
    {
        Assert.Equal(esperado, GeneradorDePaginasDelIndice.Fnv1a64(Encoding.UTF8.GetBytes(texto)));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public void SplitMix64_con_semilla_cero_coincide_con_los_vectores_publicados()
    {
        ulong estado = 0;

        Assert.Equal(0xE220A8397B1DCDAFUL, GeneradorDePaginasDelIndice.SplitMix64(ref estado));
        Assert.Equal(0x6E789E6AA1B965F4UL, GeneradorDePaginasDelIndice.SplitMix64(ref estado));
        Assert.Equal(0x06C45D188009454FUL, GeneradorDePaginasDelIndice.SplitMix64(ref estado));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public void Dos_llamadas_con_la_misma_direccion_dan_exactamente_el_mismo_texto()
    {
        Assert.Equal(GeneradorDePaginasDelIndice.Generar("s1-p1-e1-v1-pg1"), GeneradorDePaginasDelIndice.Generar("s1-p1-e1-v1-pg1"));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public void Patron_de_oro_de_una_direccion_fija_no_cambia()
    {
        // Calculado con otra implementación (BigInt de Node.js) a partir de las constantes publicadas: si cambia, cambian todas las páginas ya compartidas (R01).
        var pagina = GeneradorDePaginasDelIndice.Generar("s1-p1-e1-v1-pg1");
        var lineas = pagina.Split('\n');

        Assert.Equal("implcrjrmolsrñflijp ,,zhqoyjddjzpiwdn.fkktblxtnu qdxkttjrklgkv,pgftbqk", lineas[0]);
        Assert.Equal("xfhbhczuimnbayhicptggzcblkztdfdzogdw,yhzgcjewpebpx.ilyfdvkgf.hry,i ltv", lineas[1]);
        Assert.Equal("xhhly,svozlqxyyqqoe hjzodteqñqrnxbñnujñxn mmwklk  jo  .fnmtzuwclfoelpx", lineas[19]);
        Assert.Equal("8a946894dfc3b366e158395466f11aae7abed550d01ad93cf3eb3f08b7d8f6f3", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(pagina))));
    }

    [Theory]
    [Trait("Criterio", "AC-IND-01")]
    [InlineData("a")]
    [InlineData("s40-p4-e5-v32-pg410")]
    [InlineData("0000")]
    public void La_pagina_tiene_veinte_lineas_de_setenta_simbolos_del_alfabeto_sin_salto_final(string direccion)
    {
        var pagina = GeneradorDePaginasDelIndice.Generar(direccion);
        var lineas = pagina.Split('\n');

        Assert.Equal(20, lineas.Length);
        Assert.All(lineas, l => Assert.Equal(70, l.Length));
        Assert.All(pagina.Where(c => c != '\n'), c => Assert.Contains(c, Alfabeto));
        Assert.False(pagina.EndsWith('\n'));
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public void El_alfabeto_no_tiene_marcado_ni_enlaces()
    {
        Assert.Equal(30, Alfabeto.Length);
        Assert.DoesNotContain(Alfabeto, c => c is '<' or '>' or '&' or '/' or ':' or '"' or '\'');
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public void Direcciones_distintas_dan_textos_distintos()
    {
        var paginas = Enumerable.Range(1, 200).Select(i => GeneradorDePaginasDelIndice.Generar($"s1-p1-e1-v1-pg{i}")).ToHashSet();

        Assert.Equal(200, paginas.Count);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public void El_resultado_no_cambia_con_la_cultura_del_hilo()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            var turca = GeneradorDePaginasDelIndice.Generar("s1-p1-e1-v1-pg1");
            CultureInfo.CurrentCulture = new CultureInfo("es-PE");
            var peruana = GeneradorDePaginasDelIndice.Generar("s1-p1-e1-v1-pg1");

            Assert.Equal(turca, peruana);
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IND-01")]
    public void El_generador_no_guarda_estado_entre_llamadas_ni_entre_hilos()
    {
        var esperada = GeneradorDePaginasDelIndice.Generar("s7-p2-e3-v9-pg77");

        var paginas = Enumerable.Range(0, 64).AsParallel().Select(_ => GeneradorDePaginasDelIndice.Generar("s7-p2-e3-v9-pg77")).ToList();

        Assert.All(paginas, p => Assert.Equal(esperada, p));
    }
}
