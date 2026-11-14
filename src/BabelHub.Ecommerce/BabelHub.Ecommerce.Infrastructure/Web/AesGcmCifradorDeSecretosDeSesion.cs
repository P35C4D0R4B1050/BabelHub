using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Web.Interno;
using BabelHub.Ecommerce.Domain.Web;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// <see cref="ICifradorDeSecretosDeSesion"/> con AES-256-GCM (T11, D02, plan §6). De la <b>clave maestra del host</b> (<see cref="ConfiguracionDeSesionesWeb"/>) se derivan con
/// HKDF-SHA256 subclaves <b>por propósito</b> (credencial, renovación, borrador, claves de protección): descifrar un texto con la subclave de otro propósito falla. Cada texto
/// se liga con datos asociados a su fila (<c>id_hash</c>, propósito y usuario; <c>id</c>, usuario y plantilla en los borradores) y al host: moverlo a otra fila, a otro
/// propósito o a la base del otro host no lo descifra. El texto cifrado es <c>[formato 1][nonce 12][cifrado][etiqueta 16]</c> con un nonce aleatorio por cifrado.
/// </summary>
/// <remarks>
/// Rotación en dos pasos: con <see cref="ConfiguracionDeSesionesWeb.Anterior"/> se descifra con la clave anterior (por su identificador) y se cifra siempre con la vigente; quien
/// guarda <see cref="TextoCifrado.VersionDeClave"/> en la fila sabe cuándo difiere de <see cref="VersionVigente"/> y debe re-cifrar. Al retirar la anterior, lo que no se
/// re-cifró deja de poder abrirse: <see cref="SecretoDeSesionIlegibleException"/> (sesión inválida, nunca un error 500).
/// </remarks>
public sealed class AesGcmCifradorDeSecretosDeSesion : ICifradorDeSecretosDeSesion
{
    private const byte Formato = 1;
    private const int LongitudDelNonce = 12;
    private const int LongitudDeLaEtiqueta = 16;
    private const int LongitudDeLaSubclave = 32;
    private static readonly byte[] Sal = "babelhub/web/sesiones/v1"u8.ToArray();

    private readonly HostDeSesion _host;
    private readonly Dictionary<short, byte[][]> _subclavesPorIdentificador = [];

    public AesGcmCifradorDeSecretosDeSesion(ConfiguracionDeSesionesWeb claves)
    {
        ArgumentNullException.ThrowIfNull(claves);
        _host = claves.Host;
        VersionVigente = ConfiguracionDeSesionesWeb.IdentificadorDeClave(claves.Vigente);
        _subclavesPorIdentificador[VersionVigente] = Derivar(claves.Vigente);
        if (claves.Anterior is not null)
        {
            _subclavesPorIdentificador[ConfiguracionDeSesionesWeb.IdentificadorDeClave(claves.Anterior)] = Derivar(claves.Anterior);
        }
    }

    /// <summary>El identificador de la clave con la que se cifra.</summary>
    public short VersionVigente { get; }

    public TextoCifrado Cifrar(string texto, PropositoDeSecreto proposito, string datosAsociados)
    {
        ArgumentNullException.ThrowIfNull(texto);
        ArgumentNullException.ThrowIfNull(datosAsociados);

        var claro = Encoding.UTF8.GetBytes(texto);
        var salida = new byte[1 + LongitudDelNonce + claro.Length + LongitudDeLaEtiqueta];
        salida[0] = Formato;
        var nonce = salida.AsSpan(1, LongitudDelNonce);
        RandomNumberGenerator.Fill(nonce);
        var cifrado = salida.AsSpan(1 + LongitudDelNonce, claro.Length);
        var etiqueta = salida.AsSpan(1 + LongitudDelNonce + claro.Length, LongitudDeLaEtiqueta);

        using var aes = new AesGcm(_subclavesPorIdentificador[VersionVigente][(int)proposito], LongitudDeLaEtiqueta);
        aes.Encrypt(nonce, claro, cifrado, etiqueta, Asociados(proposito, datosAsociados));
        CryptographicOperations.ZeroMemory(claro);
        return new TextoCifrado(salida, VersionVigente);
    }

    public string Descifrar(byte[] cifrado, short versionDeClave, PropositoDeSecreto proposito, string datosAsociados)
    {
        ArgumentNullException.ThrowIfNull(cifrado);
        ArgumentNullException.ThrowIfNull(datosAsociados);

        if (!_subclavesPorIdentificador.TryGetValue(versionDeClave, out var subclaves)
            || cifrado.Length < 1 + LongitudDelNonce + LongitudDeLaEtiqueta
            || cifrado[0] != Formato)
        {
            throw new SecretoDeSesionIlegibleException();
        }

        var largo = cifrado.Length - 1 - LongitudDelNonce - LongitudDeLaEtiqueta;
        var claro = new byte[largo];
        try
        {
            using var aes = new AesGcm(subclaves[(int)proposito], LongitudDeLaEtiqueta);
            aes.Decrypt(
                cifrado.AsSpan(1, LongitudDelNonce),
                cifrado.AsSpan(1 + LongitudDelNonce, largo),
                cifrado.AsSpan(1 + LongitudDelNonce + largo, LongitudDeLaEtiqueta),
                claro,
                Asociados(proposito, datosAsociados));
            return Encoding.UTF8.GetString(claro);
        }
        catch (CryptographicException)
        {
            // Etiqueta que no coincide: otra clave, otro propósito, otra fila o un texto manipulado. Nunca se explica cuál.
            throw new SecretoDeSesionIlegibleException();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(claro);
        }
    }

    private static byte[][] Derivar(byte[] maestra)
    {
        var propositos = Enum.GetValues<PropositoDeSecreto>();
        var subclaves = new byte[propositos.Length][];
        foreach (var proposito in propositos)
        {
            subclaves[(int)proposito] = HKDF.DeriveKey(HashAlgorithmName.SHA256, maestra, LongitudDeLaSubclave, Sal, Encoding.UTF8.GetBytes($"babelhub/web/sesiones/{proposito}"));
        }

        return subclaves;
    }

    /// <summary>Los datos asociados incluyen el host y el propósito además de la fila: dos filas, dos propósitos o dos hosts nunca comparten el mismo contexto.</summary>
    private byte[] Asociados(PropositoDeSecreto proposito, string datosAsociados) =>
        Encoding.UTF8.GetBytes($"babelhub/web/{(_host == HostDeSesion.Tienda ? "tienda" : "gestion")}/{proposito}|{datosAsociados}");
}
