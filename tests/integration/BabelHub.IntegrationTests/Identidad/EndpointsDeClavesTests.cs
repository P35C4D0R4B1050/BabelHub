using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using BabelHub.Ecommerce.Api.Identidad;
using BabelHub.Ecommerce.Api.Plataforma;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.Ecommerce.Infrastructure.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using BabelHub.Shared.Contratos.Ecommerce;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using EstadoDeLaCredencial = BabelHub.Inventario.Application.Plataforma.EstadoDeLaCredencial;
using ActorDelFlujo = BabelHub.Ecommerce.Infrastructure.Plataforma.ActorDelFlujo;
using IReloj = BabelHub.Ecommerce.Application.Plataforma.IReloj;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// Los endpoints de claves de Identidad (identidad-seguridad T44 reducido; AC-IDE-09, AC-IDE-32, AC-IDE-33, AC-IDE-64; IDE-35, IDE-36, IDE-61, IDE-85): la publicación del JWKS y de las revocaciones y la administración de
/// claves, con un TestServer. Incluye el recorrido completo con el verificador del Inventario leyendo <b>estos mismos endpoints</b> (no una copia de sus documentos) y la comprobación de que ningún endpoint de Identidad
/// devuelve una credencial ni una renovación (AGENTS.md §3.9).
/// </summary>
public sealed class EndpointsDeClavesTests : IAsyncDisposable
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class RelojFijo : IReloj
    {
        public DateTimeOffset Instante { get; set; } = EndpointsDeClavesTests.Ahora;

        public DateTimeOffset Ahora => Instante;

        public DateOnly HoyEnLima => DateOnly.FromDateTime(Instante.UtcDateTime.AddHours(-5));
    }

    private sealed class ClavesDeFirmaFalsas : IClavesDeFirma
    {
        public List<(string Kid, ECDsa Clave)> Claves { get; } = [];

        public List<string> KidsRevocados { get; } = [];

        public bool Caida { get; set; }

        public List<(Guid Administrador, string? Kid)> Llamadas { get; } = [];

        public Task<Resultado<ClavePublicaDeFirma>> Rotar(Guid administradorId, CancellationToken cancelacion = default)
        {
            Llamadas.Add((administradorId, null));
            return Task.FromResult(administradorId == Administrador
                ? Resultado<ClavePublicaDeFirma>.Exito(new ClavePublicaDeFirma("kid-nuevo", "ES256", "{\"x\":\"no-debe-salir\"}", "activa"))
                : Resultado<ClavePublicaDeFirma>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "no-es-administrador-central")));
        }

        public Task<Resultado<ClavePublicaDeFirma>> DeclararComprometida(Guid administradorId, string kid, CancellationToken cancelacion = default)
        {
            Llamadas.Add((administradorId, kid));
            if (administradorId != Administrador)
            {
                return Task.FromResult(Resultado<ClavePublicaDeFirma>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Prohibido, "no-es-administrador-central")));
            }

            KidsRevocados.Add(kid);
            return Task.FromResult(Resultado<ClavePublicaDeFirma>.Exito(new ClavePublicaDeFirma(kid, "ES256", "{\"x\":\"no-debe-salir\"}", "comprometida")));
        }

        public Task<IReadOnlyList<ClavePublicaDeFirma>> Publicadas(CancellationToken cancelacion = default) =>
            Caida
                ? throw new InvalidOperationException("La base no responde.")
                : Task.FromResult<IReadOnlyList<ClavePublicaDeFirma>>([.. Claves.Select(c => new ClavePublicaDeFirma(c.Kid, "ES256", JwkEs256.Publico(c.Clave, c.Kid), "activa"))]);


        public Task<IReadOnlyList<string>> Revocadas(CancellationToken cancelacion = default) =>
            Caida ? throw new InvalidOperationException("La base no responde.") : Task.FromResult<IReadOnlyList<string>>([.. KidsRevocados]);

        public Guid Administrador { get; } = Guid.NewGuid();
    }

    private sealed class DenegacionesGuardadas : IRegistradorDeDenegacionesHttp
    {
        public List<(CategoriaDeError Categoria, string? Motivo)> Registradas { get; } = [];

        public Task Registrar(HttpContext contexto, CategoriaDeError categoria, string? motivo, string? codigoDeError)
        {
            Registradas.Add((categoria, motivo));
            return Task.CompletedTask;
        }
    }

    private sealed class ClaveEnMemoria(string kid, ECDsa clave) : IClavePrivadaDeFirma
    {
        public Task<ClavePrivadaActiva> Activa(CancellationToken cancelacion = default) => Task.FromResult(new ClavePrivadaActiva(kid, clave));
    }

    private WebApplication _app = null!;
    private readonly ClavesDeFirmaFalsas _claves = new();
    private readonly DenegacionesGuardadas _denegaciones = new();
    private readonly RelojFijo _reloj = new();
    private readonly ActorDelFlujo _actor = new();
    private readonly ECDsa _clavePrivada = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private HttpClient _cliente = null!;

    private async Task Iniciar()
    {
        _claves.Claves.Add(("kid-activo", _clavePrivada));
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(o =>
        {
            o.ValidateScopes = true;
            o.ValidateOnBuild = true;
        });
        builder.Services.AgregarManejoDeErrores(new OpcionesDeErrores("ECO"));
        builder.Services.AddSingleton<IActorActual>(_actor);
        builder.Services.AddSingleton<IEstablecedorDeActor>(_actor);
        builder.Services.AddSingleton<IReloj>(_reloj);
        builder.Services.AddSingleton<IClavesDeFirma>(_claves);
        builder.Services.AddSingleton<IRegistradorDeDenegacionesHttp>(_denegaciones);
        builder.Services.AddAntiforgery();
        builder.Services.AddSingleton(new BabelHub.Ecommerce.Api.Web.OpcionesDelHostWeb(BabelHub.Ecommerce.Application.Web.HostWeb.Gestion, []));
        _app = builder.Build();
        _app.UsarManejoDeErrores();
        _app.Use(async (contexto, siguiente) =>
        {
            _actor.Establecer(ActorActual.Anonimo());
            var rol = contexto.Request.Headers["X-Prueba-Rol"].ToString();
            if (rol.Length > 0 && Guid.TryParse(contexto.Request.Headers["X-Prueba-Usuario"], out var usuario))
            {
                _actor.Establecer(ActorActual.DeUsuario(usuario, rol, null));
            }

            await siguiente();
        });
        _app.MapearClavesDeIdentidad();
        _app.MapGet("/prueba/token", (HttpContext c, Microsoft.AspNetCore.Antiforgery.IAntiforgery a) => Results.Json(new { token = a.GetAndStoreTokens(c).RequestToken }));
        await _app.StartAsync();
        _cliente = _app.GetTestClient();
    }

    private static async Task<EndpointsDeClavesTests> Crear()
    {
        var e = new EndpointsDeClavesTests();
        await e.Iniciar();
        return e;
    }

    /// <summary>Una solicitud de administración con la cookie y el token antifalsificación que la web entregaría (<paramref name="conToken"/> = falso para probar el rechazo).</summary>
    private async Task<HttpRequestMessage> Admin(string ruta, object? cuerpo = null, Guid? usuario = null, string rol = RolesDeUsuario.AdministradorCentral, bool conToken = true)
    {
        var solicitud = new HttpRequestMessage(HttpMethod.Post, ruta) { Content = cuerpo is null ? null : JsonContent.Create(cuerpo) };
        solicitud.Headers.Add("X-Prueba-Rol", rol);
        solicitud.Headers.Add("X-Prueba-Usuario", (usuario ?? _claves.Administrador).ToString());
        if (conToken)
        {
            var respuesta = await _cliente.GetAsync("/prueba/token");
            using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());
            solicitud.Headers.Add("RequestVerificationToken", documento.RootElement.GetProperty("token").GetString());
            solicitud.Headers.Add("Cookie", string.Join("; ", respuesta.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
        }

        return solicitud;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task SinElTokenAntifalsificacion_ElPostDeAdministracionDa403_YNoLlegaALaAplicacion()
    {
        await using var e = await Crear();

        var respuesta = await e._cliente.SendAsync(await e.Admin(RutasDeClavesDeFirma.Rotar, conToken: false));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Empty(e._claves.Llamadas);
        Assert.Contains(e._denegaciones.Registradas, d => d.Motivo == "token-antifalsificacion-invalido");
    }

    [Theory]
    [InlineData("Origin", "https://otro-sitio.example")]
    [InlineData("Sec-Fetch-Site", "cross-site")]
    [InlineData("Sec-Fetch-Site", "same-site")]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task UnPostDeAdministracionDeOtroOrigenOSitio_Da403_AunConToken(string cabecera, string valor)
    {
        await using var e = await Crear();
        var solicitud = await e.Admin(RutasDeClavesDeFirma.Rotar);
        solicitud.Headers.Add(cabecera, valor);

        var respuesta = await e._cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Empty(e._claves.Llamadas);
        Assert.Contains(e._denegaciones.Registradas, d => d.Motivo == "origen-no-permitido");
    }

    [Fact]
    public async Task UnCuerpoDeMasDeUnKiB_NoLlegaALaAplicacion()
    {
        await using var e = await Crear();

        var respuesta = await e._cliente.SendAsync(await e.Admin(RutasDeClavesDeFirma.Comprometida, new DeclararClaveComprometidaDto(new string('k', 2000))));

        Assert.NotEqual(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Empty(e._claves.Llamadas);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task ElJwks_PublicaSoloClavesPublicas_ConLosCamposDelJwk()
    {
        await using var e = await Crear();

        var respuesta = await e._cliente.GetAsync(RutasDeClavesDeFirma.Jwks);
        var texto = await respuesta.Content.ReadAsStringAsync();
        using var documento = JsonDocument.Parse(texto);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Contains("application/json", respuesta.Content.Headers.ContentType!.ToString(), StringComparison.Ordinal);
        var llave = Assert.Single(documento.RootElement.GetProperty("keys").EnumerateArray());
        Assert.Equal("kid-activo", llave.GetProperty("kid").GetString());
        Assert.Equal("EC", llave.GetProperty("kty").GetString());
        Assert.Equal("P-256", llave.GetProperty("crv").GetString());
        Assert.False(llave.TryGetProperty("d", out _));
        Assert.DoesNotContain("no-debe-salir", texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task LasRevocaciones_LlevanLosKidComprometidos_YElInstanteDeGeneracion_SinCache()
    {
        await using var e = await Crear();
        e._claves.KidsRevocados.Add("kid-viejo");

        var respuesta = await e._cliente.GetAsync(RutasDeClavesDeFirma.Revocaciones);
        using var documento = JsonDocument.Parse(await respuesta.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(["kid-viejo"], documento.RootElement.GetProperty("revocadas").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(Ahora.ToUnixTimeSeconds(), documento.RootElement.GetProperty("generadoEn").GetInt64());
        Assert.Contains("no-store", respuesta.Headers.CacheControl!.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("jwks")]
    [InlineData("revocaciones")]
    [Trait("Criterio", "AC-IDE-64")]
    public async Task SiLaBaseNoResponde_LaPublicacionDa503_SinDatosInternos(string cual)
    {
        await using var e = await Crear();
        e._claves.Caida = true;

        var respuesta = await e._cliente.GetAsync(cual == "jwks" ? RutasDeClavesDeFirma.Jwks : RutasDeClavesDeFirma.Revocaciones);
        var texto = await respuesta.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, respuesta.StatusCode);
        Assert.DoesNotContain("La base no responde", texto, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-09")]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task ElInventario_VerificaCredencialesConLosDocumentosQueSirveIdentidad_YRechazaTrasLaRevocacion()
    {
        await using var e = await Crear();
        var emisor = new Es256EmisorDeCredenciales(new ClaveEnMemoria("kid-activo", e._clavePrivada));
        var credencial = (await emisor.Emitir(new BabelHub.Ecommerce.Application.Identidad.Interno.DatosDeLaCredencial(Guid.NewGuid(), BabelHub.Ecommerce.Domain.Identidad.RolDeUsuario.Cliente, null, true), Ahora)).Jwt;
        var opciones = new OpcionesDeClavesPublicadas(new Uri("http://localhost/.well-known/jwks.json"), new Uri("http://localhost/.well-known/revocaciones"));
        var obtenedor = new HttpObtenedorDeClavesPublicadas(e._app.GetTestServer().CreateClient(), opciones);
        var tiempo = new RelojDeTiempo(Ahora);
        var cache = new CacheDeClavesDelInventario(obtenedor, tiempo);
        var verificador = new VerificadorDeCredencialesDelInventario(cache);

        Assert.True(await cache.Renovar());
        Assert.Equal(EstadoDeLaCredencial.Valida, verificador.Verificar(credencial, Ahora).Estado);

        // El administrador declara comprometida la clave: la siguiente renovación de la caché la rechaza.
        var declaracion = await e._cliente.SendAsync(await e.Admin(RutasDeClavesDeFirma.Comprometida, new DeclararClaveComprometidaDto("kid-activo")));
        Assert.Equal(HttpStatusCode.OK, declaracion.StatusCode);
        tiempo.Ahora = Ahora.AddMinutes(1);
        e._reloj.Instante = tiempo.Ahora;
        Assert.True(await cache.Renovar());
        Assert.Equal(EstadoDeLaCredencial.Invalida, verificador.Verificar(credencial, tiempo.Ahora).Estado);
    }

    private sealed class RelojDeTiempo(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task Rotar_ConElAdministrador_DevuelveSoloElKidElAlgoritmoYElEstado()
    {
        await using var e = await Crear();

        var respuesta = await e._cliente.SendAsync(await e.Admin(RutasDeClavesDeFirma.Rotar));
        var texto = await respuesta.Content.ReadAsStringAsync();
        using var documento = JsonDocument.Parse(texto);

        Assert.Equal(HttpStatusCode.OK, respuesta.StatusCode);
        Assert.Equal(["algoritmo", "estado", "kid"], documento.RootElement.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.DoesNotContain("no-debe-salir", texto, StringComparison.Ordinal);
        Assert.Equal(e._claves.Administrador, Assert.Single(e._claves.Llamadas).Administrador);
    }

    [Theory]
    [InlineData("rotar")]
    [InlineData("comprometida")]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task SinSesion_ElAdministradorDeClaves_Da401_YNoLlegaALaAplicacion(string cual)
    {
        await using var e = await Crear();
        var ruta = cual == "rotar" ? RutasDeClavesDeFirma.Rotar : RutasDeClavesDeFirma.Comprometida;

        var solicitud = await e.Admin(ruta, new DeclararClaveComprometidaDto("kid-activo"));
        solicitud.Headers.Remove("X-Prueba-Rol");
        solicitud.Headers.Remove("X-Prueba-Usuario");
        var respuesta = await e._cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.Unauthorized, respuesta.StatusCode);
        Assert.Empty(e._claves.Llamadas);
        Assert.Contains(e._denegaciones.Registradas, d => d.Categoria == CategoriaDeError.NoAutenticado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task UnUsuarioQueNoEsAdministradorCentral_Recibe403_ElRechazoSeRegistra_YNoSeDeclaraNada()
    {
        await using var e = await Crear();

        var respuesta = await e._cliente.SendAsync(await e.Admin(RutasDeClavesDeFirma.Comprometida, new DeclararClaveComprometidaDto("kid-activo"), usuario: Guid.NewGuid(), rol: RolesDeUsuario.EncargadoDeSucursal));

        Assert.Equal(HttpStatusCode.Forbidden, respuesta.StatusCode);
        Assert.Empty(e._claves.KidsRevocados);
        Assert.Contains(e._denegaciones.Registradas, d => d.Categoria == CategoriaDeError.Prohibido);
    }

    [Theory]
    [InlineData("{\"kid\":\"k\",\"extra\":1}")]
    [InlineData("{}")]
    [InlineData("no es json")]
    [InlineData("")]
    public async Task UnCuerpoInvalidoOConCamposDeMas_Da400_YNoLlegaALaAplicacion(string cuerpo)
    {
        await using var e = await Crear();
        var solicitud = await e.Admin(RutasDeClavesDeFirma.Comprometida);
        solicitud.Content = new StringContent(cuerpo, System.Text.Encoding.UTF8, "application/json");

        var respuesta = await e._cliente.SendAsync(solicitud);

        Assert.Equal(HttpStatusCode.BadRequest, respuesta.StatusCode);
        Assert.Empty(e._claves.Llamadas);
    }

    [Fact]
    public async Task LosVerbosNoPrevistos_NoSeAtienden()
    {
        await using var e = await Crear();

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await e._cliente.PostAsync(RutasDeClavesDeFirma.Jwks, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await e._cliente.PostAsync(RutasDeClavesDeFirma.Revocaciones, null)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await e._cliente.GetAsync(RutasDeClavesDeFirma.Rotar)).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await e._cliente.GetAsync(RutasDeClavesDeFirma.Comprometida)).StatusCode);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task NingunEndpointJsonDeIdentidad_PublicaElIngresoLaSesionLaCuentaNiLasRenovaciones()
    {
        await using var e = await Crear();

        var rutas = ((IEndpointRouteBuilder)e._app).DataSources.SelectMany(d => d.Endpoints).OfType<RouteEndpoint>()
            .Select(r => r.RoutePattern.RawText!)
            .Where(r => !r.StartsWith("/prueba", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        // La lista es cerrada: agregar un endpoint de Identidad obliga a revisar este caso (el JWT y la renovación no llegan al navegador, AGENTS.md §3.9).
        Assert.Equal(
            new[] { RutasDeClavesDeFirma.Comprometida, RutasDeClavesDeFirma.Jwks, RutasDeClavesDeFirma.Revocaciones, RutasDeClavesDeFirma.Rotar }.Order(StringComparer.Ordinal),
            rutas.Order(StringComparer.Ordinal));
        Assert.DoesNotContain(rutas, r => r.Contains("ingreso", StringComparison.OrdinalIgnoreCase) || r.Contains("sesion", StringComparison.OrdinalIgnoreCase) || r.Contains("renovar", StringComparison.OrdinalIgnoreCase) || r.Contains("cuenta", StringComparison.OrdinalIgnoreCase));
    }

    public async ValueTask DisposeAsync()
    {
        _cliente?.Dispose();
        if (_app is not null)
        {
            await _app.DisposeAsync();
        }

        _clavePrivada.Dispose();
    }
}
