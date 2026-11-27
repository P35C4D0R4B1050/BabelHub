using System.Net;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web.Acceso;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Api;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// El enlace «Acceso del personal» (web-acceso ACC-10 a ACC-12): sale solo de la configuración, es opcional, mal escrito impide arrancar nombrando la variable (nunca su valor), y se muestra igual a cualquiera en el pie de la
/// tienda y en su ingreso, nunca en el host de gestión.
/// </summary>
public sealed class AccesoDelPersonalTests
{
    private const string Variable = "BABELHUB__Web__DireccionDeGestion";

    private const string Direccion = "https://gestion.example.org";

    private static IConfiguration Configuracion(string? valor)
    {
        var valores = AnfitrionDelEcommerceTests.ConfiguracionCompleta();
        if (valor is not null)
        {
            valores[DireccionDelAccesoDelPersonal.Ruta] = valor;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(valores).Build();
    }

    private static DireccionDelAccesoDelPersonal Valida(string valor) =>
        DireccionDelAccesoDelPersonal.Leer(Configuracion(valor), new LectorDeSecretos(Configuracion(valor)), permitirHttpLocal: false)!;

    // ---------- La configuración ----------

    [Theory]
    [Trait("Criterio", "AC-ACC-10")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SinDireccionConfigurada_ElServicioArrancaEnCualquierEntorno_YNoHayEnlace(string? valor)
    {
        foreach (var permitirHttpLocal in new[] { true, false })
        {
            var lector = new LectorDeSecretos(Configuracion(valor));

            Assert.Null(DireccionDelAccesoDelPersonal.Leer(Configuracion(valor), lector, permitirHttpLocal));
            Assert.Empty(lector.Problemas);
        }

        // La composición entera, también con la configuración endurecida de producción y de un entorno cualquiera.
        _ = OpcionesDelAnfitrionDelEcommerce.Leer(Configuracion(valor), esProduccion: false, permitirContactoDeEjemplo: false);
    }

    [Theory]
    [Trait("Criterio", "AC-ACC-11")]
    [InlineData("gestion.example.org")]
    [InlineData("/ingreso")]
    [InlineData("//gestion.example.org")]
    [InlineData("http://gestion.example.org")]
    [InlineData("ftp://gestion.example.org")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    [InlineData("https://ana:clave@gestion.example.org")]
    [InlineData("https://@gestion.example.org")]
    [InlineData("https://gestion.example.org/ingreso")]
    [InlineData("https://gestion.example.org//")]
    [InlineData("https://gestion.example.org/./")]
    [InlineData("https://gestion.example.org/x/..")]
    [InlineData("https://gestion.example.org?a=1")]
    [InlineData("https://gestion.example.org?")]
    [InlineData("https://gestion.example.org#parte")]
    [InlineData("https://gestion.example.org/ ")]
    [InlineData(" https://gestion.example.org")]
    [InlineData("https://gestion example.org")]
    [InlineData("https:\\\\gestion.example.org")]
    [InlineData("https://gestion.example.org/\n")]
    public void UnaDireccionMalFormada_ImpideArrancar_NombrandoLaVariableSinSuValor(string valor)
    {
        foreach (var permitirContactoDeEjemplo in new[] { true, false })
        {
            var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelEcommerce.Leer(Configuracion(valor), esProduccion: false, permitirContactoDeEjemplo));

            Assert.Contains(Variable, falla.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("example.org", falla.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("clave@", falla.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [Trait("Criterio", "AC-ACC-11")]
    [InlineData("https://gestion.example.org", "https://gestion.example.org/")]
    [InlineData("https://gestion.example.org/", "https://gestion.example.org/")]
    [InlineData("https://gestion.example.org:8443", "https://gestion.example.org:8443/")]
    [InlineData("HTTPS://Gestion.Example.org", "https://gestion.example.org/")]
    public void UnOrigenSeguro_SeAdmiteEnTodosLosEntornos_YElEnlaceEsSuOrigen(string valor, string enlace)
    {
        Assert.Equal(enlace, Valida(valor).Enlace);
        _ = OpcionesDelAnfitrionDelEcommerce.Leer(Configuracion(valor), esProduccion: false, permitirContactoDeEjemplo: false);
    }

    [Theory]
    [Trait("Criterio", "AC-ACC-11")]
    [InlineData("http://localhost:8081")]
    [InlineData("http://127.0.0.1")]
    [InlineData("http://[::1]:5000/")]
    public void SinConexionSegura_SoloHaciaLaPropiaMaquina_YSoloEnDesarrolloYPruebaAutomatizada(string valor)
    {
        // Development y Test: el mismo indicador que el contacto de ejemplo (COM-05).
        _ = OpcionesDelAnfitrionDelEcommerce.Leer(Configuracion(valor), esProduccion: false, permitirContactoDeEjemplo: true);

        // Cualquier otro entorno, aunque no sea de tipo producción.
        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelEcommerce.Leer(Configuracion(valor), esProduccion: false, permitirContactoDeEjemplo: false));
        Assert.Contains(Variable, falla.Message, StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Criterio", "AC-ACC-11")]
    [InlineData("Development", true)]
    [InlineData("Test", true)]
    [InlineData("ensayo", false)]
    public void LaComposicionDecideElEntorno_UnoAjenoADesarrolloYPruebaRechazaHttpLocal(string entorno, bool arranca)
    {
        var valores = AnfitrionDelEcommerceTests.ConfiguracionCompleta();
        valores[DireccionDelAccesoDelPersonal.Ruta] = "http://localhost:8081";
        var constructor = AnfitrionDelEcommerceTests.Constructor(valores, entorno);
        constructor.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Ensayo);

        var falla = Record.Exception(() => constructor.AgregarEcommerce(ModoDeArranque.Ensayo));

        if (arranca)
        {
            Assert.Null(falla);
            Assert.Contains(constructor.Services, s => s.ServiceType == typeof(DireccionDelAccesoDelPersonal));
        }
        else
        {
            Assert.Contains(Variable, Assert.IsType<ConfiguracionDeSecretosInvalidaException>(falla).Message, StringComparison.Ordinal);
        }
    }

    // ---------- La pantalla ----------

    private static Task<AccesoApiHost> Anfitrion(HostWeb host, bool conDireccion = true) =>
        AccesoApiHost.Crear(host, conDireccion ? servicios => servicios.AddSingleton(Valida(Direccion)) : null);

    private static List<string> Enlaces(string html) =>
        [.. Regex.Matches(html, "<a[^>]*href=\"(?<href>[^\"]*)\"[^>]*data-bh-acceso-del-personal[^>]*>Acceso del personal</a>", RegexOptions.None, TimeSpan.FromSeconds(2)).Select(m => m.Groups["href"].Value)];

    [Fact]
    [Trait("Criterio", "AC-ACC-09")]
    public async Task EnLaTienda_ElMismoEnlaceEstaEnElPieYEnElIngreso_TambienTrasUnIngresoFallido()
    {
        await using var host = await Anfitrion(HostWeb.Tienda);
        host.Ingreso.Resultado = Resultado<ResultadoDelIngreso>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoAutenticado, "credenciales-invalidas"));

        using var ingreso = await host.Cliente.SendAsync(host.Pedir("/ingreso"));
        using var fallido = await host.Enviar("/ingreso", [new("correo", "personal@babelhub.example"), new("contrasena", "una frase que no es")]);
        using var otroFallido = await host.Enviar("/ingreso", [new("correo", "nadie@correo.example"), new("contrasena", "una frase que no es")]);
        using var registro = await host.Cliente.SendAsync(host.Pedir("/registro"));

        var esperado = Direccion + "/";
        // En el ingreso, dos veces (la pantalla y el pie); en cualquier otra página de la tienda, en el pie.
        Assert.Equal([esperado, esperado], Enlaces(await ingreso.Content.ReadAsStringAsync()));
        Assert.Equal(HttpStatusCode.Unauthorized, fallido.StatusCode);
        Assert.Equal([esperado, esperado], Enlaces(await fallido.Content.ReadAsStringAsync()));
        Assert.Equal([esperado, esperado], Enlaces(await otroFallido.Content.ReadAsStringAsync()));
        Assert.Equal([esperado], Enlaces(await registro.Content.ReadAsStringAsync()));
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-09")]
    public async Task EnElHostDeGestion_ElEnlaceNoApareceEnNingunaParte()
    {
        await using var host = await Anfitrion(HostWeb.Gestion);

        using var ingreso = await host.Cliente.SendAsync(host.Pedir("/ingreso"));

        var html = await ingreso.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        Assert.DoesNotContain("Acceso del personal", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("gestion.example.org", html, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-ACC-10")]
    public async Task SinDireccion_LaTiendaYSuIngresoNoMuestranElEnlace()
    {
        await using var host = await Anfitrion(HostWeb.Tienda, conDireccion: false);

        using var ingreso = await host.Cliente.SendAsync(host.Pedir("/ingreso"));

        var html = await ingreso.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, ingreso.StatusCode);
        Assert.DoesNotContain("Acceso del personal", WebUtility.HtmlDecode(html), StringComparison.Ordinal);
        Assert.DoesNotContain("data-bh-acceso-del-personal", html, StringComparison.Ordinal);
    }
}
