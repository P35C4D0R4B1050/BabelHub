using System.Text.Json;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Api.Web;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace BabelHub.IntegrationTests.Web;

/// <summary>Un <see cref="ISesionesWeb"/> con respuestas fijadas por la prueba que registra cómo se lo llamó (el módulo real se prueba aparte, con PostgreSQL).</summary>
internal sealed class SesionesWebDePrueba : ISesionesWeb
{
    public Func<string?, HostWeb, OrigenDeLaAccion, ResultadoDeCarga> Carga { get; set; } = (_, _, _) => new ResultadoDeCarga(EstadoDeCarga.Inexistente, null);

    public Func<string, HostWeb, ResultadoDeCredencial> Credencial { get; set; } = (_, _) => new ResultadoDeCredencial(EstadoDeCredencial.SinSesion, null, null);

    public List<(string? Cookie, HostWeb Host, OrigenDeLaAccion Origen)> Cargas { get; } = [];

    public List<(string Cookie, HostWeb Host)> Aseguradas { get; } = [];

    public List<string> Cierres { get; } = [];

    public List<Guid> CierresDeTodas { get; } = [];

    public Guid UsuarioId { get; } = Guid.NewGuid();

    public const string ValorDeLaCookie = "Aq3ZfK9wLm2XvB7nRt4YcP8sJd6HgE1uTo5WiN0xCyU";

    /// <summary>Una sesión válida de un cliente con la credencial asegurada.</summary>
    public SesionesWebDePrueba ConSesionValida(RolWeb rol = RolWeb.Cliente, Guid? sucursal = null, string token = "eyJ.token-de-prueba.firma")
    {
        Carga = (_, _, _) => new ResultadoDeCarga(EstadoDeCarga.Valida, UsuarioId);
        Credencial = (_, _) => new ResultadoDeCredencial(EstadoDeCredencial.Asegurada, new ActorVerificado(UsuarioId, rol, sucursal), new CredencialEnMemoria(token));
        return this;
    }

    public SesionesWebDePrueba ConCarga(EstadoDeCarga estado)
    {
        Carga = (_, _, _) => new ResultadoDeCarga(estado, null);
        return this;
    }

    public SesionesWebDePrueba ConCredencial(EstadoDeCredencial estado)
    {
        Carga = (_, _, _) => new ResultadoDeCarga(EstadoDeCarga.Valida, UsuarioId);
        Credencial = (_, _) => new ResultadoDeCredencial(estado, null, null);
        return this;
    }

    public Task<Resultado<SesionIniciada>> Iniciar(SolicitudDeInicioDeSesion solicitud, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<ResultadoDeCarga> Cargar(string? valorDeLaCookie, HostWeb host, OrigenDeLaAccion origen, CancellationToken cancelacion = default)
    {
        lock (Cargas)
        {
            Cargas.Add((valorDeLaCookie, host, origen));
        }

        return Task.FromResult(Carga(valorDeLaCookie, host, origen));
    }

    public Task<ResultadoDeCredencial> AsegurarCredencial(string valorDeLaCookie, HostWeb host, CancellationToken cancelacion = default)
    {
        lock (Aseguradas)
        {
            Aseguradas.Add((valorDeLaCookie, host));
        }

        return Task.FromResult(Credencial(valorDeLaCookie, host));
    }

    public Task Cerrar(string valorDeLaCookie, CancellationToken cancelacion = default)
    {
        Cierres.Add(valorDeLaCookie);
        return Task.CompletedTask;
    }

    public Task CerrarTodasDeUnUsuario(Guid usuarioId, CancellationToken cancelacion = default)
    {
        CierresDeTodas.Add(usuarioId);
        return Task.CompletedTask;
    }
}

/// <summary>Arma un anfitrión con la sesión web real del pipeline (host, handler, actor) sobre un <see cref="SesionesWebDePrueba"/>.</summary>
internal static class AnfitrionConSesion
{
    public static Task<AnfitrionWeb> Iniciar(
        SesionesWebDePrueba sesiones,
        HostWeb host = HostWeb.Tienda,
        ModoDeArranque modo = ModoDeArranque.Normal,
        Action<IServiceCollection>? servicios = null)
    {
        return AnfitrionWeb.Iniciar(
            s =>
            {
                s.AddSingleton<ISesionesWeb>(sesiones);
                s.AddSingleton(typeof(ModoDeArranque), modo);
                s.AddSingleton<ActorDelFlujo>();
                s.AddSingleton<IActorActual>(p => p.GetRequiredService<ActorDelFlujo>());
                s.AddSingleton<IEstablecedorDeActor>(p => p.GetRequiredService<ActorDelFlujo>());
                s.AgregarTokenDelUsuarioDeLaSesion();
                s.AgregarHostWeb(new OpcionesDelHostWeb(host, []));
                servicios?.Invoke(s);
            },
            partes: [typeof(ControladorDeSesionDePrueba).Assembly],
            despuesDelEnrutado: app => app.UsarSesionWeb());
    }

