namespace BabelHub.Inventario.Domain.Reservas;

/// <summary>
/// La expiración de una reserva pendiente (SEL-01, SEL-08, SEL-22): 15 minutos desde que se inicia el pago y, una sola vez por pedido y con el pago pendiente de confirmación, hasta 30 minutos desde
/// ese inicio. Un reemplazo (SEL-16) vuelve a 15 minutos desde su propio inicio sin conservar la extensión, pero la marca de «ya se extendió» se hereda: la extensión es una sola por pedido (SEL-37).
/// Una reserva pendiente cuya fecha ya pasó está <b>vencida</b>: no cuenta como activa aunque nadie la haya barrido todavía (SEL-27).
/// </summary>
public static class ExpiracionDeReserva
{
    public static readonly TimeSpan Inicial = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan Extendida = TimeSpan.FromMinutes(30);

    /// <summary>La fecha de expiración de una reserva que se inicia ahora.</summary>
    public static DateTimeOffset AlIniciar(DateTimeOffset iniciadaEn) => iniciadaEn + Inicial;

    /// <summary>Una reserva pendiente está vencida cuando su fecha de expiración ya pasó (en el instante exacto, también): ya no cuenta como activa.</summary>
    public static bool EstaVencida(DateTimeOffset? fechaDeExpiracion, DateTimeOffset ahora) => fechaDeExpiracion is { } fecha && fecha <= ahora;

    /// <summary>
    /// Decide la extensión: solo si la reserva sigue pendiente (tiene fecha), no venció, y no se había extendido antes en este pedido. La nueva fecha es 30 minutos desde el inicio del pago;
    /// <see langword="null"/> si no se extiende (el aviso se ignora, SEL-30).
    /// </summary>
    public static DateTimeOffset? Extender(DateTimeOffset iniciadaEn, DateTimeOffset? fechaDeExpiracion, bool yaExtendida, DateTimeOffset ahora) =>
        yaExtendida || fechaDeExpiracion is null || EstaVencida(fechaDeExpiracion, ahora) ? null : iniciadaEn + Extendida;
}
