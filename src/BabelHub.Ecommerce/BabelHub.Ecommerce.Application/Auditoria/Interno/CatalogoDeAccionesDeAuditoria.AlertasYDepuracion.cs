using BabelHub.Ecommerce.Domain.Auditoria;

namespace BabelHub.Ecommerce.Application.Auditoria.Interno;

public static partial class CatalogoDeAccionesDeAuditoria
{
    /// <summary>Las acciones del propio módulo (T17 y T19; AUD-34, AUD-36): la marca de una alerta como revisada y la ejecución de la depuración diaria.</summary>
    private static IEnumerable<AccionDelCatalogo> AccionesDeAlertasYDepuracion()
    {
        yield return Auditada(nameof(AccionesAuditables.AlertaMarcadaRevisada), false, false, Clave("servicio", 16), Clave("motivo"), Clave("contadorVisto", 8));
        yield return Auditada(nameof(AccionesAuditables.DepuracionDiariaEjecutada), false, false, Clave("registrosAnonimizados", 12));
    }
}
