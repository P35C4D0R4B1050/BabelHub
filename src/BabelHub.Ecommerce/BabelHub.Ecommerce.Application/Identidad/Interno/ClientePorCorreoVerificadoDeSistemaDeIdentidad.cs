using BabelHub.Ecommerce.Domain.Identidad;

namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary><see cref="IClientePorCorreoVerificadoDeSistema"/> sobre las cuentas de Identidad: solo lee, sin actor, y no deja rastro del correo (IDE-99).</summary>
public sealed class ClientePorCorreoVerificadoDeSistemaDeIdentidad(IRepositorioDeCuentas cuentas) : IClientePorCorreoVerificadoDeSistema
{
    public async Task<Guid?> Consultar(string correoNormalizado, CancellationToken cancelacion = default)
    {
        if (string.IsNullOrWhiteSpace(correoNormalizado))
        {
            throw new ArgumentException("La consulta necesita el correo.", nameof(correoNormalizado));
        }

        var cuenta = await cuentas.PorCorreo(Usuario.NormalizarCorreo(correoNormalizado), cancelacion).ConfigureAwait(false);

        // Solo un cliente activo, no cancelado y con el correo verificado: el personal, una cuenta sin verificar, cancelada o desactivada y un correo sin cuenta responden lo mismo (nada).
        return cuenta is { CanceladaEn: null, Usuario: { Rol: RolDeUsuario.Cliente, CorreoVerificado: true, Activo: true } } ? cuenta.Usuario.Id : null;
    }
}
