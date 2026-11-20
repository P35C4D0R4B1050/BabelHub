using System.Collections.Concurrent;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// Acota las alertas técnicas que una entrada de servicio puede provocar (revisión de seguridad de ECO-6f): un motivo se publica a lo más una vez por <see cref="Intervalo"/> en este proceso, de modo que quien tenga la
/// credencial del alcance compartido no pueda inundar el canal de alertas con estados de pedidos que no existen. Es un solo objeto por proceso y no guarda nada personal: solo el motivo y el último instante.
/// </summary>
public sealed class AcotadorDeAlertasDePedidos
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _ultima = new(StringComparer.Ordinal);

    /// <summary>Verdadero si el motivo no se publicó en el último intervalo (y lo anota); falso si hay que omitirla.</summary>
    public bool Permitir(string motivo, DateTimeOffset ahora)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        while (true)
        {
            if (!_ultima.TryGetValue(motivo, out var anterior))
            {
                if (_ultima.TryAdd(motivo, ahora))
                {
                    return true;
                }

                continue;
            }

            if (ahora - anterior < Intervalo)
            {
                return false;
            }

            if (_ultima.TryUpdate(motivo, ahora, anterior))
            {
                return true;
            }
        }
    }
}
