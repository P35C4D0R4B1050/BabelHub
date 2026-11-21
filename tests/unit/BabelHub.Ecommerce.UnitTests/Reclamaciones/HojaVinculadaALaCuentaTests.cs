using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.UnitTests.Reclamaciones;

/// <summary>
/// El vínculo de la hoja con la cuenta de su titular (T24; REC-21, REC-23, REC-28, REC-31, REC-32; D14): solo pasa de ninguno a una cuenta y de una cuenta a ninguno, nunca de una cuenta a otra, y es nulo siempre que la hoja sea de una
/// cuenta cancelada o esté anonimizada.
/// </summary>
public sealed class HojaVinculadaALaCuentaTests
{
    private static readonly Guid Cuenta = Guid.Parse("c0000000-0000-0000-0000-000000000001");
    private static readonly Guid OtraCuenta = Guid.Parse("c0000000-0000-0000-0000-000000000002");
    private static readonly DateTimeOffset Registro = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    private static HojaDeReclamacion Vinculada(Guid? cuenta) =>
        HojaDeReclamacion.Registrar(
            Guid.NewGuid(),
            NumeroDeHoja.Crear(2026, 1),
            TipoDeHoja.Reclamo,
            new DatosDelConsumidor("Ana Pérez Quispe", "12345678", "Av. Los Olivos 123, Ayacucho", "ana.perez@correo.com"),
            "Libro",
            49.90m,
            "Llegó con las hojas sueltas.",
            null,
            "contenido-hash",
            "clave-hash",
            Registro,
            cuenta);

    private static HojaDeReclamacion Rehidratada(Guid? cuenta, bool deCuentaCancelada = false, DateTimeOffset? anonimizadaEn = null) =>
        HojaDeReclamacion.Rehidratar(
            Guid.NewGuid(),
            NumeroDeHoja.Crear(2026, 2),
            TipoDeHoja.Queja,
            new DatosDelConsumidor("Ana Pérez Quispe", "12345678", "Av. Los Olivos 123, Ayacucho", "ana.perez@correo.com"),
            "Libro",
            10m,
            "Detalle",
            null,
            "contenido-hash",
            Registro,
            "clave-hash-2",
            EstadoDeHoja.Pendiente,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            deCuentaCancelada,
            anonimizadaEn,
            cuenta);

    [Fact]
    [Trait("Criterio", "AC-REC-29")]
    public void Una_hoja_se_registra_sin_cuenta_por_omision()
    {
        var hoja = HojaDeReclamacionTests.Nueva();

        Assert.Null(hoja.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-20")]
    public void Una_hoja_registrada_con_sesion_lleva_la_cuenta_de_la_sesion()
    {
        var hoja = Vinculada(Cuenta);

        Assert.Equal(Cuenta, hoja.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-20")]
    public void Registrar_con_un_identificador_de_cuenta_vacio_es_un_error()
    {
        Assert.Throws<ArgumentException>(() => Vinculada(Guid.Empty));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    public void Vincular_una_hoja_sin_cuenta_la_deja_vinculada()
    {
        var hoja = Vinculada(null);

        Assert.True(hoja.Vincular(Cuenta));

        Assert.Equal(Cuenta, hoja.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-25")]
    public void Una_hoja_ya_vinculada_nunca_pasa_a_otra_cuenta_ni_cambia_al_repetir_el_vinculo()
    {
        var hoja = Vinculada(Cuenta);

        Assert.False(hoja.Vincular(OtraCuenta));
        Assert.False(hoja.Vincular(Cuenta));

        Assert.Equal(Cuenta, hoja.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    public void Una_hoja_de_una_cuenta_cancelada_no_se_vincula()
    {
        var hoja = Rehidratada(null, deCuentaCancelada: true);

        Assert.False(hoja.Vincular(Cuenta));

        Assert.Null(hoja.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    public void Una_hoja_anonimizada_no_se_vincula()
    {
        var hoja = Rehidratada(null, anonimizadaEn: Registro.AddYears(2));

        Assert.False(hoja.Vincular(Cuenta));

        Assert.Null(hoja.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-21")]
    public void Vincular_con_un_identificador_vacio_es_un_error()
    {
        Assert.Throws<ArgumentException>(() => Vinculada(null).Vincular(Guid.Empty));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-27")]
    public void Desvincular_deja_la_hoja_sin_cuenta_y_es_idempotente()
    {
        var hoja = Vinculada(Cuenta);

        Assert.True(hoja.Desvincular());
        Assert.False(hoja.Desvincular());

        Assert.Null(hoja.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public void Marcar_la_hoja_de_una_cuenta_cancelada_la_desvincula()
    {
        var hoja = Vinculada(Cuenta);

        Assert.True(hoja.MarcarDeCuentaCancelada());

        Assert.True(hoja.DeCuentaCancelada);
        Assert.Null(hoja.CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-18")]
    public void Marcar_una_hoja_sin_cuenta_tambien_la_marca_y_repetirlo_no_cambia_nada()
    {
        var hoja = Vinculada(null);

        Assert.True(hoja.MarcarDeCuentaCancelada());
        Assert.False(hoja.MarcarDeCuentaCancelada());
        Assert.Null(hoja.CuentaId);
    }

    [Theory]
    [Trait("Criterio", "AC-REC-18")]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Rehidratar_una_hoja_cancelada_o_anonimizada_con_cuenta_rompe_el_invariante(bool cancelada, bool anonimizada)
    {
        Assert.Throws<ArgumentException>(() => Rehidratada(Cuenta, cancelada, anonimizada ? Registro.AddYears(2) : null));
    }

    [Fact]
    [Trait("Criterio", "AC-REC-12")]
    public void Rehidratar_una_hoja_vinculada_la_conserva_vinculada()
    {
        Assert.Equal(Cuenta, Rehidratada(Cuenta).CuentaId);
    }

    [Fact]
    [Trait("Criterio", "AC-REC-26")]
    public void El_vinculo_no_aparece_en_la_representacion_como_texto_de_la_hoja()
    {
        Assert.DoesNotContain(Cuenta.ToString(), Vinculada(Cuenta).ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
