using System.Net;
using BabelHub.Ecommerce.Api.Auditoria;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.IntegrationTests.Auditoria;

/// <summary>El E-commerce en proceso (TestServer): el módulo <c>Auditoria</c> sobre PostgreSQL, el cliente HTTP real hacia el Inventario (otro TestServer) y los endpoints de la auditoría (T28).</summary>
public sealed class EcommerceDeAuditoriaHost : IAsyncDisposable
{
    private WebApplication _app = null!;

    public HttpClient Cliente { get; private set; } = null!;

    public IServiceProvider Servicios => _app.Services;

    public volatile bool AuditoriaCaida;

    public volatile bool InventarioCaido;

    public volatile bool DenegacionesCaidas;

    public static async Task<EcommerceDeAuditoriaHost> Crear(PostgresDeAuditoriaFixture bd, InventarioDeAuditoriaHost inventario)
    {
        var host = new EcommerceDeAuditoriaHost();
        await host.Iniciar(bd, inventario);
        return host;
    }

    private async Task Iniciar(PostgresDeAuditoriaFixture bd, InventarioDeAuditoriaHost inventario)
    {
        var actor = new ActorDelFlujo();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IOrigenDeSolicitud, OrigenDeLaSolicitudHttp>();
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<IReloj>(new RelojDeLaApi());
        builder.Services.AddSingleton<ITokenDelUsuarioDeLaSesion>(new TokenDeLaSesionDePrueba(actor));
        builder.Services.AddSingleton<IRegistroDeTrabajosDeFondo, RegistroDeTrabajosNulo>();

        builder.Services.AddSingleton(bd.Acceso);
        builder.Services.AddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>()));
        builder.Services.AddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        builder.Services.AddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        builder.Services.AddScoped<IAlmacenDeRegistros>(sp => new AlmacenQueFalla(new PostgresAlmacenDeRegistros(sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>()), () => AuditoriaCaida));

        builder.Services.AddSingleton<IEscritorDeDenegaciones>(sp => new EscritorQueFalla(new PostgresEscritorDeDenegaciones(sp.GetRequiredService<NpgsqlDataSource>(), new LimitadorDeEscrituras()), () => DenegacionesCaidas));

        // Lo que es de Identidad (T30 y T34 de identidad-seguridad): sin registros aislados, sin destinatarios y sin correo.
        builder.Services.AddSingleton<IFuenteDeRegistrosAislados, IdentidadVacia>();
        builder.Services.AddSingleton<IAlmacenDeRegistrosAislados, IdentidadVacia>();
        builder.Services.AddSingleton<IDestinatariosDeAlertas, IdentidadVacia>();
        builder.Services.AddSingleton<IEnvioDeCorreoDeAlertas, IdentidadVacia>();

        builder.Services.AgregarModuloAuditoria(new OpcionesDelModuloDeAuditoria(bd.Depuracion, new ClavesDeCursorDelEntorno(new string('e', 40), "v1")));
        builder.Services.AgregarRegistroDeDenegaciones();

        // El cliente HTTP real hacia la auditoría del Inventario, con la cadena real de Plataforma y el otro TestServer detrás.
        builder.Services.AddSingleton(new OpcionesDeCredencialesDeSalida(
        [
            new(AlcanceDeServicio.ConsultaDeAuditoria, new ValorSecreto(InventarioDeAuditoriaHost.CredencialDeConsulta)),
            new(AlcanceDeServicio.LecturaDeAlertas, new ValorSecreto(InventarioDeAuditoriaHost.CredencialDeAlertas)),
            new(AlcanceDeServicio.MarcarAlerta, new ValorSecreto(InventarioDeAuditoriaHost.CredencialDeMarca)),
            new(AlcanceDeServicio.Historial, new ValorSecreto(InventarioDeAuditoriaHost.CredencialDeHistorial)),
        ]));
        builder.Services.AgregarClienteDeAuditoriaDelInventario(new Uri("https://inventario.interno/"));
        builder.Services.AddHttpClient(HttpClienteDeAuditoriaDelInventario.NombreDelCliente)
            .ConfigurePrimaryHttpMessageHandler(() => new ManejadorQueSePuedeCaer(inventario.CrearManejador(), () => InventarioCaido));

        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.UsarRegistroDeDenegaciones();
        _app.Use(async (contexto, siguiente) =>
        {
            var rol = contexto.Request.Headers["X-Prueba-Rol"].ToString();
            if (rol.Length > 0 && Guid.TryParse(contexto.Request.Headers["X-Prueba-Usuario"], out var usuario))
            {
                var sucursal = contexto.Request.Headers["X-Prueba-Sucursal"].ToString();
                actor.Establecer(ActorActual.DeUsuario(usuario, rol, sucursal.Length > 0 ? Guid.Parse(sucursal) : null));
            }

            await siguiente();
        });
        _app.MapearAuditoria();

        // Una operación sensible de prueba: cambia algo y lo audita en la misma transacción (AUD-04, AC-AUD-03).
        _app.MapPost("/prueba/ajuste", async (IUnidadDeTrabajo unidad, IAuditoria auditoria) =>
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
        });

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

    public Task<HttpResponseMessage> Enviar(HttpMethod metodo, string ruta, string? rol = null, Guid? usuario = null, Guid? sucursal = null, HttpContent? cuerpo = null, params (string Nombre, string Valor)[] cabeceras)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta) { Content = cuerpo };
        if (rol is not null)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Rol", rol);
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", (usuario ?? Guid.NewGuid()).ToString("D"));
        }

        if (sucursal is { } id)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Sucursal", id.ToString("D"));
        }

        foreach (var (nombre, valor) in cabeceras)
        {
            solicitud.Headers.TryAddWithoutValidation(nombre, valor);
        }

        return Cliente.SendAsync(solicitud);
    }

    private sealed class AlmacenQueFalla(IAlmacenDeRegistros real, Func<bool> falla) : IAlmacenDeRegistros
    {
        public Task Insertar(BabelHub.Ecommerce.Domain.Auditoria.RegistroDeAuditoria registro, CancellationToken cancelacion = default) =>
            falla() ? Task.FromException(new InvalidOperationException("La auditoría no responde.")) : real.Insertar(registro, cancelacion);
    }

    private sealed class EscritorQueFalla(IEscritorDeDenegaciones real, Func<bool> falla) : IEscritorDeDenegaciones
    {
        public Task Escribir(BabelHub.Ecommerce.Domain.Auditoria.RegistroDeAuditoria registro, CancellationToken cancelacion = default) =>
            falla() ? Task.FromException(new InvalidOperationException("La auditoría no responde.")) : real.Escribir(registro, cancelacion);
    }

    private sealed class TokenDeLaSesionDePrueba(IActorActual actor) : ITokenDelUsuarioDeLaSesion
    {
        public Task<ValorSecreto?> Actual(CancellationToken cancelacion = default) =>
            Task.FromResult(actor.Actual is { Tipo: TipoDeActor.Usuario, UsuarioId: { } usuario, Rol: { } rol }
                ? new ValorSecreto(TokensDePrueba.Crear(rol, usuario, actor.Actual.SucursalId))
                : null);
    }

    private sealed class ManejadorQueSePuedeCaer(HttpMessageHandler interno, Func<bool> caido) : DelegatingHandler(interno)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            caido() ? throw new HttpRequestException("El Inventario no responde.") : base.SendAsync(request, cancellationToken);
    }

    private sealed class IdentidadVacia : IFuenteDeRegistrosAislados, IAlmacenDeRegistrosAislados, IDestinatariosDeAlertas, IEnvioDeCorreoDeAlertas
    {
        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => Task.FromResult(new PaginaDeRegistros([], null));

        public Task<int> Contar(SolicitudDeConteo solicitud, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task Insertar(RegistroAuditado registro, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task InsertarAislandoElFallo(RegistroAuditado registro, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task<IReadOnlyList<DestinatarioDeAlerta>> Activos(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<DestinatarioDeAlerta>>([]);

        public Task<ResultadoDeEnvioDeAlerta> Enviar(CorreoDeAlerta correo, IReadOnlyList<DestinatarioDeAlerta> destinatarios, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeEnvioDeAlerta.Enviado);
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
