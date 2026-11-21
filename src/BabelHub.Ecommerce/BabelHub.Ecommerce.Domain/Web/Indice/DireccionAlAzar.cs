using System.Globalization;
using System.Text.RegularExpressions;

namespace BabelHub.Ecommerce.Domain.Web.Indice;

/// <summary>
/// La dirección al azar del índice infinito (IND-03; D05): sala, pared, estante, volumen y página, con la forma <c>s{1–40}-p{1–4}-e{1–5}-v{1–32}-pg{1–410}</c>, como el prototipo. Siempre es una dirección válida (<see cref="DireccionDelIndice"/>) y de
/// menos de 64 caracteres. Los enteros los elige quien llama con una función de enteros (la Aplicación, con muestreo por rechazo sin sesgo sobre bytes criptográficos), de modo que el Dominio no tiene azar, cultura, reloj ni estado.
/// </summary>
public static partial class DireccionAlAzar
{
    public const int Salas = 40;
    public const int Paredes = 4;
    public const int Estantes = 5;
    public const int Volumenes = 32;
    public const int Paginas = 410;

    /// <summary>Arma una dirección con <paramref name="elegir"/>, que recibe el tamaño de cada rango y devuelve un entero de 0 a ese tamaño menos uno.</summary>
    public static string Crear(Func<int, int> elegir)
    {
        ArgumentNullException.ThrowIfNull(elegir);
        return string.Create(CultureInfo.InvariantCulture, $"s{1 + Elegir(elegir, Salas)}-p{1 + Elegir(elegir, Paredes)}-e{1 + Elegir(elegir, Estantes)}-v{1 + Elegir(elegir, Volumenes)}-pg{1 + Elegir(elegir, Paginas)}");
    }

    /// <summary>Verdadero si la dirección tiene exactamente la forma de una dirección al azar (cada componente en su rango y sin ceros a la izquierda).</summary>
    public static bool TieneLaForma(string direccion) => Componentes(direccion) is not null;

    /// <summary>
    /// Una dirección distinta de la misma forma: avanza el componente de la página (de 410 vuelve a 1). Es la salida determinista cuando el azar repite la dirección actual (D05: sin bucle infinito). Solo acepta una dirección de la forma:
    /// cualquier otra nunca coincide con una dirección al azar.
    /// </summary>
    public static string AvanzarPagina(string direccion)
    {
        var c = Componentes(direccion) ?? throw new ArgumentException("La dirección no tiene la forma de una dirección al azar.", nameof(direccion));
        var pagina = (c[4] % Paginas) + 1;
        return string.Create(CultureInfo.InvariantCulture, $"s{c[0]}-p{c[1]}-e{c[2]}-v{c[3]}-pg{pagina}");
    }

    private static int Elegir(Func<int, int> elegir, int tamano)
    {
        var valor = elegir(tamano);
        return valor < 0 || valor >= tamano ? throw new InvalidOperationException("La función de enteros devolvió un valor fuera del rango pedido.") : valor;
    }

    private static int[]? Componentes(string direccion)
    {
        var m = Forma().Match(direccion);
        if (!m.Success)
        {
            return null;
        }

        int[] limites = [Salas, Paredes, Estantes, Volumenes, Paginas];
        var valores = new int[5];
        for (var i = 0; i < 5; i++)
        {
            valores[i] = int.Parse(m.Groups[i + 1].Value, CultureInfo.InvariantCulture);
            if (valores[i] < 1 || valores[i] > limites[i])
            {
                return null;
            }
        }

        return valores;
    }

    [GeneratedRegex(@"^s([1-9][0-9]{0,2})-p([1-9][0-9]{0,2})-e([1-9][0-9]{0,2})-v([1-9][0-9]{0,2})-pg([1-9][0-9]{0,2})\z", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex Forma();
}
