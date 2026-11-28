using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Web;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Ecommerce.Infrastructure.Composicion;
using BabelHub.Ecommerce.Infrastructure.Identidad;
using BabelHub.Shared.Contratos;
using RolesDeUsuario = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.IntegrationTests.Identidad;

/// <summary>
/// El emisor, el verificador y el publicador ES256 (identidad-seguridad T32; AC-IDE-09, AC-IDE-32, AC-IDE-33, AC-IDE-64; IDE-10, IDE-33, IDE-35, IDE-61, IDE-85): ES256 fijo con <c>kid</c> y <c>aud</c>, rechazo de <c>none</c>, otros
/// algoritmos, <c>jwk</c>, <c>jku</c> y <c>x5u</c> de la cabecera, un <c>kid</c> desconocido o revocado, un contexto de lectura y una caché de claves de más de 5 minutos.
/// </summary>
public sealed class CredencialesEs256Tests
{
    private static readonly DateTimeOffset Ahora = new(2026, 11, 19, 15, 0, 0, TimeSpan.Zero);

    private sealed class ClaveEnMemoria(string kid, ECDsa clave) : IClavePrivadaDeFirma
    {
        public Task<ClavePrivadaActiva> Activa(CancellationToken cancelacion = default) => Task.FromResult(new ClavePrivadaActiva(kid, clave));
    }

    private sealed class FuenteFija(FotoDeClavesDeVerificacion? foto) : IFuenteDeClavesDeVerificacion
    {
        public FotoDeClavesDeVerificacion? Actual { get; set; } = foto;
    }

    private sealed class RelojManual(DateTimeOffset inicio) : TimeProvider
    {
        public DateTimeOffset Ahora { get; set; } = inicio;

        public override DateTimeOffset GetUtcNow() => Ahora;
    }

    private sealed class Escenario
    {
        public Escenario(string kid = "kid-de-prueba-uno")
        {
            Kid = kid;
            Clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            Foto = new FotoDeClavesDeVerificacion(new Dictionary<string, ECDsa> { [kid] = Clave }, new HashSet<string>(), Ahora);
            Fuente = new FuenteFija(Foto);
            Emisor = new Es256EmisorDeCredenciales(new ClaveEnMemoria(kid, Clave));
            Verificador = new Es256VerificadorDeCredenciales(Fuente);
        }

        public string Kid { get; }

        public ECDsa Clave { get; }

        public FotoDeClavesDeVerificacion Foto { get; }

        public FuenteFija Fuente { get; }

        public Es256EmisorDeCredenciales Emisor { get; }

        public Es256VerificadorDeCredenciales Verificador { get; }

        public async Task<string> Credencial(RolDeUsuario rol = RolDeUsuario.Cliente, Guid? sucursal = null, DateTimeOffset? en = null) =>
            (await Emisor.Emitir(new DatosDeLaCredencial(Guid.NewGuid(), rol, sucursal, true), en ?? Ahora)).Jwt;
    }

    private static Dictionary<string, object?> Reclamos(DateTimeOffset desde, TimeSpan? vida = null, object? aud = null) => new()
    {
        ["iss"] = CredencialDeAcceso.Emisor,
        ["sub"] = Guid.NewGuid().ToString("D"),
        ["aud"] = aud ?? new[] { CredencialDeAcceso.AudienciaEcommerce, CredencialDeAcceso.AudienciaInventario },
        ["rol"] = RolesDeUsuario.Cliente,
        ["iat"] = desde.ToUnixTimeSeconds(),
        ["nbf"] = desde.ToUnixTimeSeconds(),
        ["exp"] = (desde + (vida ?? TimeSpan.FromMinutes(15))).ToUnixTimeSeconds(),
    };

