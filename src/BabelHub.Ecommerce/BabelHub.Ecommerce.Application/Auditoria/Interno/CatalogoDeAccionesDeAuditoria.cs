using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

/// <summary>
/// El catálogo cerrado de las acciones auditables del servicio (AUD-01, AUD-02, AUD-14 a AUD-16, AUD-21, AUD-22, AUD-33, AUD-35, AUD-37 y los
/// eventos de seguridad). Cada acción declara su clase, el tipo de su registro, las claves permitidas de su detalle (cada una con su marca de
/// personal), si su objeto y su rol son personales y, en un evento de seguridad, sus motivos. Una acción que no está aquí no se registra (AUD-26: la
/// de otra feature se agrega aquí, con su prueba de contrato). Está repartido por tema en archivos parciales, una tarea cada uno.
/// </summary>
public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>El catálogo completo del servicio.</summary>
    public static CatalogoDeAcciones Crear() => new([.. AccionesDeOperaciones(), .. AccionesDeStockCatalogoYFuentes(), .. AccionesDeIdentidadYOtrosModulos(), .. AccionesDeEventosDeSeguridad(), .. AccionesDeAvisosYOrigenReenviado()]);

    /// <summary>Una acción que concede o cambia algo, de auditoría pura, sin motivo: el objeto y el rol se declaran siempre (sin valor por defecto).</summary>
    private static AccionDelCatalogo Auditada(string identificador, bool objetoPersonal, bool rolPersonal, params ClaveDeDetalle[] detalle) =>
        new(identificador, ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Auditoria, aislada: false, detalle, objetoPersonal, rolPersonal, motivos: [], exigeMotivo: false);

    /// <summary>Una acción de Identidad que concede o cambia algo: va al almacén aislado (D17); el rol del actor no es personal.</summary>
    private static AccionDelCatalogo AuditadaAislada(string identificador, bool objetoPersonal, params ClaveDeDetalle[] detalle) =>
        new(identificador, ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Auditoria, aislada: true, detalle, objetoPersonal, rolPersonal: false, motivos: [], exigeMotivo: false);

    /// <summary>
    /// Una acción de Identidad que es a la vez operación sensible y evento de seguridad (AUD-20): un único registro de ambos tipos, con motivo de
    /// catálogo cerrado, en el almacén aislado.
    /// </summary>
    private static AccionDelCatalogo AuditadaYSeguridadAislada(string identificador, bool objetoPersonal, string[] motivos, params ClaveDeDetalle[] detalle) =>
        new(identificador, ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Ambos, aislada: true, detalle, objetoPersonal, rolPersonal: false, motivos, exigeMotivo: true);

    /// <summary>Una clave del detalle que no identifica a una persona.</summary>
    private static ClaveDeDetalle Clave(string nombre) => new(nombre, personal: false);

    /// <summary>Una clave del detalle cuyo valor identifica a una persona: la depuración la anonimiza (AUD-19).</summary>
    private static ClaveDeDetalle ClavePersonal(string nombre) => new(nombre, personal: true);
}
