using System.Text;
using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>El segundo factor (T03; AC-IDE-18, AC-IDE-19, AC-IDE-20, AC-IDE-38, AC-IDE-45, AC-IDE-46; IDE-20 a IDE-22, IDE-41, IDE-50 a IDE-52).</summary>
public sealed class SegundoFactorTests
{
    // El secreto de los vectores de prueba de RFC 6238 (SHA-1): "12345678901234567890".
    private static readonly byte[] Secreto = Encoding.ASCII.GetBytes("12345678901234567890");
    private static readonly DateTimeOffset Ahora = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static IReadOnlyList<string> Huellas(string prefijo = "h") => [.. Enumerable.Range(1, 8).Select(i => $"{prefijo}{i}")];

    private static string CodigoDe(DateTimeOffset instante, int pasos = 0) => Totp.Codigo(Secreto, Totp.PasoDe(instante) + pasos);

    private static SegundoFactor Activo(string prefijo = "h")
    {
        var factor = SegundoFactor.Nuevo(Guid.NewGuid());
        Assert.Equal(ResultadoDeSegundoFactor.Aceptado, factor.Confirmar(Secreto, CodigoDe(Ahora), Ahora, Huellas(prefijo)));
        return factor;
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-45")]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    [InlineData(20000000000L, "353130")]
    public void El_codigo_coincide_con_los_vectores_de_prueba_de_rfc_6238(long segundos, string esperado) =>
        Assert.Equal(esperado, Totp.Codigo(Secreto, segundos / Totp.SegundosPorPaso));

    [Fact]
    [Trait("Criterio", "AC-IDE-45")]
    public void Se_aceptan_la_ventana_anterior_la_actual_y_la_siguiente_pero_no_dos_atras_ni_dos_adelante()
    {
        Assert.NotNull(Totp.PasoAceptado(Secreto, CodigoDe(Ahora, -1), Ahora, null));
        Assert.NotNull(Totp.PasoAceptado(Secreto, CodigoDe(Ahora, 0), Ahora, null));
        Assert.NotNull(Totp.PasoAceptado(Secreto, CodigoDe(Ahora, 1), Ahora, null));
        Assert.Null(Totp.PasoAceptado(Secreto, CodigoDe(Ahora, -2), Ahora, null));
        Assert.Null(Totp.PasoAceptado(Secreto, CodigoDe(Ahora, 2), Ahora, null));
    }

    [Theory]
    [Trait("Criterio", "AC-IDE-18")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12345")]
    [InlineData("1234567")]
    [InlineData("12345a")]
    [InlineData(" 12345")]
    public void Un_codigo_con_otra_forma_se_rechaza(string? codigo) => Assert.Null(Totp.PasoAceptado(Secreto, codigo, Ahora, null));

    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public void El_segundo_factor_se_activa_solo_tras_confirmar_con_un_codigo_y_entrega_ocho_codigos_de_respaldo()
    {
        var factor = SegundoFactor.Nuevo(Guid.NewGuid());

        Assert.False(factor.EstaActivo);
        Assert.Equal(ResultadoDeSegundoFactor.NoEstaActivo, factor.Presentar(Secreto, CodigoDe(Ahora), Ahora));
        Assert.Equal(ResultadoDeSegundoFactor.Rechazado, factor.Confirmar(Secreto, "000000", Ahora, Huellas()));
        Assert.False(factor.EstaActivo);
        Assert.Equal(0, factor.CodigosDeRespaldoRestantes);

        Assert.Equal(ResultadoDeSegundoFactor.Aceptado, factor.Confirmar(Secreto, CodigoDe(Ahora), Ahora, Huellas()));

        Assert.True(factor.EstaActivo);
        Assert.Equal(8, factor.CodigosDeRespaldoRestantes);
    }

[Fact]    [Trait("Criterio", "AC-IDE-19")]    public void Un_segundo_factor_ya_activo_no_se_confirma_de_nuevo()    {        var factor = Activo();        Assert.Equal(ResultadoDeSegundoFactor.YaActivo, factor.Confirmar(Secreto, CodigoDe(Ahora, 1), Ahora, Huellas("otro")));        Assert.Equal(8, factor.CodigosDeRespaldoRestantes);    }
    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public void Confirmar_exige_exactamente_ocho_huellas_distintas()
    {
        var factor = SegundoFactor.Nuevo(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => factor.Confirmar(Secreto, CodigoDe(Ahora), Ahora, [.. Huellas().Take(7)]));
        Assert.Throws<ArgumentException>(() => factor.Confirmar(Secreto, CodigoDe(Ahora), Ahora, [.. Huellas().Take(7), "h1"]));
        Assert.False(factor.EstaActivo);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-18")]
    public void Con_el_segundo_factor_activo_solo_un_codigo_valido_deja_pasar()
    {
        var factor = Activo();
        var despues = Ahora.AddSeconds(31);

        Assert.Equal(ResultadoDeSegundoFactor.Rechazado, factor.Presentar(Secreto, "000000", despues));
        Assert.Equal(ResultadoDeSegundoFactor.Aceptado, factor.Presentar(Secreto, CodigoDe(despues), despues));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-38")]
    public void Un_codigo_de_un_paso_ya_usado_se_rechaza_y_tambien_uno_de_un_paso_anterior()
    {
        var factor = Activo();
        var codigo = CodigoDe(Ahora);

        Assert.Equal(ResultadoDeSegundoFactor.Rechazado, factor.Presentar(Secreto, codigo, Ahora));
        Assert.Equal(ResultadoDeSegundoFactor.Rechazado, factor.Presentar(Secreto, CodigoDe(Ahora, -1), Ahora));
        Assert.Equal(ResultadoDeSegundoFactor.Aceptado, factor.Presentar(Secreto, CodigoDe(Ahora, 1), Ahora));
        Assert.Equal(ResultadoDeSegundoFactor.Rechazado, factor.Presentar(Secreto, codigo, Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public void Un_codigo_de_respaldo_sirve_una_sola_vez_y_se_indica_cuantos_quedan()
    {
        var factor = Activo();

        var primero = factor.UsarCodigoDeRespaldo("h3");
        var repetido = factor.UsarCodigoDeRespaldo("h3");
        var inexistente = factor.UsarCodigoDeRespaldo("otro");

        Assert.True(primero.Aceptado);
        Assert.Equal(7, primero.Restantes);
        Assert.False(repetido.Aceptado);
        Assert.Equal(7, repetido.Restantes);
        Assert.False(inexistente.Aceptado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-20")]
    public void Un_codigo_de_respaldo_no_sirve_si_el_segundo_factor_no_esta_activo()
    {
        var factor = SegundoFactor.Restaurar(Guid.NewGuid(), EstadoDelSegundoFactor.SinConfirmar, null, Huellas());

        Assert.False(factor.UsarCodigoDeRespaldo("h1").Aceptado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-46")]
    public void Regenerar_pide_un_codigo_vigente_invalida_los_anteriores_y_entrega_ocho_nuevos()
    {
        var factor = Activo("viejo");
        var despues = Ahora.AddSeconds(60);

        var conCodigoMalo = factor.RegenerarCodigosDeRespaldo(Secreto, "000000", despues, Huellas("nuevo"));
        Assert.Equal(ResultadoDeSegundoFactor.Rechazado, conCodigoMalo);
        Assert.True(factor.UsarCodigoDeRespaldo("viejo1").Aceptado);

        var regenerado = factor.RegenerarCodigosDeRespaldo(Secreto, CodigoDe(despues), despues, Huellas("nuevo"));

        Assert.Equal(ResultadoDeSegundoFactor.Aceptado, regenerado);
        Assert.Equal(8, factor.CodigosDeRespaldoRestantes);
        Assert.False(factor.UsarCodigoDeRespaldo("viejo2").Aceptado);
        Assert.True(factor.UsarCodigoDeRespaldo("nuevo2").Aceptado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public void Restablecer_deja_el_segundo_factor_sin_confirmar_y_sin_codigos()
    {
        var factor = Activo();

        factor.Restablecer();

        Assert.False(factor.EstaActivo);
        Assert.Equal(0, factor.CodigosDeRespaldoRestantes);
        Assert.Null(factor.UltimoPasoUsado);
        Assert.Equal(ResultadoDeSegundoFactor.NoEstaActivo, factor.Presentar(Secreto, CodigoDe(Ahora), Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-19")]
    public void Los_codigos_de_respaldo_generados_son_ocho_distintos_de_al_menos_cincuenta_bits_y_en_su_forma_canonica()
    {
        var codigos = CodigosDeRespaldo.Generar();

        Assert.Equal(8, codigos.Count);
        Assert.Equal(8, codigos.Distinct().Count());
        Assert.All(codigos, c =>
        {
            Assert.Matches("^[A-Z2-9]{4}-[A-Z2-9]{4}-[A-Z2-9]{4}$", c);
            Assert.True(CodigosDeRespaldo.TieneLaForma(c));
            Assert.Equal(12, CodigosDeRespaldo.Canonico(c).Length);
        });

        // 12 símbolos de un alfabeto de 32 = 60 bits (mínimo exigido: 50).
        Assert.True(CodigosDeRespaldo.Simbolos * Math.Log2(32) >= 50);
        Assert.Equal("ABCD2345EFGH", CodigosDeRespaldo.Canonico(" abcd-2345 efgh "));
        Assert.False(CodigosDeRespaldo.TieneLaForma("ABCD-2345"));
        Assert.False(CodigosDeRespaldo.TieneLaForma("ABCD-2345-EFG0"));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-45")]
    public void Un_secreto_de_menos_de_128_bits_nunca_valida()
    {
        var corto = new byte[15];

        Assert.Null(Totp.PasoAceptado(corto, Totp.Codigo(corto, Totp.PasoDe(Ahora)), Ahora, null));
        Assert.Null(Totp.PasoAceptado([], "000000", Ahora, null));
    }
}
