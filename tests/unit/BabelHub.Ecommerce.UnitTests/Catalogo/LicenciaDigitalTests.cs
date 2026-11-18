using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>La licencia de una edición digital (T03; CAT-05 a CAT-08): vigencia posterior a hoy solo al registrar o cambiar, dominio público sin vigencia y los motivos de no disponible.</summary>
public sealed class LicenciaDigitalTests
{
    private static readonly DateOnly Hoy = new(2026, 11, 14);

    private static ResultadoDeLicencia Comercial(DateOnly? hasta, string? titular = "Editorial Andina", IEnumerable<string>? territorios = null, DateOnly? hoy = null) =>
        LicenciaDigital.Crear(TipoDeLicencia.Comercial, titular, territorios ?? ["PE"], hasta, FormatoDigital.Epub, hoy ?? Hoy);

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public void Una_licencia_comercial_con_vigencia_pasada_o_de_hoy_se_rechaza()
    {
        var pasada = Comercial(Hoy.AddDays(-1));
        var hoy = Comercial(Hoy);

        Assert.False(pasada.EsExito);
        Assert.Contains(new ErrorDeLicencia("vigenteHasta", "vigencia-no-posterior-a-hoy"), pasada.Errores);
        Assert.False(hoy.EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public void Con_una_fecha_futura_titular_y_territorios_se_acepta()
    {
        var resultado = Comercial(Hoy.AddDays(1));

        Assert.True(resultado.EsExito);
        Assert.Equal(Hoy.AddDays(1), resultado.Licencia!.VigenteHasta);
        Assert.Equal("Editorial Andina", resultado.Licencia.Titular);
        Assert.Equal(FormatoDigital.Epub, resultado.Licencia.Formato);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-03")]
    public void El_dia_de_hoy_es_el_de_lima_que_recibe_el_dominio()
    {
        // En UTC ya es el 15, pero en Lima todavía es el 14: quien llama pasa el día de Lima (IReloj.HoyEnLima).
        Assert.False(Comercial(new DateOnly(2026, 11, 14), hoy: new DateOnly(2026, 11, 14)).EsExito);
        Assert.True(Comercial(new DateOnly(2026, 11, 15), hoy: new DateOnly(2026, 11, 14)).EsExito);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-13")]
    public void Sin_titular_se_rechaza_indicando_el_campo_faltante_y_con_dominio_publico_sin_vigencia_se_acepta()
    {
        var sinTitular = Comercial(Hoy.AddDays(30), titular: "  ");
        var publica = LicenciaDigital.Crear(TipoDeLicencia.DominioPublico, "Dominio público", ["PE"], null, FormatoDigital.Pdf, Hoy);

        Assert.False(sinTitular.EsExito);
        Assert.Equal([new ErrorDeLicencia("titular", "obligatorio")], sinTitular.Errores);
        Assert.True(publica.EsExito);
        Assert.Null(publica.Licencia!.VigenteHasta);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-13")]
    public void La_comercial_exige_vigencia_y_la_de_dominio_publico_la_prohibe()
    {
        var comercialSinVigencia = Comercial(null);
        var publicaConVigencia = LicenciaDigital.Crear(TipoDeLicencia.DominioPublico, "Titular", ["PE"], Hoy.AddDays(5), FormatoDigital.Epub, Hoy);

        Assert.Contains(new ErrorDeLicencia("vigenteHasta", "obligatorio"), comercialSinVigencia.Errores);
        Assert.Contains(new ErrorDeLicencia("vigenteHasta", "no-aplica"), publicaConVigencia.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-13")]
    public void Los_territorios_son_obligatorios()
    {
        var sinLista = Comercial(Hoy.AddDays(5), territorios: []);

        Assert.Contains(new ErrorDeLicencia("territorios", "obligatorio"), sinLista.Errores);
    }

    [Theory]
    [Trait("Criterio", "AC-CAT-13")]
    [InlineData("PER")]
    [InlineData("P")]
    [InlineData("P1")]
    [InlineData("")]
    public void Un_territorio_debe_ser_un_codigo_de_pais_de_dos_letras(string codigo)
    {
        var resultado = Comercial(Hoy.AddDays(5), territorios: ["PE", codigo]);

        Assert.Contains(new ErrorDeLicencia("territorios", "formato-invalido"), resultado.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-13")]
    public void Los_territorios_se_normalizan_en_mayusculas_sin_repetir_y_con_un_tope()
    {
        var resultado = Comercial(Hoy.AddDays(5), territorios: ["pe", "CL", "PE", " ar "]);
        var demasiados = Comercial(Hoy.AddDays(5), territorios: Enumerable.Range(0, 251).Select(i => $"{(char)('A' + (i / 26 % 26))}{(char)('A' + (i % 26))}"));

        Assert.Equal(["AR", "CL", "PE"], resultado.Licencia!.Territorios);
        Assert.Contains(new ErrorDeLicencia("territorios", "formato-invalido"), demasiados.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-13")]
    public void El_titular_tiene_un_tope_de_longitud()
    {
        var resultado = Comercial(Hoy.AddDays(5), titular: new string('a', 201));

        Assert.Contains(new ErrorDeLicencia("titular", "demasiado-largo"), resultado.Errores);
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public void Una_licencia_vencida_o_sin_peru_no_se_ofrece_y_cada_una_explica_su_motivo()
    {
        var vencida = LicenciaDigital.Rehidratar(TipoDeLicencia.Comercial, "Editorial", ["PE"], Hoy.AddDays(-1), FormatoDigital.Epub);
        var sinPeru = Comercial(Hoy.AddDays(30), territorios: ["CL", "AR"]).Licencia!;
        var vigente = Comercial(Hoy.AddDays(30)).Licencia!;

        Assert.Equal(MotivoDeNoDisponible.LicenciaVencida, vencida.MotivoDeNoDisponiblePara(Hoy));
        Assert.Equal(MotivoDeNoDisponible.SinPeru, sinPeru.MotivoDeNoDisponiblePara(Hoy));
        Assert.Null(vigente.MotivoDeNoDisponiblePara(Hoy));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public void La_licencia_vence_despues_de_su_ultimo_dia_y_ese_dia_todavia_rige()
    {
        var licencia = Comercial(Hoy.AddDays(10)).Licencia!;

        Assert.True(licencia.EstaVigente(Hoy.AddDays(10)));
        Assert.False(licencia.EstaVigente(Hoy.AddDays(11)));
        Assert.Equal(MotivoDeNoDisponible.LicenciaVencida, licencia.MotivoDeNoDisponiblePara(Hoy.AddDays(11)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-04")]
    public void Una_licencia_de_dominio_publico_nunca_vence()
    {
        var licencia = LicenciaDigital.Crear(TipoDeLicencia.DominioPublico, "Dominio público", ["PE"], null, FormatoDigital.Epub, Hoy).Licencia!;

        Assert.True(licencia.EstaVigente(Hoy.AddYears(500)));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public void Rehidratar_una_licencia_vencida_no_vuelve_a_exigir_una_vigencia_futura_pero_si_su_forma()
    {
        var vencida = LicenciaDigital.Rehidratar(TipoDeLicencia.Comercial, "Editorial", ["PE"], Hoy.AddYears(-3), FormatoDigital.Pdf);

        Assert.False(vencida.EstaVigente(Hoy));
        Assert.Throws<ArgumentException>(() => LicenciaDigital.Rehidratar(TipoDeLicencia.Comercial, "", ["PE"], Hoy.AddYears(1), FormatoDigital.Pdf));
        Assert.Throws<ArgumentException>(() => LicenciaDigital.Rehidratar(TipoDeLicencia.DominioPublico, "Titular", ["PE"], Hoy, FormatoDigital.Pdf));
    }

    [Fact]
    [Trait("Criterio", "AC-CAT-05")]
    public void Dos_licencias_con_los_mismos_datos_son_iguales()
    {
        var a = Comercial(Hoy.AddDays(10), territorios: ["PE", "CL"]).Licencia!;
        var b = Comercial(Hoy.AddDays(10), territorios: ["CL", "PE"]).Licencia!;
        var c = Comercial(Hoy.AddDays(11), territorios: ["CL", "PE"]).Licencia!;

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }
}
