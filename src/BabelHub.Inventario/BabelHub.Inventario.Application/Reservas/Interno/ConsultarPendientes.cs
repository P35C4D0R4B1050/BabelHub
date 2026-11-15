using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

internal sealed class ValidadorDeConsultarPendientes : IValidador<ConsultarPendientesEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarPendientesEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return entrada.TitularId == Guid.Empty
            ? ResultadoDeValidacion.Invalido([new ErrorDeCampo("titularId", ConstructorDeValidacion.Obligatorio)])
            : ResultadoDeValidacion.Valido;
    }
}

/// <summary>
/// Cuántos pedidos tiene un titular con una reserva viva (SEL-33): lo consulta el E-commerce por el puerto de <c>identidad-seguridad</c> antes de cancelar una cuenta (IDE-27), con la credencial de servicio de
/// <c>consulta-de-pendientes</c> y sin token de usuario. Cuenta pedidos con una línea confirmada, una pendiente que no venció o una cerrada por traslado que sigue en tránsito entre sucursales (una reserva confirmada cuya línea
/// viaja sigue siendo una reserva confirmada); no cuenta las líneas anuladas, y la respuesta no lleva datos de las ediciones. <b>El alcance se comprueba antes de leer nada</b>: un cliente, un usuario o la credencial de otro
/// alcance son «prohibido» con su evento (AC-SEL-35). El identificador del titular viaja en el cuerpo, nunca en la ruta.
/// </summary>
internal sealed class ConsultarPendientes(
    AutorizadorDeReservas autorizador,
    IValidador<ConsultarPendientesEntrada> validador,
    IRepositorioDeReservas reservas,
    IReloj reloj) : ICasoDeUso<ConsultarPendientesEntrada, PendientesDeTitular>
{
    public async Task<Resultado<PendientesDeTitular>> Ejecutar(ConsultarPendientesEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirIntegracion(AlcanceDeServicio.ConsultaDePendientes, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<PendientesDeTitular>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<PendientesDeTitular>(validacion);
        }

        var activas = await reservas.PedidosActivosDelCliente(entrada.TitularId, reloj.Ahora, cancelacion).ConfigureAwait(false);
        return Resultado<PendientesDeTitular>.Exito(new PendientesDeTitular(activas));
    }
}
