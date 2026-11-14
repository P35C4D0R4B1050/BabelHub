namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Marca un caso de uso que se puede repetir entero: no tiene efectos fuera de su transacción (no publica eventos, no llama a otro servicio ni
/// envía avisos) y repetirlo con la misma entrada deja el mismo resultado. Solo los casos con esta marca los repite el decorador de
/// reintento por interbloqueo de la Infraestructura (<c>40P01</c>, <c>40001</c>), en una transacción nueva y una sola vez.
/// </summary>
public interface ICasoDeUsoIdempotente
{
}

/// <summary>
/// Lectura coherente: una transacción de solo lectura con aislamiento <c>REPEATABLE READ</c>, de modo que todo lo que se lee dentro ve la
/// misma instantánea (el stock y las vencidas que informa <c>IReservasDeStock</c> nunca reflejan un estado intermedio, spec §6 y D06).
/// Si ya hay una transacción activa se une a ella y hereda su aislamiento.
/// </summary>
public interface ILecturaCoherente
{
    Task<T> EjecutarLectura<T>(Func<CancellationToken, Task<T>> lectura, CancellationToken cancelacion = default);
}
