using System.Globalization;
using System.Text.RegularExpressions;

namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Reúne las reglas de tipo, longitud, formato y rango de una entrada (SEG-01) para los validadores de los módulos. Los
/// errores llevan el campo y una clave simbólica, nunca el valor recibido. Los formatos usan expresiones con tiempo límite.
/// </summary>
public sealed class ConstructorDeValidacion
{
    public const string Obligatorio = "obligatorio";
    public const string DemasiadoCorto = "demasiado-corto";
    public const string DemasiadoLargo = "demasiado-largo";
    public const string FormatoInvalido = "formato-invalido";
    public const string NoEsNumero = "no-es-numero";
    public const string FueraDeRango = "fuera-de-rango";

    private readonly List<ErrorDeCampo> _errores = [];

    /// <summary>Texto obligatorio con largo mínimo y máximo y, si se indica, un formato.</summary>
    public ConstructorDeValidacion Texto(string campo, string? valor, int minimo, int maximo, Regex? formato = null)
    {
        if (string.IsNullOrWhiteSpace(valor))
        {
            return Agregar(campo, Obligatorio);
        }

        if (valor.Length < minimo)
        {
            return Agregar(campo, DemasiadoCorto);
        }

        if (valor.Length > maximo)
        {
            return Agregar(campo, DemasiadoLargo);
        }

        if (formato is not null && !Coincide(formato, valor))
        {
            return Agregar(campo, FormatoInvalido);
        }

        return this;
    }

    /// <summary>Número entero recibido como texto: debe ser un entero y estar dentro del rango.</summary>
    public ConstructorDeValidacion Entero(string campo, string? texto, long minimo, long maximo)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return Agregar(campo, Obligatorio);
        }

        if (!long.TryParse(texto, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var valor))
        {
            return Agregar(campo, NoEsNumero);
        }

        return Rango(campo, valor, minimo, maximo);
    }

    /// <summary>Un valor ya tipado dentro de un rango cerrado.</summary>
    public ConstructorDeValidacion Rango<T>(string campo, T valor, T minimo, T maximo)
        where T : IComparable<T> =>
        valor.CompareTo(minimo) < 0 || valor.CompareTo(maximo) > 0 ? Agregar(campo, FueraDeRango) : this;

    public ResultadoDeValidacion Resultado() =>
        _errores.Count == 0 ? ResultadoDeValidacion.Valido : ResultadoDeValidacion.Invalido(_errores);

    private ConstructorDeValidacion Agregar(string campo, string clave)
    {
        _errores.Add(new ErrorDeCampo(campo, clave));
        return this;
    }

    private static bool Coincide(Regex formato, string valor)
    {
        try
        {
            return formato.IsMatch(valor);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
