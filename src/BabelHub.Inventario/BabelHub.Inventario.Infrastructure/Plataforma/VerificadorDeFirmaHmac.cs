using System.Security.Cryptography;
using System.Text;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>
/// Secretos de firma de las integraciones, uno o dos por proveedor (el actual y, durante una rotación, el anterior). Cada secreto
/// es de al menos 32 bytes y viene del entorno (<c>BABELHUB__Integraciones__&lt;Proveedor&gt;__Secreto</c>).
/// </summary>
public sealed class OpcionesDeFirmaDeIntegraciones
{
    public const int LongitudMinimaDelSecreto = 32;

    private readonly Dictionary<string, IReadOnlyList<byte[]>> _secretos = new(StringComparer.Ordinal);

    public OpcionesDeFirmaDeIntegraciones(IEnumerable<KeyValuePair<string, IReadOnlyList<byte[]>>> secretosPorProveedor)
    {
        ArgumentNullException.ThrowIfNull(secretosPorProveedor);
        foreach (var (proveedor, secretos) in secretosPorProveedor)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(proveedor);
            if (secretos.Count is 0 or > 2)
            {
                throw new ArgumentException($"El proveedor «{proveedor}» necesita un secreto y, durante una rotación, dos.", nameof(secretosPorProveedor));
            }

            if (secretos.Any(s => s.Length < LongitudMinimaDelSecreto))
            {
                throw new ArgumentException($"El secreto de «{proveedor}» debe tener al menos {LongitudMinimaDelSecreto} bytes.", nameof(secretosPorProveedor));
            }

            if (!_secretos.TryAdd(proveedor, secretos))
            {
                throw new ArgumentException($"El proveedor «{proveedor}» está repetido.", nameof(secretosPorProveedor));
            }
        }
    }

    internal IReadOnlyList<byte[]>? SecretosDe(string proveedor) => _secretos.GetValueOrDefault(proveedor);
}

/// <summary>
/// Verifica la firma de una entrada de integración (SEG-12): HMAC-SHA256 sobre «{marca}.{cuerpo crudo}» con el secreto del proveedor,
/// en hexadecimal (con o sin el prefijo <c>sha256=</c>), comparada en tiempo constante. La marca va dentro de lo firmado para que
/// una entrada vieja no se pueda reutilizar con una marca nueva; su vigencia la comprueba <c>ProcesarEntradaDeIntegracion</c>.
/// </summary>
public sealed class VerificadorDeFirmaHmac(OpcionesDeFirmaDeIntegraciones opciones) : IVerificadorDeFirma
{
    private const string Prefijo = "sha256=";

    public bool Verificar(string proveedor, string marcaDeTiempo, ReadOnlyMemory<byte> cuerpo, string firma)
    {
        if (string.IsNullOrEmpty(proveedor) || string.IsNullOrEmpty(marcaDeTiempo) || string.IsNullOrEmpty(firma))
        {
            return false;
        }

        var secretos = opciones.SecretosDe(proveedor);
        if (secretos is null)
        {
            return false;
        }

        var recibida = firma.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase) ? firma[Prefijo.Length..] : firma;
        byte[] esperadoEnBytes;
        try
        {
            esperadoEnBytes = Convert.FromHexString(recibida);
        }
        catch (FormatException)
        {
            return false;
        }

        var prefijoFirmado = Encoding.UTF8.GetBytes(marcaDeTiempo + ".");
        var mensaje = new byte[prefijoFirmado.Length + cuerpo.Length];
        prefijoFirmado.CopyTo(mensaje, 0);
        cuerpo.Span.CopyTo(mensaje.AsSpan(prefijoFirmado.Length));

        // Se prueban todos los secretos sin cortar al primer acierto, para que el tiempo no diga cuál coincidió.
        var valida = false;
        foreach (var secreto in secretos)
        {
            var calculada = HMACSHA256.HashData(secreto, mensaje);
            valida |= CryptographicOperations.FixedTimeEquals(calculada, esperadoEnBytes);
        }

        return valida;
    }
}
