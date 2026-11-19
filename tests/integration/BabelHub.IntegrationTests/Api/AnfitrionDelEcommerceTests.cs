using System.Security.Cryptography;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// La composición del servicio del E-commerce (COMP-1; SEG-11, D10, PLA-19): con la configuración completa el grafo se construye y valida; <b>si falta una variable obligatoria o es débil, el arranque falla nombrándola</b> sin imprimir ningún
/// valor; en el modo de restauración no corre ningún trabajo de fondo; y en producción un adaptador provisional falla el arranque nombrándolo.
/// </summary>
public sealed class AnfitrionDelEcommerceTests
{
    private static string Secreto() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));

    private static string Cuenta(string usuario)
    {
        var pruebaClave = Secreto();
        return new NpgsqlConnectionStringBuilder { Host = "localhost", Database = "ecommerce", Username = usuario, Password = pruebaClave }.ConnectionString;
    }

    internal static Dictionary<string, string?> ConfiguracionCompleta()
    {
        var valores = new Dictionary<string, string?>
        {
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion] = Cuenta("acceso"),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeAuditoria] = Cuenta("depurador_auditoria"),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeRegistros] = Cuenta("depurador_registros"),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaClaveDelCursor] = Secreto(),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaClaveDeIngresos] = Secreto(),
            [OpcionesDelAnfitrionDelEcommerce.RutaDeLaClaveDeLaCola] = Secreto(),
            [OpcionesDelAnfitrionDelEcommerce.RutaDelInventario] = "https://inventario.interno/",
            ["Seguridad:ClaveDeHuella"] = "v1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Seguridad:Turnstile:Secreto"] = Secreto(),
            ["Seguridad:Turnstile:Hostnames"] = "tienda.example.org",
            ["Seguridad:Turnstile:ClaveDeSitio"] = "0x4AAAAAAAdeSitioDePrueba",
            ["Seguridad:ClaveDeRedisDeLimites"] = Secreto(),
            ["Identidad:ClaveDeCifradoDeClaves"] = "v1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Identidad:Pimienta"] = "v1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Web:Host"] = "tienda",
            ["Web:HostsPermitidos"] = "tienda.example.org",
            ["Web:ClaveDeSesionesTienda"] = Secreto(),
            ["Carrito:CostoDeEnvio"] = "12.00",
        };
        foreach (var raiz in new[] { "Almacen:Catalogo:", "Almacen:Supresiones:" })
        {
            valores[raiz + "Endpoint"] = "https://almacen.example.org/";
            valores[raiz + "Region"] = "sa-lima-1";
            valores[raiz + "ClaveDeAcceso"] = Secreto();
            valores[raiz + "ClaveSecreta"] = Secreto();
        }

        foreach (var alcance in OpcionesDelAnfitrionDelEcommerce.AlcancesRequeridos)
        {
            valores[$"Seguridad:Credenciales:{alcance}"] = Secreto();
        }

        return valores;
    }

    /// <summary>Lo que además exige producción: el Redis de contadores, los proxies conocidos y el servidor de correo.</summary>
    internal static Dictionary<string, string?> ConfiguracionDeProduccion() => new()
    {
        [OpcionesDelAnfitrionDelEcommerce.RutaDelRedis] = "redis-contadores.interno:6379",
        [OpcionesDelAnfitrionDelEcommerce.RutaDeLosProxies] = "10.0.0.0/8",
        ["Ecommerce:Redis:Tls"] = "1",
        ["Correo:Smtp:Servidor"] = "smtp.example.org",
        ["Correo:Smtp:Cuenta"] = "avisos@example.org",
        ["Correo:Smtp:Credencial"] = Secreto(),
        ["Correo:Smtp:Remitente"] = "avisos@example.org",
    };

    private static WebApplicationBuilder Constructor(Dictionary<string, string?> configuracion, string entorno)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = entorno });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(configuracion);
        builder.Services.AddControllersWithViews();
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        return builder;
    }

    private static WebApplication Compuesto(ModoDeArranque modo = ModoDeArranque.Normal, string entorno = "Development")
    {
        var builder = Constructor(ConfiguracionCompleta(), entorno);
        builder.Services.AddSingleton(typeof(ModoDeArranque), modo);
        builder.Services.AddScoped<EjecutorDePasosDeRestauracion>();
        builder.AgregarEcommerce(modo);
        var app = builder.Build();
        app.UsarEcommerce();
        return app;
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public async Task ConLaConfiguracionCompleta_ElGrafoSeValida_YLaSuperficieDeServicioDeclaraTodasSusOperaciones()
    {
        await using var app = Compuesto();

        app.ExigirComposicionDeProduccion();

        Assert.NotNull(app.Services.GetRequiredService<IMatrizDeAlcancesDeServicio>());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task EnElModoDeRestauracion_NoSeRegistraNingunTrabajoDeFondo()
    {
        await using var normal = Compuesto(ModoDeArranque.Normal);
        await using var restauracion = Compuesto(ModoDeArranque.Restauracion);

        static int Trabajos(WebApplication a) => a.Services.GetServices<IHostedService>().Count(h => h.GetType().Name is not ("GenericWebHostService" or "DataProtectionHostedService"));
        var enNormal = Trabajos(normal);
        var enRestauracion = Trabajos(restauracion);

        Assert.True(enNormal >= 5, "El modo normal corre los trabajos de auditoría, Identidad, Catálogo, Carrito y la purga de sesiones.");
        Assert.True(enRestauracion == 0, "Servicios hospedados en restauración: " + string.Join(", ", restauracion.Services.GetServices<IHostedService>().Select(h => h.GetType().Name)));
    }

    public static TheoryData<string> Variables()
    {
        var datos = new TheoryData<string>
        {
            OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion,
            OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeAuditoria,
            OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeRegistros,
            OpcionesDelAnfitrionDelEcommerce.RutaDeLaClaveDelCursor,
            OpcionesDelAnfitrionDelEcommerce.RutaDeLaClaveDeIngresos,
            OpcionesDelAnfitrionDelEcommerce.RutaDeLaClaveDeLaCola,
            OpcionesDelAnfitrionDelEcommerce.RutaDelInventario,
            "Seguridad:ClaveDeHuella",
            "Seguridad:Turnstile:Secreto",
            "Seguridad:ClaveDeRedisDeLimites",
            "Web:ClaveDeSesionesTienda",
            "Almacen:Catalogo:ClaveSecreta",
            "Almacen:Supresiones:Endpoint",
        };
        foreach (var alcance in OpcionesDelAnfitrionDelEcommerce.AlcancesRequeridos)
        {
            datos.Add($"Seguridad:Credenciales:{alcance}");
        }

        return datos;
    }

    [Theory]
    [MemberData(nameof(Variables))]
    [Trait("Criterio", "AC-SEG-25")]
    public void SiFaltaUnaVariableObligatoria_ElArranqueFalla_NombrandolaYSinValores(string ruta)
    {
        var configuracion = ConfiguracionCompleta();
        var valoresDeLasDemas = configuracion.Where(p => p.Key != ruta && p.Value!.Length >= 30).Select(p => p.Value!).ToList();
        configuracion.Remove(ruta);

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: true));

        Assert.Contains(LectorDeSecretos.Variable(ruta), falla.Message, StringComparison.Ordinal);
        Assert.All(valoresDeLasDemas, v => Assert.DoesNotContain(v, falla.Message, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void LaClaveDelCursor_NoPuedeRepetirUnSecretoComun_YLasCuentasDeBaseDebenSerDistintas()
    {
        var configuracion = ConfiguracionCompleta();
        configuracion[OpcionesDelAnfitrionDelEcommerce.RutaDeLaClaveDelCursor] = configuracion["Seguridad:Credenciales:Historial"];
        configuracion[OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeDepuracionDeRegistros] = configuracion[OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion] + ";Application Name=depurador";

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: true));

        Assert.Contains("BABELHUB__Auditoria__ClaveDeCursor", falla.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Ecommerce__Base__DepuracionDeRegistros", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(configuracion["Seguridad:Credenciales:Historial"]!, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void EnProduccion_ConUnAdaptadorProvisional_ElArranqueFalla_NombrandoloYSoloEnElModoNormal()
    {
        var configuracion = ConfiguracionCompleta();
        foreach (var (ruta, valor) in ConfiguracionDeProduccion())
        {
            configuracion[ruta] = valor;
        }

        var normal = Constructor(configuracion, "Production");
        var falla = Assert.Throws<InvalidOperationException>(() => normal.AgregarEcommerce(ModoDeArranque.Normal));
        var restauracion = Constructor(configuracion, "Production");

        Assert.Contains("ICancelacionDeAvisos", falla.Message, StringComparison.Ordinal);
        restauracion.AgregarEcommerce(ModoDeArranque.Restauracion);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void UnSecretoNuevo_NoPuedeRepetirOtro_NiLaClaveDeSesionesNiLaPimientaNiLaCredencialDelCorreo()
    {
        var casos = new (string Ruta, Func<Dictionary<string, string?>, string?> Valor)[]
        {
            ("Web:ClaveDeSesionesTienda", c => c["Seguridad:Credenciales:Historial"]),
            ("Web:ClaveDeSesionesTienda", c => c["Seguridad:ClaveDeHuella"]![(c["Seguridad:ClaveDeHuella"]!.IndexOf(':') + 1)..]),
            ("Identidad:Pimienta", c => c["Auditoria:ClaveDeCursor"]),
            ("Correo:Smtp:Credencial", c => c["Catalogo:ClaveDeIngresos"]),
            ("Almacen:Supresiones:ClaveSecreta", c => c["Almacen:Catalogo:ClaveSecreta"]),
            (OpcionesDelAnfitrionDelEcommerce.RutaDeLaClaveDeLaCola, c => c["Web:ClaveDeSesionesTienda"]),
        };

        foreach (var (ruta, valor) in casos)
        {
            var configuracion = ConfiguracionCompleta();
            configuracion["Correo:Smtp:Servidor"] = "smtp.example.org";
            configuracion["Correo:Smtp:Cuenta"] = "avisos@example.org";
            configuracion["Correo:Smtp:Remitente"] = "avisos@example.org";
            configuracion["Correo:Smtp:Credencial"] = Secreto();
            var repetido = valor(configuracion)!;
            configuracion[ruta] = ruta == "Identidad:Pimienta" ? "v1:" + repetido : repetido;

            var excepcion = Record.Exception(() => OpcionesDelAnfitrionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: false));
            Assert.True(excepcion is ConfiguracionDeSecretosInvalidaException, "Sin fallo para " + ruta + ": " + excepcion?.GetType().Name + " " + excepcion?.Message);
            var falla = (ConfiguracionDeSecretosInvalidaException)excepcion!;

            Assert.Contains(LectorDeSecretos.Variable(ruta), falla.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(repetido, falla.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-22")]
    public async Task EnElEnsayo_ElCorreoEsNuloAunqueHayaSmtp_YEnElModoNormalNo()
    {
        var configuracion = ConfiguracionCompleta();
        foreach (var (ruta, valor) in ConfiguracionDeProduccion())
        {
            configuracion[ruta] = valor;
        }

        static WebApplication Con(Dictionary<string, string?> c, ModoDeArranque modo)
        {
            var builder = Constructor(c, "Development");
            builder.Services.AddSingleton(typeof(ModoDeArranque), modo);
            builder.AgregarEcommerce(modo);
            return builder.Build();
        }

        await using var ensayo = Con(configuracion, ModoDeArranque.Ensayo);
        await using var normal = Con(configuracion, ModoDeArranque.Normal);

        var deEnsayo = ensayo.Services.GetServices<IAdaptadorSaliente>().Where(a => a.EsHaciaProveedor).ToList();
        var deNormal = normal.Services.GetServices<IAdaptadorSaliente>().Where(a => a.EsHaciaProveedor).ToList();
        Assert.All(deEnsayo, a => Assert.True(a.EsNulo, a.Nombre));
        Assert.Contains(deNormal, a => a.Nombre == "correo" && !a.EsNulo);
        ArranqueSeguro.ExigirCoherencia(ModoDeArranque.Ensayo, esProduccion: false, deEnsayo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void EnProduccion_ElRedisExigeTls_YLasCuentasDeBaseSinContrasenaNoArrancan()
    {
        var configuracion = ConfiguracionCompleta();
        foreach (var (ruta, valor) in ConfiguracionDeProduccion())
        {
            configuracion[ruta] = valor;
        }

        configuracion.Remove("Ecommerce:Redis:Tls");
        configuracion[OpcionesDelAnfitrionDelEcommerce.RutaDeLaBaseDeLaAplicacion] = new NpgsqlConnectionStringBuilder { Host = "localhost", Database = "ecommerce", Username = "acceso" }.ConnectionString;

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: true));

        Assert.Contains("BABELHUB__Ecommerce__Redis__Tls", falla.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Ecommerce__Base__Aplicacion", falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FueraDeProduccion_ElRedisYElSmtpSonOpcionales_PeroEnProduccionNo()
    {
        var configuracion = ConfiguracionCompleta();

        var enDesarrollo = OpcionesDelAnfitrionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: false);
        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: true));

        Assert.NotNull(enDesarrollo);
        Assert.Contains("BABELHUB__Ecommerce__Redis__Direccion", falla.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Correo__Smtp__Servidor", falla.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Ecommerce__ProxiesConocidos", falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void UnServicioConLaCuentaPropietariaEnSuEntorno_NoArranca_NombrandoLaVariableSinElValor()
    {
        var configuracion = ConfiguracionCompleta();
        var cuenta = Cuenta("postgres");
        configuracion["Migrador:Ecommerce:Propietaria"] = cuenta;

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: false));

        Assert.Contains("BABELHUB__Migrador", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cuenta, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LasOpciones_NoImprimenNingunValor()
    {
        var opciones = OpcionesDelAnfitrionDelEcommerce.Leer(new ConfigurationBuilder().AddInMemoryCollection(ConfiguracionCompleta()).Build(), esProduccion: false);

        Assert.Equal("[configuración del anfitrión del E-commerce]", opciones.ToString());
    }
}
