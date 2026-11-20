using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Infrastructure.Reclamaciones;

/// <summary>
/// El correo <b>verificado</b> de una cuenta sobre el contrato <b>de sistema</b> de Identidad (<see cref="ICorreoVerificadoDeSistema"/>; T14, D04): devuelve el correo normalizado (recortado y en minúsculas) si la cuenta lo tiene
/// verificado, o <see langword="null"/>. Si Identidad no responde lanza (falla cerrada: nunca se confunde «no se pudo saber» con «no hay correo verificado»). Funciona con cualquier actor —el visitante que reemplaza una cuenta
/// sin verificar (IDE-84) o ninguno, al reaplicar supresiones tras restaurar (PLA-19)—, porque el contrato de sistema no comprueba titularidad.
/// </summary>
internal sealed class CorreoVerificadoDeLaCuenta(ICorreoVerificadoDeSistema correos) : ICorreoVerificadoDeLaCuenta
{
    public async Task<string?> De(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La consulta necesita la cuenta.", nameof(usuarioId));
        }

        var correo = await correos.Consultar(usuarioId, cancelacion).ConfigureAwait(false);
        return correo is not null && CorreoNormalizado.TryCrear(correo, out var normalizado) ? normalizado : null;
    }
}
