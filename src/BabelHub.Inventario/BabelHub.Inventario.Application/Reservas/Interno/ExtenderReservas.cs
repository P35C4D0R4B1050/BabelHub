using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

internal sealed class ValidadorDeExtender : IValidador<ExtenderReservasEntrada>
{
    public ResultadoDeValidacion Validar(ExtenderReservasEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return entrada.PedidoId == Guid.Empty
            ? ResultadoDeValidacion.Invalido([new ErrorDeCampo("pedidoId", ConstructorDeValidacion.Obligatorio)])
            : ResultadoDeValidacion.Valido;
    }
}

/// <summary>
/// Informa que el pago quedó pendiente de confirmación y extiende la expiración a 30 minutos desde el inicio del pago, <b>una sola vez por pedido</b> (SEL-22; la marca la heredan las reservas que lo reemplazan).
/// Solo la integración <c>expiracion-de-reservas</c>, bajo el bloqueo del pedido. El aviso es un <b>éxito sin cambios</b> (<c>Extendida = false</c>) si la reserva ya venció, se confirmó, se liberó o ya se extendió
/// (SEL-30): el E-commerce lo manda también cuando ya no sirve y no debe fallar. La sentencia es condicional: dos avisos simultáneos extienden una sola vez.
/// </summary>
internal sealed class ExtenderReservas(
    AutorizadorDeReservas autorizador,
    IValidador<ExtenderReservasEntrada> validador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeReservas reservas,
    ConstructorDeLaReserva constructor,
    IReloj reloj) : ICasoDeUso<ExtenderReservasEntrada, ExtensionDeReserva>
{
    public async Task<Resultado<ExtensionDeReserva>> Ejecutar(ExtenderReservasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirIntegracionDeReservas(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ExtensionDeReserva>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<ExtensionDeReserva>(validacion);
        }

        try
        {
            return await Extender(entrada, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeReservas.BloqueoOcupado<ExtensionDeReserva>();
        }
    }

    private async Task<Resultado<ExtensionDeReserva>> Extender(ExtenderReservasEntrada entrada, CancellationToken cancelacion)
    {
        await bloqueos.Pedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);

        var ahora = reloj.Ahora;
        var actual = await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (actual is null)
        {
            return FallosDeReservas.De<ExtensionDeReserva>(CategoriaDeError.NoEncontrado, ClavesDeReservas.ReservaNoEncontrada);
        }

        // El Dominio decide si procede; la sentencia lo repite como condición (una sola extensión aunque lleguen dos avisos a la vez).
        var extendida = actual.Extender(ahora)
            && await reservas.ExtenderExpiracion(actual.Id, actual.FechaDeExpiracion!.Value, ahora, cancelacion).ConfigureAwait(false);

        var vigente = extendida ? actual : await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false) ?? actual;
        return Resultado<ExtensionDeReserva>.Exito(new ExtensionDeReserva(extendida, await constructor.Construir(vigente, esRepeticion: false, cancelacion).ConfigureAwait(false)));
    }
}
