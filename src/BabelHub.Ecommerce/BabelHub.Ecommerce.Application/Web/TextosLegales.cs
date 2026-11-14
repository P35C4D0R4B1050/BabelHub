using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web;

/// <summary>Los textos legales que publica la tienda (WEB-29, NN15).</summary>
public enum TipoDeTextoLegal
{
    Terminos,
    Privacidad,
    Reembolso,
}

/// <summary>El nombre de cada texto en la ruta (<c>/legal/terminos</c>…): una lista cerrada, nunca un valor libre.</summary>
public static class TiposDeTextoLegal
{
    public static bool TryParse(string? clave, out TipoDeTextoLegal tipo)
    {
        (var encontrado, tipo) = clave switch
        {
            "terminos" => (true, TipoDeTextoLegal.Terminos),
            "privacidad" => (true, TipoDeTextoLegal.Privacidad),
            "reembolso" => (true, TipoDeTextoLegal.Reembolso),
            _ => (false, default(TipoDeTextoLegal)),
        };
        return encontrado;
    }

    public static string Clave(TipoDeTextoLegal tipo) => tipo switch
    {
        TipoDeTextoLegal.Terminos => "terminos",
        TipoDeTextoLegal.Privacidad => "privacidad",
        TipoDeTextoLegal.Reembolso => "reembolso",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };
}

/// <summary>Una sección de un texto legal: un título y sus párrafos, en texto plano.</summary>
public sealed record SeccionDeTextoLegal(string Titulo, IReadOnlyList<string> Parrafos);

/// <summary>
/// Un texto legal listo para mostrar: su versión y la fecha desde la que rige (las informa Identidad, IDE-57) y su cuerpo
/// (recursos versionados de la web). <see cref="Provisional"/> marca el texto de trabajo que la persona responsable aún no redactó.
/// </summary>
public sealed record TextoLegal(
    TipoDeTextoLegal Tipo,
    string Titulo,
    string Version,
    DateOnly VigenteDesde,
    bool Provisional,
    string Resumen,
    IReadOnlyList<SeccionDeTextoLegal> Secciones);

/// <summary>Contrato público: los textos legales con su versión y su vigencia (WEB-29, AC-WEB-24).</summary>
public interface ITextosLegales
{
    /// <summary>El texto vigente; <see cref="CategoriaDeError.NoDisponible"/> si Identidad no informa su versión (no se inventa una).</summary>
    Task<Resultado<TextoLegal>> Obtener(TipoDeTextoLegal tipo, CancellationToken cancelacion = default);
}
