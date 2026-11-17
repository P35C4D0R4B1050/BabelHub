using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Catalogo;

/// <summary>Una obra disponible elegida al azar para «Sorpréndeme» (WEB-17): solo su identificador; la ficha es del catálogo.</summary>
public sealed record ObraAlAzar(Guid ObraId);

/// <summary>
/// Contrato público de lectura del Catálogo (regla 13). <c>web-base</c> T26 declaró <see cref="ObtenerObraAlAzar"/>; <c>ecommerce-catalogo</c> T05 lo amplía con la búsqueda, la
/// obra con sus ediciones y las ediciones por identificador sin cambiar lo que ya había. No exige sesión: sus modelos excluyen todo campo interno.
/// </summary>
public interface IConsultaDeCatalogo
{
    /// <summary>
    /// Una obra disponible al azar por muestreo de hasta 3 lotes de 50 (sin conteo exacto): una edición digital vigente cuenta como disponible sin consultar al Inventario (RF04) y, si
    /// el Inventario no responde, solo se ofrecen digitales o ninguna. <see langword="null"/> en el valor si no hay ninguna disponible.
    /// </summary>
    Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default);

    /// <summary>
    /// Busca obras (CAT-09). Primero se filtra por la base y la disponibilidad se consulta sobre a lo más 500 ediciones candidatas; con más, <c>ECO-BUSQUEDA-DEMASIADO-AMPLIA</c> (422). La
    /// disponibilidad usa la memoria de 30 segundos y, si el Inventario no responde, el último dato de menos de 10 minutos (CAT-12).
    /// </summary>
    Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default);

    /// <summary>La ficha de una obra con sus ediciones no dadas de baja y su disponibilidad (CAT-10); una obra dada de baja o inexistente es «no encontrada».</summary>
    Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default);

    /// <summary>
    /// Las ediciones por identificador con su título, su ISBN, su precio, su formato, su estado y el motivo por el que no se ofrecen (CAT-13, CAT-18): incluye las de baja y retiradas, que
    /// los pedidos y traslados en curso siguen mostrando. Las que no existen se omiten.
    /// </summary>
    Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default);
}
