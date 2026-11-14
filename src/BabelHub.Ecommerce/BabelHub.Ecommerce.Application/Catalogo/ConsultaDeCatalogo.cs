using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Catalogo;

/// <summary>Una obra disponible elegida al azar para «Sorpréndeme» (WEB-17): solo su identificador; la ficha es del catálogo.</summary>
public sealed record ObraAlAzar(Guid ObraId);

/// <summary>
/// Contrato público de lectura del Catálogo. <b>Declaración mínima</b> hecha por <c>web-base</c> T26: solo lo que «Sorpréndeme» necesita. El plan de
/// <c>ecommerce-catalogo</c> (T05) lo amplía con la búsqueda, la obra con sus ediciones y las ediciones por identificador sin cambiar lo que ya hay aquí.
/// </summary>
public interface IConsultaDeCatalogo
{
    /// <summary>
    /// Una obra disponible al azar: una edición digital vigente cuenta como disponible sin consultar al Inventario (RF04) y, si el Inventario no
    /// responde, solo se ofrecen digitales o ninguna. <see langword="null"/> en el valor si no hay ninguna disponible.
    /// </summary>
    Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default);
}
