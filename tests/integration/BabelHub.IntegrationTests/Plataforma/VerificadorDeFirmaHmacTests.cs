using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class VerificadorDeFirmaHmacTests
{
    private static readonly byte[] SecretoDePagos = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] SecretoAnterior = Enumerable.Range(60, 32).Select(i => (byte)i).ToArray();
    private static readonly byte[] SecretoDeAvisos = Enumerable.Range(120, 40).Select(i => (byte)i).ToArray();

    private const string Marca = "1763035200";
    private static readonly byte[] Cuerpo = Encoding.UTF8.GetBytes("{\"evento\":\"pago.confirmado\",\"monto\":\"125.00\"}");

    private static VerificadorDeFirmaHmac Verificador(params (string Proveedor, byte[][] Secretos)[] proveedores) =>
        new(new OpcionesDeFirmaDeIntegraciones(proveedores.Select(p => new KeyValuePair<string, IReadOnlyList<byte[]>>(p.Proveedor, p.Secretos))));

    private static VerificadorDeFirmaHmac Estandar() => Verificador(("pasarela", [SecretoDePagos]), ("avisos", [SecretoDeAvisos]));

    private static string Firmar(byte[] secreto, string marca, byte[] cuerpo)
    {
        var mensaje = Encoding.UTF8.GetBytes(marca + ".").Concat(cuerpo).ToArray();
        return Convert.ToHexString(HMACSHA256.HashData(secreto, mensaje)).ToLowerInvariant();
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public void Verificar_ConUnaFirmaValidaSobreMarcaPuntoCuerpo_Acepta()
    {
        var firma = Firmar(SecretoDePagos, Marca, Cuerpo);

        Assert.True(Estandar().Verificar("pasarela", Marca, Cuerpo, firma));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-11")]
    [InlineData(false, "sha256=")]
    [InlineData(true, "")]
    [InlineData(true, "sha256=")]
    [InlineData(true, "SHA256=")]
    public void Verificar_ConMayusculasOElPrefijoSha256_Acepta(bool mayusculas, string prefijo)
    {
        var firma = Firmar(SecretoDePagos, Marca, Cuerpo);
        firma = prefijo + (mayusculas ? firma.ToUpperInvariant() : firma);

        Assert.True(Estandar().Verificar("pasarela", Marca, Cuerpo, firma));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public void Verificar_ConUnaFirmaCalculadaConOtroSecreto_Rechaza()
    {
        Assert.False(Estandar().Verificar("pasarela", Marca, Cuerpo, Firmar(SecretoDeAvisos, Marca, Cuerpo)));
        Assert.False(Estandar().Verificar("pasarela", Marca, Cuerpo, Firmar(SecretoAnterior, Marca, Cuerpo)));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public void Verificar_ConElCuerpoOLaMarcaAlterados_Rechaza()
    {
        var firma = Firmar(SecretoDePagos, Marca, Cuerpo);
        var alterado = Encoding.UTF8.GetBytes("{\"evento\":\"pago.confirmado\",\"monto\":\"1250.00\"}");

        Assert.False(Estandar().Verificar("pasarela", Marca, alterado, firma));
        Assert.False(Estandar().Verificar("pasarela", "1763035201", Cuerpo, firma));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public void Verificar_ConUnaFirmaDeOtroProveedor_RechazaAunqueSeaValidaParaEse()
    {
        var firmaDeAvisos = Firmar(SecretoDeAvisos, Marca, Cuerpo);

        Assert.True(Estandar().Verificar("avisos", Marca, Cuerpo, firmaDeAvisos));
        Assert.False(Estandar().Verificar("pasarela", Marca, Cuerpo, firmaDeAvisos));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-11")]
    [InlineData("desconocido")]
    [InlineData("")]
    public void Verificar_ConUnProveedorQueNoTieneSecreto_Rechaza(string proveedor)
    {
        Assert.False(Estandar().Verificar(proveedor, Marca, Cuerpo, Firmar(SecretoDePagos, Marca, Cuerpo)));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-11")]
    [InlineData("")]
    [InlineData("no-es-hexadecimal")]
    [InlineData("abc")]
    [InlineData("00")]
    public void Verificar_ConUnaFirmaVaciaMalFormadaOCorta_RechazaSinLanzar(string firma)
    {
        Assert.False(Estandar().Verificar("pasarela", Marca, Cuerpo, firma));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-11")]
    public void Verificar_ConUnaMarcaVacia_Rechaza()
    {
        Assert.False(Estandar().Verificar("pasarela", string.Empty, Cuerpo, Firmar(SecretoDePagos, string.Empty, Cuerpo)));
    }

    [Fact]
    public void Verificar_DuranteUnaRotacion_AceptaElSecretoActualYElAnterior()
    {
        var verificador = Verificador(("pasarela", [SecretoDePagos, SecretoAnterior]));

        Assert.True(verificador.Verificar("pasarela", Marca, Cuerpo, Firmar(SecretoDePagos, Marca, Cuerpo)));
        Assert.True(verificador.Verificar("pasarela", Marca, Cuerpo, Firmar(SecretoAnterior, Marca, Cuerpo)));
        Assert.False(verificador.Verificar("pasarela", Marca, Cuerpo, Firmar(SecretoDeAvisos, Marca, Cuerpo)));
    }

    [Fact]
    public void Verificar_ConUnCuerpoVacio_FirmaYVerificaSobreSoloLaMarca()
    {
        var vacio = Array.Empty<byte>();

        Assert.True(Estandar().Verificar("pasarela", Marca, vacio, Firmar(SecretoDePagos, Marca, vacio)));
    }

    [Fact]
    public void Opciones_ConSecretosCortosRepetidosOSinSecretos_Lanzan()
    {
        Assert.Throws<ArgumentException>(() => Verificador(("pasarela", [new byte[16]])));
        Assert.Throws<ArgumentException>(() => Verificador(("pasarela", [])));
        Assert.Throws<ArgumentException>(() => Verificador(("pasarela", [SecretoDePagos, SecretoAnterior, SecretoDeAvisos])));
        Assert.Throws<ArgumentException>(() => Verificador(("pasarela", [SecretoDePagos]), ("pasarela", [SecretoAnterior])));
        Assert.Throws<ArgumentException>(() => Verificador((" ", [SecretoDePagos])));
    }
}
