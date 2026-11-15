using System.Net;
using BabelHub.Inventario.Api.Auditoria;
using BabelHub.Inventario.Api.Fuentes;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Application.Plataforma.Interno;
using BabelHub.Inventario.Domain.Auditoria;
using BabelHub.Inventario.Domain.Plataforma;
using BabelHub.Inventario.Infrastructure.Auditoria;
using BabelHub.Inventario.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.IntegrationTests.Auditoria;

/// <summary>El Inventario en proceso (TestServer): la cadena real de credencial de servicio, el módulo <c>Auditoria</c> sobre PostgreSQL y los endpoints de la auditoría (T28).</summary>
public sealed class InventarioDeAuditoriaHost : IAsyncDisposable
{
    public const string CredencialDeConsulta = "credencial-de-consulta-de-auditoria-0123456789";
    public const string CredencialDeAlertas = "credencial-de-lectura-de-alertas-abcdefghijk";
    public const string CredencialDeMarca = "credencial-de-marcar-alerta-qwertyuiop1234";
    public const string CredencialDeHistorial = "credencial-de-historial-zxcvbnmasdfgh12";
    public const string CredencialDeOrigen = "credencial-de-origen-reenviado-zxcvbnmasdf";
    private static readonly byte[] ClaveDeHuella = Enumerable.Range(1, 32).Select(i => (byte)i).ToArray();

    private WebApplication _app = null!;

    public HttpClient Cliente { get; private set; } = null!;

    public IServiceProvider Servicios => _app.Services;

    public volatile bool AuditoriaCaida;

    public PostgresAlmacenDeRegistros? Almacen { get; private set; }

    public static async Task<InventarioDeAuditoriaHost> Crear(BabelHub.IntegrationTests.AuditoriaInventario.PostgresDeAuditoriaFixture bd)
    {
        var host = new InventarioDeAuditoriaHost();
        await host.Iniciar(bd);
        return host;
    }

