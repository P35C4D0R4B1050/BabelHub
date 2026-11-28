using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>
/// El ISBN y el precio del catálogo (T01; CAT-02, CAT-17). El ISBN de ejemplo de AC-CAT-02 en la spec (978-0-00-000001-1) tiene el dígito de control
/// inválido (hallazgo de la spec): estas pruebas usan el válido, 978-0-00-000001-9.
/// </summary>
public sealed class IsbnYPrecioTests
{
    private const string Valido13 = "978-0-00-000001-9";

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public void Un_isbn_13_valido_se_acepta_sin_guiones_ni_espacios_y_conserva_el_texto_ingresado()
    {
        Assert.True(Isbn.TryCrear(" 978-0-00-000001-9 ", out var isbn));

        Assert.Equal("9780000000019", isbn!.Isbn13);
        Assert.Equal("978-0-00-000001-9", isbn.TextoIngresado);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-14")]
    [InlineData("978-0-00-000001-1")]
    [InlineData("0-306-40615-3")]
    public void Un_digito_de_control_invalido_se_rechaza(string texto) => Assert.False(Isbn.TryCrear(texto, out _));

    [Theory]
    [Trait("Criterio", "AC-CAT-14")]
    [InlineData(null)]
    [InlineData("978000000001")]
    [InlineData("97800000000190")]
    [InlineData("978-0-00-00000A-9")]
    [InlineData("978.0.00.000001.9")]
    public void Un_texto_que_no_tiene_10_ni_13_digitos_se_rechaza(string? texto) => Assert.False(Isbn.TryCrear(texto, out _));

    [Theory]
    [Trait("Criterio", "AC-CAT-14")]
    [InlineData("9780000000019", true)]
    [InlineData("9790000000018", true)]
    [InlineData("9770000000010", false)]
    public void Un_isbn_13_solo_admite_el_prefijo_978_o_979(string texto, bool valido)
    {
        // 977 es el prefijo de las publicaciones periódicas: se rechaza aunque el dígito de control cuadre.
        Assert.Equal(valido, Isbn.TryCrear(texto, out _));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public void Un_isbn_10_se_convierte_a_isbn_13_y_es_el_mismo_que_el_isbn_13()
    {
        Assert.True(Isbn.TryCrear("0-306-40615-2", out var diez));
        Assert.True(Isbn.TryCrear("978-0-306-40615-7", out var trece));

        Assert.Equal("9780306406157", diez!.Isbn13);
        Assert.Equal(trece!.Isbn13, diez.Isbn13);
        Assert.Equal("0-306-40615-2", diez.TextoIngresado);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-14")]
    [InlineData("0-8044-2957-X", "9780804429573")]
    [InlineData("0-8044-2957-x", "9780804429573")]
    public void La_x_del_isbn_10_vale_diez_y_se_admite_en_mayuscula_o_minuscula(string texto, string esperado)
    {
        Assert.True(Isbn.TryCrear(texto, out var isbn));
        Assert.Equal(esperado, isbn!.Isbn13);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-14")]
    [InlineData("0-8044-X957-2")]
    [InlineData("978-0-00-00000X-9")]
    public void La_x_solo_es_valida_como_ultimo_caracter_de_un_isbn_10(string texto) => Assert.False(Isbn.TryCrear(texto, out _));

    [Fact]
    [Trait("Criterio", "AC-CAT-02")]
    public void Crear_con_un_texto_invalido_lanza_y_con_uno_valido_devuelve_el_isbn()
    {
        Assert.Throws<ArgumentException>(() => Isbn.Crear("978-0-00-000001-1"));
        Assert.Equal("9780000000019", Isbn.Crear(Valido13).Isbn13);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-14")]
    public void Dos_isbn_son_iguales_si_tienen_el_mismo_isbn_13_aunque_el_texto_ingresado_sea_distinto()
    {
        var a = Isbn.Crear("0-306-40615-2");
        var b = Isbn.Crear("9780306406157");

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-08")]
    [InlineData("0", true)]
    [InlineData("9999999999.99", true)]
    [InlineData("-0.01", false)]
    [InlineData("129.005", false)]
    [InlineData("10000000000", false)]
    public void El_precio_es_en_soles_con_dos_decimales_no_negativo_y_cabe_en_numeric_12_2(string texto, bool valido)
    {
        var valor = decimal.Parse(texto, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(valido, Precio.TryCrear(valor, out var precio));
        if (valido)
        {
            Assert.Equal(valor, precio!.Valor);
        }
    }
}
