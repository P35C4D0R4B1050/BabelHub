using System.Globalization;
using System.Text;

namespace BabelHub.Ecommerce.Domain.Identidad;

/// <summary>El resultado de evaluar una contraseña contra la política (IDE-15): el motivo del rechazo es uno de estos valores, nunca el texto de la contraseña.</summary>
public enum ResultadoDeContrasena
{
    Aceptable,
    Vacia,
    Corta,
    Larga,

    /// <summary>Figura en la lista de contraseñas comunes o filtradas (con cualquier normalización Unicode, IDE-59).</summary>
    Comun,
}

/// <summary>
/// La política de contraseñas (IDE-15, IDE-59, D03): de 10 a 128 caracteres Unicode contados como caracteres (un emoji es uno, no dos unidades UTF-16), normalización NFKC antes de contar y de comparar, que no figure
/// en la lista de contraseñas comunes o filtradas y <b>ninguna regla de composición</b> (ni mayúsculas, ni dígitos, ni símbolos). La lista la da un puerto de Aplicación; aquí llega como una función, porque el Dominio no
/// referencia la Aplicación.
/// </summary>
public static class PoliticaDeContrasenas
{
    public const int LongitudMinima = 10;

    public const int LongitudMaxima = 128;

    /// <summary>La normalización con la que se cuenta, se compara con la lista y se resume (IDE-59): NFKC.</summary>
    public static string Normalizar(string texto)
    {
        ArgumentNullException.ThrowIfNull(texto);
        return texto.Normalize(NormalizationForm.FormKC);
    }

    /// <summary>La longitud en caracteres Unicode (puntos de código) de la contraseña ya normalizada.</summary>
    public static int Longitud(string normalizada)
    {
        ArgumentNullException.ThrowIfNull(normalizada);
        return normalizada.EnumerateRunes().Count();
    }

    /// <param name="texto">Lo que escribió la persona (puede pegarse y autocompletarse: no se recorta ni se altera).</param>
    /// <param name="esComun">Si la contraseña normalizada figura en la lista local de al menos 10 000 contraseñas comunes o filtradas.</param>
    public static ResultadoDeContrasena Evaluar(string? texto, Func<string, bool> esComun)
    {
        ArgumentNullException.ThrowIfNull(esComun);
        if (string.IsNullOrEmpty(texto))
        {
            return ResultadoDeContrasena.Vacia;
        }

        var normalizada = Normalizar(texto);
        var longitud = Longitud(normalizada);
        if (longitud < LongitudMinima)
        {
            return ResultadoDeContrasena.Corta;
        }

        if (longitud > LongitudMaxima)
        {
            return ResultadoDeContrasena.Larga;
        }

        // La lista se guarda en minúsculas; se compara la forma normalizada y su minúscula (sin cultura: una «I» turca no cambia el resultado).
        return esComun(normalizada) || esComun(normalizada.ToLowerInvariant()) ? ResultadoDeContrasena.Comun : ResultadoDeContrasena.Aceptable;
    }
}

/// <summary>
/// Una contraseña aceptable y normalizada, a punto de resumirse o verificarse. Nunca se imprime ni se serializa: <see cref="ToString"/> no la muestra y solo <see cref="Revelar"/> la entrega, a quien la resume (el
/// adaptador de Argon2id) o la compara.
/// </summary>
public sealed class Contrasena
{
    private readonly string _normalizada;

    private Contrasena(string normalizada) => _normalizada = normalizada;

    /// <summary>La contraseña normalizada cuando <see cref="PoliticaDeContrasenas.Evaluar"/> la acepta; si no, el motivo.</summary>
    public static (Contrasena? Contrasena, ResultadoDeContrasena Resultado) Crear(string? texto, Func<string, bool> esComun)
    {
        var resultado = PoliticaDeContrasenas.Evaluar(texto, esComun);
        return resultado == ResultadoDeContrasena.Aceptable ? (new Contrasena(PoliticaDeContrasenas.Normalizar(texto!)), resultado) : (null, resultado);
    }

