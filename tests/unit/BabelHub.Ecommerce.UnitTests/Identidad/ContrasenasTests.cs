using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>La política y el resumen de contraseñas (T01; AC-IDE-08, AC-IDE-13, AC-IDE-42; IDE-09, IDE-15, IDE-59).</summary>
public sealed class ContrasenasTests
{
    private static readonly HashSet<string> Comunes = new(StringComparer.Ordinal) { "contraseña123", "qwertyuiop", "password12", "añoañoañoaño" };

    private static bool EsComun(string normalizada) => Comunes.Contains(normalizada);

    [Theory]
    [Trait("Criterio", "AC-IDE-13")]
    [InlineData(9, ResultadoDeContrasena.Corta)]
    [InlineData(10, ResultadoDeContrasena.Aceptable)]
    [InlineData(128, ResultadoDeContrasena.Aceptable)]
    [InlineData(129, ResultadoDeContrasena.Larga)]
    public void Los_bordes_de_longitud_son_9_rechaza_10_y_128_aceptan_129_rechaza(int largo, ResultadoDeContrasena esperado)
    {
        var texto = new string('x', largo - 1) + "z";

        Assert.Equal(esperado, PoliticaDeContrasenas.Evaluar(texto, EsComun));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-13")]
    public void Se_cuenta_en_caracteres_unicode_y_no_en_unidades_utf16()
    {
        var conEmojis = string.Concat(Enumerable.Repeat("🔐", 10));
        var nueveEmojis = string.Concat(Enumerable.Repeat("🔐", 9));

        Assert.Equal(20, conEmojis.Length);
        Assert.Equal(ResultadoDeContrasena.Aceptable, PoliticaDeContrasenas.Evaluar(conEmojis, EsComun));
        Assert.Equal(ResultadoDeContrasena.Corta, PoliticaDeContrasenas.Evaluar(nueveEmojis, EsComun));
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-13")]
    [InlineData("")]
    [InlineData(null)]
    public void Una_contrasena_vacia_se_rechaza(string? texto) => Assert.Equal(ResultadoDeContrasena.Vacia, PoliticaDeContrasenas.Evaluar(texto, EsComun));

    [Theory]
    [Trait("Criterio", "AC-IDE-13")]
    [InlineData("password12")]
    [InlineData("PASSWORD12")]
    [InlineData("qwertyuiop")]
    public void Una_contrasena_comun_se_rechaza_aunque_tenga_la_longitud_y_cambie_de_mayusculas(string texto) =>
        Assert.Equal(ResultadoDeContrasena.Comun, PoliticaDeContrasenas.Evaluar(texto, EsComun));

    [Fact]
    [Trait("Criterio", "AC-IDE-13")]
    public void Una_frase_de_doce_letras_minusculas_fuera_de_la_lista_se_acepta_sin_reglas_de_composicion() =>
        Assert.Equal(ResultadoDeContrasena.Aceptable, PoliticaDeContrasenas.Evaluar("abrigoazulrio", EsComun));

    [Fact]
    [Trait("Criterio", "AC-IDE-42")]
    public void Una_contrasena_comun_con_otra_normalizacion_unicode_se_rechaza_igual()
    {
        // «contraseña123» con la ñ descompuesta (n + tilde combinada) y con anchos completos: NFKC las lleva a la misma forma.
        var descompuesta = "contraseña123";
        var anchoCompleto = "ｃｏｎｔｒａｓｅñａ１２３";

        Assert.NotEqual("contraseña123", descompuesta);
        Assert.Equal(ResultadoDeContrasena.Comun, PoliticaDeContrasenas.Evaluar(descompuesta, EsComun));
        Assert.Equal(ResultadoDeContrasena.Comun, PoliticaDeContrasenas.Evaluar(anchoCompleto, EsComun));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-42")]
    public void La_longitud_se_cuenta_despues_de_normalizar()
    {
        // Nueve caracteres con una ligadura «ﬁ» que NFKC separa en dos: son diez al normalizar.
        var texto = "abcdefghﬁ";

        Assert.Equal(9, texto.Length);
        Assert.Equal(ResultadoDeContrasena.Aceptable, PoliticaDeContrasenas.Evaluar(texto, EsComun));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-13")]
    public void No_se_recorta_ni_se_altera_lo_que_se_pega()
    {
        var conEspacios = "  una frase con espacios  ";
        var (contrasena, resultado) = Contrasena.Crear(conEspacios, EsComun);

        Assert.Equal(ResultadoDeContrasena.Aceptable, resultado);
        Assert.Equal(conEspacios, contrasena!.Revelar());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-08")]
    public void La_contrasena_no_se_muestra_en_texto()
    {
        var (contrasena, _) = Contrasena.Crear("una contraseña larga y buena", EsComun);

        Assert.DoesNotContain("larga", contrasena!.ToString(), StringComparison.Ordinal);
        Assert.Null(Contrasena.Presentada(""));
        Assert.Null(Contrasena.Presentada(null));
        Assert.Equal("é", Contrasena.Presentada("é")!.Revelar());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-08")]
    public void Un_resumen_de_argon2id_se_lee_con_su_sal_y_sus_parametros_y_dos_cuentas_distan()
    {
        var uno = ResumenDeContrasena.Leer("$argon2id$v=19$m=19456,t=2,p=1$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ");
        var otro = ResumenDeContrasena.Leer("$argon2id$v=19$m=19456,t=2,p=1$c2FsLW90cmEtY3VlbnRhLWRpc3RpbnRh$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ");

        Assert.NotNull(uno);
        Assert.NotNull(otro);
        Assert.NotEqual(uno.Sal, otro!.Sal);
        Assert.Equal(ParametrosDelResumen.MemoriaEnKiB, uno.Memoria);
        Assert.False(uno.NecesitaActualizarse());
        Assert.Equal("[resumen]", uno.ToString());
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-08")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("texto-claro-de-la-contraseña")]
    [InlineData("$argon2i$v=19$m=19456,t=2,p=1$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$corta$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ")]
    [InlineData("$argon2id$v=19$m=0,t=2,p=1$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ")]
    [InlineData("$argon2id$v=19$m=x,t=2,p=1$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ")]
    public void Lo_que_no_es_un_resumen_de_argon2id_valido_no_se_lee(string? valor) => Assert.Null(ResumenDeContrasena.Leer(valor));

    [Fact]
    [Trait("Criterio", "AC-IDE-08")]
    public void Un_resumen_con_parametros_mas_debiles_pide_actualizarse()
    {
        var debil = ResumenDeContrasena.Leer("$argon2id$v=19$m=4096,t=1,p=1$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ");

        Assert.True(debil!.NecesitaActualizarse());
        Assert.Equal(16, ParametrosDelResumen.BytesDeSal);
    }
[Theory]    [Trait("Criterio", "AC-IDE-08")]    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2FsLWNvcnRh$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ")]    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1jb3J0bw")]    public void Una_sal_de_menos_de_16_bytes_o_un_resumen_de_menos_de_32_no_se_leen(string valor) => Assert.Null(ResumenDeContrasena.Leer(valor));

    [Theory]
    [Trait("Criterio", "AC-IDE-08")]
    [InlineData("$argon2id$v=19$m=2000000000,t=2,p=1$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ")]
    [InlineData("$argon2id$v=19$m=19456,t=500,p=1$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=64$c2FsLXVuaWNhLWRlLXVuYS1jdWVudGE$cmVzdW1lbi1kZS1sYS1jb250cmFzZW5hLWRlLWxhLXBydWViYQ")]
    public void Un_resumen_con_parametros_desmedidos_no_se_lee(string valor) => Assert.Null(ResumenDeContrasena.Leer(valor));

    [Fact]
    [Trait("Criterio", "AC-IDE-13")]
    public void Un_texto_enorme_o_con_sustitutos_sueltos_se_rechaza_sin_error_del_servidor()
    {
        var enorme = new string('x', PoliticaDeContrasenas.MaximoCrudoEnUtf16 + 1);
        var sustitutoSuelto = "abcdefghij\uD800klmnop";

        Assert.Equal(ResultadoDeContrasena.Larga, PoliticaDeContrasenas.Evaluar(enorme, EsComun));
        Assert.Null(Contrasena.Presentada(enorme));
        Assert.NotEqual(ResultadoDeContrasena.Aceptable, PoliticaDeContrasenas.Evaluar("\uD800", EsComun));
        Assert.Null(Contrasena.Presentada("\uD800"));
        _ = PoliticaDeContrasenas.Evaluar(sustitutoSuelto, EsComun);
    }
}
