using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Auditoria;
using BabelHub.Inventario.Application.Auditoria.Interno;
using BabelHub.Inventario.Application.Plataforma;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BabelHub.IntegrationTests.Auditoria;

/// <summary>
/// Los puntos de la Api que terminan en un 401 o un 403 registran <b>un único evento por solicitud</b> (T28; AC-AUD-04, AC-AUD-21; AUD-05): el reto y la prohibición de un esquema de autenticación y el resultado de una política
/// de autorización (que no pasan por ningún caso de uso), el rechazo de un endpoint propio, y una excepción que llega al manejador de errores. Con un esquema de autenticación de prueba y las políticas reales de ASP.NET Core;
/// el registrador cuenta lo que recibe y el contexto de denegación real garantiza que un mismo rechazo no se cuente dos veces.
/// </summary>
public sealed class DenegacionesHttpInventarioTests
{
    public sealed class Contador : IRegistradorDeDenegacionesHttp
    {
        public List<(CategoriaDeError Categoria, string? Motivo, string? Codigo, string Ruta)> Recibidas { get; } = [];

        public Task Registrar(HttpContext contexto, CategoriaDeError categoria, string? motivo, string? codigoDeError)
        {
            lock (Recibidas)
            {
                Recibidas.Add((categoria, motivo, codigoDeError, contexto.Request.Path.Value ?? string.Empty));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DenegacionNula : IDenegacionDeAcceso
    {
        public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default) => Task.CompletedTask;
    }

    private sealed class EsquemaDePrueba(IOptionsMonitor<AuthenticationSchemeOptions> opciones, ILoggerFactory registro, UrlEncoder codificador) : AuthenticationHandler<AuthenticationSchemeOptions>(opciones, registro, codificador)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var rol = Request.Headers["X-Rol"].ToString();
            if (rol.Length == 0)
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identidad = new ClaimsIdentity([new Claim(ClaimTypes.Name, "u"), new Claim(ClaimTypes.Role, rol)], "prueba");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identidad), "prueba")));
        }
    }

    private static async Task<(WebApplication App, Contador Contador)> Crear()
    {
        var contador = new Contador();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV"));
        builder.Services.AddSingleton<IRegistradorDeDenegacionesHttp>(contador);
        builder.Services.AddSingleton<IDenegacionDeAcceso>(new DenegacionNula());
        builder.Services.AddAuthentication("prueba").AddScheme<AuthenticationSchemeOptions, EsquemaDePrueba>("prueba", _ => { });
        builder.Services.AddAuthorization(o => o.AddPolicy("solo-admin", p => p.RequireRole("administrador-central")));
        var app = builder.Build();
        app.UsarManejoDeErrores();
        app.UsarRegistroDeDenegaciones();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/protegido", () => "ok").RequireAuthorization("solo-admin");
        app.MapGet("/libre", () => "ok");
        app.MapGet("/rechazo-propio", (HttpContext c) => Results.StatusCode(StatusCodes.Status403Forbidden));
        app.MapGet("/excepcion-de-negocio", () => { throw new ExcepcionDeNegocio("fuera-de-sucursal", CategoriaDeError.Prohibido); });
        app.MapGet("/sin-actor", () => { throw new ContextoDeActorAusenteException(); });
        app.MapGet("/alcance-de-datos", () => { throw new AlcanceDeDatosDenegadoException(); });
        app.MapGet("/rota", () => { throw new InvalidOperationException("secreto-interno"); });
        await app.StartAsync();
        return (app, contador);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task ElRetoDeLaAutenticacion_ResponderConUn401_YSeRegistraUnaVez()
    {
        var (app, contador) = await Crear();
        await using var _ = app;

        var respuesta = await app.GetTestClient().GetAsync("/protegido");

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        var recibida = Assert.Single(contador.Recibidas);
        Assert.Equal(CategoriaDeError.NoAutenticado, recibida.Categoria);
        Assert.Equal("/protegido", recibida.Ruta);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task LaProhibicionDeUnaPolitica_ResponderConUn403_YSeRegistraUnaVez()
    {
        var (app, contador) = await Crear();
        await using var _ = app;
        var cliente = app.GetTestClient();
        cliente.DefaultRequestHeaders.Add("X-Rol", "cliente");

        var respuesta = await cliente.GetAsync("/protegido");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        var recibida = Assert.Single(contador.Recibidas);
        Assert.Equal(CategoriaDeError.Prohibido, recibida.Categoria);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task UnRechazoPropioDeUnEndpoint_ConUn403_SeRegistra()
    {
        var (app, contador) = await Crear();
        await using var _ = app;

        var respuesta = await app.GetTestClient().GetAsync("/rechazo-propio");

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Single(contador.Recibidas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task UnaExcepcionDeNegocioProhibida_OUnContextoDeActorAusente_LlegaAlManejador_YSeRegistraConSuMotivo()
    {
        var (app, contador) = await Crear();
        await using var _ = app;
        var cliente = app.GetTestClient();

        var prohibida = await cliente.GetAsync("/excepcion-de-negocio");
        var sinActor = await cliente.GetAsync("/sin-actor");

        Assert.Equal(HttpStatusCode.Forbidden, prohibida.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sinActor.StatusCode);
        Assert.Equal(2, contador.Recibidas.Count);
        Assert.Contains(contador.Recibidas, r => r.Categoria == CategoriaDeError.Prohibido && r.Motivo == "fuera-de-sucursal" && r.Codigo == "INV-PROHIBIDO");
        Assert.Contains(contador.Recibidas, r => r.Categoria == CategoriaDeError.NoAutenticado && r.Codigo == "INV-NO-AUTENTICADO");
    }

[Fact]    [Trait("Criterio", "AC-AUD-04")]    public async Task UnaLecturaFueraDelAlcanceDeDatos_EsUn403Registrado_NoUnErrorInesperado()    {        var (app, contador) = await Crear();        await using var _ = app;        var respuesta = await app.GetTestClient().GetAsync("/alcance-de-datos");        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);        var recibida = Assert.Single(contador.Recibidas);        Assert.Equal("alcance-insuficiente", recibida.Motivo);    }
    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public async Task UnErrorInesperado_NoEsUnAccesoDenegado_NiFiltraSuMensaje()
    {
        var (app, contador) = await Crear();
        await using var _ = app;

        var respuesta = await app.GetTestClient().GetAsync("/rota");

        Assert.Equal(HttpStatusCode.InternalServerError, respuesta.StatusCode);
        Assert.DoesNotContain("secreto-interno", await respuesta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Empty(contador.Recibidas);
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-21")]
    public void SinElRegistradorNiElModuloCompuestos_ElPipelineNoSeConstruye()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV"));
        var app = builder.Build();
        app.UsarManejoDeErrores();

        var falla = Assert.Throws<InvalidOperationException>(() => app.UsarRegistroDeDenegaciones());

        Assert.Contains("registro de accesos denegados", falla.Message, StringComparison.Ordinal);
    }

    private sealed class RegistroEnMemoria : ILoggerProvider, ILogger
    {
        public List<string> Mensajes { get; } = [];

        public ILogger CreateLogger(string categoria) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel nivel) => true;

        public void Log<TState>(LogLevel nivel, EventId evento, TState estado, Exception? excepcion, Func<TState, Exception?, string> formato)
        {
            lock (Mensajes)
            {
                Mensajes.Add(formato(estado, excepcion));
            }
        }

        public void Dispose()
        {
        }
    }

    [Fact]
    [Trait("Criterio", "AC-AUD-06")]
    public async Task LaConsultaDeLaAuditoria_NoLlegaAlRegistroDeLaPeticionDelServidor()
    {
        var registro = new RegistroEnMemoria();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(LogLevel.Trace);
        builder.Logging.AddProvider(registro);
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("INV"));
        builder.Services.AgregarRegistroDeDenegaciones();
        var app = builder.Build();
        app.MapGet("/api/inventario/v1/auditoria", () => "ok");
        await app.StartAsync();
        await using var _ = app;

        var respuesta = await app.GetTestClient().GetAsync("/api/inventario/v1/auditoria?texto=dato-buscado-secreto&usuarioId=11111111-1111-4111-8111-111111111111");

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.DoesNotContain(registro.Mensajes, m => m.Contains("dato-buscado-secreto", StringComparison.Ordinal) || m.Contains("11111111-1111-4111", StringComparison.Ordinal));
    }
}
