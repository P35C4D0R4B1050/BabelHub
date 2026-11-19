using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Identidad.Interno;
using BabelHub.Ecommerce.Domain.Identidad;
using BabelHub.Shared.Contratos;
using RolesCompartidos = BabelHub.Shared.Contratos.RolesDeUsuario;

namespace BabelHub.Ecommerce.Infrastructure.Identidad;

/// <summary>
/// El formato JWS compacto con ES256 (T32; IDE-35): <c>cabecera.carga.firma</c> en base64url, la firma ECDSA P-256 de 64 bytes (r y s concatenados, RFC 7515). La <b>cabecera</b> la fija quien firma: <c>alg</c> es siempre
/// <c>ES256</c> y la clave se elige solo por el <c>kid</c>; al leer, <b>cualquier otro algoritmo</b> (incluido <c>none</c>), <c>crit</c> o una firma de otro largo se rechaza, y <c>jwk</c>, <c>jku</c>, <c>x5u</c> y <c>x5c</c> de la
/// cabecera <b>se ignoran por completo</b>: la clave sale de lo publicado por Identidad, nunca de lo que la credencial dice de sí misma.
/// </summary>
public static class JwsEs256
{
    public const int LargoMaximoDelToken = 4096;

    private const int BytesDeLaFirma = 64;

    public static string Firmar(ECDsa clave, string kid, IReadOnlyDictionary<string, object?> reclamos)
    {
        ArgumentNullException.ThrowIfNull(clave);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        ArgumentNullException.ThrowIfNull(reclamos);
        var cabecera = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, string> { ["alg"] = CredencialDeAcceso.Algoritmo, ["typ"] = "JWT", ["kid"] = kid });
        var carga = JsonSerializer.SerializeToUtf8Bytes(reclamos);
        var firmado = $"{Base64Url.Codificar(cabecera)}.{Base64Url.Codificar(carga)}";
        var firma = clave.SignData(Encoding.ASCII.GetBytes(firmado), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return $"{firmado}.{Base64Url.Codificar(firma)}";
    }

    /// <summary>La cabecera ya leída de un token cuyo formato es válido: solo el <c>kid</c> (el algoritmo ya se comprobó).</summary>
    public sealed record TokenLeido(string Kid, string Firmado, byte[] Firma, JsonElement Carga, JsonDocument Documento) : IDisposable
    {
        public void Dispose() => Documento.Dispose();
    }

    /// <summary>
    /// Lee un token sin comprobar todavía la firma: <see langword="null"/> si no es un JWS compacto de tres partes, si su cabecera no es exactamente ES256 con un <c>kid</c> de texto, si trae <c>crit</c>, si la firma no mide 64
    /// bytes o si la carga no es un objeto JSON. Nada de la cabecera, aparte del <c>kid</c>, se usa.
    /// </summary>
    public static TokenLeido? Leer(string? token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > LargoMaximoDelToken)
        {
            return null;
        }

        var partes = token.Split('.');
        if (partes.Length != 3 || partes.Any(p => p.Length == 0))
        {
            return null;
        }

        if (!Base64Url.TryDecodificar(partes[0], out var cabecera) || !Base64Url.TryDecodificar(partes[1], out var carga) || !Base64Url.TryDecodificar(partes[2], out var firma) || firma.Length != BytesDeLaFirma)
        {
            return null;
        }

        try
        {
            using var documentoDeLaCabecera = JsonDocument.Parse(cabecera, new JsonDocumentOptions { MaxDepth = 4 });
            var raiz = documentoDeLaCabecera.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object
                || !raiz.TryGetProperty("alg", out var alg) || alg.ValueKind != JsonValueKind.String || !string.Equals(alg.GetString(), CredencialDeAcceso.Algoritmo, StringComparison.Ordinal)
                || !raiz.TryGetProperty("kid", out var kid) || kid.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(kid.GetString())
                || raiz.TryGetProperty("crit", out _))
            {
                return null;
            }

            var documento = JsonDocument.Parse(carga, new JsonDocumentOptions { MaxDepth = 8 });
            if (documento.RootElement.ValueKind != JsonValueKind.Object)
            {
                documento.Dispose();
                return null;
            }

