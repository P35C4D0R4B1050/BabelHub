using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>
    /// Movimientos de stock, catálogo, licencias, fuentes y rutas (T12; AUD-14, AUD-15). Ninguna lleva datos de una persona: los objetos son pedidos,
    /// ediciones, fuentes o rutas y las claves son identificadores opacos, cantidades y nombres de campos. La excepción es la baja de un titular, cuyo
    /// objeto es el identificador del titular y se anonimiza con la cuenta.
    /// </summary>
    private static IEnumerable<AccionDelCatalogo> AccionesDeStockCatalogoYFuentes()
    {
        // Movimientos de stock: objeto = el pedido; la fuente va en la sucursal de origen del registro.
        yield return Auditada(nameof(AccionesAuditables.ReservaConfirmada), false, false, Clave("edicionId"), Clave("cantidad"));
        yield return Auditada(nameof(AccionesAuditables.ReservaLiberada), false, false, Clave("edicionId"), Clave("cantidad"), Clave("causa"));
        yield return Auditada(nameof(AccionesAuditables.ReservaExpirada), false, false, Clave("edicionId"), Clave("cantidad"));
        yield return Auditada(nameof(AccionesAuditables.StockDescontadoPorEntrega), false, false, Clave("edicionId"), Clave("cantidad"), Clave("cantidadAnterior"), Clave("cantidadPosterior"));
        yield return Auditada(nameof(AccionesAuditables.StockDescontadoPorReparto), false, false, Clave("edicionId"), Clave("cantidad"), Clave("cantidadAnterior"), Clave("cantidadPosterior"));
        yield return Auditada(nameof(AccionesAuditables.BajaDeTitularProcesada), true, false);

        // Catálogo y licencias.
        yield return Auditada(nameof(AccionesAuditables.ObraRegistrada), false, false, Clave("titulo", 200));
        yield return Auditada(nameof(AccionesAuditables.ObraEditada), false, false, Clave("titulo", 200), Clave("camposCambiados", 200));
        yield return Auditada(nameof(AccionesAuditables.ObraDadaDeBaja), false, false, Clave("titulo", 200));
        yield return Auditada(nameof(AccionesAuditables.EdicionRegistrada), false, false, Clave("isbn"), new ClaveDeDetalle("formato", personal: false) { Valores = ["impresa", "digital"] });
        yield return Auditada(nameof(AccionesAuditables.EdicionEditada), false, false, Clave("isbn"), Clave("camposCambiados", 200));
        yield return Auditada(nameof(AccionesAuditables.EdicionDadaDeBaja), false, false, Clave("isbn"));
        yield return Auditada(nameof(AccionesAuditables.EdicionDigitalRetirada), false, false, Clave("isbn"));
        yield return Auditada(
            nameof(AccionesAuditables.LicenciaCambiada),
            false,
            false,
            Clave("tipoAnterior"),
            Clave("tipoNuevo"),
            Clave("vigenciaAnterior"),
            Clave("vigenciaNueva"),
            Clave("territoriosAnterior"),
            Clave("territoriosNuevo"));

        // Archivos del catálogo: tipo, tamaño y huella corta (los primeros 16 caracteres del resumen); nunca la clave del almacén ni un enlace.
        yield return Auditada(nameof(AccionesAuditables.ArchivoDeEdicionReemplazado), false, false, Clave("tipo"), Clave("tamanoEnBytes"), Clave("huellaCorta"), Clave("huellaCortaAnterior"));
        yield return Auditada(nameof(AccionesAuditables.PortadaDeEdicionReemplazada), false, false, Clave("tipo"), Clave("tamanoEnBytes"), Clave("huellaCorta"), Clave("huellaCortaAnterior"));

        // Fuentes y rutas: sin el número de contacto, que nunca entra a la auditoría. La ruta lleva su origen y su destino en las sucursales del registro.
        yield return Auditada(nameof(AccionesAuditables.FuenteRegistrada), false, false, Clave("codigoDeFuente"));
        yield return Auditada(nameof(AccionesAuditables.FuenteEditada), false, false, Clave("codigoDeFuente"), Clave("camposCambiados", 200));
        yield return Auditada(nameof(AccionesAuditables.RecepcionDesignada), false, false, Clave("fuenteAnteriorId"), Clave("fuenteNuevaId"));
        yield return Auditada(nameof(AccionesAuditables.RutaRegistrada), false, false, Clave("plazoNuevoEnDias"));
        yield return Auditada(nameof(AccionesAuditables.RutaModificada), false, false, Clave("plazoAnteriorEnDias"), Clave("plazoNuevoEnDias"));
    }
}
