using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// OAS-1 (E-commerce): el documento OpenAPI 3 se genera desde el servicio compuesto de verdad (sin base de datos: solo el grafo y los endpoints), describe <b>toda</b> ruta pública con su seguridad, su alcance, su cuerpo y sus errores
/// <c>application/problem+json</c>, y <b>falla si una ruta pública no está documentada</b>. El archivo publicado en <c>docs/03-diseno-de-software/openapi/</c> debe coincidir con el generado.
/// </summary>
public sealed class DocumentacionOpenApiEcommerceTests
{
    private const string Publicado = "docs/03-diseno-de-software/openapi/ecommerce.v1.json";

    /// <summary>Operaciones del contrato v1 que el E-commerce publica y aún no se sirven: ninguna (los estados de traslado de Pedidos y la auditoría ya se mapean en <c>UsarEcommerce</c>); si se añade una operación antes que su endpoint, entra aquí.</summary>
    private static readonly HashSet<string> PendientesDeImplementar = [];

    private static async Task<WebApplication> Compuesto(Action<WebApplication>? antes = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.WebHost.UseTestServer();
        builder.Services.AddControllersWithViews();
        builder.Services.AddSingleton(typeof(ModoDeArranque), ModoDeArranque.Ensayo);
        builder.Services.AddScoped<EjecutorDePasosDeRestauracion>();
        builder.Configuration.AddInMemoryCollection(AnfitrionDelEcommerceTests.ConfiguracionCompleta());
        builder.AgregarEcommerce(ModoDeArranque.Ensayo);
        var app = builder.Build();
        app.UsarEcommerce();
        antes?.Invoke(app);
        await app.StartAsync();
        return app;
    }

    private static async Task<OpenApiDocument> Documento(WebApplication app) =>
        await app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>(DocumentacionOpenApi.Documento).GetOpenApiDocumentAsync(CancellationToken.None);

    /// <summary>Las rutas HTTP del servicio que debe documentar: todas menos el propio documento.</summary>
    private static IEnumerable<(string Metodo, string Ruta)> RutasPublicas(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is null)
            .Select(e => (Ruta: DocumentacionOpenApi.Normalizar(e.RoutePattern.RawText ?? string.Empty), Metodos: e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? []))
            .Where(r => r.Ruta != DocumentacionOpenApi.RutaDelDocumento)
            .SelectMany(r => r.Metodos.Select(m => (m, r.Ruta)));

    /// <summary>Lo que falta o está mal en el documento respecto de las rutas mapeadas; vacío si todo está documentado.</summary>
    private static List<string> Problemas(WebApplication app, OpenApiDocument documento)
    {
        var problemas = new List<string>();
        foreach (var (metodo, ruta) in RutasPublicas(app))
        {
            var clave = HttpMethod.Parse(metodo);
            if (documento.Paths is null || !documento.Paths.TryGetValue(ruta, out var item) || item.Operations is null || !item.Operations.TryGetValue(clave, out var operacion))
            {
                problemas.Add($"{metodo} {ruta}: no está en el documento");
                continue;
            }

            if (string.IsNullOrWhiteSpace(operacion.Summary))
            {
                problemas.Add($"{metodo} {ruta}: no está en el catálogo de contratos ni en las rutas complementarias (sin descripción)");
            }

            if (operacion.Responses is null || operacion.Responses.Count == 0)
            {
                problemas.Add($"{metodo} {ruta}: sin respuestas");
            }

            if (operacion.Security is null)
            {
                problemas.Add($"{metodo} {ruta}: sin declaración de seguridad (ni credencial ni pública explícita)");
            }
        }

        return problemas;
    }

    [Fact]
    public async Task TodaRutaPublica_EstaDocumentada_ConSeguridadRespuestasYDescripcion()
    {
        await using var app = await Compuesto();
        var documento = await Documento(app);

        var problemas = Problemas(app, documento);
        Assert.True(problemas.Count == 0, string.Join(Environment.NewLine, problemas));
        Assert.True(RutasPublicas(app).Count() > 5, "El E-commerce publica sus rutas de contrato v1 y complementarias.");
    }

