using BabelHub.Ecommerce.Application.Pagos.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;
using BabelHub.Shared.Contratos.Inventario;

namespace BabelHub.Ecommerce.Infrastructure.Pagos;

/// <summary>
/// Las reservas del pago sobre <see cref="IClienteDelInventario"/> de <c>Plataforma</c> (T26; PAG-04, PAG-11, PAG-13): ese cliente pone el plazo de 10 segundos, la credencial de cada alcance y el token del usuario, y <b>este adaptador no toca el token ni abre un <c>HttpClient</c></b> (regla 9,
/// D01). <c>consultar</c> lleva el token de la persona (la cuenta sale de él, no del parámetro); <c>extender</c>, <c>confirmar</c> y <c>liberar</c> son de sistema. Una falla del Inventario nunca es una excepción: «sin respuesta» (SEG-13, falla cerrada) salvo dos rechazos que se distinguen:
/// <c>INV-RESERVA-ESTADO-INVALIDO</c> (la reserva ya no está en el estado que la operación necesita) e <c>INV-NO-ENCONTRADO</c>. <b>Cada respuesta se valida</b>: debe ser la reserva del pedido, en el estado que la operación deja (<c>consultar</c> trae su instante de expiración, <c>extender</c> también,
/// <c>confirmar</c> la deja <c>confirmada</c> con la fuente y el traslado de cada línea, <c>liberar</c> la deja <c>liberada</c> o <c>expirada</c>); si no, es «sin respuesta». <b>Ninguna llamada ocurre dentro de una transacción</b> (D03): la Aplicación no la abre.
/// </summary>
internal sealed class RestReservasDelPago(IClienteDelInventario cliente, IReloj reloj) : IReservasDelPago
{
    public async Task<RespuestaDeReserva<EstadoDeLaReserva>> Consultar(Guid pedidoId, Guid cuentaId, CancellationToken cancelacion = default)
    {
        ExigirPedido(pedidoId);
        var (reserva, fallo) = await Llamar(() => cliente.ConsultarReserva(pedidoId, cancelacion)).ConfigureAwait(false);
        if (reserva is null)
        {
            return Fallo<EstadoDeLaReserva>(fallo);
        }

        if (!EsDelPedido(reserva, pedidoId) || reserva.ExpiraEn is not { } expiraEn)
        {
            return Fallo<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
        }

        return new RespuestaDeReserva<EstadoDeLaReserva>(ResultadoDeLaLlamadaDeReserva.Correcto, new EstadoDeLaReserva(expiraEn, reserva.Estado == EstadosDeReserva.Pendiente && expiraEn > reloj.Ahora));
    }