    /// <summary>Arma un token a mano con la cabecera y la carga que se quiera, firmado con la clave dada (lo que haría un atacante).</summary>
    private static string Armar(Dictionary<string, object?> cabecera, Dictionary<string, object?> carga, ECDsa? clave, byte[]? firmaExplicita = null)
    {
        var parte1 = Base64Url.Codificar(JsonSerializer.SerializeToUtf8Bytes(cabecera));
        var parte2 = Base64Url.Codificar(JsonSerializer.SerializeToUtf8Bytes(carga));
        var firma = firmaExplicita ?? clave!.SignData(Encoding.ASCII.GetBytes($"{parte1}.{parte2}"), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{parte1}.{parte2}.{Base64Url.Codificar(firma)}";
    }

    private static Dictionary<string, object?> Cabecera(string kid, string alg = "ES256") => new() { ["alg"] = alg, ["typ"] = "JWT", ["kid"] = kid };

    // ---------------------------------------------------------------- emisión y verificación

    [Fact]
    [Trait("Criterio", "AC-IDE-09")]
    public async Task LaCredencialEmitida_LlevaES256ConKidAudienciasYQuinceMinutos_YSeVerificaSinConsultarAIdentidad()
    {
        var e = new Escenario();
        var sucursal = Guid.NewGuid();
        var usuario = Guid.NewGuid();

        var emitida = await e.Emisor.Emitir(new DatosDeLaCredencial(usuario, RolDeUsuario.EncargadoDeSucursal, sucursal, true), Ahora);
        var partes = emitida.Jwt.Split('.');
        using var cabecera = JsonDocument.Parse(Base64Url.TryDecodificar(partes[0], out var c) ? c : []);
        using var carga = JsonDocument.Parse(Base64Url.TryDecodificar(partes[1], out var p) ? p : []);

        Assert.Equal(Ahora.AddMinutes(15), emitida.ExpiraEn);
        Assert.Equal("ES256", cabecera.RootElement.GetProperty("alg").GetString());
        Assert.Equal(e.Kid, cabecera.RootElement.GetProperty("kid").GetString());
        Assert.Equal([CredencialDeAcceso.AudienciaEcommerce, CredencialDeAcceso.AudienciaInventario], carga.RootElement.GetProperty("aud").EnumerateArray().Select(a => a.GetString()));
        Assert.Equal(usuario.ToString("D"), carga.RootElement.GetProperty("sub").GetString());
        Assert.Equal(RolesDeUsuario.EncargadoDeSucursal, carga.RootElement.GetProperty("rol").GetString());
        Assert.Equal(sucursal.ToString("D"), carga.RootElement.GetProperty("sucursal").GetString());
        Assert.True(carga.RootElement.GetProperty("correo_verificado").GetBoolean());
        Assert.Equal(Ahora.AddMinutes(15).ToUnixTimeSeconds(), carga.RootElement.GetProperty("exp").GetInt64());
        Assert.False(carga.RootElement.TryGetProperty("typ", out _));
        Assert.DoesNotContain("@", emitida.Jwt, StringComparison.Ordinal);
        Assert.Equal("[credencial]", emitida.ToString());

        var verificada = e.Verificador.Verificar(emitida.Jwt, Ahora.AddMinutes(1));

        Assert.Equal(EstadoDeLaCredencial.Valida, verificada.Estado);
        Assert.Equal(usuario, verificada.UsuarioId);
        Assert.Equal(RolesDeUsuario.EncargadoDeSucursal, verificada.Rol);
        Assert.Equal(sucursal, verificada.SucursalId);
    }

    [Fact]
    public async Task UnClienteNoLlevaSucursal_YCadaCredencialTieneSuPropioJti()
    {
        var e = new Escenario();

        var uno = await e.Credencial();
        var dos = await e.Credencial();
        var verificada = e.Verificador.Verificar(uno, Ahora);

        Assert.NotEqual(uno, dos);
        Assert.Null(verificada.SucursalId);
        Assert.Equal(RolesDeUsuario.Cliente, verificada.Rol);
    }

    [Theory]
    [InlineData(0, EstadoDeLaCredencial.Valida)]
    [InlineData(899, EstadoDeLaCredencial.Valida)]
    [InlineData(900, EstadoDeLaCredencial.Valida)]
    [InlineData(929, EstadoDeLaCredencial.Valida)]
    [InlineData(930, EstadoDeLaCredencial.Vencida)]
    [InlineData(2000, EstadoDeLaCredencial.Vencida)]
    [Trait("Criterio", "AC-IDE-09")]
    public async Task LaVigencia_Es15Minutos_ConUnDesfaseDe30Segundos(int segundos, EstadoDeLaCredencial esperado)
    {
        var e = new Escenario();
        var jwt = await e.Credencial();
        e.Fuente.Actual = new FotoDeClavesDeVerificacion(e.Foto.Claves, e.Foto.Revocadas, Ahora.AddSeconds(segundos));

        Assert.Equal(esperado, e.Verificador.Verificar(jwt, Ahora.AddSeconds(segundos)).Estado);
    }

    [Fact]
    public async Task UnaCredencialQueAunNoEsValida_ConMasDe30SegundosDeAdelanto_SeRechaza()
    {
        var e = new Escenario();
        var jwt = await e.Credencial(en: Ahora.AddSeconds(31));
        var tolerable = await e.Credencial(en: Ahora.AddSeconds(30));

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(jwt, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(tolerable, Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-09")]
    public async Task ElInventario_AceptaLaMismaCredencial_PorSuPropiaAudiencia_YUnaSoloDelEcommerceNoLeSirve()
    {
        var e = new Escenario();
        var inventario = new Es256VerificadorDeCredenciales(e.Fuente, CredencialDeAcceso.AudienciaInventario);
        var jwt = await e.Credencial();
        var soloEcommerce = Armar(Cabecera(e.Kid), Reclamos(Ahora, aud: CredencialDeAcceso.AudienciaEcommerce), e.Clave);

        Assert.Equal(EstadoDeLaCredencial.Valida, inventario.Verificar(jwt, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Invalida, inventario.Verificar(soloEcommerce, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(soloEcommerce, Ahora).Estado);
    }

    // ---------------------------------------------------------------- ataques a la cabecera y a la firma

    [Theory]
    [InlineData("none")]
    [InlineData("NONE")]
    [InlineData("HS256")]
    [InlineData("RS256")]
    [InlineData("ES384")]
    [InlineData("es256")]
    [InlineData("")]
    [Trait("Criterio", "AC-IDE-32")]
    public void UnAlgoritmoDistintoDeES256_SeRechaza_AunqueLaFirmaSeaDeLaClaveCorrecta(string alg)
    {
        var e = new Escenario();
        var jwt = Armar(Cabecera(e.Kid, alg), Reclamos(Ahora), e.Clave);

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(jwt, Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public void ElAtaqueDeNone_ConFirmaVacia_YElDeHmacConLaClavePublica_SeRechazan()
    {
        var e = new Escenario();
        var sinFirma = $"{Base64Url.Codificar(JsonSerializer.SerializeToUtf8Bytes(Cabecera(e.Kid, "none")))}.{Base64Url.Codificar(JsonSerializer.SerializeToUtf8Bytes(Reclamos(Ahora)))}.";
        var publica = e.Clave.ExportSubjectPublicKeyInfo();
        var hmac = Armar(Cabecera(e.Kid, "HS256"), Reclamos(Ahora), null, HMACSHA256.HashData(publica, Encoding.ASCII.GetBytes("x.y")));

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(sinFirma, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(hmac, Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public void JwkJkuX5uYX5cDeLaCabecera_SeIgnoran_LaClaveSalSoloDeLoPublicado()
    {
        var e = new Escenario();
        using var atacante = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var jwkDelAtacante = JsonSerializer.Deserialize<object>(JwkEs256.Publico(atacante, "kid-del-atacante"));

        foreach (var truco in new[] { "jwk", "jku", "x5u", "x5c" })
        {
            var cabecera = Cabecera(e.Kid);
            cabecera[truco] = truco == "jwk" ? jwkDelAtacante : "https://atacante.example/claves";
            var conKidPublicado = Armar(cabecera, Reclamos(Ahora), atacante);
            var cabeceraConKidNuevo = Cabecera("kid-del-atacante");
            cabeceraConKidNuevo[truco] = truco == "jwk" ? jwkDelAtacante : "https://atacante.example/claves";
            var conKidDelAtacante = Armar(cabeceraConKidNuevo, Reclamos(Ahora), atacante);

            Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(conKidPublicado, Ahora).Estado);
            Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(conKidDelAtacante, Ahora).Estado);
        }

        // La misma cabecera con jwk y firmada con la clave legítima sí vale: el jwk no cambia nada.
        var legitima = Cabecera(e.Kid);
        legitima["jwk"] = jwkDelAtacante;
        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(Armar(legitima, Reclamos(Ahora), e.Clave), Ahora).Estado);
    }

    [Fact]
    public void UnaCabeceraConCrit_SeRechaza()
    {
        var e = new Escenario();
        var cabecera = Cabecera(e.Kid);
        cabecera["crit"] = new[] { "exp" };

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(Armar(cabecera, Reclamos(Ahora), e.Clave), Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public async Task UnKidDesconocido_UnaFirmaAlteradaOUnaCargaAlterada_SeRechazan()
    {
        var e = new Escenario();
        var jwt = await e.Credencial();
        var partes = jwt.Split('.');
        var conOtraCarga = $"{partes[0]}.{Base64Url.Codificar(JsonSerializer.SerializeToUtf8Bytes(Reclamos(Ahora)))}.{partes[2]}";
        var firma = Base64Url.TryDecodificar(partes[2], out var bytes) ? bytes : [];
        firma[10] ^= 0x01;
        var conOtraFirma = $"{partes[0]}.{partes[1]}.{Base64Url.Codificar(firma)}";
        var desconocido = Armar(Cabecera("kid-que-no-existe"), Reclamos(Ahora), e.Clave);
        using var otraClave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var firmadoPorOtro = Armar(Cabecera(e.Kid), Reclamos(Ahora), otraClave);

        foreach (var malo in new[] { conOtraCarga, conOtraFirma, desconocido, firmadoPorOtro })
        {
            Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(malo, Ahora).Estado);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("a.b")]
    [InlineData("a.b.c.d")]
    [InlineData("..")]
    [InlineData("no es base64.no es base64.no es base64")]
    [InlineData("eyJhbGciOiJFUzI1NiJ9.e30.AAAA")]
    public void UnTokenIlegible_SeRechaza_SinLanzar(string? token)
    {
        var e = new Escenario();

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(token!, Ahora).Estado);
    }

    [Fact]
    public async Task UnTokenDemasiadoLargo_UnaFirmaDeOtroLargo_OUnaCargaQueNoEsUnObjeto_SeRechazan()
    {
        var e = new Escenario();
        var jwt = await e.Credencial();
        var partes = jwt.Split('.');
        var largo = new string('a', JwsEs256.LargoMaximoDelToken + 1);
        var firmaCorta = Armar(Cabecera(e.Kid), Reclamos(Ahora), null, new byte[63]);
        var cargaDeArreglo = $"{partes[0]}.{Base64Url.Codificar(Encoding.UTF8.GetBytes("[1,2]"))}.{partes[2]}";

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(largo, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(firmaCorta, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(cargaDeArreglo, Ahora).Estado);
    }

    [Theory]
    [InlineData("iss", "otro-emisor")]
    [InlineData("sub", "no-es-un-guid")]
    [InlineData("sub", "00000000-0000-0000-0000-000000000000")]
    [InlineData("rol", "superusuario")]
    [InlineData("rol", "")]
    [InlineData("aud", "otra-audiencia")]
    [InlineData("typ", "lectura-asistente")]
    [InlineData("typ", "acceso")]
    [InlineData("exp", "no-es-un-numero")]
    [InlineData("nbf", "no-es-un-numero")]
    [InlineData("sucursal", "no-es-un-guid")]
    public void UnReclamoInvalidoOAjeno_SeRechaza_ConLaFirmaCorrecta(string reclamo, string valor)
    {
        var e = new Escenario();
        var carga = Reclamos(Ahora);
        carga[reclamo] = valor;

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(Armar(Cabecera(e.Kid), carga, e.Clave), Ahora).Estado);
    }

    [Theory]
    [InlineData("iss")]
    [InlineData("sub")]
    [InlineData("aud")]
    [InlineData("rol")]
    [InlineData("exp")]
    [InlineData("nbf")]
    public void UnReclamoObligatorioAusente_SeRechaza(string reclamo)
    {
        var e = new Escenario();
        var carga = Reclamos(Ahora);
        carga.Remove(reclamo);

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(Armar(Cabecera(e.Kid), carga, e.Clave), Ahora).Estado);
    }

    [Theory]
    [InlineData(15 * 60 + 30, true)]
    [InlineData(15 * 60 + 31, false)]
    [InlineData(24 * 60 * 60, false)]
    [InlineData(0, false)]
    [Trait("Criterio", "AC-IDE-09")]
    public void UnaCredencialConMasVidaQueLaDeIdentidadONinguna_SeRechaza_AunqueEstePorBienFirmada(int vidaEnSegundos, bool valida)
    {
        var e = new Escenario();
        var jwt = Armar(Cabecera(e.Kid), Reclamos(Ahora, TimeSpan.FromSeconds(vidaEnSegundos)), e.Clave);

        Assert.Equal(valida ? EstadoDeLaCredencial.Valida : EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(jwt, Ahora).Estado);
    }

    [Fact]
    public async Task UnContextoDeOtroEmisor_NoSeLee()
    {
        var e = new Escenario();
        var firma = new Es256FirmaDeContextosDeLectura(new ClaveEnMemoria(e.Kid, e.Clave), e.Fuente, new RelojManual(Ahora));
        var otro = Armar(
            Cabecera(e.Kid),
            new Dictionary<string, object?>
            {
                ["iss"] = "otro-emisor",
                ["sub"] = Guid.NewGuid().ToString("D"),
                ["aud"] = "pedidos",
                ["typ"] = "lectura-asistente",
                ["alcance"] = "leer-pedidos",
                ["iat"] = Ahora.ToUnixTimeSeconds(),
                ["exp"] = Ahora.AddMinutes(5).ToUnixTimeSeconds(),
                ["jti"] = "id",
            },
            e.Clave);

        Assert.Null(await firma.Verificar(otro));
    }

    [Fact]
    public async Task UnaFotoEstampadaPorUnRelojUnPocoAdelantado_NoRechazaTodoPorUnInstante()
    {
        var e = new Escenario();
        var jwt = await e.Credencial();
        e.Fuente.Actual = new FotoDeClavesDeVerificacion(e.Foto.Claves, e.Foto.Revocadas, Ahora.AddSeconds(10));

        Assert.Equal(EstadoDeLaCredencial.Valida, e.Verificador.Verificar(jwt, Ahora).Estado);
    }

    // ---------------------------------------------------------------- claves, revocaciones y caché (IDE-61, IDE-85)

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task AlRotar_LaCredencialFirmadaConLaClaveAnteriorSigueValiendo_YLaNuevaUsaLaNueva()
    {
        var anterior = new Escenario("kid-de-la-anterior");
        var nuevaClave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var claves = new Dictionary<string, ECDsa> { ["kid-de-la-anterior"] = anterior.Clave, ["kid-de-la-nueva-1"] = nuevaClave };
        var fuente = new FuenteFija(new FotoDeClavesDeVerificacion(claves, new HashSet<string>(), Ahora));
        var verificador = new Es256VerificadorDeCredenciales(fuente);
        var conLaAnterior = await anterior.Credencial();
        var conLaNueva = (await new Es256EmisorDeCredenciales(new ClaveEnMemoria("kid-de-la-nueva-1", nuevaClave)).Emitir(new DatosDeLaCredencial(Guid.NewGuid(), RolDeUsuario.Cliente, null, true), Ahora)).Jwt;

        Assert.Equal(EstadoDeLaCredencial.Valida, verificador.Verificar(conLaAnterior, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.Valida, verificador.Verificar(conLaNueva, Ahora).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task UnKidRevocado_SeRechazaAunqueSuClaveSigaEnLaFoto()
    {
        var e = new Escenario();
        var jwt = await e.Credencial();
        e.Fuente.Actual = new FotoDeClavesDeVerificacion(e.Foto.Claves, new HashSet<string> { e.Kid }, Ahora);

        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(jwt, Ahora).Estado);
    }

    [Theory]
    [InlineData(299, EstadoDeLaCredencial.Valida)]
    [InlineData(300, EstadoDeLaCredencial.Valida)]
    [InlineData(301, EstadoDeLaCredencial.SinClavesVigentes)]
    [Trait("Criterio", "AC-IDE-64")]
    public async Task SiNoSePuedeRenovarLaCacheEn5Minutos_SeRechazaTodaCredencial(int segundosDesdeLaFoto, EstadoDeLaCredencial esperado)
    {
        var e = new Escenario();
        var jwt = await e.Credencial();

        Assert.Equal(esperado, e.Verificador.Verificar(jwt, Ahora.AddSeconds(segundosDesdeLaFoto)).Estado);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-64")]
    public async Task SinFotoDeClaves_NoHayCredencialValida_YElEstadoLoDice()
    {
        var e = new Escenario();
        var jwt = await e.Credencial();
        e.Fuente.Actual = null;

        Assert.Equal(EstadoDeLaCredencial.SinClavesVigentes, e.Verificador.Verificar(jwt, Ahora).Estado);
        Assert.Equal(EstadoDeLaCredencial.SinClavesVigentes, e.Verificador.Verificar("basura", Ahora).Estado);
    }

    // ---------------------------------------------------------------- caché en proceso y publicación

    private sealed class PublicadorFalso : IClavesDeFirma
    {
        public List<ClavePublicaDeFirma> Claves { get; } = [];

        public List<string> Revoc { get; } = [];

        public bool Falla { get; set; }

        public Task<IReadOnlyList<ClavePublicaDeFirma>> Publicadas(CancellationToken cancelacion = default) =>
            Falla ? throw new InvalidOperationException("Sin base.") : Task.FromResult<IReadOnlyList<ClavePublicaDeFirma>>([.. Claves]);

        public Task<IReadOnlyList<string>> Revocadas(CancellationToken cancelacion = default) => Task.FromResult<IReadOnlyList<string>>([.. Revoc]);

        public Task<Resultado<ClavePublicaDeFirma>> Rotar(Guid administradorId, CancellationToken cancelacion = default) => throw new NotSupportedException();

        public Task<Resultado<ClavePublicaDeFirma>> DeclararComprometida(Guid administradorId, string kid, CancellationToken cancelacion = default) => throw new NotSupportedException();
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-64")]
    public async Task LaCache_SeRenuevaDeLoPublicado_ConservaLaFotoAnteriorSoloHasta5Minutos_YDespuesSeRechazaTodo()
    {
        var e = new Escenario();
        var publicador = new PublicadorFalso();
        publicador.Claves.Add(new ClavePublicaDeFirma(e.Kid, "ES256", JwkEs256.Publico(e.Clave, e.Kid), "activa"));
        var reloj = new RelojManual(Ahora);
        var cache = new CacheDeClavesDeVerificacion(publicador, reloj);
        var verificador = new Es256VerificadorDeCredenciales(cache);
        var jwt = await e.Credencial();

        Assert.Equal(EstadoDeLaCredencial.SinClavesVigentes, verificador.Verificar(jwt, Ahora).Estado);
        Assert.True(await cache.Renovar());
        Assert.Equal(EstadoDeLaCredencial.Valida, verificador.Verificar(jwt, Ahora).Estado);

        publicador.Falla = true;
        reloj.Ahora = Ahora.AddMinutes(4);
        Assert.False(await cache.Renovar());
        Assert.Equal(EstadoDeLaCredencial.Valida, verificador.Verificar(jwt, Ahora.AddMinutes(4)).Estado);
        Assert.Equal(EstadoDeLaCredencial.SinClavesVigentes, verificador.Verificar(jwt, Ahora.AddMinutes(5).AddSeconds(1)).Estado);

        publicador.Falla = false;
        publicador.Revoc.Add(e.Kid);
        reloj.Ahora = Ahora.AddMinutes(6);
        Assert.True(await cache.Renovar());
        Assert.Equal(EstadoDeLaCredencial.Invalida, verificador.Verificar(jwt, Ahora.AddMinutes(6)).Estado);
    }

    [Fact]
    public async Task LaCache_OmiteUnaClaveIlegibleOQueNoEsES256()
    {
        var e = new Escenario();
        var publicador = new PublicadorFalso();
        publicador.Claves.Add(new ClavePublicaDeFirma("kid-roto-1234", "ES256", "{no es json", "activa"));
        publicador.Claves.Add(new ClavePublicaDeFirma("kid-rs256-1234", "RS256", JwkEs256.Publico(e.Clave, "kid-rs256-1234"), "activa"));
        publicador.Claves.Add(new ClavePublicaDeFirma(e.Kid, "ES256", JwkEs256.Publico(e.Clave, e.Kid), "activa"));
        var cache = new CacheDeClavesDeVerificacion(publicador, new RelojManual(Ahora));

        await cache.Renovar();

        Assert.Equal([e.Kid], cache.Actual!.Claves.Keys);
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-32")]
    public void ElJwks_LlevaSoloLosJwkPublicos_NuncaUnaPrivada_YOmiteLosIlegibles()
    {
        using var clave = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var privadaEnElJwk = JsonSerializer.Serialize(new Dictionary<string, string> { ["kty"] = "EC", ["crv"] = "P-256", ["x"] = "AA", ["y"] = "AA", ["d"] = "AAAA" });
        var claves = new List<ClavePublicaDeFirma>
        {
            new("kid-publico-001", "ES256", JwkEs256.Publico(clave, "kid-publico-001"), "activa"),
            new("kid-con-privada1", "ES256", privadaEnElJwk, "activa"),
            new("kid-ilegible-001", "ES256", "no es json", "anterior"),
        };

        var json = DocumentosDeClavesPublicadas.Jwks(claves);
        using var documento = JsonDocument.Parse(json);
        var llaves = documento.RootElement.GetProperty("keys").EnumerateArray().ToList();

        var unica = Assert.Single(llaves);
        Assert.Equal("kid-publico-001", unica.GetProperty("kid").GetString());
        Assert.Equal("EC", unica.GetProperty("kty").GetString());
        Assert.Equal("P-256", unica.GetProperty("crv").GetString());
        Assert.Equal("ES256", unica.GetProperty("alg").GetString());
        Assert.False(unica.TryGetProperty("d", out _));
        Assert.DoesNotContain("\"d\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("[]")]
    [InlineData("{\"kty\":\"RSA\",\"crv\":\"P-256\",\"x\":\"AA\",\"y\":\"AA\"}")]
    [InlineData("{\"kty\":\"EC\",\"crv\":\"P-384\",\"x\":\"AA\",\"y\":\"AA\"}")]
    [InlineData("{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"AA\",\"y\":\"AA\"}")]
    [InlineData("{\"kty\":\"EC\",\"crv\":\"P-256\"}")]
    public void UnJwkQueNoEsEcP256Publico_NoSeLee(string? jwk)
    {
        Assert.False(JwkEs256.TryLeerLaPublica(jwk, out _));
    }

    // ---------------------------------------------------------------- el contexto de lectura (T26)

    [Fact]
    [Trait("Criterio", "AC-IDE-30")]
    public async Task ElContextoDeLectura_ViajaFirmado_ConTypAudYAlcance_NoSirveComoCredencial_NiLaCredencialComoContexto()
    {
        var e = new Escenario();
        var firma = new Es256FirmaDeContextosDeLectura(new ClaveEnMemoria(e.Kid, e.Clave), e.Fuente, new RelojManual(Ahora));
        var cliente = Guid.NewGuid();
        var reclamos = new ReclamosDelContexto(cliente, "pedidos", "lectura-asistente", "leer-pedidos", Ahora, Ahora.AddMinutes(5), Guid.NewGuid().ToString("N"));

        var token = await firma.Firmar(reclamos);
        var leidos = await firma.Verificar(token);

        Assert.Equal(reclamos, leidos);
        Assert.Equal(EstadoDeLaCredencial.Invalida, e.Verificador.Verificar(token, Ahora).Estado);
        Assert.Null(await firma.Verificar(await e.Credencial()));
        Assert.Null(await firma.Verificar("no es un contexto"));
    }

    [Fact]
    public async Task ElContextoDeLectura_ConUnKidRevocadoOLaFotoVencida_NoSeLee()
    {
        var e = new Escenario();
        var reloj = new RelojManual(Ahora);
        var firma = new Es256FirmaDeContextosDeLectura(new ClaveEnMemoria(e.Kid, e.Clave), e.Fuente, reloj);
        var token = await firma.Firmar(new ReclamosDelContexto(Guid.NewGuid(), "pedidos", "lectura-asistente", "leer-pedidos", Ahora, Ahora.AddMinutes(5), "id"));

        reloj.Ahora = Ahora.AddMinutes(6);
        Assert.Null(await firma.Verificar(token));

        reloj.Ahora = Ahora;
        e.Fuente.Actual = new FotoDeClavesDeVerificacion(e.Foto.Claves, new HashSet<string> { e.Kid }, Ahora);
        Assert.Null(await firma.Verificar(token));
    }

    // ---------------------------------------------------------------- generación de la clave (IDE-36)

    private sealed class AlmacenPrivadoFalso : IAlmacenDeClavesPrivadas
    {
        public Dictionary<string, byte[]> Guardadas { get; } = [];

        public Task Guardar(string kid, byte[] privadaCifrada, string jwkPublico, DateTimeOffset creada, CancellationToken cancelacion = default)
        {
            Guardadas[kid] = privadaCifrada;
            return Task.CompletedTask;
        }
    }

    [Fact]
    [Trait("Criterio", "AC-IDE-33")]
    public async Task LaClaveGenerada_GuardaLaPrivadaCifradaLigadaAlKid_YDevuelveSoloElJwkPublico()
    {
        var cifrador = new AesGcmCifradorDeSecretos(ClaveVersionadaDeIdentidad.Analizar($"v1:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}", "c"));
        var almacen = new AlmacenPrivadoFalso();
        var generador = new Es256GeneradorDeClavesDeFirma(cifrador, almacen);

        var (clave, jwk) = await generador.Generar(Ahora);

        var cifrada = Assert.Single(almacen.Guardadas);
        Assert.Equal(clave.Kid, cifrada.Key);
        Assert.Equal(22, clave.Kid.Length);
        Assert.Equal(EstadoDeClaveDeFirma.Activa, clave.Estado);
        Assert.False(JsonDocument.Parse(jwk).RootElement.TryGetProperty("d", out _));
        Assert.True(JwkEs256.TryLeerLaPublica(jwk, out var publica));

        var pkcs8 = cifrador.Descifrar(AesGcmCifradorDeSecretos.PropositoDeLaClaveDeFirma, clave.Kid, cifrada.Value);
        using var privada = ECDsa.Create();
        privada.ImportPkcs8PrivateKey(pkcs8, out _);
        var firma = privada.SignData(new byte[] { 1, 2, 3 }, HashAlgorithmName.SHA256);
        Assert.True(publica.VerifyData(new byte[] { 1, 2, 3 }, firma, HashAlgorithmName.SHA256));

        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar(AesGcmCifradorDeSecretos.PropositoDeLaClaveDeFirma, "otro-kid-distinto-123456", cifrada.Value));
        Assert.ThrowsAny<CryptographicException>(() => cifrador.Descifrar(AesGcmCifradorDeSecretos.PropositoDelSegundoFactor, clave.Kid, cifrada.Value));
    }

    // ---------------------------------------------------------------- el puerto de la Web

    [Fact]
    public async Task ElAdaptadorDeLaWeb_TraduceElEstadoYElRol()
    {
        var e = new Escenario();
        var adaptador = new VerificadorDeCredencialDeLaWeb(e.Verificador);
        var sucursal = Guid.NewGuid();
        var encargado = await e.Credencial(RolDeUsuario.EncargadoDeSucursal, sucursal);

        var valida = adaptador.Verificar(encargado, Ahora);
        var vencida = adaptador.Verificar(encargado, Ahora.AddMinutes(10));
        var invalida = adaptador.Verificar("basura", Ahora);

        Assert.Equal(EstadoDeVerificacion.Valida, valida.Estado);
        Assert.Equal(RolWeb.Encargado, valida.Actor!.Rol);
        Assert.Equal(sucursal, valida.Actor.SucursalId);
        Assert.Equal(EstadoDeVerificacion.SinClavesVigentes, vencida.Estado);
        Assert.Equal(EstadoDeVerificacion.Invalida, invalida.Estado);
        Assert.Null(invalida.Actor);
        e.Fuente.Actual = null;
        Assert.Equal(EstadoDeVerificacion.SinClavesVigentes, adaptador.Verificar(encargado, Ahora).Estado);
    }
}
