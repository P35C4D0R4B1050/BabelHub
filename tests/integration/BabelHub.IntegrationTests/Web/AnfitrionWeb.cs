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
using Microsoft.Extensions.DependencyInjection.Extensions;
using BabelHub.IntegrationTests.Pagos;
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
        builder.Services.AgregarRetiroNuloDeCarritos();
        builder.Services.AgregarBibliotecaPendiente();
        var mvc = builder.Services.AddControllersWithViews().AddApplicationPart(AssemblyMarker.Assembly);
        foreach (var parte in partes ?? [])
        {
            mvc.AddApplicationPart(parte);
        }

        servicios?.Invoke(builder.Services);
        // Lo que las pantallas de ingreso, registro y recuperación piden a Identidad, a la verificación de persona y a Carrito: dobles que fallan («no disponible») salvo que la prueba registre los suyos.
        builder.Services.TryAddScoped(sp => DobleDeMetodos.Crear<BabelHub.Ecommerce.Application.Identidad.IIngreso>(out _));
        builder.Services.TryAddScoped(sp => DobleDeMetodos.Crear<BabelHub.Ecommerce.Application.Identidad.IRegistroDeCuentas>(out _));
        builder.Services.TryAddScoped(sp => DobleDeMetodos.Crear<BabelHub.Ecommerce.Application.Identidad.IRecuperacionDeAcceso>(out _));
        builder.Services.TryAddScoped(sp => DobleDeMetodos.Crear<BabelHub.Ecommerce.Application.Carrito.ICarritoDelCliente>(out _));
        builder.Services.TryAddScoped<BabelHub.Ecommerce.Application.Plataforma.IExigenciaDeVerificacionDePersona, ExigenciaNoExigidaDePrueba>();
        builder.Services.TryAddSingleton(new OpcionesDeVerificacionWeb("clave-de-sitio-de-prueba"));
        builder.Services.TryAddScoped(sp => new CierreDelIngresoWeb(
            sp.GetService<BabelHub.Ecommerce.Application.Web.ISesionesWeb>() ?? DobleDeMetodos.Crear<BabelHub.Ecommerce.Application.Web.ISesionesWeb>(out _),
            sp.GetService<BabelHub.Ecommerce.Application.Identidad.ISesiones>() ?? DobleDeMetodos.Crear<BabelHub.Ecommerce.Application.Identidad.ISesiones>(out _),
            sp.GetService<OpcionesDelHostWeb>() ?? new OpcionesDelHostWeb(BabelHub.Ecommerce.Application.Web.HostWeb.Tienda, []),
            sp.GetService<BabelHub.Ecommerce.Application.Plataforma.IEstablecedorDeActor>() ?? DobleDeMetodos.Crear<BabelHub.Ecommerce.Application.Plataforma.IEstablecedorDeActor>(out _),
            sp.GetRequiredService<BabelHub.Ecommerce.Application.Carrito.ICarritoDelCliente>()));


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

/// <summary>La verificación de persona que no exige nada: para las pantallas que la piden en los hosts de prueba que no la prueban.</summary>
internal sealed class ExigenciaNoExigidaDePrueba : BabelHub.Ecommerce.Application.Plataforma.IExigenciaDeVerificacionDePersona
{
    public Task<BabelHub.Ecommerce.Application.Plataforma.ResultadoDeExigencia> Exigir(BabelHub.Ecommerce.Application.Plataforma.AccionProtegida accion, string? tokenDeVerificacion, CancellationToken cancelacion = default) =>
        Task.FromResult(BabelHub.Ecommerce.Application.Plataforma.ResultadoDeExigencia.NoExigida);
}
