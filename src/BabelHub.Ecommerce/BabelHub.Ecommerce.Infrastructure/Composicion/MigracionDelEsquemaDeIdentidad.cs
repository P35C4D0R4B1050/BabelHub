using BabelHub.Ecommerce.Infrastructure.Auditoria;
using BabelHub.Ecommerce.Infrastructure.Identidad;

namespace BabelHub.Ecommerce.Infrastructure.Composicion;

/// <summary>
/// La migración completa del esquema <c>identidad</c> (identidad-seguridad T30): roles, tablas, registros aislados con la plantilla de inmutabilidad y la función de depuración de <c>Auditoria</c>, y los permisos
/// mínimos. Une dos módulos (Identidad y Auditoria) sin que uno referencie el interior del otro, por eso vive en la composición. Presupone la migración de <c>Auditoria</c> (que crea
/// <c>auditoria_migraciones</c>, el propietario de la función de depuración y la política).
/// </summary>
public static class MigracionDelEsquemaDeIdentidad
{
    public static string Completa() =>
        DefinicionDeIdentidad.Roles
        + DefinicionDeIdentidad.Tablas
        + DefinicionDeAuditoria.RegistrosInmutables(DefinicionDeIdentidad.Esquema)
        + DefinicionDeAuditoria.PorAnonimizar(DefinicionDeIdentidad.Esquema)
        + DefinicionDeAuditoria.PermisosDeLaDepuracion(DefinicionDeIdentidad.Esquema)
        + DefinicionDeIdentidad.Permisos
        + DefinicionDeAuditoria.FuncionDeDepuracion(DefinicionDeIdentidad.Esquema, DefinicionDeIdentidad.Esquema);
}
