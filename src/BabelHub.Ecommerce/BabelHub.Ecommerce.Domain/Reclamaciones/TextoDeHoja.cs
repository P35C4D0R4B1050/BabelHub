namespace BabelHub.Ecommerce.Domain.Reclamaciones;

/// <summary>
/// Los textos que un tercero escribe en una hoja (nombre, documento, domicilio, bien, referencia del pedido, detalle y la respuesta): se rechazan los que la base no puede guardar o que
/// suplantan lo que ve la administración. No se filtra el HTML (un <c>&lt;script&gt;</c> es solo texto): la codificación de la salida es de las pantallas.
/// </summary>
public static class TextoDeHoja
{
    private const int SeparadorDeLinea = 0x2028;
    private const int SeparadorDeParrafo = 0x2029;
    private const int PrimerControlBidireccional = 0x202A;
    private const int UltimoControlBidireccional = 0x202E;
    private const int PrimerAisladorBidireccional = 0x2066;
    private const int UltimoAisladorBidireccional = 0x2069;

    /// <summary>
    /// <c>false</c> si hay un carácter de control (también <c>NUL</c> y el separador <c>U+001F</c> del contenido canónico), un sustituto sin par, un separador de línea o de párrafo de Unicode o
    /// un carácter de control bidireccional. Con <paramref name="permitirSaltos"/> se admiten el salto de línea, el retorno de carro y el tabulador (el detalle y la respuesta son de varias líneas).
    /// </summary>
    public static bool EsSeguro(string valor, bool permitirSaltos)
    {
        ArgumentNullException.ThrowIfNull(valor);
        for (var i = 0; i < valor.Length; i++)
        {
            var c = valor[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= valor.Length || !char.IsLowSurrogate(valor[i + 1]))
                {
                    return false;
                }

                i++;
                continue;
            }

            if (char.IsLowSurrogate(c))
            {
                return false;
            }

            if (c < 0x20 || (c >= 0x7F && c <= 0x9F))
            {
                if (permitirSaltos && (c == 0x0A || c == 0x0D || c == 0x09))
                {
                    continue;
                }

                return false;
            }

            if (c == SeparadorDeLinea || c == SeparadorDeParrafo
                || (c >= PrimerControlBidireccional && c <= UltimoControlBidireccional)
                || (c >= PrimerAisladorBidireccional && c <= UltimoAisladorBidireccional))
            {
                return false;
            }
        }

        return true;
    }
}
