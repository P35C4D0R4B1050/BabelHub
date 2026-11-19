using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Shared.Contratos;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>
/// La lectura del JWS compacto con ES256 (identidad-seguridad T41; IDE-35). Solo <b>lee</b>: el Inventario no tiene ni puede tener código que firme, porque la clave privada solo la posee Identidad. La cabecera debe ser
/// exactamente ES256 con un <c>kid</c> de texto: cualquier otro algoritmo (incluido <c>none</c>), <c>crit</c> o una firma de otro largo se rechaza, y <c>jwk</c>, <c>jku</c>, <c>x5u</c> y <c>x5c</c> se ignoran: la clave sale
/// de lo publicado, nunca de lo que la credencial dice de sí misma.
/// </summary>
internal static class LectorDeJwsEs256
{
    public const int LargoMaximoDelToken = 4096;

    private const int BytesDeLaFirma = 64;

    public sealed record TokenLeido(string Kid, string Firmado, byte[] Firma, JsonElement Carga, JsonDocument Documento) : IDisposable
    {
        public void Dispose() => Documento.Dispose();
    }

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
internal static class Base64Url
{
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

/// <summary>Lee un JWK público EC P-256 (RFC 7517, RFC 7518): <see langword="false"/> si no es esa curva, si le falta una coordenada o si trae una clave privada (<c>d</c>).</summary>
internal static class JwkPublicoEs256
{
    public static bool TryLeer(JsonElement raiz, out ECDsa clave)
    {
        clave = null!;
        var alg = Texto(raiz, "alg");
        if (raiz.ValueKind != JsonValueKind.Object || raiz.TryGetProperty("d", out _)
            || Texto(raiz, "kty") != "EC" || Texto(raiz, "crv") != "P-256"
            || (!string.IsNullOrEmpty(alg) && alg != CredencialDeAcceso.Algoritmo)
            || !Base64Url.TryDecodificar(Texto(raiz, "x") ?? string.Empty, out var x) || !Base64Url.TryDecodificar(Texto(raiz, "y") ?? string.Empty, out var y)
            || x.Length != 32 || y.Length != 32)
        {
            return false;
        }

        try
        {
            clave = ECDsa.Create(new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = x, Y = y } });
            return true;
        }
        catch (Exception e) when (e is CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    public static string? Texto(JsonElement raiz, string nombre) =>
        raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty(nombre, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

/// <summary>Una copia de lo que se publica para verificar (IDE-61, IDE-85): las claves públicas por <c>kid</c>, los <c>kid</c> revocados y cuándo se obtuvo.</summary>
public sealed class CopiaDeClavesDeVerificacion(IReadOnlyDictionary<string, ECDsa> claves, IReadOnlySet<string> revocadas, DateTimeOffset obtenidaEn)
{
    public IReadOnlyDictionary<string, ECDsa> Claves { get; } = claves;

    public IReadOnlySet<string> Revocadas { get; } = revocadas;

    public DateTimeOffset ObtenidaEn { get; } = obtenidaEn;

    /// <summary>Una copia vale hasta 5 minutos desde que se obtuvo; pasado ese plazo (la réplica no respondió) el verificador rechaza toda credencial (IDE-61).</summary>
    public bool EstaVigente(DateTimeOffset ahora) =>
        ahora + TimeSpan.FromSeconds(CredencialDeAcceso.DesfaseToleradoEnSegundos) >= ObtenidaEn && ahora - ObtenidaEn <= TimeSpan.FromMinutes(CredencialDeAcceso.RenovacionDeClavesEnMinutos);
}

/// <summary>De dónde saca el verificador la copia vigente.</summary>
public interface IFuenteDeClavesDeVerificacion
{
    CopiaDeClavesDeVerificacion? Actual { get; }
}

/// <summary>
/// El verificador de la credencial de acceso del Inventario (T41; IDE-10, IDE-35, IDE-61): <b>fija ES256</b>, busca la clave solo por el <c>kid</c> en lo publicado, rechaza un <c>kid</c> revocado o desconocido, todo
/// <c>typ</c> (el contexto de lectura del asistente no es una credencial) y toda <c>aud</c> que no contenga <c>babelhub-inventario</c> (la del contexto de lectura es <c>pedidos</c>), y exige el emisor, un rol conocido y
/// la vigencia con un desfase de 30 segundos. Sin una copia de claves de menos de 5 minutos <b>rechaza toda credencial</b>.
/// </summary>
public sealed class VerificadorDeCredencialesDelInventario(IFuenteDeClavesDeVerificacion fuente) : IVerificadorDeCredenciales
{
    private static readonly CredencialLeida Invalida = new(EstadoDeLaCredencial.Invalida, null, null, null);

    public CredencialLeida Verificar(string? credencial, DateTimeOffset ahora)
    {
        var copia = fuente.Actual;
        if (copia is null || !copia.EstaVigente(ahora))
        {
            return new CredencialLeida(EstadoDeLaCredencial.SinClavesVigentes, null, null, null);
        }

        using var token = LectorDeJwsEs256.Leer(credencial);
        if (token is null || copia.Revocadas.Contains(token.Kid) || !copia.Claves.TryGetValue(token.Kid, out var clave) || !LectorDeJwsEs256.FirmaValida(clave, token))
        {
            return Invalida;
        }

        var c = token.Carga;
        if (c.TryGetProperty(CredencialDeAcceso.ReclamoTipo, out _)
            || !TryTexto(c, "iss", out var emisor) || !string.Equals(emisor, CredencialDeAcceso.Emisor, StringComparison.Ordinal)
            || !TieneLaAudiencia(c, CredencialDeAcceso.AudienciaInventario)
            || !TryInstante(c, "exp", out var vence) || !TryInstante(c, "nbf", out var desde)
            || !TryTexto(c, "sub", out var sujeto) || !Guid.TryParse(sujeto, out var usuarioId) || usuarioId == Guid.Empty
            || !TryTexto(c, CredencialDeAcceso.ReclamoRol, out var rol) || !RolesDeUsuario.Todos.Contains(rol))
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

        var desfase = TimeSpan.FromSeconds(CredencialDeAcceso.DesfaseToleradoEnSegundos);
        if (vence <= desde || vence - desde > TimeSpan.FromMinutes(CredencialDeAcceso.VigenciaDeLaCredencialEnMinutos) + desfase || ahora + desfase < desde)
        {
            return Invalida;
        }

        return ahora - desfase >= vence
            ? new CredencialLeida(EstadoDeLaCredencial.Vencida, null, null, null)
            : new CredencialLeida(EstadoDeLaCredencial.Valida, usuarioId, rol, sucursal);
    }

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
        texto = JwkPublicoEs256.Texto(c, nombre) ?? string.Empty;
        return texto.Length > 0;
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

/// <summary>Lo que el Inventario obtiene de la réplica de gestión de su propia VM (IDE-85): el JWKS y las revocaciones, como texto JSON sin interpretar.</summary>
public interface IObtenedorDeClavesPublicadas
{
    Task<string> ObtenerJwks(CancellationToken cancelacion = default);

    Task<string> ObtenerRevocaciones(CancellationToken cancelacion = default);
}

/// <summary>
/// La caché en proceso de las claves de verificación del Inventario (T41; IDE-61, IDE-85): se renueva desde lo que sirve la réplica de gestión y <b>falla cerrada</b>: si la renovación falla, la copia anterior se conserva
/// solo hasta cumplir 5 minutos y después el verificador rechaza toda credencial. Un JWK ilegible, que no sea ES256 o que traiga una privada se omite; un documento de revocaciones generado hace más de 5 minutos (una
/// respuesta viciada o repetida) no sirve; y una copia sin ninguna clave utilizable no reemplaza a la anterior.
/// </summary>
public sealed class CacheDeClavesDelInventario(IObtenedorDeClavesPublicadas obtenedor, TimeProvider? reloj = null) : IFuenteDeClavesDeVerificacion
{
    public const int MaximoDeClaves = 16;

    public const int MaximoDeRevocadas = 64;

    private readonly TimeProvider _reloj = reloj ?? TimeProvider.System;
    private volatile CopiaDeClavesDeVerificacion? _copia;

    public CopiaDeClavesDeVerificacion? Actual => _copia;

    /// <summary>Renueva la caché; devuelve si lo logró. Un fallo no borra la copia anterior (sigue valiendo hasta cumplir 5 minutos).</summary>
    public async Task<bool> Renovar(CancellationToken cancelacion = default)
    {
        try
        {
            var jwks = await obtenedor.ObtenerJwks(cancelacion).ConfigureAwait(false);
            var revocaciones = await obtenedor.ObtenerRevocaciones(cancelacion).ConfigureAwait(false);
            var ahora = _reloj.GetUtcNow();
            var revocadas = LeerRevocaciones(revocaciones, ahora);
            var claves = LeerClaves(jwks);
            if (revocadas is null || claves.Count == 0)
            {
                return false;
            }

            _copia = new CopiaDeClavesDeVerificacion(claves, revocadas, ahora);
            return true;
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            return false;
        }
    }

    private static Dictionary<string, ECDsa> LeerClaves(string jwks)
    {
        var claves = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        using var documento = JsonDocument.Parse(jwks, new JsonDocumentOptions { MaxDepth = 6 });
        if (documento.RootElement.ValueKind != JsonValueKind.Object || !documento.RootElement.TryGetProperty("keys", out var llaves) || llaves.ValueKind != JsonValueKind.Array)
        {
            return claves;
        }

        foreach (var llave in llaves.EnumerateArray().Take(MaximoDeClaves))
        {
            var kid = JwkPublicoEs256.Texto(llave, "kid");
            if (!string.IsNullOrEmpty(kid) && JwkPublicoEs256.TryLeer(llave, out var clave))
            {
                claves[kid] = clave;
            }
        }

        return claves;
    }

    private static HashSet<string>? LeerRevocaciones(string revocaciones, DateTimeOffset ahora)
    {
        using var documento = JsonDocument.Parse(revocaciones, new JsonDocumentOptions { MaxDepth = 6 });
        var raiz = documento.RootElement;
        if (raiz.ValueKind != JsonValueKind.Object || !raiz.TryGetProperty("revocadas", out var lista) || lista.ValueKind != JsonValueKind.Array
            || !raiz.TryGetProperty("generadoEn", out var generado) || generado.ValueKind != JsonValueKind.Number || !generado.TryGetInt64(out var segundos) || segundos < 0 || segundos > 253_402_300_799)
        {
            return null;
        }

        var generadoEn = DateTimeOffset.FromUnixTimeSeconds(segundos);
        var plazo = TimeSpan.FromMinutes(CredencialDeAcceso.RenovacionDeClavesEnMinutos);
        var desfase = TimeSpan.FromSeconds(CredencialDeAcceso.DesfaseToleradoEnSegundos);
        if (ahora - generadoEn > plazo || generadoEn - ahora > desfase || lista.GetArrayLength() > MaximoDeRevocadas)
        {
            return null;
        }

        var kids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var elemento in lista.EnumerateArray())
        {
            if (elemento.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(elemento.GetString()))
            {
                return null;
            }

            kids.Add(elemento.GetString()!);
        }

        return kids;
    }
}
