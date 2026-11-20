namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Elevación del alcance de datos de la transacción en curso a «toda la red» (<c>babelhub.alcance = red</c>) mientras dura el bloque. Es
/// <b>interna</b> (solo la ve la Infraestructura) y la prueba de arquitectura admite como llamadores únicamente a los adaptadores de
/// Infraestructura de <c>Fuentes</c> y <c>Ajustes</c> (<c>inventario-fuentes</c> D09, T04; <c>Traslados</c> ya no la usa y <c>Reservas</c> pasa por <c>IMovimientosDeStock.Bloquear</c>); nunca la Api, el Dominio ni la
/// Aplicación. Se usa solo después de que la Aplicación del módulo que llama comprobó el alcance del actor: para garantizar y bloquear
/// las filas de stock de todas las fuentes en <c>Bloquear</c> (con los deltas de las liberaciones de vencidas) y para las lecturas de
/// toda la red ya autorizadas. <c>Aplicar</c> corre con el alcance del actor.
/// </summary>
internal interface IElevacionDeAlcance
{
    Task<T> ConAlcanceDeRed<T>(Func<CancellationToken, Task<T>> bloque, CancellationToken cancelacion = default);

    /// <summary>
    /// <see cref="IUnidadDeTrabajo.EjecutarComo"/> del esquema y <see cref="ConAlcanceDeRed{T}"/> en un solo bloque: la entrada (rol y alcance) y la salida (alcance y rol anteriores) viajan cada una en
    /// <b>una sola ida a la base</b> en lugar de dos. Es exactamente lo mismo que anidarlos: el bloque corre con el rol del esquema y el alcance de la red, y al salir vuelven ambos.
    /// </summary>
    Task<T> EjecutarComoConAlcanceDeRed<T>(EsquemaDeModulo esquema, Func<CancellationToken, Task<T>> bloque, CancellationToken cancelacion = default);

    /// <summary>
    /// Ejecuta la operación y traduce el rechazo de la política de filas de la base (<c>42501</c> con nombre de tabla) a <see cref="AlcanceDeDatosDenegadoException"/>, sin confundirlo con «sin stock» ni con un error
    /// inesperado. Vive aquí, y no en la Infraestructura de la plataforma, porque la Infraestructura de un módulo no puede depender de la de otro (regla 13).
    /// </summary>
    Task<T> TraducirRechazoDeLaPolitica<T>(Func<Task<T>> operacion);
}
