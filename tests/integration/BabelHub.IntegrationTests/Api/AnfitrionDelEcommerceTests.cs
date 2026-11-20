using System.Security.Cryptography;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
            ["Pagos:Pasarela"] = "simulador",
            ["Pagos:ClaveDeIntentos"] = Secreto(),
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
        // Notificaciones: en producción el envío es n8n (el simulado no vale), con su webhook interno, la clave del sentido saliente, la clave de la huella de los números y la del sentido entrante (Integraciones).
        ["Notificaciones:Envio"] = "n8n",
        ["Notificaciones:N8n:Webhook"] = "http://n8n:5678/webhook/babelhub-avisos",
        ["Notificaciones:N8n:ClaveSaliente"] = Secreto(),
        ["Notificaciones:ClaveDeHuellaDeNumero"] = "1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        ["Integraciones:n8n:Secreto"] = Secreto(),
        // Biblioteca: el almacén de libros (credencial de solo lectura) es obligatorio en producción.
        ["Almacen:Biblioteca:Endpoint"] = "https://almacen.example.org/",
        ["Almacen:Biblioteca:Region"] = "sa-lima-1",
        ["Almacen:Biblioteca:ClaveDeAcceso"] = Secreto(),
        ["Almacen:Biblioteca:ClaveSecreta"] = Secreto(),
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


    private static HashSet<string> Rutas(WebApplication app) =>
        [.. ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>().Select(e => e.RoutePattern.RawText!)];

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public async Task UsarEcommerce_MapeaLaAuditoriaYLasClavesDeIdentidad_YRotarSoloEnElHostDeGestion()
    {
        var configuracion = ConfiguracionCompleta();
        configuracion["Web:Host"] = "gestion";
        configuracion["Web:ClaveDeSesionesGestion"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(36));
        var constructor = Constructor(configuracion, "Development");
        constructor.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Normal);
        constructor.Services.AddScoped<EjecutorDePasosDeRestauracion>();
        constructor.AgregarEcommerce(ModoDeArranque.Normal);
        var gestion = constructor.Build();
        gestion.UsarEcommerce();
        await using var tienda = Compuesto(ModoDeArranque.Normal);
        await using var _ = gestion;

        var enTienda = Rutas(tienda);
        var enGestion = Rutas(gestion);

        foreach (var ruta in new[] { BabelHub.Shared.Contratos.Ecommerce.RutasDeClavesDeFirma.Jwks, BabelHub.Shared.Contratos.Ecommerce.RutasDeClavesDeFirma.Revocaciones, BabelHub.Shared.Contratos.RutasDeServicio.Ecommerce + "/auditoria", BabelHub.Shared.Contratos.RutasDeServicio.Ecommerce + "/alertas", BabelHub.Shared.Contratos.RutasDeServicio.Ecommerce + "/historial" })
        {
            Assert.Contains(ruta, enTienda);
            Assert.Contains(ruta, enGestion);
        }

        Assert.DoesNotContain(BabelHub.Shared.Contratos.Ecommerce.RutasDeClavesDeFirma.Rotar, enTienda);
        Assert.DoesNotContain(BabelHub.Shared.Contratos.Ecommerce.RutasDeClavesDeFirma.Comprometida, enTienda);
        Assert.Contains(BabelHub.Shared.Contratos.Ecommerce.RutasDeClavesDeFirma.Rotar, enGestion);
        Assert.Contains(BabelHub.Shared.Contratos.Ecommerce.RutasDeClavesDeFirma.Comprometida, enGestion);
    }
    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task LaSalud_CompruebaLaBaseDeDatosSoloEnModoNormal()
    {
        await using var normal = Compuesto(ModoDeArranque.Normal);
        await using var restauracion = Compuesto(ModoDeArranque.Restauracion);

        var enNormal = normal.Services.GetServices<BabelHub.Ecommerce.Api.Plataforma.IComprobacionDeSalud>().Select(c => c.Nombre);
        var enRestauracion = restauracion.Services.GetServices<BabelHub.Ecommerce.Api.Plataforma.IComprobacionDeSalud>();

        Assert.Equal(["base-de-datos"], enNormal);
        Assert.Empty(enRestauracion);
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
            "Pagos:Pasarela",
            "Pagos:ClaveDeIntentos",
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
        // Notificaciones ya es real (no hay doble provisional que la guarda deba nombrar): lo primero que rechaza producción es el simulador de Pagos, que sigue esperando el adaptador de la pasarela.
        var falla = Assert.Throws<BabelHub.Ecommerce.Infrastructure.Pagos.ConfiguracionDePagosInvalidaException>(() => normal.AgregarEcommerce(ModoDeArranque.Normal));
        var restauracion = Constructor(configuracion, "Production");

        Assert.Contains("simulador", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ICancelacionDeAvisos", falla.Message, StringComparison.Ordinal);
        restauracion.AgregarEcommerce(ModoDeArranque.Restauracion);
    }

    [Fact]
    [Trait("Criterio", "AC-BIB-12")]
    public void EnProduccion_SinElAlmacenDeLibros_ElArranqueNombraLasVariablesYNuncaUnValor()
    {
        var configuracion = ConfiguracionCompleta();
        foreach (var (ruta, valor) in ConfiguracionDeProduccion().Where(p => !p.Key.StartsWith("Almacen:Biblioteca:", StringComparison.Ordinal)))
        {
            configuracion[ruta] = valor;
        }

        var constructor = Constructor(configuracion, "Production");
        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => constructor.AgregarEcommerce(ModoDeArranque.Normal));

        Assert.Contains("BABELHUB__Almacen__Biblioteca__ClaveSecreta", falla.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Almacen__Biblioteca__Endpoint", falla.Message, StringComparison.Ordinal);
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
            ("Pagos:ClaveDeIntentos", c => c["Seguridad:Credenciales:Historial"]),
            ("Pagos:ClaveDeIntentosAnterior", c => c["Seguridad:Credenciales:LecturaDeContacto"]),
            ("Pagos:LlavePrivada", c => c["Auditoria:ClaveDeCursor"]),
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

    private static WebApplication Con(Dictionary<string, string?> c, ModoDeArranque modo, string entorno = "Development")
    {
        var builder = Constructor(c, entorno);
        builder.Services.AddSingleton(typeof(ModoDeArranque), modo);
        builder.AgregarEcommerce(modo);
        return builder.Build();
    }

    [Fact]
    public async Task Pagos_EnElEnsayoLaPasarelaYElVerificadorDePersonaSonNulos_YEnElModoNormalElSimuladorYTurnstile()
    {
        var configuracion = ConfiguracionCompleta();

        await using var ensayo = Con(configuracion, ModoDeArranque.Ensayo);
        await using var normal = Con(configuracion, ModoDeArranque.Normal);

        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Pagos.NuloPasarelaDePago>(ensayo.Services.GetRequiredService<BabelHub.Ecommerce.Application.Pagos.Interno.IPasarelaDePago>());
        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Pagos.SimuladorPasarelaDePago>(normal.Services.GetRequiredService<BabelHub.Ecommerce.Application.Pagos.Interno.IPasarelaDePago>());
        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Composicion.VerificadorDePersonaNulo>(ensayo.Services.GetRequiredService<IVerificadorDePersona>());
        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Plataforma.VerificadorTurnstile>(normal.Services.GetRequiredService<IVerificadorDePersona>());
        var salientesDelEnsayo = ensayo.Services.GetServices<IAdaptadorSaliente>().Where(a => a.EsHaciaProveedor).ToList();
        Assert.All(salientesDelEnsayo, a => Assert.True(a.EsNulo, a.Nombre));
        Assert.Contains(salientesDelEnsayo, a => a.Nombre == "pasarela-de-pago");
        Assert.Contains(salientesDelEnsayo, a => a.Nombre == "operador-logistico");
        Assert.Contains(normal.Services.GetServices<IAdaptadorSaliente>(), a => a.Nombre == "pasarela-de-pago" && !a.EsNulo);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-16")]
    public async Task ElHostCompuesto_EximeLaRutaDeLosAvisosDelTopePrevioYDelLimiteSinSesion_SoloEsaRuta()
    {
        await using var app = Compuesto();

        var opciones = app.Services.GetRequiredService<BabelHub.Ecommerce.Api.Plataforma.OpcionesDeLimitesHttp>();

        Assert.True(opciones.EsExenta(BabelHub.Ecommerce.Api.Pagos.AvisosDeLaPasarelaEnLaApi.Ruta));
        Assert.True(opciones.EsExenta(BabelHub.Ecommerce.Api.Pagos.AvisosDeLaPasarelaEnLaApi.Ruta + "/"));
        Assert.True(opciones.EsExenta("/health/live"));
        Assert.False(opciones.EsExenta("/api/ecommerce/v1/integraciones"));
        Assert.False(opciones.EsExenta("/checkout/pago"));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void EnProduccion_LasGuardasDeLosDoblesCorrenAlFinal_ElPrimerRechazoEsElDePagosYNoElDeCarrito()
    {
        var configuracion = ConfiguracionCompleta();
        foreach (var (ruta, valor) in ConfiguracionDeProduccion())
        {
            configuracion[ruta] = valor;
        }

        var builder = Constructor(configuracion, "Production");

        // Con Notificaciones ya real (aquí, dobles de otra clase), lo primero que rechaza producción es el simulador de Pagos: la guarda de Carrito ya no se adelanta a ver como doble lo que Pagos reemplaza después.
        builder.Services.AddScoped<BabelHub.Ecommerce.Application.Identidad.ICancelacionDeAvisos, OtraCancelacionDeAvisos>();
        builder.Services.AddScoped<BabelHub.Ecommerce.Application.Notificaciones.INotificador, OtroNotificador>();
        var falla = Assert.Throws<BabelHub.Ecommerce.Infrastructure.Pagos.ConfiguracionDePagosInvalidaException>(() => builder.AgregarEcommerce(ModoDeArranque.Normal));

        Assert.Contains("simulador", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ICreadorDeIntencionesDePago", falla.Message, StringComparison.Ordinal);
    }

    private sealed class OtraCancelacionDeAvisos : BabelHub.Ecommerce.Application.Identidad.ICancelacionDeAvisos
    {
        public Task CancelarLosAvisosDe(Guid usuarioId, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class OtroNotificador : BabelHub.Ecommerce.Application.Notificaciones.INotificador
    {
        public Task<BabelHub.Ecommerce.Application.Notificaciones.ResultadoDeLaSolicitudDeAviso> Solicitar(BabelHub.Ecommerce.Application.Notificaciones.SolicitudDeAviso solicitud, CancellationToken cancelacion = default) =>
            Task.FromResult(BabelHub.Ecommerce.Application.Notificaciones.ResultadoDeLaSolicitudDeAviso.Creado);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public async Task ElClienteDeTurnstile_VaAlDestinoFijoDeCloudflareConSuPlazo_YLaEntradaDeAvisosNoSeMapea()
    {
        await using var app = Compuesto();

        using var cliente = app.Services.GetRequiredService<IHttpClientFactory>().CreateClient("turnstile");

        Assert.Equal(BabelHub.Ecommerce.Infrastructure.Plataforma.VerificadorTurnstile.UrlDeVerificacion, cliente.BaseAddress!.ToString());
        Assert.Equal(TimeSpan.FromSeconds(10), cliente.Timeout);
        var rutas = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>();
        Assert.DoesNotContain(rutas, e => (e.RoutePattern.RawText ?? string.Empty).Contains("integraciones/pasarela", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("culqi")]
    [InlineData("otra-pasarela")]
    [Trait("Criterio", "AC-PAG-10")]
    public void UnaPasarelaSinAdaptadorODesconocida_FallaElArranque_NombrandoLaVariable_SinLasClaves(string pasarela)
    {
        var configuracion = ConfiguracionCompleta();
        configuracion["Pagos:Pasarela"] = pasarela;
        configuracion["Pagos:LlavePrivada"] = "sk_test_" + Secreto();

        var falla = Assert.ThrowsAny<Exception>(() => Con(configuracion, ModoDeArranque.Normal));

        Assert.Contains("Pagos:Pasarela", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(configuracion["Pagos:ClaveDeIntentos"]!, falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(configuracion["Pagos:LlavePrivada"]!, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ElOperadorLogisticoEsElNuloPorDefecto_ElSimuladorSoloEnDesarrollo_YUnoDesconocidoFalla()
    {
        var configuracion = ConfiguracionCompleta();
        await using var porDefecto = Con(configuracion, ModoDeArranque.Normal);
        configuracion["Envios:Operador"] = "simulador";
        await using var simulado = Con(configuracion, ModoDeArranque.Normal);
        await using var enEnsayo = Con(configuracion, ModoDeArranque.Ensayo);
        configuracion["Envios:Operador"] = "olva";

        var falla = Assert.ThrowsAny<Exception>(() => Con(configuracion, ModoDeArranque.Normal));

        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Envios.NuloOperadorLogistico>(porDefecto.Services.GetRequiredService<BabelHub.Ecommerce.Application.Envios.Interno.IOperadorLogistico>());
        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Envios.SimuladorOperadorLogistico>(simulado.Services.GetRequiredService<BabelHub.Ecommerce.Application.Envios.Interno.IOperadorLogistico>());
        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Envios.NuloOperadorLogistico>(enEnsayo.Services.GetRequiredService<BabelHub.Ecommerce.Application.Envios.Interno.IOperadorLogistico>());
        Assert.Contains("Envios:Operador", falla.Message, StringComparison.Ordinal);
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

    // ---------------------------------------------------------------- Notificaciones (T14, T15; NOT-11, NOT-12)

    private static Dictionary<string, string?> ConN8n(Dictionary<string, string?> c)
    {
        c["Notificaciones:Envio"] = "n8n";
        c["Notificaciones:N8n:Webhook"] = "http://n8n:5678/webhook/babelhub-avisos";
        c["Notificaciones:N8n:ClaveSaliente"] = Secreto();
        c["Integraciones:n8n:Secreto"] = Secreto();
        return c;
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-10")]
    public async Task Notificaciones_EnDesarrolloElEnvioEsElSimulado_ConN8nElReal_YEnElEnsayoYAlRestaurarElNuloSinTrabajos()
    {
        await using var simulado = Con(ConfiguracionCompleta(), ModoDeArranque.Normal);
        await using var n8n = Con(ConN8n(ConfiguracionCompleta()), ModoDeArranque.Normal);
        await using var ensayo = Con(ConN8n(ConfiguracionCompleta()), ModoDeArranque.Ensayo);
        await using var restauracion = Con(ConN8n(ConfiguracionCompleta()), ModoDeArranque.Restauracion);

        static T Resolver<T>(WebApplication a)
            where T : notnull
        {
            using var alcance = a.Services.CreateScope();
            return alcance.ServiceProvider.GetRequiredService<T>();
        }

        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Notificaciones.SimuladoEnvioDeWhatsApp>(Resolver<BabelHub.Ecommerce.Application.Notificaciones.Interno.IEnvioDeWhatsApp>(simulado));
        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Notificaciones.N8nEnvioDeWhatsApp>(Resolver<BabelHub.Ecommerce.Application.Notificaciones.Interno.IEnvioDeWhatsApp>(n8n));
        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Notificaciones.NuloEnvioDeWhatsApp>(Resolver<BabelHub.Ecommerce.Application.Notificaciones.Interno.IEnvioDeWhatsApp>(ensayo));
        Assert.IsType<BabelHub.Ecommerce.Infrastructure.Notificaciones.NuloEnvioDeWhatsApp>(Resolver<BabelHub.Ecommerce.Application.Notificaciones.Interno.IEnvioDeWhatsApp>(restauracion));

        // Los dobles provisionales ya no existen: el contrato público es el real.
        Assert.IsType<BabelHub.Ecommerce.Application.Notificaciones.Interno.SolicitarAviso>(Resolver<BabelHub.Ecommerce.Application.Notificaciones.INotificador>(n8n));
        Assert.IsType<BabelHub.Ecommerce.Application.Notificaciones.Interno.CancelarAvisosPendientes>(Resolver<BabelHub.Ecommerce.Application.Identidad.ICancelacionDeAvisos>(n8n));

        // El trabajo de fondo corre solo en el modo normal (PLA-19): los avisos restaurados traen números reales.
        static bool TieneElTrabajo(WebApplication a) => a.Services.GetServices<IHostedService>().Any(h => h is BabelHub.Ecommerce.Infrastructure.Notificaciones.ServicioDeTrabajosDeNotificaciones);
        Assert.True(TieneElTrabajo(n8n));
        Assert.False(TieneElTrabajo(ensayo));
        Assert.False(TieneElTrabajo(restauracion));

        // El adaptador declara su estado real: ExigirCoherencia lo comprueba en el ensayo y al restaurar.
        static bool NotificadorNulo(WebApplication a) => a.Services.GetServices<IAdaptadorSaliente>().Single(x => x.Nombre == "notificador").EsNulo;
        Assert.False(NotificadorNulo(n8n));
        Assert.True(NotificadorNulo(ensayo));
        Assert.True(NotificadorNulo(restauracion));

        // La firma de los estados de entrega de n8n entra a la lista blanca de entradas de integración.
        Assert.Contains("n8n", n8n.Services.GetRequiredService<BabelHub.Ecommerce.Application.Plataforma.Interno.OpcionesDeEntradasDeIntegracion>().ProveedoresPermitidos);
        Assert.DoesNotContain("n8n", simulado.Services.GetRequiredService<BabelHub.Ecommerce.Application.Plataforma.Interno.OpcionesDeEntradasDeIntegracion>().ProveedoresPermitidos);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-01")]
    public async Task LaSolicitudDeAvisoDelInventario_SinLaCredencialDeAlcanceOConUnaAjena_Es401Uniforme_ConLaCorrectaLlegaALaValidacion()
    {
        var configuracion = ConfiguracionCompleta();
        var builder = Constructor(configuracion, "Development");
        builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Ensayo);
        builder.AgregarEcommerce(ModoDeArranque.Ensayo);
        await using var app = builder.Build();
        app.UsarEcommerce();
        await app.StartAsync();
        using var cliente = app.GetTestClient();
        cliente.BaseAddress = new Uri("https://localhost");

        async Task<System.Net.HttpStatusCode> Llamar(string? credencial)
        {
            using var peticion = new HttpRequestMessage(HttpMethod.Post, BabelHub.Ecommerce.Api.Notificaciones.EndpointsDeNotificaciones.RutaDeLasSolicitudesDelInventario) { Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json") };
            if (credencial is not null)
            {
                peticion.Headers.TryAddWithoutValidation(CredencialDeServicioMiddleware.CabeceraDeCredencial, credencial);
            }

            using var respuesta = await cliente.SendAsync(peticion);
            return respuesta.StatusCode;
        }

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, await Llamar(null));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, await Llamar("una-credencial-que-no-existe-0123456789abcdef"));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, await Llamar(configuracion["Seguridad:Credenciales:LecturaDeDisponibilidad"]));
        // Con la credencial de alcance «solicitar-aviso» pasa la autenticación y llega a la validación del cuerpo vacío.
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, await Llamar(configuracion["Seguridad:Credenciales:SolicitarAviso"]));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-11")]
    public async Task LaEntradaDeEstadosDeN8n_SoloExisteSiElEnvioEsPorN8n()
    {
        await using var simulado = Con(ConfiguracionCompleta(), ModoDeArranque.Normal);
        await using var n8n = Con(ConN8n(ConfiguracionCompleta()), ModoDeArranque.Normal);

        static bool Mapea(WebApplication a)
        {
            a.UsarEcommerce();
            return ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)a).DataSources.SelectMany(d => d.Endpoints).OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
                .Any(e => e.RoutePattern.RawText == BabelHub.Ecommerce.Api.Notificaciones.EstadosDeEntregaDeN8nEnLaApi.Ruta);
        }

        Assert.False(Mapea(simulado));
        Assert.True(Mapea(n8n));
        n8n.ExigirComposicionDeProduccion();
    }

    private static string Falla(Dictionary<string, string?> c, bool esProduccion = true, bool soloEnModoNormal = true) =>
        Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => BabelHub.Ecommerce.Infrastructure.Composicion.OpcionesDeNotificaciones.Leer(new ConfigurationBuilder().AddInMemoryCollection(c).Build(), esProduccion, soloEnModoNormal)).Message;

    private static Dictionary<string, string?> NotificacionesDeProduccion() => new()
    {
        ["Notificaciones:Envio"] = "n8n",
        ["Notificaciones:N8n:Webhook"] = "http://n8n:5678/webhook/babelhub-avisos",
        ["Notificaciones:N8n:ClaveSaliente"] = Secreto(),
        ["Notificaciones:ClaveDeHuellaDeNumero"] = "1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
    };

    private static BabelHub.Ecommerce.Infrastructure.Composicion.ModoDelEnvioDeAvisos ModoDelEnvioDeAvisosDe(Dictionary<string, string?> c, bool soloEnModoNormal = true) =>
        BabelHub.Ecommerce.Infrastructure.Composicion.OpcionesDeNotificaciones.Leer(new ConfigurationBuilder().AddInMemoryCollection(c).Build(), esProduccion: true, soloEnModoNormal).Envio;

    [Fact]
    [Trait("Criterio", "AC-NOT-10")]
    public void Notificaciones_EnProduccion_ElEnvioSimuladoOSinElegirFalla_NombrandoLaVariable()
    {
        var simulado = NotificacionesDeProduccion();
        simulado["Notificaciones:Envio"] = "simulado";
        var sinElegir = NotificacionesDeProduccion();
        sinElegir.Remove("Notificaciones:Envio");
        var desconocido = NotificacionesDeProduccion();
        desconocido["Notificaciones:Envio"] = "whatsapp-directo";

        Assert.Contains("BABELHUB__Notificaciones__Envio: el envío simulado no vale en producción", Falla(simulado), StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Notificaciones__Envio: falta", Falla(sinElegir), StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Notificaciones__Envio: no es un valor conocido", Falla(desconocido), StringComparison.Ordinal);
        Assert.Equal(BabelHub.Ecommerce.Infrastructure.Composicion.ModoDelEnvioDeAvisos.N8n, ModoDelEnvioDeAvisosDe(NotificacionesDeProduccion()));
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-12")]
    public void Notificaciones_ConN8n_ExigeElWebhookLaClaveSalienteYLaClaveDeLaHuella_SinImprimirSusValores()
    {
        var clave = Secreto();
        var sinWebhook = NotificacionesDeProduccion();
        sinWebhook.Remove("Notificaciones:N8n:Webhook");
        var webhookExterno = NotificacionesDeProduccion();
        webhookExterno["Notificaciones:N8n:Webhook"] = "http://n8n.example.org/webhook/x";
        var claveDebil = NotificacionesDeProduccion();
        claveDebil["Notificaciones:N8n:ClaveSaliente"] = "corta";
        var sinHuella = NotificacionesDeProduccion();
        sinHuella.Remove("Notificaciones:ClaveDeHuellaDeNumero");
        var huellaMalFormada = NotificacionesDeProduccion();
        huellaMalFormada["Notificaciones:ClaveDeHuellaDeNumero"] = clave;
        var anteriorIgualVersion = NotificacionesDeProduccion();
        anteriorIgualVersion["Notificaciones:ClaveDeHuellaDeNumeroAnterior"] = "1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        Assert.Contains("BABELHUB__Notificaciones__N8n__Webhook: falta", Falla(sinWebhook), StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Notificaciones__N8n__Webhook", Falla(webhookExterno), StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Notificaciones__N8n__ClaveSaliente: es débil", Falla(claveDebil), StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Notificaciones__ClaveDeHuellaDeNumero: falta", Falla(sinHuella), StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Notificaciones__ClaveDeHuellaDeNumero: debe tener la forma", Falla(huellaMalFormada), StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Notificaciones__ClaveDeHuellaDeNumeroAnterior", Falla(anteriorIgualVersion), StringComparison.Ordinal);
        Assert.DoesNotContain(clave, Falla(huellaMalFormada), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-NOT-10")]
    public void Notificaciones_EnElEnsayoYAlRestaurar_ElEnvioEsSiempreElNulo_AunqueLaConfiguracionDigaN8n()
    {
        var configuracion = NotificacionesDeProduccion();

        Assert.Equal(BabelHub.Ecommerce.Infrastructure.Composicion.ModoDelEnvioDeAvisos.Nulo, ModoDelEnvioDeAvisosDe(configuracion, soloEnModoNormal: false));
        Assert.Equal(["n8n"], BabelHub.Ecommerce.Infrastructure.Composicion.OpcionesDeNotificaciones.ProveedoresDeFirma(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), soloEnModoNormal: true));
        Assert.Empty(BabelHub.Ecommerce.Infrastructure.Composicion.OpcionesDeNotificaciones.ProveedoresDeFirma(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), soloEnModoNormal: false));
    }
}
