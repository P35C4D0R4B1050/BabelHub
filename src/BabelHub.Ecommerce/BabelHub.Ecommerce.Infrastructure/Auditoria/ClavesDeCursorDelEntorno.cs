using System.Text.RegularExpressions;
using BabelHub.Ecommerce.Application.Auditoria.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Auditoria;

/// <summary>
/// <see cref="IClavesDeCursor"/> desde el entorno (T25, T26): <c>BABELHUB__Auditoria__ClaveDeCursor</c> (la vigente) y su versión, y la anterior opcional para la rotación en dos pasos. Una clave tiene al
/// menos 32 bytes, la versión usa solo <c>[A-Za-z0-9_-]</c> (no lleva punto, que separa las partes del cursor) y la anterior no puede repetir a la vigente ni su versión. Los textos nunca se registran.
/// </summary>
public sealed partial class ClavesDeCursorDelEntorno : IClavesDeCursor
{
    public const int LongitudMinima = 32;

    private readonly ClaveDeCursor? _anterior;

    public ClavesDeCursorDelEntorno(string? clave, string? version, string? claveAnterior = null, string? versionAnterior = null)
    {
        Vigente = Crear(clave, version, "vigente");
        if (!string.IsNullOrEmpty(claveAnterior) || !string.IsNullOrEmpty(versionAnterior))
        {
            _anterior = Crear(claveAnterior, versionAnterior, "anterior");
            if (_anterior.Version == Vigente.Version || _anterior.Clave.Revelar() == Vigente.Clave.Revelar())
            {
                throw new ArgumentException("La clave anterior del cursor debe ser distinta de la vigente y de otra versión.");
            }
        }
    }

    public ClaveDeCursor Vigente { get; }

    public ClaveDeCursor? Buscar(string version) =>
        version == Vigente.Version ? Vigente : _anterior is not null && version == _anterior.Version ? _anterior : null;

    private static ClaveDeCursor Crear(string? clave, string? version, string cual)
    {
        if (string.IsNullOrEmpty(clave) || System.Text.Encoding.UTF8.GetByteCount(clave) < LongitudMinima)
        {
            throw new ArgumentException($"La clave {cual} del cursor falta o tiene menos de {LongitudMinima} bytes.");
        }

        if (string.IsNullOrEmpty(version) || !FormatoDeVersion().IsMatch(version))
        {
            throw new ArgumentException($"La versión {cual} de la clave del cursor es de 1 a 16 caracteres de [A-Za-z0-9_-].");
        }

        return new ClaveDeCursor(version, new ValorSecreto(clave));
    }

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,16}\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormatoDeVersion();
}
