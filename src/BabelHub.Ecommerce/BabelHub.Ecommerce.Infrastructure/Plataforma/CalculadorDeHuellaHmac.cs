using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>
/// Claves de las huellas (SEG-25) tal como llegan del entorno: <c>versión:clave en base64</c>, de al menos 32 bytes
/// (<c>BABELHUB__Seguridad__ClaveDeHuella</c>). Al rotar, la clave anterior sigue configurada
/// (<c>BABELHUB__Seguridad__ClaveDeHuellaAnterior</c>) durante 24 horas para no reiniciar los contadores.
/// </summary>
public sealed partial class OpcionesDeHuella
{
    public const int LongitudMinimaDeLaClave = 32;

    public OpcionesDeHuella(string claveActual, string? claveAnterior = null)
    {
        Actual = Analizar(claveActual, nameof(claveActual));
        Anterior = string.IsNullOrWhiteSpace(claveAnterior) ? null : Analizar(claveAnterior, nameof(claveAnterior));

        if (Anterior is { } anterior)
        {
            if (string.Equals(anterior.Version, Actual.Version, StringComparison.Ordinal))
            {
                throw new ArgumentException("La clave anterior debe tener otra versión que la actual.", nameof(claveAnterior));
            }

            if (CryptographicOperations.FixedTimeEquals(anterior.Clave, Actual.Clave))
            {
                throw new ArgumentException("La clave anterior no puede ser igual a la actual.", nameof(claveAnterior));
            }
        }
    }

    internal ClaveVersionada Actual { get; }

    internal ClaveVersionada? Anterior { get; }

    private static ClaveVersionada Analizar(string? valor, string nombre)
    {
        var separador = valor?.IndexOf(':', StringComparison.Ordinal) ?? -1;
        if (valor is null || separador <= 0)
        {
            throw new ArgumentException("La clave de huella tiene el formato «versión:clave en base64».", nombre);
        }

        var version = valor[..separador];
        if (!FormaDeLaVersion().IsMatch(version))
        {
            throw new ArgumentException("La versión de la clave son 1 a 16 letras o dígitos.", nombre);
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(valor[(separador + 1)..]);
        }
        catch (FormatException)
        {
            throw new ArgumentException("La clave de huella no es base64 válido.", nombre);
        }

        return bytes.Length < LongitudMinimaDeLaClave
            ? throw new ArgumentException($"La clave de huella debe tener al menos {LongitudMinimaDeLaClave} bytes.", nombre)
            : new ClaveVersionada(version, bytes);
    }

    [GeneratedRegex("^[A-Za-z0-9]{1,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDeLaVersion();

    internal sealed record ClaveVersionada(string Version, byte[] Clave);
}

/// <summary>
/// Calcula las huellas con HMAC-SHA256 y la clave secreta versionada del entorno: sin la clave no se puede recuperar la dirección ni
/// el dato original (un hash simple de un espacio tan chico como el de IPv4 se revierte con un diccionario). Cada propósito es un
/// dominio aparte, los valores se normalizan antes (NFKC y minúsculas invariantes; el origen IPv6 por /64) y el resultado es
/// base64url, con la versión de la clave, que es lo único que llega a los contadores (AC-SEG-22).
/// </summary>
public sealed class CalculadorDeHuellaHmac(OpcionesDeHuella opciones) : ICalculadorDeHuella
{
    public const string PropositoDelOrigen = "origen";
    public const string PropositoDeLaRed48 = "origen-red48";

    private const char Separador = '\u001f';

    public HuellaDeOrigen DeOrigen(string direccion) =>
        HuellaDeOrigen.Crear(Calcular(opciones.Actual, PropositoDelOrigen, Normalizar(direccion, bits: 64)), opciones.Actual.Version);

    public HuellaDeOrigen? DeOrigenPorRed48(string direccion)
    {
        var ip = Analizar(direccion);
        return ip.AddressFamily == AddressFamily.InterNetworkV6
            ? HuellaDeOrigen.Crear(Calcular(opciones.Actual, PropositoDeLaRed48, Normalizar(direccion, bits: 48)), opciones.Actual.Version)
            : null;
    }

    public IReadOnlyList<HuellaDeOrigen> DeOrigenEnRotacion(string direccion)
    {
        var normalizada = Normalizar(direccion, bits: 64);
        var huellas = new List<HuellaDeOrigen> { HuellaDeOrigen.Crear(Calcular(opciones.Actual, PropositoDelOrigen, normalizada), opciones.Actual.Version) };
        if (opciones.Anterior is { } anterior)
        {
            huellas.Add(HuellaDeOrigen.Crear(Calcular(anterior, PropositoDelOrigen, normalizada), anterior.Version));
        }

        return huellas;
    }

    public HuellaDeSujeto DeSujeto(string valor, string proposito) =>
        HuellaDeSujeto.Crear(Calcular(opciones.Actual, NormalizarProposito(proposito), NormalizarTexto(valor)), opciones.Actual.Version);

    public IReadOnlyList<HuellaDeSujeto> DeSujetoEnRotacion(string valor, string proposito)
    {
        var normalizado = NormalizarTexto(valor);
        var huellas = new List<HuellaDeSujeto> { HuellaDeSujeto.Crear(Calcular(opciones.Actual, NormalizarProposito(proposito), normalizado), opciones.Actual.Version) };
        if (opciones.Anterior is { } anterior)
        {
            huellas.Add(HuellaDeSujeto.Crear(Calcular(anterior, NormalizarProposito(proposito), normalizado), anterior.Version));
        }

        return huellas;
    }

    private static string NormalizarProposito(string proposito)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(proposito);
        return proposito.Trim().ToLowerInvariant();
    }

    private static string Calcular(OpcionesDeHuella.ClaveVersionada clave, string proposito, string valorNormalizado)
    {
        var mensaje = Encoding.UTF8.GetBytes(proposito + Separador + valorNormalizado);
        var resumen = HMACSHA256.HashData(clave.Clave, mensaje);
        return Convert.ToBase64String(resumen).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string NormalizarTexto(string valor)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(valor);
        return valor.Trim().Normalize(NormalizationForm.FormKC).ToLowerInvariant();
    }

    private static IPAddress Analizar(string direccion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(direccion);
        if (!IPAddress.TryParse(direccion.Trim(), out var ip))
        {
            throw new ArgumentException("La dirección de origen no es una IP válida.", nameof(direccion));
        }

        // Una IPv4 que llega mapeada en IPv6 (::ffff:203.0.113.7) es la misma persona que la IPv4 directa.
        return ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;
    }

    /// <summary>IPv4 completa; IPv6 truncada a su prefijo (por defecto /64, la red de un hogar: cambiar de dirección dentro de ella no esquiva el límite).</summary>
    private static string Normalizar(string direccion, int bits)
    {
        var ip = Analizar(direccion);
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return "4:" + ip.ToString();
        }

        var bytes = ip.GetAddressBytes();
        var prefijo = new byte[bits / 8];
        Array.Copy(bytes, prefijo, prefijo.Length);
        return $"6/{bits}:" + Convert.ToHexString(prefijo).ToLowerInvariant();
    }
}
