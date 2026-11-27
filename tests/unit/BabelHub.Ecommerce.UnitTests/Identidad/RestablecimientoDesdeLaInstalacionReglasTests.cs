using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.UnitTests.Identidad;

/// <summary>
/// Las reglas del restablecimiento del segundo factor desde la consola (identidad-recuperacion-del-segundo-factor T01; RSF-02, RSF-04 a RSF-06): en qué entornos se atiende, quién cuenta como otra administración que puede ingresar y en qué orden se rechaza.
/// </summary>
public sealed class RestablecimientoDesdeLaInstalacionReglasTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 27, 15, 0, 0, TimeSpan.Zero);

    private static AccesoDeUnaAdministracion Administracion(
        RolDeUsuario rol = RolDeUsuario.AdministradorCentral, bool activa = true, bool cancelada = false, bool temporal = false, DateTimeOffset? hasta = null) =>
        new(Guid.NewGuid(), rol, activa, cancelada, temporal, hasta);

    private static DecisionDelRestablecimientoDesdeLaInstalacion Decidir(AccesoDeUnaAdministracion? objetivo, bool conSegundoFactor = true, params AccesoDeUnaAdministracion[] demas) =>
        ReglasDeRestablecimientoDesdeLaInstalacion.Decidir(objetivo, conSegundoFactor, objetivo is null ? demas : [objetivo, .. demas], Ahora);

    [Theory]
    [Trait("Criterio", "AC-RSF-02")]
    [InlineData("Staging", null)]
    [InlineData("Production", "")]
    [InlineData("production", "Normal")]
    [InlineData("STAGING", "normal")]
    public void ElEntornoDeRevisionYElDeProduccionEnModoNormal_SeAtienden(string entorno, string? modo) =>
        Assert.True(ReglasDeRestablecimientoDesdeLaInstalacion.EntornoPermitido(entorno, modo));

    [Theory]
    [Trait("Criterio", "AC-RSF-02")]
    [InlineData("Development", null)]
    [InlineData("Test", null)]
    [InlineData("Ensayo", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("Production", "Ensayo")]
    [InlineData("Production", "Restauracion")]
    [InlineData("Staging", "Operacion")]
    [InlineData("Production", "cualquiera")]
    public void CualquierOtroEntornoOModo_SeRechaza(string? entorno, string? modo) =>
        Assert.False(ReglasDeRestablecimientoDesdeLaInstalacion.EntornoPermitido(entorno, modo));

    [Fact]
    [Trait("Criterio", "AC-RSF-05")]
    public void LaUnicaAdministracionConSegundoFactor_SeRestablece() =>
        Assert.Equal(DecisionDelRestablecimientoDesdeLaInstalacion.Permitido, Decidir(Administracion()));

    [Fact]
    [Trait("Criterio", "AC-RSF-04")]
    public void SinCuenta_ODeOtroRol_ODesactivadaOCancelada_NoEsUnaAdministracionActiva()
    {
        Assert.Equal(DecisionDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva, Decidir(null));
        foreach (var objetivo in new[]
        {
            Administracion(RolDeUsuario.Cliente), Administracion(RolDeUsuario.EncargadoDeSucursal), Administracion(RolDeUsuario.Logistica), Administracion(activa: false), Administracion(cancelada: true),
        })
        {
            Assert.Equal(DecisionDelRestablecimientoDesdeLaInstalacion.NoEsUnaAdministracionActiva, Decidir(objetivo));
        }
    }

    [Theory]
    [Trait("Criterio", "AC-RSF-05")]
    [InlineData(false, null)]
    [InlineData(true, 1)]
    [InlineData(true, 1440)]
    public void ConOtraAdministracionQuePuedeIngresar_SeRechaza(bool temporal, int? minutosQueLeQuedan)
    {
        var otra = Administracion(temporal: temporal, hasta: minutosQueLeQuedan is { } m ? Ahora.AddMinutes(m) : null);

        Assert.True(otra.PuedeIngresar(Ahora));
        Assert.Equal(DecisionDelRestablecimientoDesdeLaInstalacion.HayOtraAdministracion, Decidir(Administracion(), true, otra));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-05")]
    public void LasDemasQueNoPuedenIngresar_NoImpidenElRestablecimiento()
    {
        var demas = new[]
        {
            Administracion(activa: false),
            Administracion(cancelada: true),
            // Vence justo ahora: vencida, con el mismo corte que el ingreso.
            Administracion(temporal: true, hasta: Ahora),
            Administracion(temporal: true, hasta: Ahora.AddHours(-3)),
            Administracion(RolDeUsuario.Logistica),
        };

        Assert.All(demas, d => Assert.False(d.PuedeIngresar(Ahora)));
        Assert.Equal(DecisionDelRestablecimientoDesdeLaInstalacion.Permitido, Decidir(Administracion(), true, demas));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-05")]
    public void LaCuentaIndicadaConLaTemporalVencida_SeRestableceIgual() =>
        Assert.Equal(DecisionDelRestablecimientoDesdeLaInstalacion.Permitido, Decidir(Administracion(temporal: true, hasta: Ahora.AddDays(-1))));

    [Fact]
    [Trait("Criterio", "AC-RSF-06")]
    public void SinSegundoFactorActivado_SeInforma_PeroLaOtraAdministracionVaAntes()
    {
        Assert.Equal(DecisionDelRestablecimientoDesdeLaInstalacion.SinSegundoFactor, Decidir(Administracion(), conSegundoFactor: false));
        Assert.Equal(DecisionDelRestablecimientoDesdeLaInstalacion.HayOtraAdministracion, Decidir(Administracion(), false, Administracion()));
    }

    [Fact]
    [Trait("Criterio", "AC-RSF-05")]
    public void LaTemporalVence_AlLlegarSuHora_YSoloSiEsTemporal()
    {
        Assert.False(VigenciaDeLaContrasenaTemporal.Vencida(debeCambiar: true, Ahora.AddTicks(1), Ahora));
        Assert.True(VigenciaDeLaContrasenaTemporal.Vencida(debeCambiar: true, Ahora, Ahora));
        Assert.False(VigenciaDeLaContrasenaTemporal.Vencida(debeCambiar: false, Ahora.AddDays(-1), Ahora));
        Assert.False(VigenciaDeLaContrasenaTemporal.Vencida(debeCambiar: true, hasta: null, Ahora));
    }
}
