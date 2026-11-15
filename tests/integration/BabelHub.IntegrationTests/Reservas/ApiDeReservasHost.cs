using System.Net;
using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Api.Reservas;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Application.Reservas;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Composicion;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.IntegrationTests.Fuentes;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BabelHub.IntegrationTests.Reservas;

/// <summary>
/// El servicio del Inventario en proceso (TestServer) con la cadena real de middleware de servicio (credencial por alcance, contexto de usuario, actor de integración), los endpoints de <c>Fuentes</c> y de <c>Reservas</c>,
/// el motor de límites de PostgreSQL y el rol real de la aplicación: lo que se prueba es la superficie HTTP de las reservas (T26, T27). Lo que aún no existe (el verificador de Identidad y el almacén de auditoría)
/// se sustituye: el usuario sale de cabeceras de prueba (solo aquí) y la auditoría y las denegaciones van a almacenes en memoria que pueden fallar.
/// </summary>
public sealed class ApiDeReservasHost : IAsyncDisposable
{
    public const string CredencialDeDisponibilidad = "credencial-de-lectura-de-disponibilidad-0123456789";
    public const string CredencialDeUsuario = "credencial-de-operacion-de-usuario-abcdefghij";
    public const string CredencialDeContacto = "credencial-de-lectura-de-contacto-qwertyuiop12";
    public const string CredencialDeExpiracion = "credencial-de-expiracion-de-reservas-zxcvbnm12";
    public const string CredencialDePendientes = "credencial-de-consulta-de-pendientes-asdfghjkl1";
    public const string CredencialDeBaja = "credencial-de-baja-de-titular-poiuytrewq98";
    public const string CabeceraDeRol = "X-Prueba-Rol";
    public const string CabeceraDeSucursal = "X-Prueba-Sucursal";
    public const string CabeceraDeUsuario = "X-Prueba-Usuario";
    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private WebApplication _app = null!;

    public HttpClient Cliente { get; private set; } = null!;

    public AlmacenDeRegistrosDePrueba Auditoria { get; } = new();

    public EscritorDeDenegacionesDePrueba Denegaciones { get; } = new();

    public AlertasTecnicasEspia Alertas { get; } = new();

    public AlertasDeIntegracionEspia AlertasDeAuditoria { get; } = new();

    public RelojDeIntegracion Reloj { get; } = new();

    public IServiceProvider Servicios => _app.Services;

    public IEnumerable<RouteEndpoint> Endpoints => _app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

    public static async Task<ApiDeReservasHost> Crear(PostgresDeReservasFixture bd)
    {
        var host = new ApiDeReservasHost();
        await host.Iniciar(bd);
        return host;
    }

