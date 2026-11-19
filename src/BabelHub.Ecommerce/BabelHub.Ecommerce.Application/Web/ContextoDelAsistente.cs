using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web;

/// <summary>
/// El contexto de lectura que la Presentación le pasa al asistente de compra en <b>cada consulta</b> (identidad-seguridad T45, web-base T08; IDE-33, AC-IDE-30): un valor secreto de 5 minutos, sin renovación, que vale
/// solo para leer los pedidos de ese cliente y solo para el módulo de pedidos. Nunca se guarda ni se reutiliza: pedir uno nuevo en cada consulta es la regla, y el valor nunca llega al navegador (AGENTS.md §3.9).
/// </summary>
public interface IContextoDelAsistente
{
    /// <summary>Pide un contexto nuevo para el cliente de la sesión; solo un cliente lo obtiene (otro rol o un visitante: <c>Prohibido</c>) y Identidad decide si la cuenta sigue vigente.</summary>
    Task<Resultado<ValorSecreto>> Pedir(ActorVerificado actor, CancellationToken cancelacion = default);
}
