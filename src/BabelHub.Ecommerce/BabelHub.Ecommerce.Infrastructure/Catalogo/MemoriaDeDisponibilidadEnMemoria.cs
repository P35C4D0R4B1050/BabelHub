using BabelHub.Ecommerce.Application.Catalogo.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Catalogo;

/// <summary>
/// La memoria de disponibilidad por instancia (D03): una entrada por edición, con un <b>tope de entradas</b> para que un catálogo grande no crezca sin límite; al pasarlo se descarta la consultada hace más tiempo. La frescura (30 s) y el respaldo
/// (10 min) los decide el resolutor con <see cref="EntradaDeDisponibilidad.ConsultadaEn"/>; esta clase solo guarda. <see cref="Invalidar"/> marca la entrada como vencida para la caché pero <b>conserva el respaldo</b>. Es segura entre hilos.
/// </summary>
public sealed class MemoriaDeDisponibilidadEnMemoria(int maximoDeEntradas = MemoriaDeDisponibilidadEnMemoria.TopePorDefecto) : IMemoriaDeDisponibilidad
{
    public const int TopePorDefecto = 5000;

    private readonly object _candado = new();
    private readonly Dictionary<Guid, EntradaDeDisponibilidad> _entradas = [];
    private readonly int _maximo = maximoDeEntradas > 0 ? maximoDeEntradas : throw new ArgumentOutOfRangeException(nameof(maximoDeEntradas));

    public EntradaDeDisponibilidad? Obtener(Guid edicionId)
    {
        lock (_candado)
        {
            return _entradas.GetValueOrDefault(edicionId);
        }
    }

    public void Guardar(EntradaDeDisponibilidad entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (entrada.VendibleEnLaRed < 0 || entrada.PorFuente.Any(f => f.Vendible < 0))
        {
            return; // un dato sin validar sus cantidades nunca se guarda
        }

        lock (_candado)
        {
            if (!_entradas.ContainsKey(entrada.EdicionId) && _entradas.Count >= _maximo)
            {
                var masAntigua = _entradas.MinBy(par => par.Value.ConsultadaEn).Key;
                _entradas.Remove(masAntigua);
            }

            _entradas[entrada.EdicionId] = entrada with { Invalidada = false };
        }
    }

    public void Invalidar(Guid edicionId)
    {
        lock (_candado)
        {
            if (_entradas.TryGetValue(edicionId, out var entrada))
            {
                _entradas[edicionId] = entrada with { Invalidada = true };
            }
        }
    }
}