    private sealed class ReceptorSinEfecto : IAlLiberarPedido
    {
        public Task AlLiberar(Guid pedidoId, MotivoDeLiberacionDeReserva motivo, IReadOnlyCollection<Guid> trasladoIds, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private async Task Iniciar(PostgresDeReservasFixture bd)
    {
        var matriz = new MatrizDeAlcances([.. MatrizDeFuentes.Declaraciones(), .. MatrizDeReservas.Declaraciones()]);
        var credenciales = new OpcionesDeCredencialesDeEntrada(
        [
            new(AlcanceDeServicio.LecturaDeDisponibilidad, (CredencialDeDisponibilidad, null)),
            new(AlcanceDeServicio.OperacionDeUsuario, (CredencialDeUsuario, null)),
            new(AlcanceDeServicio.LecturaDeContacto, (CredencialDeContacto, null)),
            new(AlcanceDeServicio.ExpiracionDeReservas, (CredencialDeExpiracion, null)),
            new(AlcanceDeServicio.ConsultaDePendientes, (CredencialDePendientes, null)),
            new(AlcanceDeServicio.BajaDeTitular, (CredencialDeBaja, null)),
            new(AlcanceDeServicio.OrigenReenviado, ("credencial-de-origen-reenviado-zxcvbnmasdf", null)),
        ]);

        var actor = new ActorDelFlujo();
        var local = new MotorDeLimitesLocal();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV", ErroresDeFuentes.Especificos.Concat(ErroresDeReservas.Especificos).ToDictionary(p => p.Key, p => p.Value)));
        builder.Services.AgregarLimitesHttp(new OpcionesDeLimitesHttp());
        builder.Services.AgregarCredencialDeServicio(new OpcionesDeSuperficieDeServicio(), credenciales);
        builder.Services.AddSingleton<IMatrizDeAlcancesDeServicio>(matriz);
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<ICalculadorDeHuella>(new CalculadorDeHuellaHmac(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella))));
        builder.Services.AddSingleton<IReloj>(Reloj);
        builder.Services.AddSingleton<IPublicadorDeEventosDeSeguridad>(new EventosDeSeguridadNulos());
        builder.Services.AddSingleton<IPublicadorDeAlertasTecnicas>(Alertas);
        builder.Services.AddSingleton<IAlertas>(AlertasDeAuditoria);
        builder.Services.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales)));
        builder.Services.AddScoped<IAutorizadorDeLlamadasDeServicio>(p => new AutorizarLlamadaDeServicio(
            matriz, p.GetRequiredService<IActorActual>(), p.GetRequiredService<IOrigenDeSolicitud>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
        builder.Services.AddScoped<IResolutorDeOrigenDeServicio>(p => new ResolverOrigenDeLlamadaDeServicio(
            p.GetRequiredService<ICalculadorDeHuella>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));

        // El motor de límites de PostgreSQL (los eventos viven en la transacción de quien reserva: si se revierte, no cuenta) y, para el resto, el local.
        builder.Services.AddScoped(p => new MotorDeLimitesPostgres(p.GetRequiredService<UnidadDeTrabajoPostgres>(), new UnidadDeTrabajoPostgres(bd.Acceso, actor: actor)));
        builder.Services.AddScoped<ISustitutorDeSujetoDeLimites>(p => p.GetRequiredService<MotorDeLimitesPostgres>());
        builder.Services.AddScoped<IControlDeLimites>(p => new EvaluarLimite(
            p.GetRequiredService<CatalogoDePoliticas>(), p.GetRequiredService<MotorDeLimitesPostgres>(), local, p.GetRequiredService<IOrigenDeSolicitud>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));

        // Auditoría real (catálogo, constructor, actor del contexto) sobre almacenes en memoria que pueden fallar.
        builder.Services.AddScoped(p => new ConstructorDeRegistros(ServicioDeOrigen.Inventario, CatalogoDeAccionesDeAuditoria.Crear(), p.GetRequiredService<IReloj>(), p.GetRequiredService<IActorActual>(), p.GetRequiredService<IOrigenDeSolicitud>()));
        builder.Services.AddScoped<IAuditoria>(p => new AuditoriaDeAplicacion(p.GetRequiredService<ConstructorDeRegistros>(), Auditoria));
        builder.Services.AddScoped<IAuditoriaDeBajasRecibidas>(p => new AuditoriaDeBajasRecibidas(p.GetRequiredService<IAuditoria>(), p.GetRequiredService<IActorActual>()));
        builder.Services.AddScoped<IContextoDeDenegacion, ContextoDeDenegacion>();
        builder.Services.AddSingleton<IPlantillasDeRuta>(_ => new PlantillasDeRutaDePrueba(() => _app.Services.GetRequiredService<EndpointDataSource>()));
        builder.Services.AddSingleton(p => new ResolutorDeRutas(p.GetRequiredService<IPlantillasDeRuta>()));
        builder.Services.AddScoped<IDenegacionDeAcceso>(p => new DenegacionDeAccesoDeAplicacion(
            p.GetRequiredService<ConstructorDeRegistros>(), p.GetRequiredService<IContextoDeDenegacion>(), p.GetRequiredService<ResolutorDeRutas>(), Denegaciones, Alertas, p.GetRequiredService<IReloj>()));
        builder.Services.AddSingleton<IAlLiberarPedido>(new ReceptorSinEfecto());

        builder.Services.AddSingleton(bd.Acceso);
        var configuracion = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Inventario:ClaveDeBajas"] = PilaDeReservas.ClaveDeBajasDePrueba }).Build();
        builder.Services.AgregarModuloReservas(configuracion);
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

        // Autenticación de la prueba: el usuario sale de cabeceras solo aquí; en producción lo verifica Identidad y ninguna cabecera de actor vale.
        _app.Use(async (contexto, siguiente) =>
        {
            var rol = contexto.Request.Headers[CabeceraDeRol].ToString();
            if (rol.Length > 0)
            {
                var sucursal = contexto.Request.Headers[CabeceraDeSucursal].ToString();
                var usuario = contexto.Request.Headers[CabeceraDeUsuario].ToString();
                actor.Establecer(ActorActual.DeUsuario(usuario.Length > 0 ? Guid.Parse(usuario) : Guid.NewGuid(), rol, sucursal.Length > 0 ? Guid.Parse(sucursal) : null));
            }

            await siguiente(contexto);
        });
        _app.UsarContextoDeUsuarioDeServicio();
        _app.UsarActorDeServicio();

        _app.MapearFuentes();
        _app.MapearReservas();

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

    /// <summary>Una solicitud con la credencial y, si se indica, el usuario de prueba (rol, sucursal e identificador) y la clave de idempotencia.</summary>
    public Task<HttpResponseMessage> Enviar(
        HttpMethod metodo,
        string ruta,
        string? credencial,
        string? rol = null,
        Guid? sucursal = null,
        Guid? usuario = null,
        string? json = null,
        string? clave = null)
    {
        var solicitud = new HttpRequestMessage(metodo, Raiz + ruta) { Content = json is null ? null : Json(json) };
        if (credencial is not null)
        {
            solicitud.Headers.TryAddWithoutValidation(CredencialDeServicioMiddleware.CabeceraDeCredencial, credencial);
        }

        if (rol is not null)
        {
            solicitud.Headers.TryAddWithoutValidation(CabeceraDeRol, rol);
        }

        if (sucursal is { } s)
        {
            solicitud.Headers.TryAddWithoutValidation(CabeceraDeSucursal, s.ToString());
        }

        if (usuario is { } u)
        {
            solicitud.Headers.TryAddWithoutValidation(CabeceraDeUsuario, u.ToString());
        }

        if (clave is not null)
        {
            solicitud.Headers.TryAddWithoutValidation(CabecerasDeServicio.ClaveDeIdempotencia, clave);
        }

        return Cliente.SendAsync(solicitud);
    }

    public static StringContent Json(string json) => new(json, System.Text.Encoding.UTF8, "application/json");

    public static string Raiz => RutasDeServicio.Inventario;
}
