using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// El número de contacto de notificación de una sucursal, por su código (FUE-01): lo pide el servicio de avisos con la credencial de alcance <c>lectura-de-contacto</c> (la
/// Api establece el actor de integración) o la administración central. Es un dato personal: <b>no se registra</b> (ni en la auditoría ni en un registro de diagnóstico) y
/// el tipo que lo lleva no lo imprime. Un código inexistente o una sucursal sin contacto es «no encontrada» (quien pide no distingue un caso del otro); un código con forma inválida es validación (la forma del código es pública). El
/// alcance de la credencial lo comprueba el middleware de servicio; aquí se exige además el actor adecuado para que ningún otro rol lo lea por la Aplicación.
/// </summary>
internal sealed class ConsultarContactoDeNotificacion(
    AutorizadorDeFuentes autorizador,
    IValidador<ConsultarContactoDeNotificacionEntrada> validador,
    IRepositorioDeFuentes fuentes) : ICasoDeUso<ConsultarContactoDeNotificacionEntrada, ContactoDeNotificacion>
{
    public async Task<Resultado<ContactoDeNotificacion>> Ejecutar(ConsultarContactoDeNotificacionEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirServicioDeAvisosOAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeFuentes.Fallo<ContactoDeNotificacion>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<ContactoDeNotificacion>(validacion);
        }

        var fuente = await fuentes.PorCodigo(entrada.CodigoDeSucursal, cancelacion).ConfigureAwait(false);
        return fuente?.ContactoDeNotificacion is { } numero
            ? Resultado<ContactoDeNotificacion>.Exito(new ContactoDeNotificacion(fuente.Codigo, numero))
            : FallosDeFuentes.De<ContactoDeNotificacion>(CategoriaDeError.NoEncontrado, fuente is null ? ClavesDeFuentes.FuenteNoEncontrada : ClavesDeFuentes.ContactoNoDefinido);
    }
}
