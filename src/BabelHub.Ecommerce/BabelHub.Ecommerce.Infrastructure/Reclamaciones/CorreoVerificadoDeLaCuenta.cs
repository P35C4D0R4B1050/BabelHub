using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Reclamaciones.Interno;
using BabelHub.Ecommerce.Domain.Reclamaciones;

namespace BabelHub.Ecommerce.Infrastructure.Reclamaciones;

/// <summary>
/// El correo <b>verificado</b> de una cuenta sobre el contrato público de Identidad (<see cref="ICuentaDelCliente"/>; T14, D04): devuelve el correo normalizado (recortado y en minúsculas) si la cuenta lo tiene verificado, <see langword="null"/> si no lo tiene verificado, y <b>lanza</b> si Identidad no
/// responde o no entrega la cuenta (falla cerrada: nunca se confunde «no se pudo saber» con «no hay correo verificado»). Solo la propia persona consulta su cuenta, que es lo que hace la consulta de sus hojas y la cancelación de su cuenta. No guarda ni registra el correo.
/// </summary>
internal sealed class CorreoVerificadoDeLaCuenta(ICuentaDelCliente cuentas) : ICorreoVerificadoDeLaCuenta
{
    public async Task<string?> De(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La consulta necesita la cuenta.", nameof(usuarioId));
        }

        var resultado = await cuentas.Consultar(usuarioId, cancelacion).ConfigureAwait(false);
        if (!resultado.EsExito)
        {
            // Sin respuesta de Identidad (o una cuenta que no puede leerse): no se sabe, así que se falla en vez de decir «sin correo verificado».
            throw new InvalidOperationException("Identidad no entregó la cuenta para leer su correo verificado.");
        }

        var cuenta = resultado.Valor;
        return cuenta.CorreoVerificado && CorreoNormalizado.TryCrear(cuenta.Correo, out var normalizado) ? normalizado : null;
    }
}