    /// <summary>La cookie de la sesión del host indicado, lista para enviarla.</summary>
    public static string Cookie(HostWeb host = HostWeb.Tienda, string valor = SesionesWebDePrueba.ValorDeLaCookie) => $"{CookieDeSesionWeb.Nombre(host)}={valor}";

    /// <summary>Pide un token antifalsificación (con la cookie de sesión, si la hay) y arma las cabeceras de un <c>POST</c> válido.</summary>
    public static async Task<HttpRequestMessage> Post(AnfitrionWeb anfitrion, string ruta, string? cookieDeSesion = null, bool conToken = true)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta);
        var cookies = new List<string>();
        if (cookieDeSesion is not null)
        {
            cookies.Add(cookieDeSesion);
        }

        if (conToken)
        {
            var pedido = new HttpRequestMessage(HttpMethod.Get, "/prueba/sesion/token");
            if (cookieDeSesion is not null)
            {
                pedido.Headers.Add("Cookie", cookieDeSesion);
            }

            var respuesta = await anfitrion.Cliente.SendAsync(pedido);
            respuesta.EnsureSuccessStatusCode();
            var token = await respuesta.Content.ReadAsStringAsync();
            var antiforgery = respuesta.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]).Single(c => c.StartsWith(".AspNetCore.Antiforgery", StringComparison.Ordinal));
            cookies.Add(antiforgery);
            solicitud.Headers.Add("RequestVerificationToken", token);
        }

        if (cookies.Count > 0)
        {
            solicitud.Headers.Add("Cookie", string.Join("; ", cookies));
        }

        return solicitud;
    }
}

/// <summary>Rutas de prueba de la sesión: quién ve la solicitud, un sondeo declarado como consulta automática, el token antifalsificación y áreas de ejemplo.</summary>
[Route("prueba/sesion")]
public sealed class ControladorDeSesionDePrueba(IActorActual actor, ITokenDelUsuarioDeLaSesion token, IAntiforgery antiforgery) : Controller
{
    [HttpGet("yo")]
    public async Task<IActionResult> Yo() => Json(new
    {
        autenticada = User.Identity?.IsAuthenticated == true,
        usuarioId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value,
        rol = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value,
        sucursal = User.FindFirst("bh:sucursal")?.Value,
        actorTipo = actor.Actual?.Tipo.ToString(),
        actorUsuarioId = actor.Actual?.UsuarioId,
        actorRol = actor.Actual?.Rol,
        estado = HttpContext.EstadoDeLaSesion().ToString(),
        tieneToken = await token.Actual() is not null,
    });

    [HttpGet("sondeo")]
    [ConsultaAutomatica]
    public IActionResult Sondeo() => Content("<p>sondeo</p>", "text/html");

    [HttpGet("token")]
    public IActionResult Token() => Content(antiforgery.GetAndStoreTokens(HttpContext).RequestToken!);

    [HttpPost("accion")]
    [ValidateAntiForgeryToken]
    public IActionResult Accion() => Ok();
}

[Area("Sucursal")]
[Route("prueba/area/sucursal")]
public sealed class ControladorDeAreaSucursalDePrueba : Controller
{
    [HttpGet]
    public IActionResult Index() => Content("sucursal");
}

[Area("Admin")]
[Route("prueba/area/admin")]
public sealed class ControladorDeAreaAdminDePrueba : Controller
{
    [HttpGet]
    public IActionResult Index() => Content("admin");
}

[Area("Tienda")]
[Route("prueba/area/tienda")]
public sealed class ControladorDeAreaTiendaDePrueba : Controller
{
    [HttpGet]
    public IActionResult Index() => Content("tienda");
}

internal static class JsonDePrueba
{
    public static JsonElement Leer(string texto) => JsonDocument.Parse(texto).RootElement.Clone();
}
