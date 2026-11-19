using System.Net;
using System.Security.Cryptography;
using BabelHub.Inventario.Api.Plataforma;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using JwsEs256 = BabelHub.Ecommerce.Infrastructure.Identidad.JwsEs256;
using JwkEs256 = BabelHub.Ecommerce.Infrastructure.Identidad.JwkEs256;

namespace BabelHub.IntegrationTests.Api;

/// <summary>
/// La credencial del usuario en el pipeline del Inventario (identidad-seguridad T41, COMP-1; IDE-10, IDE-61, D10): el actor sale <b>solo</b> de la credencial verificada; una credencial inválida, de otra audiencia, el contexto del
/// asistente o una copia de claves vencida dejan la solicitud sin actor, ninguna cabecera de actor vale y el token no sigue por el pipeline.
/// </summary>
public sealed class CredencialDeUsuarioDelInventarioTests : IAsyncDisposable
{
    private const string Kid = "kid-del-inventario-01";
    private static readonly DateTimeOffset Ahora = DateTimeOffset.UtcNow;

    private sealed class ObtenedorFijo(string jwks, string revocaciones) : IObtenedorDeClavesPublicadas
    {
        public Task<string> ObtenerJwks(CancellationToken cancelacion = default) => Task.FromResult(jwks);

        public Task<string> ObtenerRevocaciones(CancellationToken cancelacion = default) => Task.FromResult(revocaciones);
    }

    private readonly ECDsa _clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private WebApplication _app = null!;
    private HttpClient _cliente = null!;

