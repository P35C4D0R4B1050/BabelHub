using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using BabelHub.Ecommerce.Api.Areas.Admin.Controllers;
using BabelHub.Ecommerce.Api.Areas.Tienda.Controllers;
using BabelHub.Ecommerce.Api.Catalogo;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace BabelHub.IntegrationTests.Catalogo;

/// <summary>Un reloj que se adelanta a pedido (el borde de los 30 minutos del token, las 24 horas de una referencia, los 2 minutos de un intento) sin dormir.</summary>
public sealed class RelojControlado : IReloj
{
    private long _ticks;

    public TimeSpan Adelanto => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

    public DateTimeOffset Ahora => DateTimeOffset.UtcNow + Adelanto;

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);

    public void Adelantar(TimeSpan tiempo) => Interlocked.Add(ref _ticks, tiempo.Ticks);
}

/// <summary>
/// El E-commerce en proceso para el catálogo (T18 a T22): MVC con los controladores de la administración y de las portadas, la composición real del Catálogo sobre PostgreSQL (con la <b>auditoría real en la misma base</b>) y un almacén S3 real,
/// el filtro global antifalsificación y el Inventario simulado. El actor lo fija un middleware de prueba con cabeceras; el filtro de área de la interfaz (que es del módulo Web) no se compone: aquí se prueba la defensa de la Aplicación.
/// </summary>
public sealed class CatalogoApiHost : IAsyncDisposable
{
    public const string Libros = "libros-api";
    public const string Portadas = "portadas-api";
    public const string ClaveDeIngresos = "clave-de-ingresos-de-las-pruebas-0123456789-abcdef";
    public const string ClaveDeIngresosAnterior = "clave-anterior-de-las-pruebas-0123456789-abcdefgh";

    private static readonly JsonSerializerOptions JsonDePrueba = CrearJson();

    private WebApplication _app = null!;
    private (string Cookie, string Token)? _token;

    public HttpClient Cliente { get; private set; } = null!;

    public IServiceProvider Servicios => _app.Services;

    public InventarioSimulado Inventario { get; private set; } = null!;

    public RelojControlado Reloj { get; } = new();

    public IAmazonS3 Almacen { get; private set; } = null!;

    public RegistroDeTrabajosEspia RegistroDeTrabajos { get; } = new();

    /// <summary>La auditoría no responde: toda escritura de un registro falla (AUD-04).</summary>
    public volatile bool AuditoriaCaida;

    public static async Task<CatalogoApiHost> Crear(PostgresDelCatalogoYAuditoriaFixture bd, IAmazonS3 almacen, Action<IServiceCollection>? ajustes = null, bool crearContenedores = true)
    {
        var host = new CatalogoApiHost();
        await host.Iniciar(bd, almacen, ajustes, crearContenedores);
        return host;
    }

    private static JsonSerializerOptions CrearJson()
    {
        var opciones = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        opciones.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return opciones;
    }

