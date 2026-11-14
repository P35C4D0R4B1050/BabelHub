namespace BabelHub.Ecommerce.Application.Auditoria;

/// <summary>
/// Las acciones auditables que los demás módulos nombran (D15). El nombre de cada constante es el identificador de su acción en el catálogo del
/// módulo <c>Auditoria</c>, y una prueba de contrato compara ambos: una acción sin constante, o una constante sin acción, no pasa. Cada módulo pide
/// la suya por <see cref="IAuditoria"/> o <see cref="IRegistroDeSeguridad"/>; si le falta una, pide su ampliación aquí en lugar de crearla (AUD-26).
/// </summary>
public static partial class AccionesAuditables
{
    // ---------- Movimientos de stock (AUD-14, SEL-32) ----------

    /// <summary>Una reserva pasó de pendiente a confirmada (con la edición, la fuente y la cantidad).</summary>
    public static readonly AccionAuditable ReservaConfirmada = new(nameof(ReservaConfirmada));

    /// <summary>Una reserva se liberó (la causa figura en el detalle: reemplazo, anulación, cancelación…).</summary>
    public static readonly AccionAuditable ReservaLiberada = new(nameof(ReservaLiberada));

    /// <summary>Una reserva venció y se liberó sola (actor «sistema»), también la de vencidas ajenas liberadas al reservar o ajustar.</summary>
    public static readonly AccionAuditable ReservaExpirada = new(nameof(ReservaExpirada));

    /// <summary>El stock físico se descontó por la entrega de un pedido.</summary>
    public static readonly AccionAuditable StockDescontadoPorEntrega = new(nameof(StockDescontadoPorEntrega));

    /// <summary>El stock físico se descontó porque un envío pasó a en reparto.</summary>
    public static readonly AccionAuditable StockDescontadoPorReparto = new(nameof(StockDescontadoPorReparto));

    /// <summary>El Inventario recibió y aplicó la baja de un titular (actor «integración», AUD-32).</summary>
    public static readonly AccionAuditable BajaDeTitularProcesada = new(nameof(BajaDeTitularProcesada));

    // ---------- Catálogo y licencias (AUD-15) ----------

    public static readonly AccionAuditable ObraRegistrada = new(nameof(ObraRegistrada));

    public static readonly AccionAuditable ObraEditada = new(nameof(ObraEditada));

    public static readonly AccionAuditable ObraDadaDeBaja = new(nameof(ObraDadaDeBaja));

    public static readonly AccionAuditable EdicionRegistrada = new(nameof(EdicionRegistrada));

    public static readonly AccionAuditable EdicionEditada = new(nameof(EdicionEditada));

    public static readonly AccionAuditable EdicionDadaDeBaja = new(nameof(EdicionDadaDeBaja));

    /// <summary>La administración retiró una edición digital de la venta (los accesos ya concedidos se conservan, CAT-08).</summary>
    public static readonly AccionAuditable EdicionDigitalRetirada = new(nameof(EdicionDigitalRetirada));

    /// <summary>Cambió la licencia de una edición digital (valor anterior y nuevo).</summary>
    public static readonly AccionAuditable LicenciaCambiada = new(nameof(LicenciaCambiada));

    /// <summary>Se reemplazó el archivo de una edición digital (tipo, tamaño y huella corta; nunca la clave del almacén ni un enlace).</summary>
    public static readonly AccionAuditable ArchivoDeEdicionReemplazado = new(nameof(ArchivoDeEdicionReemplazado));

    public static readonly AccionAuditable PortadaDeEdicionReemplazada = new(nameof(PortadaDeEdicionReemplazada));

    // ---------- Fuentes y rutas (AUD-14, AUD-15) ----------

    public static readonly AccionAuditable FuenteRegistrada = new(nameof(FuenteRegistrada));

    /// <summary>Se editó una fuente: el detalle nombra los campos cambiados y nunca el número de contacto.</summary>
    public static readonly AccionAuditable FuenteEditada = new(nameof(FuenteEditada));

    /// <summary>Cambió la fuente de recepción (FUE-20).</summary>
    public static readonly AccionAuditable RecepcionDesignada = new(nameof(RecepcionDesignada));

    public static readonly AccionAuditable RutaRegistrada = new(nameof(RutaRegistrada));

    /// <summary>Cambió el plazo de una ruta entre fuentes: guarda el anterior y el nuevo, con la fuente de origen y la de destino.</summary>
    public static readonly AccionAuditable RutaModificada = new(nameof(RutaModificada));
}
