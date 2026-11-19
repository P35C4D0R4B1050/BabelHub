using System.Globalization;

namespace BabelHub.Ecommerce.Domain.Reclamaciones;

/// <summary>El número de una hoja (REC-05, D10): <c>AAAA-NNNNNN</c>, el año de Lima y una secuencia de seis dígitos que empieza en 1 cada año y no deja huecos.</summary>
public readonly record struct NumeroDeHoja
{
    public const int SecuenciaMaxima = 999_999;

    private NumeroDeHoja(int anio, int secuencia)
    {
        Anio = anio;
        Secuencia = secuencia;
    }

    public int Anio { get; }

    public int Secuencia { get; }

    public static NumeroDeHoja Crear(int anio, int secuencia)
    {
        if (anio is < 2000 or > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(anio), "El año del número va de 2000 a 9999.");
        }

        if (secuencia is < 1 or > SecuenciaMaxima)
        {
            throw new ArgumentOutOfRangeException(nameof(secuencia), "La secuencia va de 1 a 999 999.");
        }

        return new NumeroDeHoja(anio, secuencia);
    }

    /// <summary>Lee el formato exacto <c>AAAA-NNNNNN</c>: sin espacios, sin saltos de línea ni otros separadores.</summary>
    public static bool TryParse(string? texto, out NumeroDeHoja numero)
    {
        numero = default;
        if (texto is not { Length: 11 } || texto[4] != '-')
        {
            return false;
        }

        for (var i = 0; i < texto.Length; i++)
        {
            if (i != 4 && (texto[i] < '0' || texto[i] > '9'))
            {
                return false;
            }
        }

        var anio = int.Parse(texto.AsSpan(0, 4), NumberStyles.None, CultureInfo.InvariantCulture);
        var secuencia = int.Parse(texto.AsSpan(5, 6), NumberStyles.None, CultureInfo.InvariantCulture);
        if (anio < 2000 || secuencia < 1)
        {
            return false;
        }

        numero = new NumeroDeHoja(anio, secuencia);
        return true;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Anio:0000}-{Secuencia:000000}");
}
