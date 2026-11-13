using BabelHub.Inventario.Domain.Plataforma;

namespace BabelHub.Inventario.UnitTests.Plataforma;

/// <summary>
/// Reproduce lo que hace el motor al reservar (sin almacén): evalúa con la política y, si se permite, cuenta la entrada y
/// crea el bloqueo. Un rechazo no agrega entradas ni toca el bloqueo.
/// </summary>
internal sealed class SimuladorDeLimite(PoliticaDeLimite politica)
{
    private readonly List<DateTimeOffset> _entradas = [];
    private DateTimeOffset? _bloqueoHasta;

    public DecisionDeLimite Intentar(DateTimeOffset ahora)
    {
        var decision = politica.Evaluar(_entradas, _bloqueoHasta, ahora);
        if (decision.Permitida)
        {
            _entradas.Add(ahora);
            if (decision.BloqueoHastaNuevo is { } nuevo)
            {
                _bloqueoHasta = nuevo;
            }
        }

        return decision;
    }

    /// <summary>Hace <paramref name="cantidad"/> intentos separados por <paramref name="separacion"/> desde <paramref name="inicio"/> y devuelve el instante del último.</summary>
    public DateTimeOffset Repetir(int cantidad, DateTimeOffset inicio, TimeSpan separacion)
    {
        var instante = inicio;
        for (var i = 0; i < cantidad; i++)
        {
            instante = inicio + (separacion * i);
            Assert.True(Intentar(instante).Permitida, $"El intento {i + 1} debía permitirse.");
        }

        return instante;
    }
}
