using System.Collections.Concurrent;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// Acota las alertas técnicas que una entrada de servicio puede provocar (revisión de seguridad de ECO-6f): un motivo se publica a lo más una vez por <see cref="Intervalo"/> en este proceso, de modo que quien tenga la
/// credencial del alcance compartido no pueda inundar el canal de alertas con estados de pedidos que no existen. Es un solo objeto por proceso y no guarda nada personal: solo el motivo y el último instante.
/// </summary>
public sealed class AcotadorDeAlertasDePedidos
{
    public static readonly TimeSpan Intervalo = TimeSpan.FromMinutes(15);

    /// <summary>Cuánto puede llegar una lectura del reloj por detrás de la anterior (solicitudes concurrentes) sin tratarse como un retroceso del reloj.</summary>
    public static readonly TimeSpan ToleranciaDelReloj = TimeSpan.FromMinutes(1);

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

            // Una solicitud concurrente con unos milisegundos de desorden no abre el paso; solo un retroceso del reloj mayor que la tolerancia reanuda la cuenta.
            if (ahora - anterior < Intervalo && anterior - ahora <= ToleranciaDelReloj)
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
