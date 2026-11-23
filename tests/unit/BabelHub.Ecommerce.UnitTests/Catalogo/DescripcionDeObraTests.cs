using System.Security.Cryptography;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>La descripción opcional de la obra (T29; CAT-01, CAT-25, AC-CAT-20, D18): recortada, nula si queda vacía, hasta 600 puntos de código y solo con el salto de línea como carácter de control.</summary>
public sealed class DescripcionDeObraTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 23, 10, 0, 0, TimeSpan.FromHours(-5));

    private static PortadaDeObra UnaPortada() => new(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Jpeg, HuellaDeArchivo.Desde(SHA256.HashData([1, 2, 3])));

    private static Edicion Impresa() => Edicion.Impresa(Guid.NewGuid(), Isbn.Crear("978-0-00-000001-9"), Precio.Crear(40m));

    private static ResultadoDeObra<Obra> Registrar(string? descripcion) =>
        Obra.Registrar(Guid.NewGuid(), new DatosDeObra("Título", "Autor", "Editorial", "Técnico", descripcion), UnaPortada(), [Impresa()], Ahora);

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public void Una_descripcion_de_600_caracteres_se_acepta_y_una_de_601_se_rechaza_con_el_campo()
    {
        Assert.True(Registrar(new string('a', 600)).EsExito);

        var rechazada = Registrar(new string('a', 601));

        Assert.False(rechazada.EsExito);
        Assert.Equal(RechazoDeCatalogo.DatosInvalidos, rechazada.Rechazo!.Motivo);
        Assert.Equal("descripcion", rechazada.Rechazo.Campo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public void Los_600_se_cuentan_en_puntos_de_codigo_tras_recortar_los_espacios_de_los_extremos()
    {
        // 600 emojis son 1 200 unidades UTF-16, pero 600 puntos de código (lo mismo que cuenta char_length de la base).
        var seiscientosEmojis = string.Concat(Enumerable.Repeat("😀", 600));
        Assert.True(Registrar(seiscientosEmojis).EsExito);
        Assert.False(Registrar(seiscientosEmojis + "😀").EsExito);

        var conEspacios = Registrar("  " + new string('a', 600) + "  \n ");
        Assert.True(conEspacios.EsExito);
        Assert.Equal(600, conEspacios.Valor.Descripcion!.Length);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-20")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" \n \n ")]
    public void Una_descripcion_ausente_vacia_o_de_solo_espacios_queda_nula(string? texto)
    {
        var resultado = Registrar(texto);

        Assert.True(resultado.EsExito);
        Assert.Null(resultado.Valor.Descripcion);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public void Los_saltos_de_linea_se_conservan_y_un_CRLF_se_normaliza_a_LF()
    {
        var obra = Registrar("Primera línea\r\nSegunda línea\n\nTercera").Valor;

        Assert.Equal("Primera línea\nSegunda línea\n\nTercera", obra.Descripcion);
    }

    public static TheoryData<string> Prohibidos() => new()
    {
        "con\ttabulador",
        "con\rCR suelto",
        "con\0nulo",
        "con" + (char)0x1B + "escape",
        "con" + (char)0x202E + "bidi",
        "con" + (char)0x2066 + "aislante",
        "con" + (char)0x2069 + "aislante",
        "con" + (char)0x2028 + "separador",
        "con" + (char)0x2029 + "separador",
        "\tal inicio",
        "al final\t",
    };

    [Theory]
    [Trait("Criterio", "AC-CAT-20")]
    [MemberData(nameof(Prohibidos))]
    public void Una_descripcion_con_un_caracter_prohibido_se_rechaza_con_el_campo(string texto)
    {
        var resultado = Registrar(texto);

        Assert.False(resultado.EsExito);
        Assert.Equal(RechazoDeCatalogo.DatosInvalidos, resultado.Rechazo!.Motivo);
        Assert.Equal("descripcion", resultado.Rechazo.Campo);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public void Un_sustituto_UTF16_suelto_se_rechaza_y_un_par_valido_se_acepta()
    {
        // Sin Theory: xUnit no serializa bien un sustituto suelto y lo cambiaría por U+FFFD.
        foreach (var suelto in new[] { "con" + (char)0xD83D + "alto", "con" + (char)0xDE00 + "bajo", "termina en alto" + (char)0xD83D })
        {
            var resultado = Registrar(suelto);
            Assert.False(resultado.EsExito);
            Assert.Equal("descripcion", resultado.Rechazo!.Campo);
        }

        Assert.True(Registrar("par válido 😀").EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public void Una_descripcion_con_marcado_se_guarda_tal_como_se_escribio()
    {
        const string texto = "<script>alert(1)</script> &lt;b&gt; \"comillas\" 'simples'";

        Assert.Equal(texto, Registrar(texto).Valor.Descripcion);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public void Editar_reemplaza_la_descripcion_y_la_deja_en_camposCambiados_y_nula_o_vacia_la_quita()
    {
        var obra = Registrar("Original").Valor;

        var reemplazo = obra.EditarDatos(new DatosDeObra("Título", "Autor", "Editorial", "Técnico", "Nueva"));
        Assert.True(reemplazo.EsExito);
        Assert.Equal(["descripcion"], reemplazo.Valor.CamposCambiados);
        Assert.Equal("Nueva", obra.Descripcion);

        var sinCambio = obra.EditarDatos(new DatosDeObra("Título", "Autor", "Editorial", "Técnico", "Nueva"));
        Assert.Empty(sinCambio.Valor.CamposCambiados);

        var quitada = obra.EditarDatos(new DatosDeObra("Título", "Autor", "Editorial", "Técnico", "  "));
        Assert.Equal(["descripcion"], quitada.Valor.CamposCambiados);
        Assert.Null(obra.Descripcion);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public void Editar_con_601_caracteres_se_rechaza_y_no_cambia_nada()
    {
        var obra = Registrar("Original").Valor;
        var version = obra.Version;

        var resultado = obra.EditarDatos(new DatosDeObra("Título", "Autor", "Editorial", "Técnico", new string('x', 601)));

        Assert.False(resultado.EsExito);
        Assert.Equal("descripcion", resultado.Rechazo!.Campo);
        Assert.Equal("Original", obra.Descripcion);
        Assert.Equal(version, obra.Version);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-20")]
    public void Una_obra_sin_descripcion_sigue_valida_al_rehidratarse_y_con_ella_la_conserva()
    {
        var obra = Obra.Rehidratar(Guid.NewGuid(), new DatosDeObra("T", "A", "E", "C", "Guardada"), UnaPortada(), 3, EstadoDeObra.Activa, null, Ahora, [Impresa()]);
        var sin = Obra.Rehidratar(Guid.NewGuid(), new DatosDeObra("T", "A", "E", "C"), UnaPortada(), 3, EstadoDeObra.Activa, null, Ahora, [Impresa()]);

        Assert.Equal("Guardada", obra.Descripcion);
        Assert.Null(sin.Descripcion);
    }
}