    private async Task Iniciar()
    {
        var jwks = $"{{\"keys\":[{JwkEs256.Publico(_clave, Kid)}]}}";
        var revocaciones = $"{{\"revocadas\":[],\"generadoEn\":{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}}}";
        var cache = new CacheDeClavesDelInventario(new ObtenedorFijo(jwks, revocaciones));
        Assert.True(await cache.Renovar());
        var actor = new ActorDelFlujo();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton<IReloj, RelojDelSistema>();
        builder.Services.AddSingleton<IEstablecedorDeActor>(actor);
        builder.Services.AddSingleton<IActorActual>(actor);
        builder.Services.AddSingleton<IVerificadorDeCredenciales>(new VerificadorDeCredencialesDelInventario(cache));
        _app = builder.Build();
        _app.Use(async (contexto, siguiente) =>
        {
            actor.Limpiar();
            await siguiente();
        });
        _app.UsarCredencialDeUsuario();
        _app.MapGet("/quien", (HttpContext c, IActorActual actores) =>
            Results.Json(new { actor = actores.Actual?.UsuarioId, rol = actores.Actual?.Rol, sucursal = actores.Actual?.SucursalId, autorizacion = c.Request.Headers.Authorization.ToString() }));
        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    private static async Task<CredencialDeUsuarioDelInventarioTests> Crear()
    {
        var e = new CredencialDeUsuarioDelInventarioTests();
        await e.Iniciar();
        return e;
    }

    private string Token(Func<Dictionary<string, object?>, Dictionary<string, object?>>? ajustar = null, ECDsa? clave = null)
    {
        var reclamos = new Dictionary<string, object?>
        {
            ["iss"] = CredencialDeAcceso.Emisor,
            ["sub"] = Guid.NewGuid().ToString("D"),
            ["aud"] = new[] { CredencialDeAcceso.AudienciaEcommerce, CredencialDeAcceso.AudienciaInventario },
            ["rol"] = RolesDeUsuario.Cliente,
            ["iat"] = Ahora.ToUnixTimeSeconds(),
            ["nbf"] = Ahora.ToUnixTimeSeconds(),
            ["exp"] = Ahora.AddMinutes(15).ToUnixTimeSeconds(),
        };
        return JwsEs256.Firmar(clave ?? _clave, Kid, ajustar is null ? reclamos : ajustar(reclamos));
    }

    private async Task<(HttpStatusCode Estado, System.Text.Json.JsonElement Cuerpo)> Quien(string? cabecera, params (string, string)[] otras)
    {
        using var solicitud = new HttpRequestMessage(HttpMethod.Get, "/quien");
        if (cabecera is not null)
        {
            solicitud.Headers.TryAddWithoutValidation("Authorization", cabecera);
        }

        foreach (var (nombre, valor) in otras)
        {
            solicitud.Headers.TryAddWithoutValidation(nombre, valor);
        }

        var respuesta = await _cliente.SendAsync(solicitud);
        using var documento = System.Text.Json.JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
        return (respuesta.StatusCode, documento.RootElement.Clone());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-09")]
    public async Task UnaCredencialValida_FijaElActorDelUsuarioConSuRolYSucursal_YElTokenNoSigueElPipeline()
    {
        await using var e = await Crear();
        var sucursal = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var token = e.Token(r =>
        {
            r["sub"] = usuario.ToString("D");
            r["rol"] = RolesDeUsuario.EncargadoDeSucursal;
            r["sucursal"] = sucursal.ToString("D");
            return r;
        });

        var (estado, cuerpo) = await e.Quien("Bearer " + token);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(usuario, cuerpo.GetProperty("actor").GetGuid());
        Assert.Equal(RolesDeUsuario.EncargadoDeSucursal, cuerpo.GetProperty("rol").GetString());
        Assert.Equal(sucursal, cuerpo.GetProperty("sucursal").GetGuid());
        Assert.Equal(string.Empty, cuerpo.GetProperty("autorizacion").GetString());
    }

    [Theory]
    [InlineData("sin-cabecera")]
    [InlineData("esquema-basico")]
    [InlineData("token-roto")]
    [InlineData("otra-firma")]
    [InlineData("solo-audiencia-ecommerce")]
    [InlineData("contexto-de-lectura")]
    [InlineData("vencida")]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task UnaCredencialQueNoSirve_DejaLaSolicitudSinActor(string caso)
    {
        await using var e = await Crear();
        using var otraClave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        string? cabecera = caso switch
        {
            "sin-cabecera" => null,
            "esquema-basico" => "Basic dXN1YXJpbzpjbGF2ZQ==",
            "token-roto" => "Bearer no.es.token",
            "otra-firma" => "Bearer " + e.Token(clave: otraClave),
            "solo-audiencia-ecommerce" => "Bearer " + e.Token(r => { r["aud"] = new[] { CredencialDeAcceso.AudienciaEcommerce }; return r; }),
            "contexto-de-lectura" => "Bearer " + e.Token(r => { r["aud"] = CredencialDeAcceso.AudienciaDePedidos; r["typ"] = CredencialDeAcceso.TipoDelContextoDeLectura; return r; }),
            _ => "Bearer " + e.Token(r => { r["nbf"] = Ahora.AddHours(-2).ToUnixTimeSeconds(); r["exp"] = Ahora.AddHours(-1).ToUnixTimeSeconds(); return r; }),
        };

        var (estado, cuerpo) = await e.Quien(cabecera);

        Assert.Equal(HttpStatusCode.OK, estado);
        Assert.Equal(System.Text.Json.JsonValueKind.Null, cuerpo.GetProperty("actor").ValueKind);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task UnaCabeceraDeActor_NuncaVale_ConOSinCredencial()
    {
        await using var e = await Crear();

        var (_, sinToken) = await e.Quien(null, ("X-Prueba-Usuario", Guid.NewGuid().ToString()), ("X-Prueba-Rol", "administrador-central"), ("X-Usuario-Id", Guid.NewGuid().ToString()));

        Assert.Equal(System.Text.Json.JsonValueKind.Null, sinToken.GetProperty("actor").ValueKind);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task SiNoHayCopiaDeClavesVigente_TodaCredencialSeRechaza()
    {
        await using var e = await Crear();
        var cache = new CacheDeClavesDelInventario(new ObtenedorFijo("{\"keys\":[]}", "{\"revocadas\":[],\"generadoEn\":0}"));
        Assert.False(await cache.Renovar());
        var verificador = new VerificadorDeCredencialesDelInventario(cache);

        Assert.Equal(EstadoDeLaCredencial.SinClavesVigentes, verificador.Verificar(e.Token(), Ahora).Estado);
    }

    public async ValueTask DisposeAsync()
    {
        _cliente?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _clave.Dispose();
    }
}
