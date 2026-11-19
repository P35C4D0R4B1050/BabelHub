namespace BabelHub.Ecommerce.Application.Identidad.Interno;

/// <summary>
/// <see cref="IContactoDeAvisosDelCliente"/> sobre las cuentas de Identidad (T06 de notificaciones; NOT-03, NOT-08): el consentimiento y el número vigentes <b>al momento de enviar</b>, sin caché. Solo lee: no cambia
/// la cuenta ni deja rastro del número.
/// </summary>
public sealed class ContactoDeAvisosDeIdentidad(IRepositorioDeCuentas cuentas) : IContactoDeAvisosDelCliente
{
    public async Task<ContactoDeAvisosDelCliente?> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        if (usuarioId == Guid.Empty)
        {
            return null;
        }

        var cuenta = await cuentas.PorId(usuarioId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        if (cuenta is null)
        {
            return null;
        }

        var canceladaODesactivada = cuenta.CanceladaEn is not null || !cuenta.Usuario.Activo;
        return new ContactoDeAvisosDelCliente(canceladaODesactivada, cuenta.Whatsapp.Consentido, cuenta.Whatsapp.Numero?.Valor);
    }
}
