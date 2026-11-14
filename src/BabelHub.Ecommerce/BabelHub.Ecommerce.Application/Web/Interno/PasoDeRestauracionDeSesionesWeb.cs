using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Web.Interno;

/// <summary>Puerto: vacía las tablas de sesiones y de borradores del esquema <c>web</c> (la Infraestructura lo implementa con el rol del módulo).</summary>
public interface IVaciadoDeLaWeb
{
    Task VaciarSesionesYBorradores(CancellationToken cancelacion = default);
}

/// <summary>
/// Paso de restauración del módulo <c>Web</c> (D14, PLA-39, PLA-43): los respaldos y el archivado de WAL contienen las sesiones cifradas y ninguna sesión
/// restaurada debe seguir válida, así que se vacían las sesiones y los borradores <b>antes de abrir al público</b>. Corre después del paso de Identidad (que rota la
/// clave de firma) y antes del de Auditoría. El anillo de Data Protection se conserva.
/// </summary>
public sealed class PasoDeRestauracionDeSesionesWeb(IVaciadoDeLaWeb vaciado) : IPasoDeRestauracion
{
    public const string NombreDelPaso = "web-vaciar-sesiones";

    /// <summary>Entre <see cref="OrdenDePasoDeRestauracion.Identidad"/> y <see cref="OrdenDePasoDeRestauracion.Auditoria"/>: la constante de <c>Web</c> (R12).</summary>
    public int Orden => OrdenDePasoDeRestauracion.Web;

    public string Nombre => NombreDelPaso;

    public async Task<ResultadoDePaso> Ejecutar(CancellationToken cancelacion = default)
    {
        await vaciado.VaciarSesionesYBorradores(cancelacion).ConfigureAwait(false);
        return ResultadoDePaso.Correcto;
    }
}