    [Fact]
    public async Task UnaRutaPublicaNoDocumentada_HaceFallarLaVerificacion()
    {
        // Mapea una ruta que ni está en el catálogo de contratos ni en las complementarias, y otra excluida de la descripción a propósito.
        await using var app = await Compuesto(a =>
        {
            a.MapGet(RutasDeServicio.Ecommerce + "/sin-contrato", () => Results.Ok());
            a.MapGet(RutasDeServicio.Ecommerce + "/oculta", () => Results.Ok()).ExcludeFromDescription();
        });

        var problemas = Problemas(app, await Documento(app));

        Assert.Contains(problemas, p => p.StartsWith("GET /api/ecommerce/v1/sin-contrato", StringComparison.Ordinal));
        Assert.Contains(problemas, p => p.StartsWith("GET /api/ecommerce/v1/oculta: no está en el documento", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CadaOperacionDelContratoV1_ApareceConSuAlcanceSuCuerpoYSusErroresProblemJson()
    {
        await using var app = await Compuesto();
        var documento = await Documento(app);
        var delInventario = CatalogoDeOperaciones.V1.Where(o => o.Ruta.StartsWith(RutasDeServicio.Ecommerce, StringComparison.Ordinal) && !PendientesDeImplementar.Contains(o.Nombre)).ToList();
        Assert.NotEmpty(delInventario);

        foreach (var contrato in delInventario)
        {
            var operacion = documento.Paths[DocumentacionOpenApi.Normalizar(contrato.Ruta)].Operations![HttpMethod.Parse(contrato.Metodo)];
            Assert.Equal(contrato.Nombre, operacion.OperationId);
            Assert.True(operacion.Extensions!.ContainsKey("x-alcance"), contrato.Nombre);
            Assert.Equal(contrato.Solicitud is not null, operacion.RequestBody is not null);
            Assert.Contains(((int)contrato.CodigoDeExito).ToString(System.Globalization.CultureInfo.InvariantCulture), operacion.Responses!.Keys);
            Assert.Contains(operacion.Responses.Values, r => r.Content is not null && r.Content.ContainsKey(DocumentacionOpenApi.TipoDeProblema));
            Assert.Equal(contrato.RequiereClaveDeIdempotencia, operacion.Parameters!.Any(p => p.Name == CabecerasDeServicio.ClaveDeIdempotencia));
            var requisito = Assert.Single(operacion.Security!);
            Assert.Equal(contrato.RequiereTokenDeUsuario, requisito.Keys.Any(k => k.Reference.Id == DocumentacionOpenApi.EsquemaTokenDeUsuario));
            Assert.Equal(!contrato.RequiereTokenDeUsuario, requisito.Keys.Any(k => k.Reference.Id == DocumentacionOpenApi.EsquemaCredencialDeServicio));
        }

        Assert.Contains(DocumentacionOpenApi.EsquemaCredencialDeServicio, documento.Components!.SecuritySchemes!.Keys);
        Assert.Contains(DocumentacionOpenApi.EsquemaTokenDeUsuario, documento.Components.SecuritySchemes.Keys);
    }

    [Fact]
    public async Task ElDocumentoPublicado_CoincideConElGenerado()
    {
        await using var app = await Compuesto();
        var generado = await (await Documento(app)).SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_0, CancellationToken.None);
        var ruta = Path.Combine(RaizDelRepositorio(), Publicado.Replace('/', Path.DirectorySeparatorChar));

        if (Environment.GetEnvironmentVariable("BABELHUB_ACTUALIZAR_OPENAPI") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ruta)!);
            await File.WriteAllTextAsync(ruta, generado + "\n");
        }

        Assert.True(File.Exists(ruta), $"Falta {Publicado}: se genera con BABELHUB_ACTUALIZAR_OPENAPI=1.");
        Assert.Equal(Normalizar(await File.ReadAllTextAsync(ruta)), Normalizar(generado));
    }

    private static string Normalizar(string texto) => texto.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string RaizDelRepositorio()
    {
        var carpeta = new DirectoryInfo(AppContext.BaseDirectory);
        while (carpeta is not null && !File.Exists(Path.Combine(carpeta.FullName, "BabelHub.sln")))
        {
            carpeta = carpeta.Parent;
        }

        return carpeta?.FullName ?? throw new InvalidOperationException("No se encontró la raíz del repositorio.");
    }
}
