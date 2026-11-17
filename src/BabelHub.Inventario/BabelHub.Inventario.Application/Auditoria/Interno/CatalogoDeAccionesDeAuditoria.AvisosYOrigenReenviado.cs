using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>
    /// Los eventos de supresión de avisos (AUD-43) y de dirección reenviada sin credencial (AUD-44), de T16. Ninguno declara claves del detalle: el aviso
    /// suprimido guarda solo la cuenta como objeto y el motivo (sin correo ni contenido), y el origen reenviado solo la dirección directa del llamante
    /// como origen del registro (la cabecera de reenvío no entra a ningún campo).
    /// </summary>
    private static IEnumerable<AccionDelCatalogo> AccionesDeAvisosYOrigenReenviado()
    {
        yield return Seguridad(
            nameof(AccionesAuditables.AvisoDeSeguridadSuprimido),
            true,
            ["cuota-de-correo", "tope-por-cuenta", "tope-por-origen", "correo-sin-verificar"],
            aislada: true);
        yield return Seguridad(nameof(AccionesAuditables.OrigenReenviadoSinCredencial), false, ["origen-reenviado-sin-credencial"]);
        yield return Seguridad(nameof(AccionesAuditables.AccesoBloqueadoPorTextosPendientes), true, ["accion-no-permitida", "ruta-no-declarada", "metodo-no-declarado", "metodo-no-estandar"], Clave("metodo", 16), Clave("plantilla", 100));
    }
}
