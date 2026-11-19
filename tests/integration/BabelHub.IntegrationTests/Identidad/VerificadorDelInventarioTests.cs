using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Infrastructure.Plataforma;
using BabelHub.Shared.Contratos;
using Base64Url = BabelHub.Ecommerce.Infrastructure.Identidad.Base64Url;
using ClavePrivadaActiva = BabelHub.Ecommerce.Infrastructure.Identidad.ClavePrivadaActiva;
using DatosDeLaCredencial = BabelHub.Ecommerce.Application.Identidad.Interno.DatosDeLaCredencial;
using DocumentosDeClavesPublicadas = BabelHub.Ecommerce.Infrastructure.Identidad.DocumentosDeClavesPublicadas;
using Es256EmisorDeCredenciales = BabelHub.Ecommerce.Infrastructure.Identidad.Es256EmisorDeCredenciales;
using IClavePrivadaDeFirma = BabelHub.Ecommerce.Infrastructure.Identidad.IClavePrivadaDeFirma;
using JwkEs256 = BabelHub.Ecommerce.Infrastructure.Identidad.JwkEs256;
using JwsEs256 = BabelHub.Ecommerce.Infrastructure.Identidad.JwsEs256;
using RolDeUsuario = BabelHub.Ecommerce.Domain.Identidad.RolDeUsuario;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// El verificador de credenciales del Inventario (identidad-seguridad T41; AC-IDE-09, AC-IDE-32, AC-IDE-33, AC-IDE-64; IDE-10, IDE-35, IDE-61, IDE-85): lee lo que firma Identidad con solo la clave pública publicada, rechaza el
/// contexto de lectura y toda credencial de otra audiencia, sigue las revocaciones y <b>rechaza toda credencial si la copia de claves tiene más de 5 minutos</b>. Las credenciales las firma el emisor real del E-commerce,
/// de modo que la prueba comprueba también que los dos servicios entienden el mismo formato.
/// </summary>
public sealed class VerificadorDelInventarioTests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class ClaveEnMemoria(string kid, ECDsa clave) : IClavePrivadaDeFirma
    {
        public Task<ClavePrivadaActiva> Activa(CancellationToken cancelacion = default) => Task.FromResult(new ClavePrivadaActiva(kid, clave));
    }

    private sealed class RelojManual(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    /// <summary>La réplica de gestión simulada: sirve lo que se le indique, o falla.</summary>
    private sealed class ReplicaSimulada(RelojManual reloj) : IObtenedorDeClavesPublicadas
    {
        public List<(string Kid, ECDsa Clave)> Claves { get; } = [];

        public List<string> Revocadas { get; } = [];

        public bool Caida { get; set; }

        public string? JwksForzado { get; set; }

        public DateTimeOffset? RevocacionesGeneradasEn { get; set; }

        public Task<string> ObtenerJwks(CancellationToken cancelacion = default)
        {
            if (Caida)
            {
                throw new HttpRequestException("La réplica no responde.");
            }

            var llaves = Claves.Select(c => JsonDocument.Parse(JwkEs256.Publico(c.Clave, c.Kid)).RootElement.Clone()).ToList();
            return Task.FromResult(JwksForzado ?? JsonSerializer.Serialize(new Dictionary<string, object> { ["keys"] = llaves }));
        }

        public Task<string> ObtenerRevocaciones(CancellationToken cancelacion = default)
        {
            if (Caida)
            {
                throw new HttpRequestException("La réplica no responde.");
            }

            return Task.FromResult(DocumentosDeClavesPublicadas.Revocaciones(Revocadas, RevocacionesGeneradasEn ?? reloj.GetUtcNow()));
        }
    }

    private sealed class Escenario
    {
        public Escenario(string kid = "kid-de-prueba-uno")
        {
            Kid = kid;
            Clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Reloj = new RelojManual(Ahora);
            Replica = new ReplicaSimulada(Reloj);
            Replica.Claves.Add((kid, Clave));
            Cache = new CacheDeClavesDelInventario(Replica, Reloj);
            Verificador = new VerificadorDeCredencialesDelInventario(Cache);
            Emisor = new Es256EmisorDeCredenciales(new ClaveEnMemoria(kid, Clave));
        }

        public string Kid { get; }

        public ECDsa Clave { get; }

        public RelojManual Reloj { get; }

        public ReplicaSimulada Replica { get; }

        public CacheDeClavesDelInventario Cache { get; }

        public VerificadorDeCredencialesDelInventario Verificador { get; }

        public Es256EmisorDeCredenciales Emisor { get; }

        public async Task<string> Credencial(RolDeUsuario rol = RolDeUsuario.Cliente, Guid? sucursal = null) =>
            (await Emisor.Emitir(new DatosDeLaCredencial(Guid.NewGuid(), rol, sucursal, true), Reloj.GetUtcNow())).Jwt;

        public string Firmar(Dictionary<string, object?> reclamos, string? kid = null) => JwsEs256.Firmar(Clave, kid ?? Kid, reclamos);

        public Dictionary<string, object?> Reclamos(object? aud = null, string? tipo = null)
        {
            var desde = Reloj.GetUtcNow();
            var reclamos = new Dictionary<string, object?>
            {
                ["iss"] = CredencialDeAcceso.Emisor,
                ["sub"] = Guid.NewGuid().ToString("D"),
                ["aud"] = aud ?? new[] { CredencialDeAcceso.AudienciaEcommerce, CredencialDeAcceso.AudienciaInventario },
                ["rol"] = RolesDeUsuario.Cliente,
                ["iat"] = desde.ToUnixTimeSeconds(),
                ["nbf"] = desde.ToUnixTimeSeconds(),
                ["exp"] = (desde + TimeSpan.FromMinutes(15)).ToUnixTimeSeconds(),
            };
            if (tipo is not null)
            {
                reclamos["typ"] = tipo;
            }

            return reclamos;
        }
    }

    private static async Task<Escenario> ConClavesRenovadas()
    {
        var e = new Escenario();
        Assert.True(await e.Cache.Renovar());
        return e;
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-09")]
    public async Task UnaCredencialFirmadaPorIdentidad_SeVerificaConLaClavePublicada_SinLlamarAIdentidad()
    {
        var e = await ConClavesRenovadas();
        var sucursal = Guid.NewGuid();

        var leida = e.Verificador.Verificar(await e.Credencial(RolDeUsuario.EncargadoDeSucursal, sucursal), Ahora);

        Assert.Equal(EstadoDeLaCredencial.Valida, leida.Estado);
        Assert.Equal(RolesDeUsuario.EncargadoDeSucursal, leida.Rol);
        Assert.Equal(sucursal, leida.SucursalId);
        Assert.NotNull(leida.UsuarioId);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-09")]
    public async Task UnaCredencialVencida_SeDistingueDeUnaInvalida()
    {
        var e = await ConClavesRenovadas();
        var credencial = await e.Credencial();

        // Dentro de la vigencia de la copia de claves solo cabe vencer si la credencial es más corta que la copia: se simula con una copia renovada después.
        e.Reloj.Ahora = Ahora.AddMinutes(16);
        Assert.True(await e.Cache.Renovar());

        Assert.Equal(EstadoDeLaCredencial.Vencida, e.Verificador.Verificar(credencial, e.Reloj.Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task ElContextoDeLecturaDelAsistente_NoEsUnaCredencial_NiConSuAudienciaNiConSuTipo()
    {
        var e = await ConClavesRenovadas();
        var contexto = e.Firmar(e.Reclamos(aud: CredencialDeAcceso.AudienciaDePedidos, tipo: CredencialDeAcceso.TipoDelContextoDeLectura));
        var conLasDosAudienciasPeroTipo = e.Firmar(e.Reclamos(tipo: CredencialDeAcceso.TipoDelContextoDeLectura));

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(contexto, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(conLasDosAudienciasPeroTipo, Ahora).Estado);
    }

    [Theory]
    [InlineData("babelhub-ecommerce")]
    [InlineData("otro-servicio")]
    [InlineData("")]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task UnaCredencialSinLaAudienciaDelInventario_SeRechaza(string audiencia)
    {
        var e = await ConClavesRenovadas();

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(e.Firmar(e.Reclamos(aud: audiencia)), Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(e.Firmar(e.Reclamos(aud: new[] { audiencia })), Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task UnaFirmaDeOtraClave_UnKidDesconocido_ElAlgoritmoNoneYUnaCredencialAlterada_SeRechazan()
    {
        var e = await ConClavesRenovadas();
        var credencial = await e.Credencial();
        using var otra = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var firmadaPorOtra = JwsEs256.Firmar(otra, e.Kid, e.Reclamos());
        var kidDesconocido = e.Firmar(e.Reclamos(), kid: "kid-que-nadie-publico");
        var sinFirma = $"{Base64Url.Codificar(Encoding.UTF8.GetBytes("{\"alg\":\"none\",\"kid\":\"" + e.Kid + "\"}"))}.{Base64Url.Codificar(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e.Reclamos())))}.{Base64Url.Codificar(new byte[64])}";
        var partes = credencial.Split('.');
        var carga = Base64Url.Codificar(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(e.Reclamos())));
        var alterada = $"{partes[0]}.{carga}.{partes[2]}";

        foreach (var invalida in new[] { firmadaPorOtra, kidDesconocido, sinFirma, alterada, "no-es-un-token", string.Empty, new string('a', 5000) })
        {
            Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(invalida, Ahora).Estado);
        }

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(null, Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task UnaClaveDeclaradaComprometida_SeRechazaAlRenovarLaCopia_ElSiguienteCiclo()
    {
        var e = await ConClavesRenovadas();
        var credencial = await e.Credencial();
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(credencial, Ahora).Estado);

        e.Replica.Revocadas.Add(e.Kid);
        e.Reloj.Ahora = Ahora.AddMinutes(1);
        Assert.True(await e.Cache.Renovar());

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(credencial, e.Reloj.Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task LasCredencialesDeLaClaveAnterior_SiguenValidasMientrasSigaPublicada()
    {
        var e = await ConClavesRenovadas();
        var credencialAnterior = await e.Credencial();
        using var nueva = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        e.Replica.Claves.Add(("kid-de-prueba-dos", nueva));
        e.Reloj.Ahora = Ahora.AddMinutes(1);
        Assert.True(await e.Cache.Renovar());
        var credencialNueva = JwsEs256.Firmar(nueva, "kid-de-prueba-dos", e.Reclamos());

        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(credencialAnterior, e.Reloj.Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(credencialNueva, e.Reloj.Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task SinRenovarLaCopiaEn5Minutos_SeRechazaTodaCredencial()
    {
        var e = await ConClavesRenovadas();
        var credencial = await e.Credencial();

        e.Reloj.Ahora = Ahora.AddMinutes(4).AddSeconds(59);
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(credencial, e.Reloj.Ahora).Estado);

        e.Reloj.Ahora = Ahora.AddMinutes(5).AddSeconds(1);
        Assert.Equal(EstadoDeLaCredencial.SinClavesVigentes, e.Verificador.Verificar(credencial, e.Reloj.Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public void AntesDeLaPrimeraRenovacion_SeRechazaTodaCredencial()
    {
        var e = new Escenario();

        Assert.Equal(EstadoDeLaCredencial.SinClavesVigentes, e.Verificador.Verificar("cualquiera", Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-64")]
    public async Task ConLaVentasCaida_LaReplicaDeGestionSigueSirviendo_YSiTampocoResponde_SeRechazaAlVencerLos5Minutos()
    {
        var e = await ConClavesRenovadas();
        var credencial = await e.Credencial();

        // VM-VENTAS caída: la réplica de la propia VM del Inventario sigue respondiendo y la copia se renueva.
        e.Reloj.Ahora = Ahora.AddMinutes(3);
        Assert.True(await e.Cache.Renovar());
        e.Reloj.Ahora = Ahora.AddMinutes(7);
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(credencial, e.Reloj.Ahora).Estado);

        // La réplica también cae: la copia anterior vale hasta cumplir 5 minutos y luego se rechaza todo.
        e.Replica.Caida = true;
        e.Reloj.Ahora = Ahora.AddMinutes(8);
        Assert.False(await e.Cache.Renovar());
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(credencial, e.Reloj.Ahora).Estado);
        e.Reloj.Ahora = Ahora.AddMinutes(8).AddSeconds(3 * 100);
        Assert.False(await e.Cache.Renovar());
        Assert.Equal(EstadoDeLaCredencial.SinClavesVigentes, e.Verificador.Verificar(credencial, e.Reloj.Ahora).Estado);

        // Al volver la réplica, la renovación restablece el servicio.
        e.Replica.Caida = false;
        Assert.True(await e.Cache.Renovar());
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(credencial, e.Reloj.Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task UnaRespuestaViciada_NoReemplazaLaCopiaAnterior()
    {
        var e = await ConClavesRenovadas();
        var anterior = e.Cache.Actual;

        e.Replica.RevocacionesGeneradasEn = Ahora.AddMinutes(-10);
        Assert.False(await e.Cache.Renovar());
        e.Replica.RevocacionesGeneradasEn = Ahora.AddMinutes(10);
        Assert.False(await e.Cache.Renovar());
        e.Replica.RevocacionesGeneradasEn = null;
        e.Replica.JwksForzado = "{\"keys\":[]}";
        Assert.False(await e.Cache.Renovar());
        e.Replica.JwksForzado = "esto no es json";
        Assert.False(await e.Cache.Renovar());

        Assert.Same(anterior, e.Cache.Actual);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task UnJwkConClavePrivada_OConOtraCurvaOAlgoritmo_NoSeAcepta()
    {
        var e = new Escenario();
        using var p384 = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var bueno = JsonDocument.Parse(JwkEs256.Publico(e.Clave, e.Kid)).RootElement.Clone();
        var conPrivada = JsonSerializer.Serialize(new Dictionary<string, object> { ["keys"] = new[] { JsonSerializer.Deserialize<Dictionary<string, string>>(bueno.GetRawText())!.Append(new KeyValuePair<string, string>("d", "AAAA")).ToDictionary(x => x.Key, x => x.Value) } });
        var otraCurva = JsonSerializer.Serialize(new Dictionary<string, object> { ["keys"] = new[] { new Dictionary<string, string> { ["kty"] = "EC", ["crv"] = "P-384", ["x"] = "AAAA", ["y"] = "AAAA", ["kid"] = "k" } } });
        var otroAlgoritmo = JsonSerializer.Serialize(new Dictionary<string, object> { ["keys"] = new[] { JsonSerializer.Deserialize<Dictionary<string, string>>(bueno.GetRawText())!.ToDictionary(x => x.Key, x => x.Key == "alg" ? "ES384" : x.Value) } });

        foreach (var jwks in new[] { conPrivada, otraCurva, otroAlgoritmo })
        {
            e.Replica.JwksForzado = jwks;
            Assert.False(await e.Cache.Renovar());
            Assert.Null(e.Cache.Actual);
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public void ElInventario_NoTieneCodigoParaEmitirNiFirmarCredenciales()
    {
        var ensamblados = new[] { typeof(VerificadorDeCredencialesDelInventario).Assembly, typeof(IVerificadorDeCredenciales).Assembly };
        var nombres = new System.Text.RegularExpressions.Regex("(Emisor|Emitir|Firmar|Firmador|Sign).*Credencial|Credencial.*(Emisor|Emitir|Firmar|Firmador|Sign)|PrivateKey|ClavePrivada", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        const System.Reflection.BindingFlags Todos = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly;

        var encontrados = ensamblados.SelectMany(a => a.GetTypes())
            .SelectMany(t => new[] { t.Name }.Concat(t.GetMethods(Todos).Select(m => $"{t.Name}.{m.Name}")).Concat(t.GetProperties(Todos).Select(p => $"{t.Name}.{p.Name}")))
            .Where(n => nombres.IsMatch(n))
            .ToList();

        Assert.Empty(encontrados);
    }

    private sealed class ManejadorFijo(Func<HttpRequestMessage, HttpResponseMessage> respuesta) : HttpMessageHandler
    {
        public List<Uri> Pedidas { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Pedidas.Add(request.RequestUri!);
            var devuelta = respuesta(request);
            devuelta.RequestMessage = request;
            return Task.FromResult(devuelta);
        }
    }

    private static HttpObtenedorDeClavesPublicadas Cliente(ManejadorFijo manejador, OpcionesDeClavesPublicadas? opciones = null) =>
        new(new HttpClient(manejador), opciones ?? new OpcionesDeClavesPublicadas(new Uri("http://replica.interna/.well-known/jwks.json"), new Uri("http://replica.interna/.well-known/revocaciones")));

    [Fact]
    [Trait("Criterio", "AC-IDE-64")]
    public async Task ElClienteHttp_PideSoloLasDireccionesConfiguradas_YDevuelveElDocumento()
    {
        var manejador = new ManejadorFijo(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"keys\":[]}") });
        var cliente = Cliente(manejador);

        Assert.Equal("{\"keys\":[]}", await cliente.ObtenerJwks());
        await cliente.ObtenerRevocaciones();

        Assert.Equal(["http://replica.interna/.well-known/jwks.json", "http://replica.interna/.well-known/revocaciones"], manejador.Pedidas.Select(u => u.ToString()));
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Found)]
    [InlineData(HttpStatusCode.NoContent)]
    [Trait("Criterio", "AC-IDE-64")]
    public async Task UnaRespuestaQueNoEsUn200_FallaYLaCopiaNoSeReemplaza(HttpStatusCode codigo)
    {
        var cliente = Cliente(new ManejadorFijo(_ => new HttpResponseMessage(codigo)));

        await Assert.ThrowsAsync<InvalidOperationException>(() => cliente.ObtenerJwks());
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-64")]
    public async Task UnDocumentoMasGrandeQueElLimite_SeDescarta()
    {
        var opciones = new OpcionesDeClavesPublicadas(new Uri("http://replica.interna/jwks"), new Uri("http://replica.interna/rev")) { MaximoDeBytes = 100 };
        var cliente = Cliente(new ManejadorFijo(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 101)) }), opciones);

        await Assert.ThrowsAsync<InvalidOperationException>(() => cliente.ObtenerJwks());
    }

    [Theory]
    [InlineData("ftp://replica.interna/jwks")]
    [InlineData("http://usuario:clave@replica.interna/jwks")]
    [InlineData("file:///etc/jwks")]
    public void LasDireccionesDeLasClaves_SonHttpSinUsuario(string direccion)
    {
        Assert.Throws<ArgumentException>(() => new OpcionesDeClavesPublicadas(new Uri(direccion), new Uri("http://replica.interna/rev")));
    }

    private sealed class AlertasGuardadas : IPublicadorDeAlertasTecnicas
    {
        public List<AlertaTecnica> Publicadas { get; } = [];

        public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
        {
            Publicadas.Add(alerta);
            return Task.CompletedTask;
        }
    }

    private sealed class ActorGuardado : IEstablecedorDeActor
    {
        public void Establecer(ActorActual actor)
        {
        }

        public void Limpiar()
        {
        }
    }

    private sealed class RelojDelInventario(RelojManual reloj) : IReloj
    {
        public DateTimeOffset Ahora => reloj.GetUtcNow();

        public DateOnly HoyEnLima => DateOnly.FromDateTime(reloj.GetUtcNow().UtcDateTime.AddHours(-5));
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task ElTrabajoDeRenovacion_AlertaUnaVezTrasVariosFallosSeguidos_YSeRecuperaAlResponderLaReplica()
    {
        var e = new Escenario();
        var alertas = new AlertasGuardadas();
        var trabajo = new TrabajoDeRenovacionDeClaves(e.Cache, alertas, new ActorGuardado(), new RelojDelInventario(e.Reloj), new OpcionesDeLaRenovacionDeClaves { FallosSeguidosParaAlertar = 3 });

        await trabajo.EjecutarUnaVez();
        Assert.NotNull(e.Cache.Actual);
        e.Replica.Caida = true;
        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        Assert.Empty(alertas.Publicadas);
        await trabajo.EjecutarUnaVez();
        await trabajo.EjecutarUnaVez();
        Assert.Single(alertas.Publicadas);

        e.Replica.Caida = false;
        await trabajo.EjecutarUnaVez();
        Assert.Single(alertas.Publicadas);
        Assert.NotNull(e.Cache.Actual);
    }
}
