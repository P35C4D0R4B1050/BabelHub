namespace BabelHub.Ecommerce.Application.Plataforma;

/// <summary>
/// Categoría de un error de un caso de uso (SEG-03). La Aplicación expresa sus fallos con una categoría y la Api la
/// traduce al código estable <c>&lt;SRV&gt;-…</c> y al estado HTTP; el catálogo de códigos no vive en el Dominio.
/// </summary>
public enum CategoriaDeError
{
    /// <summary>La solicitud no cumple el formato, el largo o el rango esperados.</summary>
    Validacion,

    /// <summary>La solicitud es válida pero incumple una regla de negocio.</summary>
    ReglaDeNegocio,

    NoAutenticado,

    Prohibido,

    NoEncontrado,

    Conflicto,

    /// <summary>Límite de uso excedido (SEG-07).</summary>
    Limite,

    /// <summary>Error no previsto: la respuesta es un mensaje genérico, sin trazas ni datos internos.</summary>
    Inesperado,

    /// <summary>Una dependencia necesaria no responde (falla cerrada).</summary>
    NoDisponible,
}
