using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web;

/// <summary>
/// Un código QR ya calculado (web-acceso ACC-13): su lado en módulos, sin margen, y qué módulos van oscuros. No guarda el texto codificado ni se imprime: el código <b>es</b> el secreto del segundo factor (ACC-14), así que
/// no es un registro ni expone nada que se pueda serializar por descuido.
/// </summary>
public sealed class CodigoQr
{
    private readonly bool[] _oscuros;

    public CodigoQr(int lado, Func<int, int, bool> esOscuro)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(lado, 1);
        ArgumentNullException.ThrowIfNull(esOscuro);
        Lado = lado;
        _oscuros = new bool[lado * lado];
        for (var fila = 0; fila < lado; fila++)
        {
            for (var columna = 0; columna < lado; columna++)
            {
                _oscuros[(fila * lado) + columna] = esOscuro(columna, fila);
            }
        }
    }

    public int Lado { get; }

    public bool EsOscuro(int columna, int fila)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(columna);
        ArgumentOutOfRangeException.ThrowIfNegative(fila);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(columna, Lado);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fila, Lado);
        return _oscuros[(fila * Lado) + columna];
    }

    public override string ToString() => "[código QR]";
}

/// <summary>Genera en el servidor el código QR de la configuración del segundo factor (ACC-13, ACC-14), sin terceros.</summary>
public interface IGeneradorDeCodigoQr
{
    /// <summary>El código con ese contenido, o nulo si no se puede generar (ACC-16). Nunca lanza, guarda ni registra el contenido.</summary>
    CodigoQr? Generar(ValorSecreto contenido);
}
