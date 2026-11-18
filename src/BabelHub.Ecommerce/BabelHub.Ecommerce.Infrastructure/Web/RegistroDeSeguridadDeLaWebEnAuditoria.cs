using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Web.Interno;

namespace BabelHub.Ecommerce.Infrastructure.Web;

/// <summary>
/// <see cref="IRegistroDeSeguridadDeLaWeb"/> sobre el contrato público de <c>Auditoría</c> (<see cref="IDenegacionDeAcceso"/>, AUD-05): un acceso denegado propio de la web con un
/// motivo fijo del catálogo y sin datos de la sesión (ni el valor de la cookie ni su hash, ni el usuario: el contexto de la solicitud lo aporta Auditoría). <b>Nunca lanza</b>:
/// un registro de seguridad no interrumpe un caso de uso (AUD-12); si Auditoría falla, la protección se mantiene.
/// </summary>
/// <remarks>
/// El catálogo de motivos de <c>acceso_denegado</c> aún no tiene motivos propios de la web (R12: antiforgery, descifrado, otro host, identificador cerrado, pareja de rol y host);
/// hasta que el carril A los agregue, cada uno se registra con el motivo existente más cercano. Cambiar <see cref="MotivoDelCatalogo"/> es lo único que hay que tocar entonces.
/// </remarks>
public sealed class RegistroDeSeguridadDeLaWebEnAuditoria(IDenegacionDeAcceso denegaciones, RegistroDeBloqueosPorTextosPendientes bloqueos) : IRegistroDeSeguridadDeLaWeb
{
    public async Task AccesoDenegado(MotivoDeDenegacionWeb motivo, Guid? usuarioId, CancellationToken cancelacion = default)
    {
        try
        {
            await denegaciones.Registrar(new DenegacionDeAcceso(MotivoDelCatalogo(motivo), ruta: null), cancelacion).ConfigureAwait(false);
        }
        catch (Exception) when (!cancelacion.IsCancellationRequested)
        {
            // Auditoría ya deja su alerta técnica si no puede escribir; aquí solo se evita que el rechazo se convierta en un error.
        }
    }

    /// <summary>
    /// El bloqueo por textos legales pendientes es un evento <b>propio</b> (WEB-47): su acción, su contador por ventana de 15 minutos y su alerta técnica (AUD-29) los lleva <see cref="RegistroDeBloqueosPorTextosPendientes"/>. Registrarlo como
    /// <c>acceso_denegado</c> lo contaría para SEG-09, que el requisito prohíbe.
    /// </summary>
    public Task TextosPendientesBloqueado(ClaveDeBloqueoPorTextos clave, CancellationToken cancelacion = default) => bloqueos.Registrar(clave, cancelacion);

    /// <summary>El motivo del catálogo de <c>acceso_denegado</c> que corresponde a cada situación (R12 pide motivos propios; hasta entonces, el más cercano).</summary>
    public static string MotivoDelCatalogo(MotivoDeDenegacionWeb motivo) => motivo switch
    {
        MotivoDeDenegacionWeb.ParejaDeRolYHostInvalida => "fuera-de-rol",
        MotivoDeDenegacionWeb.SesionDeOtroHost => "sin-sesion",
        MotivoDeDenegacionWeb.SecretoIlegible => "sin-sesion",
        MotivoDeDenegacionWeb.IdentificadorDeSesionCerrado => "sin-sesion",
        _ => throw new ArgumentOutOfRangeException(nameof(motivo), motivo, "Motivo de denegación de la web desconocido."),
    };
}