    /// <summary>
    /// La contraseña que se presenta al ingresar: se normaliza como la guardada pero no se le aplica la política (una contraseña antigua o inválida debe poder compararse, y rechazarla por ser corta delataría la regla).
    /// Una cadena vacía no es una contraseña.
    /// </summary>
    public static Contrasena? Presentada(string? texto) => string.IsNullOrEmpty(texto) ? null : new Contrasena(PoliticaDeContrasenas.Normalizar(texto));

    public string Revelar() => _normalizada;

    public override string ToString() => "[contraseña]";
}

/// <summary>Los parámetros del resumen de contraseñas (D03): Argon2id con 19 MiB de memoria, 2 iteraciones, 1 hilo y una sal aleatoria única de al menos 16 bytes por cuenta.</summary>
public static class ParametrosDelResumen
{
    public const int MemoriaEnKiB = 19 * 1024;

    public const int Iteraciones = 2;

    public const int Paralelismo = 1;

    public const int BytesDeSal = 16;

    public const int BytesDelResumen = 32;
}

/// <summary>
/// El resumen guardado de una contraseña, en el formato PHC de Argon2id (<c>$argon2id$v=19$m=19456,t=2,p=1$sal$resumen</c>): la sal y los parámetros viajan con él, así dos cuentas con la misma contraseña tienen resúmenes
/// distintos (AC-IDE-08) y los parámetros pueden subir sin invalidar los resúmenes viejos (<see cref="NecesitaActualizarse"/>). Nunca se imprime.
/// </summary>
public sealed class ResumenDeContrasena
{
    private const string Prefijo = "$argon2id$";

    private ResumenDeContrasena(string valor, int memoria, int iteraciones, int paralelismo, string sal)
    {
        Valor = valor;
        Memoria = memoria;
        Iteraciones = iteraciones;
        Paralelismo = paralelismo;
        Sal = sal;
    }

    public string Valor { get; }

    public int Memoria { get; }

    public int Iteraciones { get; }

    public int Paralelismo { get; }

    public string Sal { get; }

    public static ResumenDeContrasena? Leer(string? valor)
    {
        if (string.IsNullOrEmpty(valor) || valor.Length > 256 || !valor.StartsWith(Prefijo, StringComparison.Ordinal))
        {
            return null;
        }

        var partes = valor.Split('$');
        // ["", "argon2id", "v=19", "m=..,t=..,p=..", sal, resumen]
        if (partes.Length != 6 || partes[2] != "v=19" || partes[4].Length < 16 || partes[5].Length < 16)
        {
            return null;
        }

        var parametros = partes[3].Split(',');
        if (parametros.Length != 3
            || !TryParametro(parametros[0], "m=", out var memoria)
            || !TryParametro(parametros[1], "t=", out var iteraciones)
            || !TryParametro(parametros[2], "p=", out var paralelismo))
        {
            return null;
        }

        return new ResumenDeContrasena(valor, memoria, iteraciones, paralelismo, partes[4]);
    }

    /// <summary>Verdadero si se guardó con parámetros más débiles que los vigentes: se vuelve a resumir en el siguiente ingreso correcto.</summary>
    public bool NecesitaActualizarse() => Memoria < ParametrosDelResumen.MemoriaEnKiB || Iteraciones < ParametrosDelResumen.Iteraciones || Paralelismo != ParametrosDelResumen.Paralelismo;

    public override string ToString() => "[resumen]";

    private static bool TryParametro(string texto, string prefijo, out int valor)
    {
        valor = 0;
        return texto.StartsWith(prefijo, StringComparison.Ordinal)
            && int.TryParse(texto.AsSpan(prefijo.Length), NumberStyles.None, CultureInfo.InvariantCulture, out valor)
            && valor > 0;
    }
}
