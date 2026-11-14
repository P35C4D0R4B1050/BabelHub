using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary><see cref="IAleatoriosSeguros"/> con el generador criptográfico del sistema: el valor de la cookie y la marca de dispositivo nunca salen de un <c>Random</c>.</summary>
public sealed class AleatoriosSeguros : IAleatoriosSeguros
{
    public byte[] Bytes(int cantidad)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cantidad, 1);
        return RandomNumberGenerator.GetBytes(cantidad);
    }
}
