using System.Text;

namespace BabelHub.Ecommerce.Domain.Web.Indice;

/// <summary>
/// El generador del índice infinito (IND-01, IND-02, IND-06; D02): una <b>función pura</b> de una dirección a una página de 20 líneas de 70 símbolos de un alfabeto fijo de 30 (26 letras sin tilde, ñ, espacio, coma y punto), unidas por
/// <c>\n</c> sin salto final. La dirección (UTF-8) se resume con <b>FNV-1a de 64 bits</b>, que siembra un <b>SplitMix64</b>; cada símbolo es <c>(salida &gt;&gt; 32) % 30</c>. Son algoritmos publicados, escritos desde cero: no usa
/// <c>string.GetHashCode</c> (aleatorizado por proceso), <c>System.Random</c> (su secuencia no está garantizada entre versiones) ni código de terceros. Sin cultura, reloj ni estado: cambiar el algoritmo cambiaría todas las páginas ya
/// compartidas (R01) y vuelve la spec a Draft.
/// </summary>
public static class GeneradorDePaginasDelIndice
{
    public const int Lineas = 20;
    public const int SimbolosPorLinea = 70;

    /// <summary>El alfabeto fijo de 30 símbolos: sin <c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c>, <c>/</c> ni <c>:</c>, de modo que el texto no puede formar marcado ni enlaces.</summary>
    private const string Alfabeto = "abcdefghijklmnñopqrstuvwxyz ,.";

    private const ulong BaseFnv = 0xcbf29ce484222325UL;
    private const ulong PrimoFnv = 0x100000001b3UL;
    private const ulong IncrementoSplitMix = 0x9E3779B97F4A7C15UL;
    private const ulong MultiplicadorUno = 0xBF58476D1CE4E5B9UL;
    private const ulong MultiplicadorDos = 0x94D049BB133111EBUL;

    /// <summary>La página de una dirección válida: siempre el mismo texto para la misma dirección.</summary>
    public static string Generar(string direccion)
    {
        ArgumentNullException.ThrowIfNull(direccion);
        var estado = Fnv1a64(Encoding.UTF8.GetBytes(direccion));
        var pagina = new StringBuilder((SimbolosPorLinea + 1) * Lineas);
        for (var linea = 0; linea < Lineas; linea++)
        {
            if (linea > 0)
            {
                pagina.Append('\n');
            }

            for (var simbolo = 0; simbolo < SimbolosPorLinea; simbolo++)
            {
                pagina.Append(Alfabeto[(int)((SplitMix64(ref estado) >> 32) % (ulong)Alfabeto.Length)]);
            }
        }

        return pagina.ToString();
    }

    /// <summary>FNV-1a de 64 bits (base y primo publicados).</summary>
    internal static ulong Fnv1a64(ReadOnlySpan<byte> bytes)
    {
        var hash = BaseFnv;
        foreach (var b in bytes)
        {
            hash ^= b;
            hash = unchecked(hash * PrimoFnv);
        }

        return hash;
    }

    /// <summary>La siguiente salida de SplitMix64 (incremento y multiplicadores publicados); avanza el estado.</summary>
    internal static ulong SplitMix64(ref ulong estado)
    {
        unchecked
        {
            estado += IncrementoSplitMix;
            var z = estado;
            z = (z ^ (z >> 30)) * MultiplicadorUno;
            z = (z ^ (z >> 27)) * MultiplicadorDos;
            return z ^ (z >> 31);
        }
    }
}
