using System.Security.Cryptography;
using System.Text;
using BabelHub.Inventario.Application.Reservas;

namespace BabelHub.Inventario.Infrastructure.Reservas;

/// <summary>
/// <see cref="IGeneradorDeIdentificadorAnonimo"/> (D09, SEL-34): <c>HMAC-SHA256(clave, "baja:v1:" + titularId)</c> truncado a 128 bits y devuelto como GUID. La clave sale de <c>BABELHUB__Inventario__ClaveDeBajas</c> (mínimo
/// 32 bytes, con variedad de caracteres, solo del Inventario y distinta de cualquier otro secreto del servicio) y <b>no se rota</b>: cambiarla rompería la repetibilidad de la baja. El prefijo <c>baja:v1:</c> separa el dominio:
/// la misma clave nunca produce el mismo valor para otro uso. El mismo titular da siempre el mismo identificador, titulares distintos dan identificadores distintos y, sin la clave, el original no se recupera.
/// </summary>
public sealed class HmacGeneradorDeIdentificadorAnonimo : IGeneradorDeIdentificadorAnonimo
{
    /// <summary>La ruta de configuración de la clave; la variable de entorno es <c>BABELHUB__Inventario__ClaveDeBajas</c>.</summary>
    public const string Ruta = "Inventario:ClaveDeBajas";

    /// <summary>Los bytes mínimos de la clave.</summary>
    public const int LongitudMinima = 32;

    /// <summary>La separación de dominio de la versión actual.</summary>
    public const string Dominio = "baja:v1:";

    private readonly byte[] _clave;

    /// <param name="clave">La clave, de al menos 32 bytes UTF-8.</param>
    /// <exception cref="ArgumentException">La clave es corta (el mensaje no incluye su valor).</exception>
    public HmacGeneradorDeIdentificadorAnonimo(string clave)
    {
        ArgumentNullException.ThrowIfNull(clave);
        if (Encoding.UTF8.GetByteCount(clave) < LongitudMinima)
        {
            throw new ArgumentException($"La clave de bajas debe tener al menos {LongitudMinima} bytes.", nameof(clave));
        }

        _clave = Encoding.UTF8.GetBytes(clave);
    }

    public Guid Generar(Guid titularId)
    {
        if (titularId == Guid.Empty)
        {
            throw new ArgumentException("El titular necesita su identificador.", nameof(titularId));
        }

        var mensaje = Encoding.UTF8.GetBytes(Dominio + titularId.ToString("D"));
        Span<byte> huella = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(_clave, mensaje, huella);
        return new Guid(huella[..16]);
    }
}
