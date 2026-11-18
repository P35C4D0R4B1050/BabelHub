using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BabelHub.Ecommerce.Application.Catalogo.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// El emisor del token de ingreso (D04, CAT-22): <c>nonce.usuario.edicion.vence.firma</c>, con el nonce de 128 bits (la clave de idempotencia del ingreso), el usuario y la edición a los que sirve y 30 minutos de vigencia, firmado con HMAC-SHA256
/// con la clave propia del entorno (<c>BABELHUB__Catalogo__ClaveDeIngresos</c>). Para rotar la clave, <c>…ClaveDeIngresosAnterior</c> sigue verificando los tokens ya emitidos sin firmar nada nuevo. La comparación es en <b>tiempo constante</b>
/// y se evalúan siempre ambas claves; un token manipulado, de otro usuario o de otra edición, vencido (a los 30 minutos exactos ya lo está) o firmado con otra clave devuelve <see langword="null"/> sin distinguir la causa. Un token es de un solo uso
/// por construcción: su nonce es la clave única de <c>ingresos_solicitados</c>.
/// </summary>
public sealed class EmisorDeClavesDeIngresoHmac : IEmisorDeClavesDeIngreso
{
    /// <summary>Los 30 minutos de vigencia del token.</summary>
    public static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(30);

    /// <summary>La clave mide al menos 32 bytes (como las demás claves del servicio).</summary>
    public const int LongitudMinimaDeLaClave = 32;

    private readonly byte[] _clave;
    private readonly byte[]? _anterior;

    public EmisorDeClavesDeIngresoHmac(string clave, string? claveAnterior = null)
    {
        _clave = Validar(clave, nameof(clave));
        _anterior = string.IsNullOrWhiteSpace(claveAnterior) ? null : Validar(claveAnterior, nameof(claveAnterior));
    }

    public TokenDeIngresoEmitido Emitir(Guid usuarioId, Guid edicionId, DateTimeOffset ahora)
    {
        var nonce = Guid.NewGuid();
        var vence = ahora + Vigencia;
        var cuerpo = string.Create(CultureInfo.InvariantCulture, $"{nonce:N}.{usuarioId:N}.{edicionId:N}.{vence.ToUnixTimeSeconds()}");
        return new TokenDeIngresoEmitido($"{cuerpo}.{Firmar(_clave, cuerpo)}", nonce, vence);
    }

    public Guid? Verificar(string? token, Guid usuarioId, Guid edicionId, DateTimeOffset ahora)
    {
        var partes = token?.Split('.');
        if (partes is not { Length: 5 }
            || !Guid.TryParseExact(partes[0], "N", out var nonce)
            || !Guid.TryParseExact(partes[1], "N", out var usuario)
            || !Guid.TryParseExact(partes[2], "N", out var edicion)
            || !long.TryParse(partes[3], NumberStyles.None, CultureInfo.InvariantCulture, out var vence))
        {
            return null;
        }

        var cuerpo = string.Join('.', partes[..4]);
        var recibida = Encoding.ASCII.GetBytes(partes[4]);

        // Se evalúan las dos claves siempre, para que el tiempo no diga con cuál se firmó.
        var conLaActual = CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Firmar(_clave, cuerpo)), recibida);
        var conLaAnterior = _anterior is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Firmar(_anterior, cuerpo)), recibida);
        if (!(conLaActual | conLaAnterior) || usuario != usuarioId || edicion != edicionId || ahora.ToUnixTimeSeconds() >= vence)
        {
            return null;
        }

        return nonce;
    }

    private static string Firmar(byte[] clave, string cuerpo) => Convert.ToHexStringLower(HMACSHA256.HashData(clave, Encoding.UTF8.GetBytes(cuerpo)));

    private static byte[] Validar(string clave, string nombre)
    {
        var bytes = Encoding.UTF8.GetBytes(clave ?? throw new ArgumentNullException(nombre));
        return bytes.Length >= LongitudMinimaDeLaClave ? bytes : throw new ArgumentException($"La clave de firma de los tokens de ingreso mide al menos {LongitudMinimaDeLaClave} bytes.", nombre);
    }
}
