using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Infrastructure.Plataforma;

/// <summary>
/// Ejecuta un trabajo de fondo (la expiración de reservas, el buzón de salida, una depuración) <b>como actor «sistema»</b> explícito. Sin actor, el alcance de datos de la transacción no tiene valor y las
/// políticas de <c>stock</c> devuelven cero filas <i>sin error</i>, de modo que un trabajo que lee y mueve stock parecería funcionar mientras no hace nada (hallazgo del 2026-11-14, <c>inventario-fuentes</c> D09).
/// Todo trabajo de fondo que toque el stock debe correr dentro de <see cref="Ejecutar{T}"/>; si algo lo llama sin actor, el acceso al stock falla a la vista y deja una alerta técnica. Al terminar (o fallar)
/// deja el flujo sin actor, así el siguiente trabajo no hereda el anterior.
/// </summary>
public sealed class EjecutorDeTrabajoDeFondo(IEstablecedorDeActor actor)
{
    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(trabajo);

        actor.Establecer(ActorActual.DelSistema());
        try
        {
            return await trabajo(cancelacion).ConfigureAwait(false);
        }
        finally
        {
            actor.Limpiar();
        }
    }
}
