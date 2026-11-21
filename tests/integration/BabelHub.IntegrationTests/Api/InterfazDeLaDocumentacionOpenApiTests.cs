using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Inventario.Api.Plataforma;
using DocumentacionDelEcommerce = BabelHub.Ecommerce.Api.Plataforma.DocumentacionOpenApi;
using DocumentacionDelInventario = BabelHub.Inventario.Api.Plataforma.DocumentacionOpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// La interfaz interactiva de la documentación OpenAPI (Scalar) de cada servicio: existe en <c>/documentacion/v1</c> solo en <c>Development</c> (en producción ni ella ni el documento se exponen), carga sus recursos desde el propio
/// servicio (funciona sin internet: ninguna etiqueta de la página apunta a un CDN) y lee el mismo documento <c>/openapi/v1.json</c> que ya se sirve.
/// </summary>
public sealed class InterfazDeLaDocumentacionOpenApiTests
{
    private static async Task<WebApplication> EcommerceCompuesto(string entorno)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = entorno });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton(typeof(BabelHub.Ecommerce.Api.Plataforma.ModoDeArranque), BabelHub.Ecommerce.Api.Plataforma.ModoDeArranque.Ensayo);
        builder.Services.AddScoped<BabelHub.Ecommerce.Application.Plataforma.EjecutorDePasosDeRestauracion>();
        builder.Configuration.AddInMemoryCollection(AnfitrionDelEcommerceTests.ConfiguracionCompleta());
        builder.AgregarEcommerce(BabelHub.Ecommerce.Api.Plataforma.ModoDeArranque.Ensayo);
        var app = builder.Build();
        app.UsarEcommerce();
        await app.StartAsync();
        return app;
    }

    private static async Task<WebApplication> InventarioCompuesto(string entorno)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = entorno });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(AnfitrionDelInventarioTests.ConfiguracionCompleta());
        builder.AgregarInventario(BabelHub.Inventario.Api.Plataforma.ModoDeArranque.Ensayo);
        var app = builder.Build();
        app.UsarInventario();
        await app.StartAsync();
        return app;
    }

    public static TheoryData<string> Servicios() => ["ecommerce", "inventario"];

    private static Task<WebApplication> Compuesto(string servicio, string entorno) =>
        servicio == "ecommerce" ? EcommerceCompuesto(entorno) : InventarioCompuesto(entorno);

    [Theory]
    [MemberData(nameof(Servicios))]
    public async Task EnDevelopment_LaInterfazResponde_ApuntaAlDocumentoYCargaSusRecursosDelPropioServicio(string servicio)
    {
        await using var app = await Compuesto(servicio, Environments.Development);
        var cliente = app.GetTestClient();

        using var pagina = await cliente.GetAsync("/documentacion/v1");
        var html = await pagina.Content.ReadAsStringAsync();
        using var documento = await cliente.GetAsync("/openapi/v1.json");

        Assert.Equal(System.Net.HttpStatusCode.OK, pagina.StatusCode);
        Assert.Contains("text/html", pagina.Content.Headers.ContentType?.ToString(), StringComparison.Ordinal);
        // El adaptador de Scalar resuelve esta URL relativa contra el origen del propio servicio, sin salir a internet.
        Assert.Contains("\"url\":\"openapi/v1.json\"", html, StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.OK, documento.StatusCode);

        // Sin internet: ningún script ni hoja de estilo apunta a otro origen, y el script que la página pide lo sirve el propio servicio.
        Assert.DoesNotMatch("""(?i)<(script|link)[^>]+(src|href)\s*=\s*["']https?://""", html);
        var script = System.Text.RegularExpressions.Regex.Match(html, """<script[^>]+src\s*=\s*["']([^"']+)["']""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        Assert.True(script.Success, "La página debe cargar un script propio.");
        using var recurso = await cliente.GetAsync(new Uri(new Uri("http://localhost/documentacion/v1"), script.Groups[1].Value));
        Assert.Equal(System.Net.HttpStatusCode.OK, recurso.StatusCode);
    }

    /// <summary>La puerta de cada servicio (<c>MapearDocumentacionOpenApi</c>, la misma que usa <c>UsarEcommerce</c> y <c>UsarInventario</c>) sobre un anfitrión mínimo en el entorno dado: la composición completa de producción exige todos sus secretos y aquí solo importa el entorno.</summary>
    private static async Task<WebApplication> SoloLaPuerta(string servicio, string entorno)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = entorno });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        if (servicio == "ecommerce")
        {
            DocumentacionDelEcommerce.AgregarDocumentacionOpenApi(builder.Services);
        }
        else
        {
            DocumentacionDelInventario.AgregarDocumentacionOpenApi(builder.Services);
        }

        var app = builder.Build();
        _ = servicio == "ecommerce" ? DocumentacionDelEcommerce.MapearDocumentacionOpenApi(app) : DocumentacionDelInventario.MapearDocumentacionOpenApi(app);
        await app.StartAsync();
        return app;
    }

    [Theory]
    [MemberData(nameof(Servicios))]
    public async Task EnProduction_NiLaInterfazNiElDocumentoSeExponen_YEnDevelopmentLaMismaPuertaSiLosExpone(string servicio)
    {
        await using var produccion = await SoloLaPuerta(servicio, Environments.Production);
        await using var desarrollo = await SoloLaPuerta(servicio, Environments.Development);
        var enProduccion = produccion.GetTestClient();

        using var pagina = await enProduccion.GetAsync("/documentacion/v1");
        using var recursos = await enProduccion.GetAsync("/documentacion/scalar.js");
        using var documento = await enProduccion.GetAsync("/openapi/v1.json");
        using var paginaEnDesarrollo = await desarrollo.GetTestClient().GetAsync("/documentacion/v1");

        Assert.Equal(System.Net.HttpStatusCode.NotFound, pagina.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, recursos.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, documento.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, paginaEnDesarrollo.StatusCode);

        // Quien escribe solo «/documentacion» llega a la interfaz siguiendo las redirecciones del propio servicio.
        var cliente = desarrollo.GetTestClient();
        var destino = new Uri("http://localhost/documentacion");
        HttpResponseMessage respuesta;
        var saltos = 0;
        while (true)
        {
            respuesta = await cliente.GetAsync(destino);
            if (respuesta.Headers.Location is null || saltos++ >= 4)
            {
                break;
            }

            destino = new Uri(destino, respuesta.Headers.Location);
        }

        Assert.Equal(System.Net.HttpStatusCode.OK, respuesta.StatusCode);
        Assert.StartsWith("/documentacion", destino.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public void LaInterfaz_UsaLaMismaRutaEnLosDosServicios_YReconoceSusRecursos()
    {
        Assert.Equal(DocumentacionDelEcommerce.RutaDeLaInterfaz, DocumentacionDelInventario.RutaDeLaInterfaz);
        Assert.True(DocumentacionDelEcommerce.EsDeLaInterfaz("/documentacion/scalar.js"));
        Assert.True(DocumentacionDelInventario.EsDeLaInterfaz("/documentacion"));
        Assert.False(DocumentacionDelEcommerce.EsDeLaInterfaz("/documentacion-falsa"));
        Assert.False(DocumentacionDelInventario.EsDeLaInterfaz("/api/inventario/v1/fuentes"));
    }
}
