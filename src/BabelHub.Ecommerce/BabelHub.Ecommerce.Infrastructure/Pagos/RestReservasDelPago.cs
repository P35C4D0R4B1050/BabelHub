using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// Las reservas del pago sobre <see cref="IClienteDelInventario"/> de <c>Plataforma</c> (T26; PAG-04, PAG-11, PAG-13): ese cliente pone el plazo de 10 segundos, la credencial de cada alcance y el token del usuario, y <b>este adaptador no toca el token ni
/// abre un <c>HttpClient</c></b> (regla 9, D01). <b>Hoy implementa de verdad solo <c>liberar</c></b> (alcance de sistema; el motivo viaja como su valor estable del contrato); <b><c>consultar</c>, <c>extender</c> y <c>confirmar</c> aún no existen en
/// <see cref="IClienteDelInventario"/></b> (los agrega el carril de plataforma): hasta entonces fallan cerrado —«sin respuesta»— y una compra con impresos no se confirma ni se extiende su reserva, nunca se simula stock; el hallazgo está en <c>progress/hallazgos.md</c>.
/// Una falla del Inventario nunca es una excepción. <b>Ninguna llamada ocurre dentro de una transacción</b> (D03): la Aplicación no la abre. Se valida la respuesta (SEG-13): una liberación solo es correcta si la reserva quedó <c>liberada</c> o <c>expirada</c>.
/// </summary>
internal sealed class RestReservasDelPago(IClienteDelInventario cliente) : IReservasDelPago
{
    // consultar, extender y confirmar faltan en IClienteDelInventario (carril de plataforma): fallan cerrado.

    public Task<RespuestaDeReserva<EstadoDeLaReserva>> Consultar(Guid pedidoId, Guid cuentaId, CancellationToken cancelacion = default) =>
        Task.FromResult(new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null));

    public Task<RespuestaDeReserva<ResultadoDeExtension>> Extender(Guid pedidoId, CancellationToken cancelacion = default) =>
        Task.FromResult(new RespuestaDeReserva<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null));

    public Task<RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>> Confirmar(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default) =>
        Task.FromResult(new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.SinRespuesta, null));

    public async Task<RespuestaDeReserva<bool>> Liberar(Guid pedidoId, MotivoDeLiberacion motivo, CancellationToken cancelacion = default)
    {
        if (pedidoId == Guid.Empty)
        {
            throw new ArgumentException("La liberación necesita el pedido.", nameof(pedidoId));
        }

        ResultadoDelInventario<ReservaDelInventario> resultado;
        try
        {
            resultado = await cliente.Liberar(pedidoId, motivo.ATexto(), lineaIds: null, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return Fallo(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
        }

        switch (resultado.Estado)
        {
            case EstadoDeLlamadaAlInventario.Exitosa:
                var reserva = resultado.Valor;
                // La liberación debe dejar la reserva liberada o expirada; cualquier otro estado no es lo que pedimos (SEG-13): falla cerrada.
                return reserva is not null && reserva.PedidoId == pedidoId && reserva.Estado is EstadosDeReserva.Liberada or EstadosDeReserva.Expirada
                    ? new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.Correcto, true)
                    : Fallo(ResultadoDeLaLlamadaDeReserva.SinRespuesta);

            case EstadoDeLlamadaAlInventario.Rechazada when resultado.Codigo == CodigosDeErrorDelInventario.ReservaEstadoInvalido:
                return Fallo(ResultadoDeLaLlamadaDeReserva.EstadoInvalido);

            case EstadoDeLlamadaAlInventario.Rechazada when resultado.Codigo == CodigosDeErrorDelInventario.NoEncontrado:
                return Fallo(ResultadoDeLaLlamadaDeReserva.NoExiste);

            default:
                // Otro rechazo, el límite o la falta de respuesta: no se sabe si se liberó.
                return Fallo(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
        }
    }

    private static RespuestaDeReserva<bool> Fallo(ResultadoDeLaLlamadaDeReserva resultado) => new(resultado, false);
}
