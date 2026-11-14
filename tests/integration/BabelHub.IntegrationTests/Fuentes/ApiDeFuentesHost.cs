using System.Net;
using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Fuentes;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Fuentes;

/// <summary>Un registro de auditoría en memoria (el almacén real de PostgreSQL es de AUD-4): puede fallar a pedido, como una base que no responde.</summary>
public sealed class AlmacenDeRegistrosDePrueba : IAlmacenDeRegistros
{
    public List<RegistroDeAuditoria> Registros { get; } = [];

    public volatile bool Falla;

    public Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
    {
        if (Falla)
        {
            return Task.FromException(new InvalidOperationException("La auditoría no responde."));
        }

        lock (Registros)
        {
            Registros.Add(registro);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Las denegaciones que escribió el módulo, fuera de la transacción del caso de uso (así persisten aunque ese caso se revierta).</summary>
public sealed class EscritorDeDenegacionesDePrueba : IEscritorDeDenegaciones
{
    public List<RegistroDeAuditoria> Registros { get; } = [];

    public Task Escribir(RegistroDeAuditoria registro, CancellationToken cancelacion = default)
    {
        lock (Registros)
        {
            Registros.Add(registro);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Las plantillas de ruta de los endpoints (con segmentos <c>{nombre}</c>, sin restricciones), como las entregará la Api al módulo de Auditoría.</summary>
public sealed class PlantillasDeRutaDePrueba(Func<EndpointDataSource> fuente) : IPlantillasDeRuta
{
    public IReadOnlyCollection<string> Todas => [.. fuente().Endpoints.OfType<RouteEndpoint>()
        .Select(e => "/" + System.Text.RegularExpressions.Regex.Replace(e.RoutePattern.RawText ?? string.Empty, @"\{(\w+):[^}]+\}", "{$1}").TrimStart('/'))
        .Distinct()];
}

/// <summary>
/// El servicio del Inventario en proceso (TestServer) con la cadena real de middleware de servicio (credencial, contexto de usuario, actor de integración), los endpoints de <c>Fuentes</c> y PostgreSQL con el
/// rol real de la aplicación. Lo que aún no existe (el verificador de Identidad T41 y el almacén de auditoría de AUD-4) se sustituye: el usuario sale de cabeceras de prueba (D10, solo en pruebas) y la
/// auditoría y las denegaciones van a almacenes en memoria que pueden fallar.
/// </summary>
public sealed class ApiDeFuentesHost : IAsyncDisposable
{
    public const string CredencialDeDisponibilidad = "credencial-de-lectura-de-disponibilidad-0123456789";
    public const string CredencialDeUsuario = "credencial-de-operacion-de-usuario-abcdefghij";
    public const string CredencialDeContacto = "credencial-de-lectura-de-contacto-qwertyuiop12";
    public const string CredencialDeExpiracion = "credencial-de-expiracion-de-reservas-zxcvbnm12";
    public const string CabeceraDeRol = "X-Prueba-Rol";
    public const string CabeceraDeSucursal = "X-Prueba-Sucursal";
    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private WebApplication _app = null!;

    public HttpClient Cliente { get; private set; } = null!;

    public AlmacenDeRegistrosDePrueba Auditoria { get; } = new();

    public EscritorDeDenegacionesDePrueba Denegaciones { get; } = new();

    public AlertasTecnicasEspia Alertas { get; } = new();

    public IServiceProvider Servicios => _app.Services;

    public IEnumerable<RouteEndpoint> Endpoints => _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    /// <param name="conActorDeServicio">Falso para probar qué pasa si la Api olvida establecer el actor de integración (debe fallar a la vista).</param>
    public static async Task<ApiDeFuentesHost> Crear(PostgresDeFuentesFixture bd, bool conActorDeServicio = true)
    {
        var host = new ApiDeFuentesHost();
        await host.Iniciar(bd, conActorDeServicio);
        return host;
    }

    private async Task Iniciar(PostgresDeFuentesFixture bd, bool conActorDeServicio)
    {
        var matriz = new MatrizDeAlcances(
        [
            .. MatrizDeFuentes.Declaraciones(),
            (new OperacionDeServicio("servicio.prueba.mover-stock"), AlcanceDeServicio.ExpiracionDeReservas),
            (new OperacionDeServicio("servicio.prueba.leer-stock"), AlcanceDeServicio.LecturaDeDisponibilidad),
        ]);
        var credenciales = new OpcionesDeCredencialesDeEntrada(
        [
            new(AlcanceDeServicio.LecturaDeDisponibilidad, (CredencialDeDisponibilidad, null)),
            new(AlcanceDeServicio.OperacionDeUsuario, (CredencialDeUsuario, null)),
            new(AlcanceDeServicio.LecturaDeContacto, (CredencialDeContacto, null)),
            new(AlcanceDeServicio.ExpiracionDeReservas, (CredencialDeExpiracion, null)),
            new(AlcanceDeServicio.OrigenReenviado, ("credencial-de-origen-reenviado-zxcvbnmasdf", null)),
        ]);

        var actor = new ActorDelFlujo();
        var local = new MotorDeLimitesLocal();
        var reloj = new RelojDeIntegracion();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV", ErroresDeFuentes.Especificos));
        builder.Services.AgregarLimitesHttp(new OpcionesDeLimitesHttp());
        builder.Services.AgregarCredencialDeServicio(new OpcionesDeSuperficieDeServicio(), credenciales);
        builder.Services.AddSingleton<IMatrizDeAlcancesDeServicio>(matriz);
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<ICalculadorDeHuella>(new CalculadorDeHuellaHmac(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella))));
        builder.Services.AddSingleton<IReloj>(reloj);
        builder.Services.AddSingleton<IPublicadorDeEventosDeSeguridad>(new EventosDeSeguridadNulos());
        builder.Services.AddSingleton<IPublicadorDeAlertasTecnicas>(Alertas);
        builder.Services.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales)));
        builder.Services.AddScoped<IAutorizadorDeLlamadasDeServicio>(p => new AutorizarLlamadaDeServicio(
            matriz, p.GetRequiredService<IActorActual>(), p.GetRequiredService<IOrigenDeSolicitud>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
        builder.Services.AddScoped<IResolutorDeOrigenDeServicio>(p => new ResolverOrigenDeLlamadaDeServicio(
            p.GetRequiredService<ICalculadorDeHuella>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
        builder.Services.AddScoped<IControlDeLimites>(p => new EvaluarLimite(
            p.GetRequiredService<CatalogoDePoliticas>(), local, local, p.GetRequiredService<IOrigenDeSolicitud>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));

        // Auditoría real (catálogo, constructor, actor del contexto) sobre almacenes en memoria que pueden fallar.
        builder.Services.AddScoped(p => new ConstructorDeRegistros(ServicioDeOrigen.Inventario, CatalogoDeAccionesDeAuditoria.Crear(), p.GetRequiredService<IReloj>(), p.GetRequiredService<IActorActual>(), p.GetRequiredService<IOrigenDeSolicitud>()));
        builder.Services.AddScoped<IAuditoria>(p => new AuditoriaDeAplicacion(p.GetRequiredService<ConstructorDeRegistros>(), Auditoria));
        builder.Services.AddScoped<IContextoDeDenegacion, ContextoDeDenegacion>();
        builder.Services.AddSingleton<IPlantillasDeRuta>(_ => new PlantillasDeRutaDePrueba(() => _app.Services.GetRequiredService<EndpointDataSource>()));
        builder.Services.AddSingleton(p => new ResolutorDeRutas(p.GetRequiredService<IPlantillasDeRuta>()));
        builder.Services.AddScoped<IDenegacionDeAcceso>(p => new DenegacionDeAccesoDeAplicacion(
            p.GetRequiredService<ConstructorDeRegistros>(), p.GetRequiredService<IContextoDeDenegacion>(), p.GetRequiredService<ResolutorDeRutas>(), Denegaciones, Alertas, p.GetRequiredService<IReloj>()));

        builder.Services.AddSingleton(bd.Acceso);
        builder.Services.AgregarModuloFuentes();

        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.Use(async (contexto, siguiente) =>
        {
            contexto.Connection.RemoteIpAddress = IPAddress.Parse("10.1.0.7");
            contexto.Request.Scheme = "https";
            await siguiente(contexto);
        });
        _app.UsarCredencialDeServicio();

        // Autenticación de la prueba (D10): el usuario sale de cabeceras solo aquí; en producción lo verifica Identidad (T41) y ninguna cabecera de actor vale.
        _app.Use(async (contexto, siguiente) =>
        {
            var rol = contexto.Request.Headers[CabeceraDeRol].ToString();
            if (rol.Length > 0)
            {
                var sucursal = contexto.Request.Headers[CabeceraDeSucursal].ToString();
                actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), rol, sucursal.Length > 0 ? Guid.Parse(sucursal) : null));
            }

            await siguiente(contexto);
        });
        _app.UsarContextoDeUsuarioDeServicio();
        if (conActorDeServicio)
        {
            _app.UsarActorDeServicio();
        }

        _app.MapearFuentes();

        // Rutas de prueba que mueven y leen stock como lo hará una llamada de servicio (Reservas es de INV-2): usan el actor que dejó el middleware.
        _app.MapGet("/servicio/prueba/stock/{edicionId:guid}", async (Guid edicionId, IExistencias existencias, ILecturaCoherente lectura) =>
            Results.Json((await lectura.EjecutarLectura(t => existencias.Disponibilidad([edicionId], t))).Select(d => d.VendibleDeLaRed)))
            .WithMetadata(new OperacionDeServicioAttribute("servicio.prueba.leer-stock"));
        _app.MapPost("/servicio/prueba/mover-stock/{fuenteId:guid}/{edicionId:guid}", async (Guid fuenteId, Guid edicionId, IMovimientosDeStock movimientos, IUnidadDeTrabajo unidad) =>
        {
            var resultado = await unidad.Ejecutar(async t =>
            {
                await movimientos.Bloquear([edicionId], t);
                return await movimientos.Aplicar(new MovimientoDeStock(fuenteId, edicionId, 0, 1), t);
            });
            return Results.Json(new { aplicado = resultado.Aplicado, reservado = resultado.Posterior?.Reservado });
        }).WithMetadata(new OperacionDeServicioAttribute("servicio.prueba.mover-stock"));

        await _app.StartAsync();
        Cliente = _app.GetTestClient();
    }

    public async ValueTask DisposeAsync()
    {
        Cliente?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    /// <summary>Una solicitud con la credencial y, si se indica, el usuario de prueba (rol y sucursal).</summary>
    public Task<HttpResponseMessage> Enviar(HttpMethod metodo, string ruta, string? credencial, string? rol = null, Guid? sucursal = null, HttpContent? cuerpo = null)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta) { Content = cuerpo };
        if (credencial is not null)
        {
            solicitud.Headers.TryAddWithoutValidation(CredencialDeServicioMiddleware.CabeceraDeCredencial, credencial);
        }

        if (rol is not null)
        {
            solicitud.Headers.TryAddWithoutValidation(CabeceraDeRol, rol);
        }

        if (sucursal is { } id)
        {
            solicitud.Headers.TryAddWithoutValidation(CabeceraDeSucursal, id.ToString());
        }

        return Cliente.SendAsync(solicitud);
    }

    public static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    public static string Raiz => RutasDeServicio.Inventario;
}

/// <summary>Los eventos de seguridad de la plataforma no son lo que se prueba aquí.</summary>
public sealed class EventosDeSeguridadNulos : IPublicadorDeEventosDeSeguridad
{
    public Task Publicar(EventoDeSeguridad evento, CancellationToken cancelacion = default) => Task.CompletedTask;
}