            return new TokenLeido(kid.GetString()!, $"{partes[0]}.{partes[1]}", firma, documento.RootElement, documento);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static bool FirmaValida(ECDsa publica, TokenLeido token) =>
        publica.VerifyData(Encoding.ASCII.GetBytes(token.Firmado), token.Firma, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
}

/// <summary>Base64url sin relleno (RFC 7515).</summary>
public static class Base64Url
{
    public static string Codificar(ReadOnlySpan<byte> datos) => Convert.ToBase64String(datos).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static bool TryDecodificar(string texto, out byte[] bytes)
    {
        bytes = [];
        if (texto.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
        {
            return false;
        }

        var normal = texto.Replace('-', '+').Replace('_', '/');
        normal = normal.PadRight(normal.Length + ((4 - (normal.Length % 4)) % 4), '=');
        try
        {
            bytes = Convert.FromBase64String(normal);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>La clave privada activa de firma: su <c>kid</c> y la clave ECDSA P-256. Solo Identidad la tiene; la implementa el almacén cifrado (T33) y nunca sale del proceso.</summary>
public sealed record ClavePrivadaActiva(string Kid, ECDsa Clave)
{
    public override string ToString() => "[clave privada]";
}

/// <summary>Entrega la clave privada de firma vigente (descifrada del almacén, con caché breve): el emisor y el firmador de contextos la piden en cada firma.</summary>
public interface IClavePrivadaDeFirma
{
    Task<ClavePrivadaActiva> Activa(CancellationToken cancelacion = default);
}

/// <summary>
/// El emisor de la credencial de acceso (T32; IDE-02, IDE-35): ES256 con el <c>kid</c> de la clave activa, <c>aud</c> con las dos audiencias (se reenvía al Inventario), 15 minutos de vida, el usuario, su rol, su sucursal si es
/// encargado y si su correo está verificado. Sin renovación dentro del token: la renovación es opaca y vive aparte.
/// </summary>
public sealed class Es256EmisorDeCredenciales(IClavePrivadaDeFirma claves) : IEmisorDeCredenciales
{
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(CredencialDeAcceso.VigenciaDeLaCredencialEnMinutos);

    public async Task<CredencialEmitida> Emitir(DatosDeLaCredencial datos, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(datos);
        var clave = await claves.Activa(cancelacion).ConfigureAwait(false);
        var vence = ahora + Vigencia;
        var reclamos = new Dictionary<string, object?>
        {
            ["iss"] = CredencialDeAcceso.Emisor,
            ["sub"] = datos.UsuarioId.ToString("D"),
            ["aud"] = new[] { CredencialDeAcceso.AudienciaEcommerce, CredencialDeAcceso.AudienciaInventario },
            [CredencialDeAcceso.ReclamoRol] = NombreDelRol(datos.Rol),
            [CredencialDeAcceso.ReclamoCorreoVerificado] = datos.CorreoVerificado,
            ["iat"] = ahora.ToUnixTimeSeconds(),
            ["nbf"] = ahora.ToUnixTimeSeconds(),
            ["exp"] = vence.ToUnixTimeSeconds(),
            ["jti"] = Guid.NewGuid().ToString("N"),
        };
        if (datos.Sucursal is { } sucursal)
        {
            reclamos[CredencialDeAcceso.ReclamoSucursal] = sucursal.ToString("D");
        }

        return new CredencialEmitida(JwsEs256.Firmar(clave.Clave, clave.Kid, reclamos), vence);
    }

    internal static string NombreDelRol(RolDeUsuario rol) => rol switch
    {
        RolDeUsuario.Cliente => RolesCompartidos.Cliente,
        RolDeUsuario.EncargadoDeSucursal => RolesCompartidos.EncargadoDeSucursal,
        RolDeUsuario.Logistica => RolesCompartidos.PersonalDeLogistica,
        RolDeUsuario.AdministradorCentral => RolesCompartidos.AdministradorCentral,
        _ => throw new ArgumentOutOfRangeException(nameof(rol), rol, "Rol desconocido."),
    };
}

/// <summary>Una foto de lo que se publica para verificar (IDE-61, IDE-85): las claves públicas vigentes por <c>kid</c>, los <c>kid</c> revocados y cuándo se obtuvo.</summary>
public sealed class FotoDeClavesDeVerificacion(IReadOnlyDictionary<string, ECDsa> claves, IReadOnlySet<string> revocadas, DateTimeOffset obtenidaEn)
{
    public IReadOnlyDictionary<string, ECDsa> Claves { get; } = claves;

    public IReadOnlySet<string> Revocadas { get; } = revocadas;

    public DateTimeOffset ObtenidaEn { get; } = obtenidaEn;

    /// <summary>Una foto vale hasta 5 minutos: pasado ese plazo (el JWKS no respondió) el verificador rechaza toda credencial (IDE-61).</summary>
    public bool EstaVigente(DateTimeOffset ahora) => ahora >= ObtenidaEn && ahora - ObtenidaEn <= TimeSpan.FromMinutes(CredencialDeAcceso.RenovacionDeClavesEnMinutos);
}

/// <summary>De dónde saca el verificador la foto vigente: en el E-commerce, la caché en proceso de <see cref="CacheDeClavesDeVerificacion"/>; en el Inventario, la que renueva de la URL interna configurada.</summary>
public interface IFuenteDeClavesDeVerificacion
{
    FotoDeClavesDeVerificacion? Actual { get; }
}

/// <summary>El resultado de verificar una credencial de acceso (T32): el estado y, si es válida, el usuario, su rol (el nombre de <c>Shared</c>) y su sucursal. La Web lo traduce a su propio puerto.</summary>
public sealed record CredencialLeida(EstadoDeLaCredencial Estado, Guid? UsuarioId, string? Rol, Guid? SucursalId);

public enum EstadoDeLaCredencial
{
    Valida,
    Vencida,

    /// <summary>La firma, el <c>kid</c> (revocado o desconocido), la <c>aud</c> o el formato no son válidos.</summary>
    Invalida,

    /// <summary>No hay una foto de claves de menos de 5 minutos (el JWKS no responde, IDE-61): se rechaza toda credencial sin cerrar ninguna sesión.</summary>
    SinClavesVigentes,
}

/// <summary>
/// El verificador de la credencial de acceso (T32; IDE-10, IDE-35, IDE-61): <b>fija ES256</b>, ignora <c>jwk</c>, <c>jku</c> y <c>x5u</c>, busca la clave solo por el <c>kid</c> en lo publicado, rechaza un <c>kid</c>
/// revocado o desconocido y todo <c>typ</c> (un contexto de lectura no es una credencial), exige la <c>aud</c> propia, el emisor, el rol conocido y valida la vigencia con un desfase de 30 segundos. Sin una foto de claves
/// de menos de 5 minutos, el resultado es <see cref="EstadoDeVerificacion.SinClavesVigentes"/> y se rechaza (sin cerrar ninguna sesión).
/// </summary>
public sealed class Es256VerificadorDeCredenciales(IFuenteDeClavesDeVerificacion fuente, string audienciaExigida = CredencialDeAcceso.AudienciaEcommerce)
{
    private static readonly CredencialLeida Invalida = new(EstadoDeLaCredencial.Invalida, null, null, null);

    public CredencialLeida Verificar(string credencial, DateTimeOffset ahora)
    {
        var foto = fuente.Actual;
        if (foto is null || !foto.EstaVigente(ahora))
        {
            return new CredencialLeida(EstadoDeLaCredencial.SinClavesVigentes, null, null, null);
        }

        using var token = JwsEs256.Leer(credencial);
        if (token is null || foto.Revocadas.Contains(token.Kid) || !foto.Claves.TryGetValue(token.Kid, out var clave) || !JwsEs256.FirmaValida(clave, token))
        {
            return Invalida;
        }

        var c = token.Carga;
        if (c.TryGetProperty(CredencialDeAcceso.ReclamoTipo, out _)
            || !EmisorValido(c) || !TieneLaAudiencia(c, audienciaExigida)
            || !TryInstante(c, "exp", out var vence) || !TryInstante(c, "nbf", out var desde)
            || !TryTexto(c, "sub", out var sujeto) || !Guid.TryParse(sujeto, out var usuarioId) || usuarioId == Guid.Empty
            || !TryTexto(c, CredencialDeAcceso.ReclamoRol, out var nombreDelRol) || !RolesCompartidos.Todos.Contains(nombreDelRol))
        {
            return Invalida;
        }

        Guid? sucursal = null;
        if (c.TryGetProperty(CredencialDeAcceso.ReclamoSucursal, out _))
        {
            if (!TryTexto(c, CredencialDeAcceso.ReclamoSucursal, out var texto) || !Guid.TryParse(texto, out var id) || id == Guid.Empty)
            {
                return Invalida;
            }

            sucursal = id;
        }

        var desfase = TimeSpan.FromSeconds(CredencialDeAcceso.DesfaseTolerado);
        if (ahora + desfase < desde)
        {
            return Invalida;
        }

        return ahora - desfase >= vence
            ? new CredencialLeida(EstadoDeLaCredencial.Vencida, null, null, null)
            : new CredencialLeida(EstadoDeLaCredencial.Valida, usuarioId, nombreDelRol, sucursal);
    }

    private static bool EmisorValido(JsonElement c) => TryTexto(c, "iss", out var emisor) && string.Equals(emisor, CredencialDeAcceso.Emisor, StringComparison.Ordinal);

    private static bool TieneLaAudiencia(JsonElement c, string exigida)
    {
        if (!c.TryGetProperty("aud", out var aud))
        {
            return false;
        }

        return aud.ValueKind switch
        {
            JsonValueKind.String => string.Equals(aud.GetString(), exigida, StringComparison.Ordinal),
            JsonValueKind.Array => aud.EnumerateArray().Any(a => a.ValueKind == JsonValueKind.String && string.Equals(a.GetString(), exigida, StringComparison.Ordinal)),
            _ => false,
        };
    }

    private static bool TryTexto(JsonElement c, string nombre, out string texto)
    {
        texto = string.Empty;
        if (!c.TryGetProperty(nombre, out var valor) || valor.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(valor.GetString()))
        {
            return false;
        }

        texto = valor.GetString()!;
        return true;
    }

    private static bool TryInstante(JsonElement c, string nombre, out DateTimeOffset instante)
    {
        instante = default;
        if (!c.TryGetProperty(nombre, out var valor) || valor.ValueKind != JsonValueKind.Number || !valor.TryGetInt64(out var segundos) || segundos < 0 || segundos > 253_402_300_799)
        {
            return false;
        }

        instante = DateTimeOffset.FromUnixTimeSeconds(segundos);
        return true;
    }
}

/// <summary>
/// El firmador y verificador del contexto de lectura del asistente (T26, T32; IDE-33): mismo ES256 y mismas claves que la credencial, pero con <c>aud=pedidos</c>, <c>typ=lectura-asistente</c> y el alcance único; la
/// Aplicación decide la vigencia y los reclamos. Una credencial de acceso no sirve aquí (otra <c>aud</c>, sin <c>typ</c>) ni un contexto sirve como credencial (el verificador de credenciales rechaza todo <c>typ</c>).
/// </summary>
public sealed class Es256FirmaDeContextosDeLectura(IClavePrivadaDeFirma claves, IFuenteDeClavesDeVerificacion fuente, TimeProvider? reloj = null) : IFirmaDeContextosDeLectura
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;

    public async Task<string> Firmar(ReclamosDelContexto reclamos, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(reclamos);
        var clave = await claves.Activa(cancelacion).ConfigureAwait(false);
        var carga = new Dictionary<string, object?>
        {
            ["iss"] = CredencialDeAcceso.Emisor,
            ["sub"] = reclamos.ClienteId.ToString("D"),
            ["aud"] = reclamos.Audiencia,
            [CredencialDeAcceso.ReclamoTipo] = reclamos.Tipo,
            [CredencialDeAcceso.ReclamoAlcance] = reclamos.Alcance,
            ["iat"] = reclamos.EmitidoEn.ToUnixTimeSeconds(),
            ["exp"] = reclamos.VenceEn.ToUnixTimeSeconds(),
            ["jti"] = reclamos.Identificador,
        };
        return JwsEs256.Firmar(clave.Clave, clave.Kid, carga);
    }

    public Task<ReclamosDelContexto?> Verificar(string token, CancellationToken cancelacion = default)
    {
        var foto = fuente.Actual;
        if (foto is null || !foto.EstaVigente(_reloj.GetUtcNow()))
        {
            return Task.FromResult<ReclamosDelContexto?>(null);
        }

        using var leido = JwsEs256.Leer(token);
        if (leido is null || foto.Revocadas.Contains(leido.Kid) || !foto.Claves.TryGetValue(leido.Kid, out var clave) || !JwsEs256.FirmaValida(clave, leido))
        {
            return Task.FromResult<ReclamosDelContexto?>(null);
        }

        var c = leido.Carga;
        if (!TryTexto(c, "sub", out var sujeto) || !Guid.TryParse(sujeto, out var cliente)
            || !TryTexto(c, "aud", out var audiencia) || !TryTexto(c, CredencialDeAcceso.ReclamoTipo, out var tipo) || !TryTexto(c, CredencialDeAcceso.ReclamoAlcance, out var alcance)
            || !TryTexto(c, "jti", out var identificador) || !TryInstante(c, "iat", out var emitido) || !TryInstante(c, "exp", out var vence))
        {
            return Task.FromResult<ReclamosDelContexto?>(null);
        }

        return Task.FromResult<ReclamosDelContexto?>(new ReclamosDelContexto(cliente, audiencia, tipo, alcance, emitido, vence, identificador));
    }

    private static bool TryTexto(JsonElement c, string nombre, out string texto)
    {
        texto = string.Empty;
        if (!c.TryGetProperty(nombre, out var valor) || valor.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(valor.GetString()))
        {
            return false;
        }

        texto = valor.GetString()!;
        return true;
    }

    private static bool TryInstante(JsonElement c, string nombre, out DateTimeOffset instante)
    {
        instante = default;
        if (!c.TryGetProperty(nombre, out var valor) || valor.ValueKind != JsonValueKind.Number || !valor.TryGetInt64(out var segundos) || segundos < 0 || segundos > 253_402_300_799)
        {
            return false;
        }

        instante = DateTimeOffset.FromUnixTimeSeconds(segundos);
        return true;
    }
}

/// <summary>
/// La caché en proceso de las claves de verificación (T32; IDE-61): se renueva desde <see cref="IClavesDeFirma"/> (lo publicado: la activa y la anterior mientras duren sus credenciales, y los <c>kid</c> comprometidos) y
/// <b>falla cerrada</b>: si la renovación falla, la foto anterior se conserva solo hasta que cumpla 5 minutos y después el verificador rechaza todo. Un JWK ilegible o que no sea ES256 se omite.
/// </summary>
public sealed class CacheDeClavesDeVerificacion(IClavesDeFirma publicador, TimeProvider? reloj = null) : IFuenteDeClavesDeVerificacion
{
    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private volatile FotoDeClavesDeVerificacion? _foto;

    public FotoDeClavesDeVerificacion? Actual => _foto;

    /// <summary>Renueva la caché; devuelve si lo logró. Un fallo no borra la foto anterior (sigue valiendo hasta cumplir 5 minutos).</summary>
    public async Task<bool> Renovar(CancellationToken cancelacion = default)
    {
        try
        {
            var publicadas = await publicador.Publicadas(cancelacion).ConfigureAwait(false);
            var revocadas = await publicador.Revocadas(cancelacion).ConfigureAwait(false);
            var claves = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
            foreach (var publicada in publicadas)
            {
                if (string.Equals(publicada.Algoritmo, CredencialDeAcceso.Algoritmo, StringComparison.Ordinal) && JwkEs256.TryLeerLaPublica(publicada.ParametrosPublicos, out var clave))
                {
                    claves[publicada.Kid] = clave;
                }
            }

            _foto = new FotoDeClavesDeVerificacion(claves, revocadas.ToHashSet(StringComparer.Ordinal), _reloj.GetUtcNow());
            return true;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return false;
        }
    }
}

/// <summary>El JWK público de una clave ES256 (RFC 7517, RFC 7518): <c>kty=EC</c>, <c>crv=P-256</c>, <c>x</c> e <c>y</c>; nunca la privada.</summary>
public static class JwkEs256
{
    public static string Publico(ECDsa clave, string kid)
    {
        ArgumentNullException.ThrowIfNull(clave);
        var parametros = clave.ExportParameters(includePrivateParameters: false);
        return JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["kty"] = "EC",
            ["crv"] = "P-256",
            ["x"] = Base64Url.Codificar(parametros.Q.X!),
            ["y"] = Base64Url.Codificar(parametros.Q.Y!),
            ["kid"] = kid,
            ["use"] = "sig",
            ["alg"] = CredencialDeAcceso.Algoritmo,
        });
    }

    /// <summary>Lee un JWK público ES256: <see langword="false"/> si no es EC P-256, si le falta una coordenada o si trae una privada (<c>d</c>).</summary>
    public static bool TryLeerLaPublica(string? jwk, out ECDsa clave)
    {
        clave = null!;
        if (string.IsNullOrWhiteSpace(jwk) || jwk.Length > 2048)
        {
            return false;
        }

        try
        {
            using var documento = JsonDocument.Parse(jwk, new JsonDocumentOptions { MaxDepth = 4 });
            var raiz = documento.RootElement;
            if (raiz.ValueKind != JsonValueKind.Object || raiz.TryGetProperty("d", out _)
                || Texto(raiz, "kty") != "EC" || Texto(raiz, "crv") != "P-256"
                || !Base64Url.TryDecodificar(Texto(raiz, "x") ?? string.Empty, out var x) || !Base64Url.TryDecodificar(Texto(raiz, "y") ?? string.Empty, out var y)
                || x.Length != 32 || y.Length != 32)
            {
                return false;
            }

            clave = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
            return true;
        }
        catch (Exception e) when (e is JsonException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static string? Texto(JsonElement raiz, string nombre) => raiz.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>
/// Los documentos que se publican para verificar (T32; IDE-35, IDE-61, IDE-85), tal cual los sirve <c>/.well-known/jwks.json</c> y <c>/.well-known/revocaciones</c> desde la réplica de gestión: el JWKS con solo los JWK públicos y
/// las revocaciones con los <c>kid</c> comprometidos y el instante en que se generaron. Nunca llevan una clave privada.
/// </summary>
public static class DocumentosDeClavesPublicadas
{
    public static string Jwks(IReadOnlyList<ClavePublicaDeFirma> claves)
    {
        ArgumentNullException.ThrowIfNull(claves);
        var llaves = new List<JsonElement>();
        foreach (var clave in claves)
        {
            if (!JwkEs256.TryLeerLaPublica(clave.ParametrosPublicos, out var publica))
            {
                continue;
            }

            using (publica)
            {
                using var documento = JsonDocument.Parse(JwkEs256.Publico(publica, clave.Kid));
                llaves.Add(documento.RootElement.Clone());
            }
        }

        return JsonSerializer.Serialize(new Dictionary<string, object> { ["keys"] = llaves });
    }

    public static string Revocaciones(IReadOnlyList<string> kids, DateTimeOffset generadoEn)
    {
        ArgumentNullException.ThrowIfNull(kids);
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["revocadas"] = kids, ["generadoEn"] = generadoEn.ToUnixTimeSeconds() });
    }
}

/// <summary>Donde se guarda la clave privada de firma cifrada (T33): la escritura ocurre dentro de la transacción de la rotación, junto con el juego de claves.</summary>
public interface IAlmacenDeClavesPrivadas
{
    Task Guardar(string kid, byte[] privadaCifrada, CancellationToken cancelacion = default);
}

/// <summary>
/// Genera el par ES256 de una rotación (T32; IDE-36): la <b>privada</b> se exporta en PKCS#8, se cifra con AES-GCM (propósito <c>clave-de-firma</c>, ligada a su <c>kid</c>) y se entrega al almacén; la Aplicación recibe
/// solo la clave del Dominio y el JWK público. El <c>kid</c> son 128 bits aleatorios.
/// </summary>
public sealed class Es256GeneradorDeClavesDeFirma(AesGcmCifradorDeSecretos cifrador, IAlmacenDeClavesPrivadas almacen) : IGeneradorDeClavesDeFirma
{
    public async Task<(ClaveDeFirma Clave, string ParametrosPublicos)> Generar(DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        using var par = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var kid = Base64Url.Codificar(RandomNumberGenerator.GetBytes(16));
        var privada = par.ExportPkcs8PrivateKey();
        try
        {
            var cifrada = cifrador.Cifrar(AesGcmCifradorDeSecretos.PropositoDeLaClaveDeFirma, kid, privada);
            await almacen.Guardar(kid, cifrada, cancelacion).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(privada);
        }

        return (ClaveDeFirma.Nueva(kid, ahora), JwkEs256.Publico(par, kid));
    }
}
