using System.Collections.Concurrent;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>El mínimo de una fuente que la consulta pública de disponibilidad muestra: identificador, código y nombre. Sin contacto, región ni encargado.</summary>
public readonly record struct FuenteDeReferencia(Guid Id, string Codigo, string Nombre);

/// <summary>
/// Memoria del proceso, de <b>vida corta</b> (5 s), para las referencias que solo muestra la consulta pública de disponibilidad (<c>GET /disponibilidad</c>): la lista de fuentes (código y nombre) y el
/// formato de cada edición. Evita dos accesos a la base por solicitud (cada uno con su cambio de rol), que con la CPU de PostgreSQL como cuello de botella eran la mayor parte del costo (DOC-1 §6b).
/// <list type="bullet">
/// <item><b>Sin datos de un alcance:</b> solo tablas sin seguridad por filas (<c>fuentes.fuentes</c>, <c>fuentes.ediciones</c>), con la proyección mínima: lo que ve cualquier actor con contexto. El stock, lo reservado y las vencidas
/// <b>nunca</b> se guardan aquí.</item>
/// <item><b>Nunca decide:</b> reservar, aplicar movimientos, traslados y ajustes siguen leyendo de la base (<c>06-tacticas-arquitectonicas</c>); solo la consulta pública lee de aquí, y a lo sumo 5 s atrasada (una fuente nueva o un cambio
/// de formato aparecen en ese plazo).</item>
/// <item><b>Acotada:</b> no guarda ausencias (un identificador inventado no ocupa memoria) y se vacía entera al pasar de 10 000 entradas.</item>
/// </list>
/// </summary>
public sealed class CacheDeReferencias(IReloj reloj)
{
    /// <summary>Cuánto vive una entrada.</summary>
    public static readonly TimeSpan Vida = TimeSpan.FromSeconds(5);

    private const int MaximoDeEntradas = 10_000;

    private readonly ConcurrentDictionary<string, (object Valor, DateTimeOffset Vence)> _entradas = new(StringComparer.Ordinal);

    /// <summary>El valor vigente de la clave, o <see langword="null"/> si no lo hay o venció.</summary>
    public T? Obtener<T>(string clave)
        where T : class =>
        _entradas.TryGetValue(clave, out var entrada) && entrada.Vence > reloj.Ahora ? entrada.Valor as T : null;

    /// <summary>Guarda el valor por <see cref="Vida"/>.</summary>
    public void Guardar<T>(string clave, T valor)
    {
        ArgumentNullException.ThrowIfNull(valor);

        if (_entradas.Count >= MaximoDeEntradas)
        {
            _entradas.Clear();
        }

        _entradas[clave] = (valor, reloj.Ahora + Vida);
    }
}
