using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary><see cref="ICorreoVerificadoDeSistema"/> sobre las cuentas de Identidad: solo lee y no deja rastro del correo.</summary>
public sealed class CorreoVerificadoDeSistemaDeIdentidad(IRepositorioDeCuentas cuentas) : ICorreoVerificadoDeSistema
{
    public async Task<string?> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            throw new ArgumentException("La consulta necesita la cuenta.", nameof(usuarioId));
        }

        var cuenta = await cuentas.PorId(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        // Una cuenta cancelada nunca entrega su correo (defensa en profundidad: la anonimización ya apaga la verificación); una cuenta inexistente es «sin correo verificado» a propósito.
        return cuenta is { CanceladaEn: null, Usuario.CorreoVerificado: true } ? Usuario.NormalizarCorreo(cuenta.Usuario.Correo) : null;
    }
}
