using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>El nombre de la estrategia de selección configurada no corresponde a ninguna registrada: el arranque falla (SEL-10) en lugar de reservar con una regla desconocida o con otra por omisión.</summary>
public sealed class EstrategiaDeSeleccionDesconocidaException : InvalidOperationException
{
    public EstrategiaDeSeleccionDesconocidaException()
        : base("La estrategia de selección configurada no existe.")
    {
    }

    public EstrategiaDeSeleccionDesconocidaException(string message)
        : base(message)
    {
    }

    public EstrategiaDeSeleccionDesconocidaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Las estrategias de selección de fuente registradas por nombre (D02). La regla activa se elige por configuración, que se lee en la Infraestructura: sustituir la regla no cambia el contrato de reservas ni al
/// E-commerce (SEL-10, AC-SEL-10). Los nombres son únicos y se comparan sin distinguir mayúsculas.
/// </summary>
public sealed class RegistroDeEstrategiasDeSeleccion
{
    private readonly Dictionary<string, IEstrategiaDeSeleccion> _porNombre;

    public RegistroDeEstrategiasDeSeleccion(IEnumerable<IEstrategiaDeSeleccion> estrategias)
    {
        ArgumentNullException.ThrowIfNull(estrategias);
        _porNombre = [];
        foreach (var estrategia in estrategias)
        {
            if (!_porNombre.TryAdd(estrategia.Nombre.ToLowerInvariant(), estrategia))
            {
                throw new ArgumentException($"Hay dos estrategias de selección con el nombre «{estrategia.Nombre}».", nameof(estrategias));
            }
        }
    }

    public IReadOnlyCollection<string> Nombres => [.. _porNombre.Keys.Order(StringComparer.Ordinal)];

    /// <summary>La estrategia con ese nombre; si no existe (o el nombre está vacío), lanza <see cref="EstrategiaDeSeleccionDesconocidaException"/> sin repetir el valor recibido más que como nombre.</summary>
    public IEstrategiaDeSeleccion Obtener(string? nombre) =>
        !string.IsNullOrWhiteSpace(nombre) && _porNombre.TryGetValue(nombre.Trim().ToLowerInvariant(), out var estrategia)
            ? estrategia
            : throw new EstrategiaDeSeleccionDesconocidaException($"La estrategia de selección «{nombre}» no está registrada; las que hay son: {string.Join(", ", Nombres)}.");
}
