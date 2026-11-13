using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Plataforma;

/// <summary>Reloj del sistema. El día de calendario es el de Lima (UTC−5, sin horario de verano), no el del servidor.</summary>
public sealed class RelojDelSistema(TimeProvider? proveedor = null) : IReloj
{
    private static readonly TimeSpan DesfaseDeLima = TimeSpan.FromHours(-5);

    private readonly TimeProvider _proveedor = proveedor ?? TimeProvider.System;

    public DateTimeOffset Ahora => _proveedor.GetUtcNow();

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(DesfaseDeLima).DateTime);
}

/// <summary>Quien establece el actor de la solicitud o de la tarea en curso (la Api desde la credencial; un trabajo de fondo como «sistema»).</summary>
public interface IEstablecedorDeActor
{
    /// <summary>Fija el actor del flujo asíncrono actual, y de lo que este inicie.</summary>
    void Establecer(ActorActual actor);

    void Limpiar();
}

/// <summary>
/// Actor del flujo actual, guardado por flujo asíncrono (no compartido entre solicitudes simultáneas). Mientras nadie lo
/// establezca es <see langword="null"/> y toda llamada que lo exija se rechaza (SEG-15).
/// </summary>
public sealed class ActorDelFlujo : IActorActual, IEstablecedorDeActor
{
    private readonly AsyncLocal<ActorActual?> _actor = new();

    public ActorActual? Actual => _actor.Value;

    public void Establecer(ActorActual actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        _actor.Value = actor;
    }

    public void Limpiar() => _actor.Value = null;
}