    private async Task Iniciar(PostgresDelCatalogoYAuditoriaFixture bd, IAmazonS3 almacen, Action<IServiceCollection>? ajustes, bool crearContenedores)
    {
        Almacen = almacen;
        foreach (var contenedor in crearContenedores ? new[] { Libros, Portadas } : [])
        {
            try
            {
                await almacen.PutBucketAsync(contenedor);
            }
            catch (AmazonS3Exception error) when (error.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
            {
            }
        }

        var actor = new ActorDelFlujo();
        Inventario = new InventarioSimulado(actor);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddHttpClient();
        builder.Services.AddScoped<IOrigenDeSolicitud, OrigenDeLaSolicitudHttp>();
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<IReloj>(Reloj);
        builder.Services.AddSingleton<IRegistroDeTrabajosDeFondo>(RegistroDeTrabajos);

        builder.Services.AddSingleton(bd.Acceso);
        builder.Services.AddSingleton(sp => new UnidadDeTrabajoPostgres(sp.GetRequiredService<NpgsqlDataSource>()));
        builder.Services.AddSingleton<IUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        builder.Services.AddSingleton<IConexionDeLaUnidadDeTrabajo>(sp => sp.GetRequiredService<UnidadDeTrabajoPostgres>());
        builder.Services.AddScoped<IAlmacenDeRegistros>(sp => new AlmacenQueFalla(new PostgresAlmacenDeRegistros(sp.GetRequiredService<IUnidadDeTrabajo>(), sp.GetRequiredService<IConexionDeLaUnidadDeTrabajo>()), () => AuditoriaCaida));

        // Lo que es de Identidad: sin registros aislados, sin destinatarios y sin correo.
        builder.Services.AddSingleton<IFuenteDeRegistrosAislados, IdentidadVacia>();
        builder.Services.AddSingleton<IAlmacenDeRegistrosAislados, IdentidadVacia>();
        builder.Services.AddSingleton<IDestinatariosDeAlertas, IdentidadVacia>();
        builder.Services.AddSingleton<IEnvioDeCorreoDeAlertas, IdentidadVacia>();
        builder.Services.AgregarModuloAuditoria(new OpcionesDelModuloDeAuditoria(bd.Depuracion, new ClavesDeCursorDelEntorno(new string('e', 40), "v1")));
        builder.Services.AgregarRegistroDeDenegaciones();

        // El Catálogo real: PostgreSQL, el almacén S3 real y el Inventario simulado.
        builder.Services.AddSingleton<IClienteDelInventario>(Inventario);
        builder.Services.AgregarCatalogo(new OpcionesDelCatalogo(almacen, ClaveDeIngresos, ClaveDeIngresosAnterior, Libros, Portadas));
        builder.Services.AgregarTrabajosDelCatalogo(soloEnModoNormal: false);

        // Antifalsificación global del E-commerce y MVC con solo los controladores del catálogo.
        builder.Services.AddSingleton(new OpcionesDelHostWeb(HostWeb.Gestion, []));
        builder.Services.AgregarProteccionContraFalsificacion();
        builder.Services
            .AddControllers()
            .ConfigureApplicationPartManager(gestor =>
            {
                gestor.ApplicationParts.Clear();
                gestor.ApplicationParts.Add(new PartesDeTipos(typeof(AdministracionDelCatalogoController), typeof(PortadasController)));
            });

        ajustes?.Invoke(builder.Services);

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
        _app.UseRouting();
        _app.MapGet("/prueba/token", (HttpContext contexto, IAntiforgery antiforgery) => Results.Text(antiforgery.GetAndStoreTokens(contexto).RequestToken));
        _app.MapControllers();

        await _app.StartAsync();
        Cliente = _app.GetTestClient();
        Cliente.BaseAddress = new Uri("https://localhost/");
    }

    public async ValueTask DisposeAsync()
    {
        Cliente?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    // ---------- Solicitudes ----------

    private async Task<(string Cookie, string Token)> TokenAntifalsificacion()
    {
        if (_token is { } guardado)
        {
            return guardado;
        }

        using var respuesta = await Cliente.GetAsync("/prueba/token");
        var cookie = respuesta.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(ProteccionContraFalsificacion.NombreDeLaCookie + "=", StringComparison.Ordinal)).Split(';')[0];
        _token = (cookie, await respuesta.Content.ReadAsStringAsync());
        return _token.Value;
    }

    /// <summary>Prepara una solicitud: el actor por cabeceras de prueba y, si cambia algo, el token antifalsificación en su cabecera.</summary>
    public async Task<HttpRequestMessage> Solicitud(HttpMethod metodo, string ruta, string? rol = RolesDeUsuario.AdministradorCentral, Guid? usuario = null, bool conToken = true, Guid? sucursal = null)
    {
        var solicitud = new HttpRequestMessage(metodo, ruta);
        if (rol is not null)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Rol", rol);
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Usuario", (usuario ?? UsuarioPorOmision).ToString("D"));
        }

        if (sucursal is { } id)
        {
            solicitud.Headers.TryAddWithoutValidation("X-Prueba-Sucursal", id.ToString("D"));
        }

        if (conToken && metodo != HttpMethod.Get)
        {
            var (cookie, token) = await TokenAntifalsificacion();
            solicitud.Headers.TryAddWithoutValidation("Cookie", cookie);
            solicitud.Headers.TryAddWithoutValidation(ProteccionContraFalsificacion.NombreDeLaCabecera, token);
        }

        return solicitud;
    }

    /// <summary>El administrador central de las pruebas (un solo usuario salvo que se indique otro).</summary>
    public static readonly Guid UsuarioPorOmision = Guid.Parse("a0000000-0000-0000-0000-000000000001");

    public async Task<HttpResponseMessage> Json(HttpMethod metodo, string ruta, object? cuerpo = null, string? rol = RolesDeUsuario.AdministradorCentral, Guid? usuario = null, bool conToken = true)
    {
        using var solicitud = await Solicitud(metodo, ruta, rol, usuario, conToken);
        if (cuerpo is not null)
        {
            solicitud.Content = cuerpo is string texto
                ? new StringContent(texto, System.Text.Encoding.UTF8, "application/json")
                : JsonContent.Create(cuerpo, options: JsonDePrueba);
        }

        return await Cliente.SendAsync(solicitud);
    }

    public Task<HttpResponseMessage> Subir(string finalidad, Stream contenido, string? rol = RolesDeUsuario.AdministradorCentral, Guid? usuario = null, bool conToken = true) =>
        SubirConRuta($"/administracion/archivos?finalidad={finalidad}", contenido, rol, usuario, conToken);

