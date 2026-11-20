using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Catalogo;

namespace BabelHub.Ecommerce.Application.Catalogo.Interno;

/// <summary>
/// La implementación de <see cref="IReferenciaDeArchivosDelCatalogo"/> (D13, D17 de Catálogo; la necesita <c>ecommerce-biblioteca-digital</c>): la clave del archivo en el almacén, su formato, su huella y su tamaño, <b>también de una edición retirada de la venta o dada de baja</b> (el acceso de quien ya
/// la compró se conserva, BIB-09). «No encontrada» si la edición no existe o no es digital. Es solo lectura, no llama a la red ni al Inventario y su resultado nunca llega a la web ni se registra (la clave del almacén no se imprime).
/// </summary>
internal sealed class ReferenciaDeArchivosDelCatalogoDeLaAplicacion(ILecturasDelCatalogo lecturas) : IReferenciaDeArchivosDelCatalogo
{
    public async Task<Resultado<ReferenciaDeArchivoDigital>> ObtenerDeEdicion(Guid edicionId, CancellationToken cancelacion = default)
    {
        if (edicionId == Guid.Empty)
        {
            return Resultado<ReferenciaDeArchivoDigital>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "edicion-no-encontrada"));
        }

        var halladas = await lecturas.EdicionesPorId([edicionId], cancelacion).ConfigureAwait(false);
        if (halladas.FirstOrDefault(h => h.Edicion.Id == edicionId)?.Edicion is not { EsDigital: true, Archivo: { } archivo })
        {
            return Resultado<ReferenciaDeArchivoDigital>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "edicion-no-encontrada"));
        }

        var formato = archivo.TipoDetectado switch
        {
            TipoDeArchivo.Epub => FormatoDeArchivoDigital.Epub,
            TipoDeArchivo.Pdf => FormatoDeArchivoDigital.Pdf,
            _ => (FormatoDeArchivoDigital?)null,
        };
        return formato is null
            ? Resultado<ReferenciaDeArchivoDigital>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "edicion-no-encontrada"))
            : Resultado<ReferenciaDeArchivoDigital>.Exito(new ReferenciaDeArchivoDigital(edicionId, archivo.ClaveDelAlmacen, formato.Value, archivo.Huella.Hex, archivo.TamanoEnBytes));
    }
}
