using System.Security.Cryptography;
using System.Text;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Reservas;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>T24: <c>HmacGeneradorDeIdentificadorAnonimo</c> (D09, SEL-34): estable, distinto entre titulares, sin recuperar el original y una clave que el arranque exige fuerte, presente y propia.</summary>
public sealed class HmacGeneradorDeIdentificadorAnonimoTests
{
    private const string Clave = "una-clave-de-bajas-larga-y-variada-9f3a7c1e5b";

    private static IConfiguration Configuracion(params (string Ruta, string? Valor)[] valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores.Select(v => new KeyValuePair<string, string?>(v.Ruta, v.Valor))).Build();

    private static ConfiguracionDeSecretosInvalidaException Falla(IConfiguration configuracion) =>
        Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => ClaveDeBajasDelEntorno.Cargar(configuracion));

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void ElMismoTitularDaSiempreElMismoIdentificador_YTitularesDistintosDanDistintos()
    {
        var generador = new HmacGeneradorDeIdentificadorAnonimo(Clave);
        var titular = Guid.NewGuid();
        var otro = Guid.NewGuid();

        var primero = generador.Generar(titular);

        Assert.Equal(primero, generador.Generar(titular));
        Assert.Equal(primero, new HmacGeneradorDeIdentificadorAnonimo(Clave).Generar(titular));
        Assert.NotEqual(primero, generador.Generar(otro));
        Assert.NotEqual(titular, primero);
        Assert.NotEqual(Guid.Empty, primero);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void ElValorEsElHmacSha256ConSeparacionDeDominioTruncadoA128Bits()
    {
        var titular = Guid.NewGuid();
        var esperado = new Guid(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Clave), Encoding.UTF8.GetBytes("baja:v1:" + titular.ToString("D")))[..16]);
        var sinDominio = new Guid(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Clave), Encoding.UTF8.GetBytes(titular.ToString("D")))[..16]);

        var generado = new HmacGeneradorDeIdentificadorAnonimo(Clave).Generar(titular);

        Assert.Equal(esperado, generado);
        Assert.NotEqual(sinDominio, generado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void OtraClaveDaOtroIdentificador_ElOriginalNoSeRecuperaSinLaClave()
    {
        var titular = Guid.NewGuid();

        var conUna = new HmacGeneradorDeIdentificadorAnonimo(Clave).Generar(titular);
        var conOtra = new HmacGeneradorDeIdentificadorAnonimo(Clave + "-otra").Generar(titular);

        Assert.NotEqual(conUna, conOtra);

        // La función solo va en un sentido: la salida no es el titular ni sus bytes.
        Assert.False(conUna.ToByteArray().SequenceEqual(titular.ToByteArray()));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void UnTitularVacio_SeRechaza()
    {
        Assert.Throws<ArgumentException>(() => new HmacGeneradorDeIdentificadorAnonimo(Clave).Generar(Guid.Empty));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void UnaClaveCorta_SeRechazaEnElConstructor_SinMostrarSuValor()
    {
        var corta = "corta-pero-secreta";

        var error = Assert.Throws<ArgumentException>(() => new HmacGeneradorDeIdentificadorAnonimo(corta));

        Assert.DoesNotContain(corta, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void ConLaClaveBien_CargarDevuelveElGenerador()
    {
        var generador = ClaveDeBajasDelEntorno.Cargar(Configuracion((HmacGeneradorDeIdentificadorAnonimo.Ruta, Clave)));

        var titular = Guid.NewGuid();
        Assert.Equal(new HmacGeneradorDeIdentificadorAnonimo(Clave).Generar(titular), generador.Generar(titular));
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void SiLaClaveFalta_ElArranqueFalla_NombrandoLaVariable()
    {
        var error = Falla(Configuracion());

        Assert.Contains(error.Problemas, p => p.StartsWith("BABELHUB__Inventario__ClaveDeBajas", StringComparison.Ordinal) && p.Contains("falta", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-36")]
    [InlineData("corta-y-variada-123")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void SiLaClaveEsCortaODebil_ElArranqueFalla_SinMostrarSuValor(string clave)
    {
        var error = Falla(Configuracion((HmacGeneradorDeIdentificadorAnonimo.Ruta, clave)));

        Assert.Contains(error.Problemas, p => p.Contains("es débil", StringComparison.Ordinal));
        Assert.DoesNotContain(error.Problemas, p => p.Contains(clave, StringComparison.Ordinal));
        Assert.DoesNotContain(clave, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEL-36")]
    [InlineData("Seguridad:Credenciales:ConsultaDePendientes", Clave)]
    [InlineData("Seguridad:ClaveDeHuella", "v1:" + Clave)]
    [InlineData("Integraciones:Pagos:Secreto", Clave)]
    public void SiLaClaveSeRepiteConOtroSecretoDelServicio_ElArranqueFalla(string ruta, string valor)
    {
        var error = Falla(Configuracion((HmacGeneradorDeIdentificadorAnonimo.Ruta, Clave), (ruta, valor)));

        Assert.Contains(error.Problemas, p => p.Contains("se repite en " + LectorDeSecretos.Variable(ruta), StringComparison.Ordinal));
        Assert.DoesNotContain(Clave, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void ConUnLectorCompartido_LaClaveSeComparaConLosDemasSecretosYSeJuntanLosProblemas()
    {
        var configuracion = Configuracion((HmacGeneradorDeIdentificadorAnonimo.Ruta, Clave), ("Seguridad:OtraClave", Clave));
        var lector = new LectorDeSecretos(configuracion);
        lector.Comprobar("Seguridad:OtraClave", Clave);

        var generador = ClaveDeBajasDelEntorno.Leer(lector);

        Assert.Null(generador);
        Assert.Contains(lector.Problemas, p => p.Contains("se repite en BABELHUB__Seguridad__OtraClave", StringComparison.Ordinal));
        Assert.Throws<ConfiguracionDeSecretosInvalidaException>(lector.ExigirValida);
    }

    [Fact]
    [Trait("Criterio", "AC-SEL-36")]
    public void LaComposicionDelModulo_ExigeLaClaveAlArrancar()
    {
        var sinClave = new ServiceCollection();
        sinClave.AgregarModuloReservas(Configuracion());
        using var proveedorSinClave = sinClave.BuildServiceProvider();

        Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => ComposicionDeReservas.ExigirClaveDeBajas(proveedorSinClave));

        var conClave = new ServiceCollection();
        conClave.AgregarModuloReservas(Configuracion((HmacGeneradorDeIdentificadorAnonimo.Ruta, Clave)));
        using var proveedorConClave = conClave.BuildServiceProvider();

        Assert.IsType<HmacGeneradorDeIdentificadorAnonimo>(ComposicionDeReservas.ExigirClaveDeBajas(proveedorConClave));
        Assert.IsAssignableFrom<IGeneradorDeIdentificadorAnonimo>(proveedorConClave.GetRequiredService<IGeneradorDeIdentificadorAnonimo>());
    }
}