    public async Task<HttpResponseMessage> SubirConRuta(string ruta, Stream contenido, string? rol = RolesDeUsuario.AdministradorCentral, Guid? usuario = null, bool conToken = true)
    {
        using var solicitud = await Solicitud(HttpMethod.Post, ruta, rol, usuario, conToken);
        solicitud.Content = new StreamContent(contenido);
        solicitud.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        return await Cliente.SendAsync(solicitud);
    }

    public Task<HttpResponseMessage> Subir(string finalidad, byte[] contenido, string? rol = RolesDeUsuario.AdministradorCentral, Guid? usuario = null, bool conToken = true) =>
        Subir(finalidad, new MemoryStream(contenido), rol, usuario, conToken);

    public static async Task<JsonElement> Leer(HttpResponseMessage respuesta) => (await respuesta.Content.ReadFromJsonAsync<JsonElement>(JsonDePrueba)).Clone();

    // ---------- Atajos ----------

    /// <summary>Sube un archivo y devuelve su referencia.</summary>
    public async Task<Guid> SubirYReferenciar(string finalidad, byte[] contenido, Guid? usuario = null)
    {
        using var respuesta = await Subir(finalidad, contenido, usuario: usuario);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return (await Leer(respuesta)).GetProperty("referenciaId").GetGuid();
    }

    public async Task<JsonElement> RegistrarImpresa(string titulo = "Estructuras de datos", string isbn = "978-0-00-000001-9", int? stockInicial = null, Guid? usuario = null)
    {
        var portada = await SubirYReferenciar("portada", ArchivosDeMuestra.Png(800, 600, 6000), usuario);
        using var respuesta = await Json(
            HttpMethod.Post,
            "/administracion/obras",
            new { titulo, autor = "Ana Quispe", editorial = "Editorial Andina", categoria = "Técnico", portadaRef = portada, ediciones = new[] { new { formato = "impresa", isbn, precio = 129m, stockInicial } } },
            usuario: usuario);
        Assert.Equal(HttpStatusCode.Created, respuesta.StatusCode);
        return await Leer(respuesta);
    }

    private sealed class PartesDeTipos(params Type[] tipos) : ApplicationPart, IApplicationPartTypeProvider
    {
        public override string Name => "controladores-del-catalogo";

        public IEnumerable<TypeInfo> Types => tipos.Select(t => t.GetTypeInfo());
    }

    private sealed class AlmacenQueFalla(IAlmacenDeRegistros real, Func<bool> falla) : IAlmacenDeRegistros
    {
        public Task Insertar(BabelHub.Ecommerce.Domain.Auditoria.RegistroDeAuditoria registro, CancellationToken cancelacion = default) =>
            falla() ? Task.FromException(new InvalidOperationException("La auditoría no responde.")) : real.Insertar(registro, cancelacion);
    }

    private sealed class IdentidadVacia : IFuenteDeRegistrosAislados, IAlmacenDeRegistrosAislados, IDestinatariosDeAlertas, IEnvioDeCorreoDeAlertas
    {
        public Task<PaginaDeRegistros> Consultar(ConsultaDeAuditoria consulta, CancellationToken cancelacion = default) => Task.FromResult(new PaginaDeRegistros([], null));

        public Task<int> Contar(SolicitudDeConteo solicitud, CancellationToken cancelacion = default) => Task.FromResult(0);

        public Task Insertar(RegistroAuditado registro, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task InsertarAislandoElFallo(RegistroAuditado registro, CancellationToken cancelacion = default) => Task.CompletedTask;

        public Task<IReadOnlyList<DestinatarioDeAlerta>> Activos(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<DestinatarioDeAlerta>>([]);

        public Task<IReadOnlyList<DestinatarioDeAlerta>> DeLogistica(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<DestinatarioDeAlerta>>([]);

        public Task<ResultadoDeEnvioDeAlerta> Enviar(CorreoDeAlerta correo, IReadOnlyList<DestinatarioDeAlerta> destinatarios, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeEnvioDeAlerta.Enviado);
    }

    public sealed class RegistroDeTrabajosEspia : IRegistroDeTrabajosDeFondo
    {
        private readonly List<(string Trabajo, ResultadoDeTrabajo Resultado)> _corridas = [];

        public IReadOnlyList<(string Trabajo, ResultadoDeTrabajo Resultado)> Corridas
        {
            get
            {
                lock (_corridas)
                {
                    return [.. _corridas];
                }
            }
        }

        public Task Registrar(string trabajo, ResultadoDeTrabajo resultado, DateTimeOffset instante, CancellationToken cancelacion = default)
        {
            lock (_corridas)
            {
                _corridas.Add((trabajo, resultado));
            }

            return Task.CompletedTask;
        }
    }
}
