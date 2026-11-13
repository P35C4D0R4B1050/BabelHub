using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.Extensions.Configuration;

namespace BabelHub.IntegrationTests.Plataforma;

public sealed class ConfiguracionDeSeguridadTests
{
    private static readonly AlcanceDeServicio[] Alcances = [AlcanceDeServicio.OperacionDeUsuario, AlcanceDeServicio.LecturaDeDisponibilidad];

    private static string Aleatorio(int largo) => Convert.ToBase64String(Guid.NewGuid().ToByteArray().Concat(Guid.NewGuid().ToByteArray()).Concat(Guid.NewGuid().ToByteArray()).ToArray())[..largo];

    private static string ClaveDeHuella(string version = "v1") => version + ":" + Convert.ToBase64String(Enumerable.Range(0, 32).Select(_ => (byte)Random.Shared.Next(256)).ToArray());

    private static Dictionary<string, string?> Valida() => new()
    {
        ["Seguridad:ClaveDeHuella"] = ClaveDeHuella(),
        ["Seguridad:Credenciales:OperacionDeUsuario"] = Aleatorio(40),
        ["Seguridad:Credenciales:LecturaDeDisponibilidad"] = Aleatorio(40),
        ["Integraciones:pasarela:Secreto"] = Aleatorio(40),
        ["Seguridad:Turnstile:Secreto"] = Aleatorio(40),
        ["Seguridad:Turnstile:Hostnames"] = "tienda.babelhub.pe, www.babelhub.pe",
        ["Seguridad:ClaveDeRedisDeLimites"] = Aleatorio(40),
    };

    private static IConfiguration Configuracion(Dictionary<string, string?> valores) =>
        new ConfigurationBuilder().AddInMemoryCollection(valores).Build();