    public async Task<RespuestaDeReserva<ResultadoDeExtension>> Extender(Guid pedidoId, CancellationToken cancelacion = default)
    {
        ExigirPedido(pedidoId);
        ResultadoDelInventario<ExtensionDeLaReserva> resultado;
        try
        {
            resultado = await cliente.ExtenderReserva(pedidoId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return Fallo<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
        }

        if (!resultado.Exitosa)
        {
            return Fallo<ResultadoDeExtension>(Traducir(resultado));
        }

        var extension = resultado.Valor;
        if (extension?.Reserva is not { } reserva || !EsDelPedido(reserva, pedidoId) || reserva.ExpiraEn is not { } expiraEn)
        {
            return Fallo<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
        }

        // Una reserva que dice haberse extendido debe seguir pendiente: cualquier otro estado no es lo que pedimos (falla cerrada, como confirmar y liberar).
        if (extension.Extendida && reserva.Estado != EstadosDeReserva.Pendiente)
        {
            return Fallo<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
        }

        // Sin cambios (ya extendida, vencida o confirmada) es un éxito con Extendida = false, nunca un error (contrato v1).
        return new RespuestaDeReserva<ResultadoDeExtension>(ResultadoDeLaLlamadaDeReserva.Correcto, new ResultadoDeExtension(expiraEn, extension.Extendida));
    }

    public async Task<RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>> Confirmar(Guid pedidoId, string claveDeIdempotencia, CancellationToken cancelacion = default)
    {
        ExigirPedido(pedidoId);
        ArgumentException.ThrowIfNullOrWhiteSpace(claveDeIdempotencia);
        var (reserva, fallo) = await Llamar(() => cliente.ConfirmarReserva(pedidoId, claveDeIdempotencia, cancelacion)).ConfigureAwait(false);
        if (reserva is null)
        {
            return Fallo<IReadOnlyList<LineaConfirmadaDeReserva>>(fallo);
        }

        // Una confirmación debe dejar la reserva confirmada y, por cada línea que requiere traslado, traer el traslado que la cubre (D02): lo que falte no se inventa.
        if (!EsDelPedido(reserva, pedidoId) || reserva.Estado != EstadosDeReserva.Confirmada || reserva.Lineas.Count == 0)
        {
            return Fallo<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
        }

        var disponibles = reserva.Traslados.ToList();
        var lineas = new List<LineaConfirmadaDeReserva>(reserva.Lineas.Count);
        foreach (var linea in reserva.Lineas)
        {
            TrasladoDeLaReserva? traslado = null;
            if (linea.RequiereTraslado)
            {
                traslado = disponibles.FirstOrDefault(t => t.EdicionId == linea.EdicionId && t.Cantidad == linea.Cantidad && t.CodigoDeOrigen == linea.CodigoDeFuente);
                if (traslado is null)
                {
                    return Fallo<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
                }

                disponibles.Remove(traslado);
            }

            lineas.Add(new LineaConfirmadaDeReserva(linea.LineaId, linea.CodigoDeFuente, linea.RequiereTraslado, traslado?.TrasladoId, traslado?.FechaEstimadaDeLlegada));
        }

        return new RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>>(ResultadoDeLaLlamadaDeReserva.Correcto, lineas);
    }

    public async Task<RespuestaDeReserva<bool>> Liberar(Guid pedidoId, MotivoDeLiberacion motivo, CancellationToken cancelacion = default)
    {
        ExigirPedido(pedidoId);
        var (reserva, fallo) = await Llamar(() => cliente.Liberar(pedidoId, motivo.ATexto(), lineaIds: null, cancelacion)).ConfigureAwait(false);
        if (reserva is null)
        {
            return Fallo<bool>(fallo);
        }

        // La liberación debe dejar la reserva liberada o expirada; cualquier otro estado no es lo que pedimos (SEG-13): falla cerrada.
        return EsDelPedido(reserva, pedidoId) && reserva.Estado is EstadosDeReserva.Liberada or EstadosDeReserva.Expirada
            ? new RespuestaDeReserva<bool>(ResultadoDeLaLlamadaDeReserva.Correcto, true)
            : Fallo<bool>(ResultadoDeLaLlamadaDeReserva.SinRespuesta);
    }

    // ---------- Piezas ----------

    private static void ExigirPedido(Guid pedidoId)
    {
        if (pedidoId == Guid.Empty)
        {
            throw new ArgumentException("La operación necesita el pedido.", nameof(pedidoId));
        }
    }

    private static bool EsDelPedido(ReservaDelInventario reserva, Guid pedidoId) => reserva.PedidoId == pedidoId;

    private static RespuestaDeReserva<T> Fallo<T>(ResultadoDeLaLlamadaDeReserva resultado) => new(resultado, default);

    private static ResultadoDeLaLlamadaDeReserva Traducir(ResultadoDelInventario resultado) => resultado.Estado == EstadoDeLlamadaAlInventario.Rechazada
        ? resultado.Codigo switch
        {
            CodigosDeErrorDelInventario.ReservaEstadoInvalido => ResultadoDeLaLlamadaDeReserva.EstadoInvalido,
            CodigosDeErrorDelInventario.NoEncontrado => ResultadoDeLaLlamadaDeReserva.NoExiste,
            _ => ResultadoDeLaLlamadaDeReserva.SinRespuesta,
        }
        : ResultadoDeLaLlamadaDeReserva.SinRespuesta;

    /// <summary>Llama al cliente: la reserva si salió bien, o por qué no (una falla del Inventario o una excepción nunca se propagan).</summary>
    private static async Task<(ReservaDelInventario? Reserva, ResultadoDeLaLlamadaDeReserva Fallo)> Llamar(Func<Task<ResultadoDelInventario<ReservaDelInventario>>> llamada)
    {
        ResultadoDelInventario<ReservaDelInventario> resultado;
        try
        {
            resultado = await llamada().ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return (null, ResultadoDeLaLlamadaDeReserva.SinRespuesta);
        }

        return resultado.Exitosa && resultado.Valor is { } reserva ? (reserva, ResultadoDeLaLlamadaDeReserva.SinRespuesta) : (null, Traducir(resultado));
    }
}
