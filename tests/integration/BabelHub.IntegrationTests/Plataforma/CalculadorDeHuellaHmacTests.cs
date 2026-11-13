using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class CalculadorDeHuellaHmacTests
{
    private static readonly byte[] BytesV2 = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] BytesV1 = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();
    private static readonly string ClaveV2 = "v2:" + Convert.ToBase64String(BytesV2);
    private static readonly string ClaveV1 = "v1:" + Convert.ToBase64String(BytesV1);

    private static CalculadorDeHuellaHmac Calculador(string actual = "", string? anterior = null) =>
        new(new OpcionesDeHuella(actual.Length == 0 ? ClaveV2 : actual, anterior));

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public void DeOrigen_ConLaMismaDireccion_DaLaMismaHuellaConLaVersionDeLaClave()
    {
        var calculador = Calculador();

        var huella = calculador.DeOrigen("203.0.113.7");

        Assert.Equal(huella, calculador.DeOrigen("203.0.113.7"));
        Assert.Equal("v2", huella.VersionDeClave);
        Assert.Equal(43, huella.Valor.Length);
        Assert.Matches("^[A-Za-z0-9_-]+$", huella.Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public void DeOrigen_ConUnaDireccion_NoGuardaLaDireccionSoloUnHmacConLaClaveDelEntorno()
    {
        var huella = Calculador().DeOrigen("203.0.113.7");

        var esperado = Convert.ToBase64String(HMACSHA256.HashData(BytesV2, Encoding.UTF8.GetBytes("origen\u001f4:203.0.113.7"))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Equal(esperado, huella.Valor);
        Assert.DoesNotContain("203", huella.ToString().Split(':')[1], StringComparison.Ordinal);

        // Sin la clave no hay forma de reproducirla: otra clave da otra huella para la misma dirección.
        var otraClave = Calculador("v9:" + Convert.ToBase64String(Enumerable.Range(50, 32).Select(i => (byte)i).ToArray()));
        Assert.NotEqual(huella.Valor, otraClave.DeOrigen("203.0.113.7").Valor);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-22")]
    [InlineData("203.0.113.7", "::ffff:203.0.113.7")]
    [InlineData("203.0.113.7", " 203.0.113.7 ")]
    [InlineData("2001:db8:85a3:1::1", "2001:0DB8:85A3:0001:0000:0000:0000:0001")]
    [InlineData("2001:db8:85a3:1::1", "2001:db8:85a3:1:ffff:ffff:ffff:ffff")]
    [InlineData("2001:db8:85a3:1::1", "2001:db8:85a3:1::1%eth0")]
    public void DeOrigen_ConLaMismaPersonaEnOtraFormaOElMismoPrefijo64_DaLaMismaHuella(string a, string b)
    {
        var calculador = Calculador();

        Assert.Equal(calculador.DeOrigen(a), calculador.DeOrigen(b));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-22")]
    [InlineData("203.0.113.7", "203.0.113.8")]
    [InlineData("2001:db8:85a3:1::1", "2001:db8:85a3:2::1")]
    [InlineData("2001:db8:85a3:1::1", "203.0.113.7")]
    public void DeOrigen_ConOrigenesDistintos_DaHuellasDistintas(string a, string b)
    {
        var calculador = Calculador();

        Assert.NotEqual(calculador.DeOrigen(a), calculador.DeOrigen(b));
    }

    [Theory]
    [InlineData("")]
    [InlineData("no-es-una-ip")]
    [InlineData("203.0.113.256")]
    [InlineData("203.0.113.7, 198.51.100.1")]
    public void DeOrigen_ConUnaDireccionInvalida_Lanza(string direccion)
    {
        Assert.Throws<ArgumentException>(() => Calculador().DeOrigen(direccion));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public void DeOrigenPorRed48_AgrupaLasRedesIPv6DeUnMismoUsuarioYNoAplicaAIPv4()
    {
        var calculador = Calculador();

        var a = calculador.DeOrigenPorRed48("2001:db8:85a3:1::1");
        var b = calculador.DeOrigenPorRed48("2001:db8:85a3:ffff::9");
        var otra = calculador.DeOrigenPorRed48("2001:db8:85a4:1::1");

        Assert.Equal(a, b);
        Assert.NotEqual(a, otra);
        Assert.Null(calculador.DeOrigenPorRed48("203.0.113.7"));
        Assert.NotEqual(a!.Valor, calculador.DeOrigen("2001:db8:85a3:1::1").Valor);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public void DeSujeto_NormalizaNfkcYMinusculasInvariantesYSeparaLosPropositos()
    {
        var calculador = Calculador();

        var correo = calculador.DeSujeto("lector@correo.pe", "correo");

        Assert.Equal(correo, calculador.DeSujeto("  Lector@Correo.PE ", "correo"));
        Assert.Equal(correo, calculador.DeSujeto("ｌｅｃｔｏｒ＠ｃｏｒｒｅｏ．ｐｅ", "correo")); // ancho completo
        Assert.Equal(calculador.DeSujeto("oficial@correo.pe", "CORREO"), calculador.DeSujeto("oficial@correo.pe", " correo "));
        Assert.Equal(calculador.DeSujeto("ﬁnal@correo.pe", "correo"), calculador.DeSujeto("final@correo.pe", "correo")); // ligadura fi
        Assert.NotEqual(correo, calculador.DeSujeto("lector@correo.pe", "codigos"));
        Assert.DoesNotContain("lector", correo.Valor, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "correo")]
    [InlineData("a@b.pe", "")]
    [InlineData("a@b.pe", "   ")]
    public void DeSujeto_ConDatosVacios_Lanza(string valor, string proposito)
    {
        Assert.Throws<ArgumentException>(() => Calculador().DeSujeto(valor, proposito));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-22")]
    public void EnRotacion_ConlaClaveAnterior_DevuelveLaHuellaActualYLaDeLaVersionAnteriorParaNoReiniciarLosContadores()
    {
        var antes = Calculador(ClaveV1).DeOrigen("203.0.113.7");
        var calculador = Calculador(ClaveV2, ClaveV1);

        var huellas = calculador.DeOrigenEnRotacion("203.0.113.7");

        Assert.Equal(2, huellas.Count);
        Assert.Equal("v2", huellas[0].VersionDeClave);
        Assert.Equal("v1", huellas[1].VersionDeClave);
        Assert.Equal(antes, huellas[1]); // el contador abierto con la clave anterior se sigue encontrando
        Assert.Equal(calculador.DeOrigen("203.0.113.7"), huellas[0]);
        Assert.NotEqual(huellas[0].Valor, huellas[1].Valor);

        var correos = calculador.DeSujetoEnRotacion("lector@correo.pe", "correo");
        Assert.Equal(["v2", "v1"], correos.Select(c => c.VersionDeClave));
        Assert.Equal(Calculador(ClaveV1).DeSujeto("lector@correo.pe", "correo"), correos[1]);
    }

    [Fact]
    public void EnRotacion_SinClaveAnterior_DevuelveSoloLaActual()
    {
        var calculador = Calculador();

        Assert.Single(calculador.DeOrigenEnRotacion("203.0.113.7"));
        Assert.Single(calculador.DeSujetoEnRotacion("lector@correo.pe", "correo"));
    }

    [Theory]
    [InlineData("sin-version")]
    [InlineData(":AAAA")]
    [InlineData("v 1:AAAA")]
    [InlineData("v2:no-es-base64!")]
    [InlineData("v2:AAAAAAAA")]
    public void Opciones_ConUnaClaveInvalidaOCorta_Lanzan(string clave)
    {
        Assert.Throws<ArgumentException>(() => new OpcionesDeHuella(clave));
    }

    [Fact]
    public void Opciones_ConLaClaveAnteriorIgualOConLaMismaVersion_Lanzan()
    {
        Assert.Throws<ArgumentException>(() => new OpcionesDeHuella(ClaveV2, "v2:" + Convert.ToBase64String(BytesV1)));
        Assert.Throws<ArgumentException>(() => new OpcionesDeHuella(ClaveV2, "v1:" + Convert.ToBase64String(BytesV2)));
        Assert.Single(new CalculadorDeHuellaHmac(new OpcionesDeHuella(ClaveV2, "  ")).DeOrigenEnRotacion("203.0.113.7"));
    }

    [Fact]
    public void ElCalculadorDelInventario_AplicaLasMismasReglasDeOrigen()
    {
        var calculador = new BabelHub.Inventario.Infrastructure.Plataforma.CalculadorDeHuellaHmac(
            new BabelHub.Inventario.Infrastructure.Plataforma.OpcionesDeHuella(ClaveV2, ClaveV1));

        var huella = calculador.DeOrigen("2001:db8:85a3:1::1");

        Assert.Equal(huella, calculador.DeOrigen("2001:db8:85a3:1:ffff::2"));
        Assert.Equal(Calculador().DeOrigen("2001:db8:85a3:1::1").Valor, huella.Valor);
        Assert.Equal(2, calculador.DeOrigenEnRotacion("203.0.113.7").Count);
        Assert.Null(calculador.DeOrigenPorRed48("203.0.113.7"));
    }
}
