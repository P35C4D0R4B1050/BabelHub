using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

public sealed class HuellasTests
{
    // Un resumen de 43 caracteres como el de un HMAC-SHA256 en base64url.
    private const string Resumen = "k3Jx9bQ2vT7mYw1ZpLr4NfA8sUe0dHcGiXoVtB5yMqE";

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public void HuellaDeOrigen_ConUnResumenYUnaVersion_ConservaSoloEsosDosValores()
    {
        var huella = HuellaDeOrigen.Crear(Resumen, "v2");

        Assert.Equal(Resumen, huella.Valor);
        Assert.Equal("v2", huella.VersionDeClave);
        Assert.False(huella.EsDesconocido);
        Assert.Equal("v2:" + Resumen, huella.ToString());
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-22")]
    [InlineData("203.0.113.7")]
    [InlineData("2001:db8:85a3::8a2e:370:7334")]
    [InlineData("cliente@correo.pe")]
    [InlineData("+51987654321")]
    [InlineData("corta")]
    [InlineData("")]
    [InlineData("tiene espacios en el medio de la huella")]
    public void HuellaDeOrigen_ConUnDatoQueNoEsUnResumen_Lanza(string dato)
    {
        Assert.Throws<ArgumentException>(() => HuellaDeOrigen.Crear(dato, "v1"));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-22")]
    [InlineData("")]
    [InlineData("v 1")]
    [InlineData("version-demasiado-larga-1")]
    [InlineData("v.1")]
    public void Huella_ConUnaVersionInvalida_Lanza(string version)
    {
        Assert.Throws<ArgumentException>(() => HuellaDeOrigen.Crear(Resumen, version));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public void HuellaDeOrigen_ConElMismoResumenYDistintaVersion_NoSonIguales()
    {
        // Al rotar la clave, el contador de la versión anterior y el de la nueva son distintos.
        Assert.Equal(HuellaDeOrigen.Crear(Resumen, "v1"), HuellaDeOrigen.Crear(Resumen, "v1"));
        Assert.NotEqual(HuellaDeOrigen.Crear(Resumen, "v1"), HuellaDeOrigen.Crear(Resumen, "v2"));
    }

    [Fact]
    public void HuellaDeOrigen_Desconocido_EsUnaHuellaFijaCompartida()
    {
        Assert.True(HuellaDeOrigen.Desconocido.EsDesconocido);
        Assert.Same(HuellaDeOrigen.Desconocido, HuellaDeOrigen.Desconocido);
        Assert.False(HuellaDeOrigen.Crear(Resumen, "v1").EsDesconocido);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public void LosConstructoresDeLasHuellas_NoSonPublicos()
    {
        // Solo la Infraestructura (ICalculadorDeHuella) puede construirlas: ningún módulo guarda el dato original.
        foreach (var tipo in new[] { typeof(HuellaDeOrigen) })
        {
            Assert.Empty(tipo.GetConstructors());
            Assert.DoesNotContain(tipo.GetMethods(), m => m.IsPublic && m.IsStatic && m.ReturnType == tipo && m.Name == "Crear");
        }
    }
}
