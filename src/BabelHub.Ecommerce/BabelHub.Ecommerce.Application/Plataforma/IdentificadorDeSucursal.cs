using System.Security.Cryptography;
using System.Text;

namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// La equivalencia canónica entre el <b>código</b> de una sucursal (<c>ayacucho</c>) y su <b>identificador</b> (un <c>Guid</c>), D20 de <c>docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md</c>: el identificador se <b>deriva</b>
/// del código (SHA-256 de <c>babelhub:sucursal:&lt;código en minúsculas&gt;</c>, sus primeros 16 bytes), así Identidad (el <c>SucursalId</c> del token del encargado), Pedidos y cualquier otro módulo llegan al mismo valor sin una tabla
/// ni una llamada. <b>Falla cerrado</b>: un código vacío o que no tiene la forma de un código de sucursal (letras, dígitos y guiones, hasta 40) no se resuelve. Un único helper: no se reimplementa en otro lugar.
/// </summary>
public static class IdentificadorDeSucursal
{
    public const int LargoMaximoDelCodigo = 40;

    private const string Prefijo = "babelhub:sucursal:";

    /// <summary>El identificador del código, o <see langword="null"/> si el código no tiene la forma de un código de sucursal. Insensible a mayúsculas y espacios en los extremos.</summary>
    public static Guid? DesdeCodigo(string? codigoDeSucursal)
    {
        // La forma ASCII se comprueba sobre el texto recortado <b>antes</b> de pasar a minúsculas: un carácter que se pliega a ASCII (el signo Kelvin, U+212A) no se cuela como una `k`.
        var recortado = codigoDeSucursal?.Trim();
        if (string.IsNullOrEmpty(recortado) || recortado.Length > LargoMaximoDelCodigo || !recortado.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            return null;
        }

        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(Prefijo + recortado.ToLowerInvariant())).AsSpan(0, 16));
    }
}
