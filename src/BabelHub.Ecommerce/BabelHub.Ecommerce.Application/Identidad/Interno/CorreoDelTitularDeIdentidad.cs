namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// <see cref="ICorreoDelTitular"/> sobre las cuentas de Identidad: solo lee el correo (nunca otros datos de la cuenta) y solo de una cuenta vigente con el correo verificado; no comprueba al actor porque no
/// entrega nada a quien pide más que el correo al que Pagos avisa del propio pago de esa cuenta, y no registra denegaciones.
/// </summary>
public sealed class CorreoDelTitularDeIdentidad(IRepositorioDeCuentas cuentas) : ICorreoDelTitular
{
    public async Task<string?> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            return null;
        }

        var cuenta = await cuentas.PorId(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        if (cuenta is null || cuenta.CanceladaEn is not null || !cuenta.Usuario.Activo || !cuenta.Usuario.CorreoVerificado)
        {
            return null;
        }

        return cuenta.Usuario.Correo;
    }
}
