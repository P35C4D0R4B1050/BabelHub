using System.Text.Json;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Plataforma;

/// <summary>El contrato <see cref="ITokenDelUsuarioDeLaSesion"/> y su doble (hallazgo R12; D13 de <c>docs/03/08</c> y de <c>web-base</c>).</summary>
public sealed class TokenDelUsuarioDeLaSesionTests
{
    [Fact]
    public async Task ConSesion_EntregaUnTokenQueSoloSeRevelaConRevelar()
    {
        var doble = TokenDelUsuarioDePrueba.ConSesion();

        var token = await doble.Actual();

        Assert.NotNull(token);
        Assert.StartsWith("jwt.", token.Revelar(), StringComparison.Ordinal);
        Assert.Equal(1, doble.Pedidos);
    }

    [Fact]
    public async Task SinSesion_EntregaNull_ParaQueElLlamadorNoHagaLaLlamada()
    {
        Assert.Null(await TokenDelUsuarioDePrueba.SinSesion().Actual());
    }

    [Fact]
    public async Task ElToken_NoApareceEnSuTextoNiEnSuSerializacion()
    {
        var token = (await TokenDelUsuarioDePrueba.ConSesion().Actual())!;
        var texto = token.Revelar();

        Assert.Equal(ValorSecreto.Redactado, token.ToString());
        Assert.DoesNotContain(texto, $"{token}", StringComparison.Ordinal);
        Assert.DoesNotContain(texto, JsonSerializer.Serialize(token), StringComparison.Ordinal);
        Assert.DoesNotContain(texto, RedactorDeDatosProhibidos.Redactar($"token {token}"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Establecer_SimulaUnaRenovacionOLaPerdidaDeLaSesion()
    {
        var doble = TokenDelUsuarioDePrueba.ConSesion();
        var anterior = (await doble.Actual())!;

        doble.Establecer(new ValorSecreto("jwt.renovado"));
        Assert.NotEqual(anterior.Revelar(), (await doble.Actual())!.Revelar());

        doble.Establecer(null);
        Assert.Null(await doble.Actual());
    }

    [Fact]
    public void ElContrato_ViveLaRaizDeLaAplicacionDePlataforma_ParaQueLoConsumaLaInfraestructura()
    {
        var tipo = typeof(ITokenDelUsuarioDeLaSesion);

        Assert.True(tipo.IsInterface);
        Assert.Equal("BabelHub.Ecommerce.Application.Plataforma", tipo.Namespace);
    }
}
