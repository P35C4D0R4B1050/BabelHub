using BabelHub.Ecommerce.Api;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Infrastructure.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Catalogo;
using BabelHub.Ecommerce.Infrastructure.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Web;

/// <summary>
/// Un anfitrión de pruebas de la interfaz web: MVC con las vistas compiladas de la Api del E-commerce, sobre un servidor de pruebas y sin
/// red. Cada prueba le agrega los dobles de lo que necesita (módulos, Inventario) y, si hace falta, un middleware propio.
/// </summary>
internal sealed class AnfitrionWeb : IAsyncDisposable
{
    private readonly WebApplication _app;

    private AnfitrionWeb(WebApplication app, HttpClient cliente)
    {
        _app = app;
        Cliente = cliente;
    }

    public HttpClient Cliente { get; }

    public IServiceProvider Servicios => _app.Services;

    public static async Task<AnfitrionWeb> Iniciar(Action<IServiceCollection>? servicios = null, Action<WebApplication>? antesDeLasRutas = null, System.Reflection.Assembly[]? partes = null, Action<WebApplication>? despuesDelEnrutado = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = RaizDelRepositorio.Api, EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AgregarInterfazWeb();
        builder.Services.AgregarModuloWeb(permitirVersionesProvisionales: true);
        builder.Services.AgregarCatalogoPendiente();
        builder.Services.AgregarBibliotecaPendiente();
        var mvc = builder.Services.AddControllersWithViews().AddApplicationPart(AssemblyMarker.Assembly);
        foreach (var parte in partes ?? [])
        {
            mvc.AddApplicationPart(parte);
        }

        servicios?.Invoke(builder.Services);

        var app = builder.Build();
        antesDeLasRutas?.Invoke(app);
        app.UseRouting();
        despuesDelEnrutado?.Invoke(app);
        app.UseAuthorization();
        app.MapControllers();
        await app.StartAsync();
        // Detrás del proxy la aplicación ve HTTPS (ForwardedHeaders con proxies conocidos): las cookies __Host- y el antiforgery exigen Secure y fallan cerrado si no.
        var cliente = app.GetTestClient();
        cliente.BaseAddress = new Uri("https://localhost/");
        return new AnfitrionWeb(app, cliente);
    }

    public async ValueTask DisposeAsync()
    {
        Cliente.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
