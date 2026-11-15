using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>El código de un solo uso por correo (T05; AC-IDE-14, AC-IDE-17, AC-IDE-49; IDE-16, IDE-19, IDE-56).</summary>
public sealed class CodigoDeUnSoloUsoTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));

    private static CodigoDeUnSoloUso Nuevo(string huella = "huella-correcta") => CodigoDeUnSoloUso.Emitir(Guid.NewGuid(), PropositoDeCodigo.VerificacionDeCorreo, huella, Ahora);

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public void El_codigo_correcto_se_acepta_una_sola_vez()
    {
        var codigo = Nuevo();

        Assert.Equal(ResultadoDelCodigo.Correcto, codigo.Verificar("huella-correcta", Ahora.AddMinutes(1)));
        Assert.Equal(ResultadoDelCodigo.NoVigente, codigo.Verificar("huella-correcta", Ahora.AddMinutes(2)));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public void Un_codigo_incorrecto_no_verifica_y_cuenta_un_intento()
    {
        var codigo = Nuevo();

        Assert.Equal(ResultadoDelCodigo.Incorrecto, codigo.Verificar("otra", Ahora));
        Assert.Equal(1, codigo.Intentos);
        Assert.False(codigo.Usado);
        Assert.Equal(ResultadoDelCodigo.Correcto, codigo.Verificar("huella-correcta", Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public void Vence_a_los_diez_minutos()
    {
        var codigo = Nuevo();

        Assert.Equal(ResultadoDelCodigo.Correcto, Nuevo().Verificar("huella-correcta", Ahora.AddMinutes(9).AddSeconds(59)));
        Assert.Equal(ResultadoDelCodigo.Vencido, codigo.Verificar("huella-correcta", Ahora.AddMinutes(10)));
        Assert.Equal(Ahora.AddMinutes(10), codigo.Vence);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public void Tras_cinco_intentos_fallidos_ni_el_valor_correcto_sirve()
    {
        var codigo = Nuevo();

        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(ResultadoDelCodigo.Incorrecto, codigo.Verificar($"mal{i}", Ahora));
        }

        Assert.Equal(ResultadoDelCodigo.IntentosAgotados, codigo.Verificar("huella-correcta", Ahora));
        Assert.Equal(5, codigo.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-49")]
    public void Un_codigo_nuevo_invalida_el_anterior()
    {
        var anterior = Nuevo("huella-vieja");
        var nuevo = Nuevo("huella-nueva");

        anterior.Invalidar();

        Assert.Equal(ResultadoDelCodigo.NoVigente, anterior.Verificar("huella-vieja", Ahora));
        Assert.Equal(ResultadoDelCodigo.Correcto, nuevo.Verificar("huella-nueva", Ahora));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public void Un_codigo_ya_usado_vencido_o_invalidado_no_cuenta_intentos()
    {
        var usado = Nuevo();
        usado.Verificar("huella-correcta", Ahora);
        var vencido = Nuevo();
        var invalidado = Nuevo();
        invalidado.Invalidar();

        usado.Verificar("x", Ahora);
        vencido.Verificar("x", Ahora.AddMinutes(11));
        invalidado.Verificar("x", Ahora);

        Assert.Equal(0, usado.Intentos);
        Assert.Equal(0, vencido.Intentos);
        Assert.Equal(0, invalidado.Intentos);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-14")]
    public void El_codigo_generado_tiene_seis_digitos_con_ceros_a_la_izquierda()
    {
        for (var i = 0; i < 200; i++)
        {
            Assert.Matches("^[0-9]{6}$", CodigoDeUnSoloUso.Generar());
        }

        Assert.True(CodigoDeUnSoloUso.TieneLaForma("012345"));
        Assert.False(CodigoDeUnSoloUso.TieneLaForma("12345"));
        Assert.False(CodigoDeUnSoloUso.TieneLaForma("12345a"));
        Assert.False(CodigoDeUnSoloUso.TieneLaForma(null));
        Assert.Equal("[código]", Nuevo().ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-17")]
    public void Un_codigo_restaurado_conserva_sus_intentos_y_su_estado()
    {
        var restaurado = CodigoDeUnSoloUso.Restaurar(Guid.NewGuid(), PropositoDeCodigo.RecuperacionDeAcceso, "h", Ahora, 4, usado: false, invalidado: false);

        Assert.Equal(ResultadoDelCodigo.Incorrecto, restaurado.Verificar("x", Ahora));
        Assert.Equal(ResultadoDelCodigo.IntentosAgotados, restaurado.Verificar("h", Ahora));
    }
}
