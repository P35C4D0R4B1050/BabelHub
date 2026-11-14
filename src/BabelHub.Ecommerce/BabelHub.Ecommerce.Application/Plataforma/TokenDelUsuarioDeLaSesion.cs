namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// El token del usuario en cuyo nombre se llama a otro servicio (operación de usuario del Inventario), tal como sale de la sesión guardada en el
/// servidor. Es un tipo en memoria de la solicitud en curso: no se serializa ni se imprime (<see cref="ToString"/> devuelve «[redactado]») y solo
/// <see cref="ValorSecreto.Revelar"/> entrega el texto, a quien lo pone en la cabecera de salida.
/// </summary>
/// <remarks>Se reutiliza <see cref="ValorSecreto"/>: ya es un secreto en memoria con su texto redactado en cualquier registro (SEG-10).</remarks>
public interface ITokenDelUsuarioDeLaSesion
{
    /// <summary>
    /// Entrega el token vigente de la persona con sesión en esta solicitud: la implementación (la Api, desde la sesión) verifica la sesión y
    /// renueva la credencial si hace falta. Devuelve <see langword="null"/> si no hay sesión o no hay credencial utilizable; en ese caso el
    /// llamador no hace la llamada (nunca la reemplaza por una cabecera de actor, D13 de <c>docs/03/08</c>, SEG-15).
    /// </summary>
    Task<ValorSecreto?> Actual(CancellationToken cancelacion = default);
}