    private static ConfiguracionDeSecretosInvalidaException Falla(Dictionary<string, string?> valores) =>
        Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => ConfiguracionDelEcommerce.Cargar(Configuracion(valores), Alcances, ["pasarela"]));

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_ConTodosLosSecretosValidos_LosLeeDelEntorno()
    {
        var valores = Valida();

        var configuracion = ConfiguracionDelEcommerce.Cargar(Configuracion(valores), Alcances, ["pasarela"]);

        Assert.Equal("v1", new CalculadorDeHuellaHmac(configuracion.Seguridad.Huella).DeOrigen("203.0.113.7").VersionDeClave);
        Assert.Equal(ResultadoDeCredencialDeEntrada.Actual, configuracion.Seguridad.CredencialesDeEntrada.Comprobar(AlcanceDeServicio.OperacionDeUsuario, valores["Seguridad:Credenciales:OperacionDeUsuario"]));
        Assert.Equal(["tienda.babelhub.pe", "www.babelhub.pe"], configuracion.Turnstile.HostnamesPermitidos.Order());
        Assert.Equal("[redactado]", configuracion.ClaveDeRedisDeLimites.ToString());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_ConLasVariablesDeEntornoConPrefijoBabelhub_LasLeeDeSuNombreReal()
    {
        var valores = Valida();
        var entorno = valores.ToDictionary(p => "BABELHUB__" + p.Key.Replace(":", "__", StringComparison.Ordinal), p => p.Value);
        foreach (var (nombre, valor) in entorno)
        {
            Environment.SetEnvironmentVariable(nombre, valor);
        }

        try
        {
            var configuracion = new ConfigurationBuilder().AddEnvironmentVariables("BABELHUB__").Build();

            var cargada = ConfiguracionDelEcommerce.Cargar(configuracion, Alcances, ["pasarela"]);

            Assert.Equal("v1", new CalculadorDeHuellaHmac(cargada.Seguridad.Huella).DeOrigen("203.0.113.7").VersionDeClave);
        }
        finally
        {
            foreach (var nombre in entorno.Keys)
            {
                Environment.SetEnvironmentVariable(nombre, null);
            }
        }
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-25")]
    [InlineData("Seguridad:ClaveDeHuella")]
    [InlineData("Seguridad:Credenciales:OperacionDeUsuario")]
    [InlineData("Seguridad:Credenciales:LecturaDeDisponibilidad")]
    [InlineData("Integraciones:pasarela:Secreto")]
    [InlineData("Seguridad:Turnstile:Secreto")]
    [InlineData("Seguridad:Turnstile:Hostnames")]
    [InlineData("Seguridad:ClaveDeRedisDeLimites")]
    public void Cargar_SiFaltaUnaObligatoria_FallaYNombraLaVariable(string ruta)
    {
        var valores = Valida();
        valores.Remove(ruta);

        var error = Falla(valores);

        Assert.Contains("BABELHUB__" + ruta.Replace(":", "__", StringComparison.Ordinal) + ": falta", error.Problemas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_ConVariasEnBlancoOMalas_ReportaTodasJuntasSinMostrarLosValores()
    {
        var valores = Valida();
        valores["Seguridad:ClaveDeHuella"] = " ";
        valores["Seguridad:Credenciales:OperacionDeUsuario"] = "corto-secreto-123";
        valores["Seguridad:ClaveDeRedisDeLimites"] = new string('x', 50);

        var error = Falla(valores);

        Assert.Equal(3, error.Problemas.Count);
        Assert.DoesNotContain("corto-secreto-123", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 50), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-25")]
    [InlineData("corto")]
    [InlineData("1234567890123456789012345678901")]
    public void Cargar_ConUnaCredencialDeMenosDe32Bytes_FallaPorDebil(string credencial)
    {
        var valores = Valida();
        valores["Seguridad:Credenciales:OperacionDeUsuario"] = credencial;

        var error = Falla(valores);

        Assert.Contains(error.Problemas, p => p.StartsWith("BABELHUB__Seguridad__Credenciales__OperacionDeUsuario: es débil", StringComparison.Ordinal));
        Assert.DoesNotContain(credencial, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_ConUnSecretoDeUnSoloCaracterRepetido_FallaPorDebilAunqueSeaLargo()
    {
        var valores = Valida();
        valores["Seguridad:Credenciales:OperacionDeUsuario"] = new string('a', 64);

        var error = Falla(valores);

        Assert.Contains(error.Problemas, p => p.Contains("casi no varía", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_ConLaMismaCredencialEnDosAlcances_FallaPorRepetida()
    {
        var valores = Valida();
        valores["Seguridad:Credenciales:LecturaDeDisponibilidad"] = valores["Seguridad:Credenciales:OperacionDeUsuario"];

        var error = Falla(valores);

        Assert.Contains(error.Problemas, p => p.Contains("se repite en BABELHUB__Seguridad__Credenciales__OperacionDeUsuario", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_ConUnSecretoRepetidoEntreFamiliasDeSecretos_FallaPorRepetido()
    {
        var valores = Valida();
        valores["Seguridad:ClaveDeRedisDeLimites"] = valores["Seguridad:Credenciales:OperacionDeUsuario"];
        valores["Seguridad:Turnstile:Secreto"] = valores["Integraciones:pasarela:Secreto"];

        var error = Falla(valores);

        Assert.Equal(2, error.Problemas.Count(p => p.Contains("se repite en", StringComparison.Ordinal)));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_ConLaCredencialAnteriorIgualALaActual_Falla()
    {
        var valores = Valida();
        valores["Seguridad:Credenciales:OperacionDeUsuarioAnterior"] = valores["Seguridad:Credenciales:OperacionDeUsuario"];
        valores["Integraciones:pasarela:SecretoAnterior"] = valores["Integraciones:pasarela:Secreto"];

        var error = Falla(valores);

        Assert.Contains("BABELHUB__Seguridad__Credenciales__OperacionDeUsuarioAnterior: es igual a la actual", error.Problemas);
        Assert.Contains("BABELHUB__Integraciones__pasarela__SecretoAnterior: es igual a la actual", error.Problemas);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_ConLaClaveDeHuellaAnteriorIgualOConLaMismaVersion_Falla()
    {
        var valores = Valida();
        valores["Seguridad:ClaveDeHuellaAnterior"] = valores["Seguridad:ClaveDeHuella"];

        var mismaClave = Falla(valores);
        Assert.Contains(mismaClave.Problemas, p => p.StartsWith("BABELHUB__Seguridad__ClaveDeHuellaAnterior:", StringComparison.Ordinal));

        valores["Seguridad:ClaveDeHuellaAnterior"] = ClaveDeHuella("v1");
        var mismaVersion = Falla(valores);
        Assert.Contains(mismaVersion.Problemas, p => p.StartsWith("BABELHUB__Seguridad__ClaveDeHuellaAnterior:", StringComparison.Ordinal));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-25")]
    [InlineData("sin-version")]
    [InlineData("v1:no-es-base64!")]
    [InlineData("v1:AAAA")]
    public void Cargar_ConUnaClaveDeHuellaMalFormadaOCorta_FallaSinMostrarla(string clave)
    {
        var valores = Valida();
        valores["Seguridad:ClaveDeHuella"] = clave;

        var error = Falla(valores);

        Assert.Contains(error.Problemas, p => p.StartsWith("BABELHUB__Seguridad__ClaveDeHuella:", StringComparison.Ordinal));
        Assert.DoesNotContain(clave, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void Cargar_DuranteUnaRotacion_AceptaLaCredencialActualYLaAnteriorYLasDistingue()
    {
        var valores = Valida();
        var anterior = Aleatorio(40);
        valores["Seguridad:Credenciales:OperacionDeUsuarioAnterior"] = anterior;
        valores["Seguridad:ClaveDeHuellaAnterior"] = ClaveDeHuella("v0");

        var configuracion = ConfiguracionDelEcommerce.Cargar(Configuracion(valores), Alcances, ["pasarela"]).Seguridad;
        var entrada = configuracion.CredencialesDeEntrada;

        Assert.Equal(ResultadoDeCredencialDeEntrada.Actual, entrada.Comprobar(AlcanceDeServicio.OperacionDeUsuario, valores["Seguridad:Credenciales:OperacionDeUsuario"]));
        Assert.Equal(ResultadoDeCredencialDeEntrada.Anterior, entrada.Comprobar(AlcanceDeServicio.OperacionDeUsuario, anterior));
        Assert.Equal(ResultadoDeCredencialDeEntrada.Invalida, entrada.Comprobar(AlcanceDeServicio.OperacionDeUsuario, Aleatorio(40)));
        Assert.Equal(ResultadoDeCredencialDeEntrada.Invalida, entrada.Comprobar(AlcanceDeServicio.OperacionDeUsuario, valores["Seguridad:Credenciales:LecturaDeDisponibilidad"]));
        Assert.Equal(["v1", "v0"], new CalculadorDeHuellaHmac(configuracion.Huella).DeOrigenEnRotacion("203.0.113.7").Select(h => h.VersionDeClave));
    }

    [Theory]
    [Trait("Criterio", "AC-SEG-25")]
    [InlineData(null)]
    [InlineData("")]
    public void Comprobar_SinCredencialPresentada_EsInvalida(string? presentada)
    {
        var entrada = ConfiguracionDelEcommerce.Cargar(Configuracion(Valida()), Alcances, ["pasarela"]).Seguridad.CredencialesDeEntrada;

        Assert.Equal(ResultadoDeCredencialDeEntrada.Invalida, entrada.Comprobar(AlcanceDeServicio.OperacionDeUsuario, presentada));
        Assert.Equal(ResultadoDeCredencialDeEntrada.Invalida, entrada.Comprobar(AlcanceDeServicio.BajaDeTitular, "cualquiera"));
    }

    [Fact]
    public void Cargar_ComoServicioSinTurnstileNiRedis_SoloExigeLosSecretosComunes()
    {
        var valores = Valida();
        valores.Remove("Seguridad:Turnstile:Secreto");
        valores.Remove("Seguridad:Turnstile:Hostnames");
        valores.Remove("Seguridad:ClaveDeRedisDeLimites");

        var comunes = ConfiguracionDeSeguridad.Cargar(Configuracion(valores), Alcances, ["pasarela"]);

        Assert.Equal("v1", new CalculadorDeHuellaHmac(comunes.Huella).DeOrigen("203.0.113.7").VersionDeClave);
    }

    [Fact]
    public void ElInventario_CargaLosMismosSecretosComunesConLasMismasReglas()
    {
        var valores = Valida();
        var configuracion = Configuracion(valores);

        var cargada = BabelHub.Inventario.Infrastructure.Plataforma.ConfiguracionDeSeguridad.Cargar(
            configuracion, [BabelHub.Inventario.Application.Plataforma.AlcanceDeServicio.OperacionDeUsuario], []);
        valores["Seguridad:ClaveDeHuella"] = "v1:AAAA";
        var error = Assert.Throws<BabelHub.Inventario.Infrastructure.Plataforma.ConfiguracionDeSecretosInvalidaException>(() =>
            BabelHub.Inventario.Infrastructure.Plataforma.ConfiguracionDeSeguridad.Cargar(Configuracion(valores), [], []));

        Assert.Equal("v1", new BabelHub.Inventario.Infrastructure.Plataforma.CalculadorDeHuellaHmac(cargada.Huella).DeOrigen("203.0.113.7").VersionDeClave);
        Assert.Contains(error.Problemas, p => p.StartsWith("BABELHUB__Seguridad__ClaveDeHuella:", StringComparison.Ordinal));
    }
}