    private async Task Iniciar(BabelHub.IntegrationTests.AuditoriaInventario.PostgresDeAuditoriaFixture bd)
    {
        var matriz = new MatrizDeAlcances(
        [
            .. MatrizDeAuditoria.Declaraciones(),
        ]);
        var credenciales = new OpcionesDeCredencialesDeEntrada(
        [
            new(AlcanceDeServicio.ConsultaDeAuditoria, (CredencialDeConsulta, null)),
            new(AlcanceDeServicio.LecturaDeAlertas, (CredencialDeAlertas, null)),
            new(AlcanceDeServicio.MarcarAlerta, (CredencialDeMarca, null)),
            new(AlcanceDeServicio.Historial, (CredencialDeHistorial, null)),
            new(AlcanceDeServicio.OrigenReenviado, (CredencialDeOrigen, null)),
        ]);

        var actor = new ActorDelFlujo();
        var local = new MotorDeLimitesLocal();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV"));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IOrigenDeSolicitud, OrigenDeLaSolicitudHttp>();
        builder.Services.AgregarCredencialDeServicio(new OpcionesDeSuperficieDeServicio(), credenciales);
        builder.Services.AddSingleton<IMatrizDeAlcancesDeServicio>(matriz);
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<IReloj>(new RelojDeLaApi());
        builder.Services.AddSingleton<ICalculadorDeHuella>(new CalculadorDeHuellaHmac(new OpcionesDeHuella("v1:" + Convert.ToBase64String(ClaveDeHuella))));
        builder.Services.AddSingleton(new CatalogoDePoliticas(PoliticasDeSeguridad.Todas.Concat(PoliticasDeSeguridad.Generales)));
        builder.Services.AddScoped<IAutorizadorDeLlamadasDeServicio>(p => new AutorizarLlamadaDeServicio(
            matriz, p.GetRequiredService<IActorActual>(), p.GetRequiredService<IOrigenDeSolicitud>(),
            p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
        builder.Services.AddScoped<IResolutorDeOrigenDeServicio>(p => new ResolverOrigenDeLlamadaDeServicio(
            p.GetRequiredService<ICalculadorDeHuella>(), p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));
        builder.Services.AddScoped<IControlDeLimites>(p => new EvaluarLimite(
            p.GetRequiredService<CatalogoDePoliticas>(), local, local, p.GetRequiredService<IOrigenDeSolicitud>(),
            p.GetRequiredService<IPublicadorDeEventosDeSeguridad>(), p.GetRequiredService<IReloj>()));

        // El módulo de auditoría real sobre PostgreSQL; la unidad de trabajo es la de la plataforma.
        builder.Services.AddSingleton(bd.Acceso);
        builder.Services.AddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>(), actor: sp.GetRequiredService<IActorActual>()));
        builder.Services.AddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        builder.Services.AddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        builder.Services.AddSingleton<IRegistroDeTrabajosDeFondo, RegistroDeTrabajosNulo>();
        builder.Services.AddScoped<IAlmacenDeRegistros>(sp => new AlmacenDelInventarioQueFalla(
            new PostgresAlmacenDeRegistros(sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>()), () => AuditoriaCaida));
        builder.Services.AgregarModuloAuditoria(new OpcionesDelModuloDeAuditoria(bd.Depuracion, new ClavesDeCursorDelEntorno(new string((char)105, 40), "v1")));
        builder.Services.AgregarRegistroDeDenegaciones();

        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.UsarRegistroDeDenegaciones();
        _app.Use(async (contexto, siguiente) =>
        {
            contexto.Connection.RemoteIpAddress = IPAddress.Parse("10.1.0.7");
            contexto.Request.Scheme = "https";
            await siguiente();
        });
        _app.UsarCredencialDeServicio();

        // Autenticación de la prueba (D10): el usuario sale del token de la prueba y de nada más (una cabecera de actor no vale).
        _app.Use(async (contexto, siguiente) =>
        {
            var cabecera = contexto.Request.Headers.Authorization.ToString();
            if (cabecera.StartsWith("Bearer ", StringComparison.Ordinal) && TokensDePrueba.Leer(cabecera["Bearer ".Length..]) is { } usuario)
            {
                actor.Establecer(ActorActual.DeUsuario(usuario.Usuario, usuario.Rol, usuario.Sucursal));
            }

            await siguiente();
        });
        _app.UsarContextoDeUsuarioDeServicio();
        _app.UsarActorDeServicio();
        _app.MapearAuditoria();

        // Una operación sensible de prueba: cambia algo y lo audita en la misma transacción (AUD-04, AC-AUD-03).
        _app.MapPost("/servicio/prueba/ajuste", async (IUnidadDeTrabajo unidad, IAuditoria auditoria, IActorActual actores) =>
        {
            var ajuste = Guid.NewGuid();
            await unidad.Ejecutar(async t =>
            {
                await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.AjusteRegistrado)
                {
                    ObjetoTipo = "ajuste",
                    ObjetoId = ajuste.ToString("D"),
                    Detalle = new Dictionary<string, string?> { ["ajusteId"] = ajuste.ToString("D"), ["cantidadAnterior"] = "5", ["cantidadPosterior"] = "4" },
                }, t).ConfigureAwait(false);
                return 0;
            }).ConfigureAwait(false);
            return Results.Json(new { ajuste });
        }).WithMetadata(new OperacionDeServicioAttribute("inventario.auditoria.consultar"));

        _app.MapGet("/servicio/prueba/clave-rara", () => { throw new ExcepcionDeNegocio("una-clave-que-el-catalogo-no-admite", CategoriaDeError.Prohibido); }).WithMetadata(new OperacionDeServicioAttribute("inventario.auditoria.consultar"));
        _app.MapGet("/servicio/prueba/alcance", () => { throw new AlcanceDeDatosDenegadoException(); }).WithMetadata(new OperacionDeServicioAttribute("inventario.auditoria.consultar"));

        await _app.StartAsync();
        Cliente = _app.GetTestClient();
    }

    public HttpMessageHandler CrearManejador() => _app.GetTestServer().CreateHandler();

    public async ValueTask DisposeAsync()
    {
        Cliente?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }
    }

    public Task<HttpResponseMessage> Enviar(HttpMethod metodo, string ruta, string? credencial, string? token = null, HttpContent? cuerpo = null, params (string Nombre, string Valor)[] cabeceras)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta) { Content = cuerpo };
        if (credencial is not null)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Credencial-De-Servicio", credencial);
        }

        if (token is not null)
        {
            solicitud.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        }

        foreach (var (nombre, valor) in cabeceras)
        {
            solicitud.Headers.TryAddWithoutValidation(nombre, valor);
        }

        return Cliente.SendAsync(solicitud);
    }

    private sealed class AlmacenDelInventarioQueFalla(IAlmacenDeRegistros real, Func<bool> falla) : IAlmacenDeRegistros
    {
        public Task Insertar(RegistroDeAuditoria registro, CancellationToken cancelacion = default) =>
            falla() ? Task.FromException(new InvalidOperationException("La auditoría no responde.")) : real.Insertar(registro, cancelacion);
    }

    private sealed class RegistroDeTrabajosNulo : IRegistroDeTrabajosDeFondo
    {
        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class RelojDeLaApi : IReloj
    {
        public DateTimeOffset Ahora => DateTimeOffset.UtcNow;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
    }
}

