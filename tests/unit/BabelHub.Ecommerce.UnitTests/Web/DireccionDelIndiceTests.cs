using BabelHub.Ecommerce.Domain.Web.Indice;

namespace BabelHub.Ecommerce.UnitTests.Web;

/// <summary>La dirección del índice infinito (web-indice-infinito T02; IND-07, AC-IND-06): válida solo de 1 a 64 caracteres de a-z, 0-9 y guion; el texto no válido nunca se guarda ni se repite.</summary>
public sealed class DireccionDelIndiceTests
{
    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData("a")]
    [InlineData("s12-p3-e4-v5-pg100")]
    [InlineData("-")]
    public void Una_direccion_de_letras_numeros_y_guiones_es_valida(string texto)
    {
        var resultado = DireccionDelIndice.Evaluar(texto);

        Assert.Equal(EstadoDeDireccion.Valida, resultado.Estado);
        Assert.Equal(texto, resultado.Direccion);
    }

    [Fact]
    [Trait("Criterio", "AC-IND-06")]
    public void El_borde_de_64_caracteres_es_valido_y_el_de_65_no()
    {
        Assert.Equal(EstadoDeDireccion.Valida, DireccionDelIndice.Evaluar(new string('a', 64)).Estado);
        Assert.Equal(EstadoDeDireccion.NoValida, DireccionDelIndice.Evaluar(new string('a', 65)).Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData("Abc")]
    [InlineData("sala-ñ")]
    [InlineData("a b")]
    [InlineData("a/b")]
    [InlineData("..")]
    [InlineData("<script>")]
    [InlineData("a\nb")]
    [InlineData("\u0000")]
    [InlineData("á")]
    public void Cualquier_otro_caracter_no_es_valido(string texto)
    {
        var resultado = DireccionDelIndice.Evaluar(texto);

        Assert.Equal(EstadoDeDireccion.NoValida, resultado.Estado);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData(null)]
    [InlineData("")]
    public void Una_direccion_vacia_se_distingue_de_una_no_valida(string? texto)
    {
        var resultado = DireccionDelIndice.Evaluar(texto);

        Assert.Equal(EstadoDeDireccion.Vacia, resultado.Estado);
        Assert.Null(resultado.Direccion);
    }

    [Theory]
    [Trait("Criterio", "AC-IND-06")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("MAYUSCULAS-con-tilde-é")]
    [InlineData("a\r\nSet-Cookie: x")]
    public void El_texto_no_valido_no_se_guarda_ni_se_repite_en_el_resultado(string texto)
    {
        var resultado = DireccionDelIndice.Evaluar(texto);

        Assert.Null(resultado.Direccion);
        Assert.DoesNotContain(texto, resultado.ToString(), StringComparison.Ordinal);
    }
}
