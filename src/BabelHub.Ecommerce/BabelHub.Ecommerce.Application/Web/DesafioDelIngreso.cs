namespace BabelHub.Ecommerce.Application.Web;

/// <summary>El paso que le falta a quien tiene un desafío de ingreso abierto (WEB-34, IDE-23, IDE-42): Identidad no emite credencial hasta completarlo.</summary>
public enum PasoDelIngresoWeb
{
    /// <summary>El desafío no existe, venció o ya se completó: no hay paso pendiente.</summary>
    Ninguno,

    ContrasenaTemporal,

    ConfigurarSegundoFactor,

    PresentarSegundoFactor,

    /// <summary>No se pudo saber (Identidad no responde o respondió algo ilegible): la web falla cerrada y lleva a la persona al ingreso, sin ofrecer otra pantalla.</summary>
    Desconocido,
}

/// <summary>
/// Contrato público de lo que la web necesita saber del <b>desafío del ingreso</b> (WEB-34, D12): el valor opaco que viaja en la cookie <c>__Host-bh-desafio</c> (5 minutos, de un solo uso) y que
/// Identidad entiende. La implementación real consulta a Identidad (<c>IIngreso</c>, aún por construir: R12); la web nunca interpreta el valor ni guarda el estado.
/// </summary>
public interface IDesafioDelIngreso
{
    /// <summary>El primer paso pendiente de ese desafío según su orden (contraseña temporal, configurar el segundo factor y presentarlo), o <see cref="PasoDelIngresoWeb.Ninguno"/>.</summary>
    Task<PasoDelIngresoWeb> PasoPendiente(string valorDelDesafio, CancellationToken cancelacion = default);

    /// <summary>Cancela el ingreso: Identidad descarta el desafío. Idempotente y sin error si ya no existe.</summary>
    Task Cancelar(string valorDelDesafio, CancellationToken cancelacion = default);
}
