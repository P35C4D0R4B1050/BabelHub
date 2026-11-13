using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Reglas comunes de una huella: un resumen opaco con el identificador de versión de la clave que lo calculó. Su forma
/// (solo letras, dígitos, «-» y «_», de 22 a 128 caracteres) impide que se guarde por error una dirección IP, un correo o
/// un número de teléfono, que tienen puntos, dos puntos, «@» o «+».
/// </summary>
internal static partial class ValorDeHuella
{
    public const int LargoMinimo = 22;
    public const int LargoMaximo = 128;

    public static string ValidarValor(string valor)
    {
        if (string.IsNullOrEmpty(valor) || valor.Length < LargoMinimo || valor.Length > LargoMaximo || !FormaDelValor().IsMatch(valor))
        {
            throw new ArgumentException("Una huella es un resumen opaco de 22 a 128 caracteres seguros, nunca el dato original.", nameof(valor));
        }

        return valor;
    }

    public static string ValidarVersion(string versionDeClave)
    {
        if (string.IsNullOrEmpty(versionDeClave) || !FormaDeLaVersion().IsMatch(versionDeClave))
        {
            throw new ArgumentException("La versión de la clave es un identificador corto de letras y dígitos.", nameof(versionDeClave));
        }

        return versionDeClave;
    }

    [GeneratedRegex("^[A-Za-z0-9_-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDelValor();

    [GeneratedRegex("^[A-Za-z0-9]{1,16}$", RegexOptions.CultureInvariant)]
    private static partial Regex FormaDeLaVersion();
}
