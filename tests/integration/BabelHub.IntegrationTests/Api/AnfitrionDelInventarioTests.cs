using System.Security.Cryptography;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using BabelHub.Inventario.Application.Auditoria;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// La composición del servicio del Inventario (COMP-1; SEG-11, D10, PLA-19): con la configuración completa el grafo se construye y valida (alcances y dependencias), cada contrato se resuelve y ninguna operación declarada queda
/// fuera de la matriz de alcances; <b>si falta una variable obligatoria o es débil, el arranque falla nombrándola</b> sin imprimir ningún valor, y en el modo de restauración no corre ningún trabajo de fondo.
/// </summary>
public sealed class AnfitrionDelInventarioTests
{
    private static string Secreto() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(36));

    private static string Cuenta(string usuario)
    {
        var pruebaClave = Secreto();
        return new NpgsqlConnectionStringBuilder { Host = "localhost", Database = "inventario", Username = usuario, Password = pruebaClave }.ConnectionString;
    }

    internal static Dictionary<string, string?> ConfiguracionCompleta()
    {
        var valores = new Dictionary<string, string?>
        {
            [OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeLaAplicacion] = Cuenta("acceso"),
            [OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeDepuracionDeAjustes] = Cuenta("depurador_ajustes"),
            [OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeDepuracionDeAuditoria] = Cuenta("depurador_auditoria"),
            [OpcionesDelAnfitrionDelInventario.RutaDeLaClaveDelCursor] = Secreto(),
            [OpcionesDelAnfitrionDelInventario.RutaDeLaClaveDeVerificacionDeIdentidad] = "http://localhost:8083/.well-known/jwks.json",
            [OpcionesDelAnfitrionDelInventario.RutaDeLasRevocacionesDeIdentidad] = "http://localhost:8083/.well-known/revocaciones",
            [OpcionesDelAnfitrionDelInventario.RutaDelEcommerce] = "https://ecommerce.interno/",
            ["Seguridad:ClaveDeHuella"] = "v1:" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["Inventario:ClaveDeBajas"] = Secreto(),
        };
        foreach (var alcance in OpcionesDelAnfitrionDelInventario.AlcancesRequeridos)
        {
            valores[$"Seguridad:Credenciales:{alcance}"] = Secreto();
        }

        return valores;
    }

    private static WebApplicationBuilder Constructor(Dictionary<string, string?> configuracion, string entorno = "Production")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = entorno });
        builder.Logging.ClearProviders();
        builder.Configuration.AddInMemoryCollection(configuracion);
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        return builder;
    }

    private static WebApplication Compuesto(ModoDeArranque modo = ModoDeArranque.Normal)
    {
        var builder = Constructor(ConfiguracionCompleta());
        builder.AgregarInventario(modo);
        var app = builder.Build();
        app.UsarInventario();
        return app;
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public async Task ConLaConfiguracionCompleta_ElGrafoSeValida_YLaSuperficieDeServicioDeclaraTodasSusOperaciones()
    {
        await using var app = Compuesto();

        app.ExigirComposicionDeProduccion(esProduccion: true);

        Assert.NotEmpty(((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>());
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public async Task CadaContratoDelInventario_SeResuelve_ConSuImplementacionReal()
    {
        await using var app = Compuesto();
        using var alcance = app.Services.CreateScope();
        var s = alcance.ServiceProvider;

        Assert.IsType<VerificadorDeCredencialesDelInventario>(s.GetRequiredService<IVerificadorDeCredenciales>());
        Assert.NotNull(s.GetRequiredService<IUnidadDeTrabajo>());
        Assert.NotNull(s.GetRequiredService<IControlDeLimites>());
        Assert.NotNull(s.GetRequiredService<IAlertas>());
        Assert.NotNull(s.GetRequiredService<IRegistroDeTrabajosDeFondo>());
        Assert.NotNull(s.GetRequiredService<IAutorizadorDeLlamadasDeServicio>());
        Assert.NotNull(s.GetRequiredService<IMatrizDeAlcancesDeServicio>());
    }

    [Fact]
    [Trait("Criterio", "AC-PLA-19")]
    public async Task EnElModoDeRestauracion_NoSeRegistraNingunTrabajoDeFondo()
    {
        await using var normal = Compuesto(ModoDeArranque.Normal);
        await using var restauracion = Compuesto(ModoDeArranque.Restauracion);

        static int Trabajos(WebApplication a) => a.Services.GetServices<IHostedService>().Count(h => h.GetType().Name != "GenericWebHostService");
        var enNormal = Trabajos(normal);
        var enRestauracion = Trabajos(restauracion);

        Assert.True(enNormal >= 5, "El modo normal corre los trabajos de auditoría, reservas, ajustes, traslados y la renovación de claves.");
        Assert.True(enRestauracion == 0, "Servicios hospedados en restauración: " + string.Join(", ", restauracion.Services.GetServices<IHostedService>().Select(h => h.GetType().Name)));
    }

    public static TheoryData<string> Variables()
    {
        var datos = new TheoryData<string>
        {
            OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeLaAplicacion,
            OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeDepuracionDeAjustes,
            OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeDepuracionDeAuditoria,
            OpcionesDelAnfitrionDelInventario.RutaDeLaClaveDelCursor,
            OpcionesDelAnfitrionDelInventario.RutaDeLaClaveDeVerificacionDeIdentidad,
            OpcionesDelAnfitrionDelInventario.RutaDeLasRevocacionesDeIdentidad,
            OpcionesDelAnfitrionDelInventario.RutaDelEcommerce,
            "Seguridad:ClaveDeHuella",
            "Inventario:ClaveDeBajas",
        };
        foreach (var alcance in OpcionesDelAnfitrionDelInventario.AlcancesRequeridos)
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
        var valoresDeLasDemas = configuracion.Where(p => p.Key != ruta).Select(p => p.Value!).ToList();
        configuracion.Remove(ruta);

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => Constructor(configuracion).AgregarInventario(ModoDeArranque.Normal));

        Assert.Contains(LectorDeSecretos.Variable(ruta), falla.Message, StringComparison.Ordinal);
        Assert.All(valoresDeLasDemas, v => Assert.DoesNotContain(v, falla.Message, StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void UnaCredencialDebilOUnaClaveRepetida_HaceFallarElArranque_SinImprimirElValor()
    {
        var configuracion = ConfiguracionCompleta();
        configuracion["Seguridad:Credenciales:BajaDeTitular"] = "corta";
        configuracion["Auditoria:ClaveDeCursor"] = configuracion["Inventario:ClaveDeBajas"];

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => Constructor(configuracion).AgregarInventario(ModoDeArranque.Normal));

        Assert.Contains("BABELHUB__Seguridad__Credenciales__BajaDeTitular", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("corta", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(configuracion["Inventario:ClaveDeBajas"]!, falla.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://identidad.interna/.well-known/jwks.json")]
    [InlineData("ftp://localhost/jwks")]
    [InlineData("no es una dirección")]
    public void LaDireccionDeLaClaveDeVerificacion_DebeSerHttpsOLoopback(string direccion)
    {
        var configuracion = ConfiguracionCompleta();
        configuracion[OpcionesDelAnfitrionDelInventario.RutaDeLaClaveDeVerificacionDeIdentidad] = direccion;

        Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => Constructor(configuracion).AgregarInventario(ModoDeArranque.Normal));
    }

    [Fact]
    public void LasCuentasDeLaAplicacionYDeDepuracion_DebenSerDistintas()
    {
        var configuracion = ConfiguracionCompleta();
        configuracion[OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeDepuracionDeAjustes] = configuracion[OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeLaAplicacion];

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => Constructor(configuracion).AgregarInventario(ModoDeArranque.Normal));

        Assert.Contains("BABELHUB__Inventario__Base__Aplicacion", falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void LaMismaCuentaConOtroNombreDeAplicacion_NoLasSepara()
    {
        var configuracion = ConfiguracionCompleta();
        configuracion[OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeDepuracionDeAuditoria] = configuracion[OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeLaAplicacion] + ";Application Name=depurador";

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => Constructor(configuracion).AgregarInventario(ModoDeArranque.Normal));

        Assert.Contains("BABELHUB__Inventario__Base__DepuracionDeAuditoria", falla.Message, StringComparison.Ordinal);
        Assert.Contains("BABELHUB__Inventario__Base__Aplicacion", falla.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("sin-usuario")]
    [InlineData("sin-contrasena")]
    [Trait("Criterio", "AC-SEG-25")]
    public void UnaCuentaSinUsuarioOSinContrasena_NoArrancaEnProduccion_PeroSiFueraDeProduccion(string caso)
    {
        var configuracion = ConfiguracionCompleta();
        var pruebaSecreto = Secreto();
        configuracion[OpcionesDelAnfitrionDelInventario.RutaDeLaBaseDeLaAplicacion] = caso == "sin-usuario"
            ? new NpgsqlConnectionStringBuilder { Host = "localhost", Database = "inventario", Password = pruebaSecreto }.ConnectionString
            : new NpgsqlConnectionStringBuilder { Host = "localhost", Database = "inventario", Username = "acceso" }.ConnectionString;

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelInventario.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: true));
        _ = OpcionesDelAnfitrionDelInventario.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: false);

        Assert.Contains("BABELHUB__Inventario__Base__Aplicacion", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(pruebaSecreto, falla.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Seguridad:Credenciales:BajaDeTitular")]
    [InlineData("Seguridad:ClaveDeHuella")]
    [Trait("Criterio", "AC-SEG-25")]
    public void LaClaveDelCursor_NoPuedeRepetirUnSecretoComun_NiLaParteSecretaDeLaHuella(string ruta)
    {
        var configuracion = ConfiguracionCompleta();
        var valor = configuracion[ruta]!;
        configuracion[OpcionesDelAnfitrionDelInventario.RutaDeLaClaveDelCursor] = ruta == "Seguridad:ClaveDeHuella" ? valor[(valor.IndexOf(':') + 1)..] : valor;

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => Constructor(configuracion).AgregarInventario(ModoDeArranque.Normal));

        Assert.Contains("BABELHUB__Auditoria__ClaveDeCursor", falla.Message, StringComparison.Ordinal);
        Assert.Contains(LectorDeSecretos.Variable(ruta), falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(valor, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void LaClaveDeBajas_NoPuedeRepetirUnaCredencialDeServicio()
    {
        var configuracion = ConfiguracionCompleta();
        configuracion["Inventario:ClaveDeBajas"] = configuracion["Seguridad:Credenciales:Historial"];

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => Constructor(configuracion).AgregarInventario(ModoDeArranque.Normal));

        Assert.Contains("BABELHUB__Inventario__ClaveDeBajas", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(configuracion["Inventario:ClaveDeBajas"]!, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-SEG-25")]
    public void UnServicioConLaCuentaPropietariaEnSuEntorno_NoArranca_NombrandoLaVariableSinElValor()
    {
        var configuracion = ConfiguracionCompleta();
        var cuenta = Cuenta("postgres");
        configuracion["Migrador:Inventario:Propietaria"] = cuenta;

        var falla = Assert.Throws<ConfiguracionDeSecretosInvalidaException>(() => OpcionesDelAnfitrionDelInventario.Leer(new ConfigurationBuilder().AddInMemoryCollection(configuracion).Build(), esProduccion: true));

        Assert.Contains("BABELHUB__Migrador", falla.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(cuenta, falla.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LasOpciones_NoImprimenNingunValor()
    {
        var opciones = OpcionesDelAnfitrionDelInventario.Leer(new ConfigurationBuilder().AddInMemoryCollection(ConfiguracionCompleta()).Build(), esProduccion: true);

        Assert.Equal("[configuración del anfitrión del Inventario]", opciones.ToString());
    }
}
