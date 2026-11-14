namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Elevación del alcance de datos de la transacción en curso a «toda la red» (<c>babelhub.alcance = red</c>) mientras dura el bloque. Es
/// <b>interna</b> (solo la ve la Infraestructura) y la prueba de arquitectura admite como llamadores únicamente a los adaptadores de
/// Infraestructura de <c>Fuentes</c>, <c>Ajustes</c> y <c>Reservas</c> (<c>inventario-fuentes</c> D09, T04); nunca la Api, el Dominio ni la
/// Aplicación. Se usa solo después de que la Aplicación del módulo que llama comprobó el alcance del actor: para garantizar y bloquear
/// las filas de stock de todas las fuentes en <c>Bloquear</c> (con los deltas de las liberaciones de vencidas) y para las lecturas de
/// toda la red ya autorizadas. <c>Aplicar</c> corre con el alcance del actor.
/// </summary>
internal interface IElevacionDeAlcance
{
    Task<T> ConAlcanceDeRed<T>(Func<CancellationToken, Task<T>> bloque, CancellationToken cancelacion = default);
}

/// <summary>
/// La política de seguridad por filas de la base rechazó una escritura por estar fuera del alcance del actor. La Aplicación que llama la
/// traduce a <c>INV-PROHIBIDO</c> con el evento de acceso denegado (no a «sin stock»); nunca lleva datos de la fila.
/// </summary>
public sealed class AlcanceDeDatosDenegadoException : Exception
{
    public AlcanceDeDatosDenegadoException()
        : base("La política de filas rechazó el acceso: la fila está fuera del alcance del actor.")
    {
    }

    public AlcanceDeDatosDenegadoException(Exception interna)
        : base("La política de filas rechazó el acceso: la fila está fuera del alcance del actor.", interna)
    {
    }
}
