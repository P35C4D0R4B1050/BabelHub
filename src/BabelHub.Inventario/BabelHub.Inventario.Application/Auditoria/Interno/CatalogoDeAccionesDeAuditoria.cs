using BabelHub.Inventario.Domain.Auditoria;

namespace BabelHub.Inventario.Application.Auditoria.Interno;

/// <summary>
/// El catálogo cerrado de las acciones auditables del servicio (AUD-01, AUD-02, AUD-14 a AUD-16, AUD-21, AUD-22, AUD-33, AUD-35, AUD-37 y los
/// eventos de seguridad). Cada acción declara su clase, el tipo de su registro, las claves permitidas de su detalle (cada una con su marca de
/// personal), si su objeto y su rol son personales y, en un evento de seguridad, sus motivos. Una acción que no está aquí no se registra (AUD-26: la
/// de otra feature se agrega aquí, con su prueba de contrato). Está repartido por tema en archivos parciales, una tarea cada uno.
/// </summary>
public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>El catálogo completo del servicio.</summary>
    public static CatalogoDeAcciones Crear() => new([.. AccionesDeStockCatalogoYFuentes()]);

    /// <summary>Una acción que concede o cambia algo, de auditoría pura, sin motivo: el objeto y el rol se declaran siempre (sin valor por defecto).</summary>
    private static AccionDelCatalogo Auditada(string identificador, bool objetoPersonal, bool rolPersonal, params ClaveDeDetalle[] detalle) =>
        new(identificador, ClaseDeAccion.ConcedeOCambia, TipoDeRegistro.Auditoria, aislada: false, detalle, objetoPersonal, rolPersonal, motivos: [], exigeMotivo: false);

    /// <summary>Una clave del detalle que no identifica a una persona.</summary>
    private static ClaveDeDetalle Clave(string nombre) => new(nombre, personal: false);

    /// <summary>Una clave del detalle cuyo valor identifica a una persona: la depuración la anonimiza (AUD-19).</summary>
    private static ClaveDeDetalle ClavePersonal(string nombre) => new(nombre, personal: true);
}
