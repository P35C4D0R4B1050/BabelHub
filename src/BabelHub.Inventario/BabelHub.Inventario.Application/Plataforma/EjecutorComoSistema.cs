namespace BabelHub.Inventario.Application.Plataforma;

/// <summary>
/// Ejecuta un trabajo <b>como actor «sistema»</b> dentro del flujo de otro actor y restaura el anterior al terminar. Lo necesita lo que el sistema hace por su cuenta en la transacción de un cliente: la
/// liberación de reservas vencidas al reservar, ajustar o trasladar se audita con actor «sistema» (decisión del 2026-11-12 de <c>transversal-auditoria</c>, SEL-27, D08) aunque la transacción sea de un cliente, y la
/// auditoría toma el actor del contexto y nunca de la solicitud. No es para elevar permisos: el alcance de datos de la transacción no cambia.
/// </summary>
public interface IEjecutorComoSistema
{
    Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default);
}
