namespace BabelHub.Ecommerce.Domain.Pagos;

/// <summary>
/// Los plazos del pago (PAG-08, PAG-13, PAG-15, PAG-18; D16, D23). Todos se miden contra un reloj que se pasa como parámetro: el Dominio no lee la hora.
/// <list type="bullet">
/// <item>La intención vence a los 15 minutos del inicio con impresos (la vigencia de la reserva, nunca después de ella) y a los 30 en una compra solo digital, que no reserva.</item>
/// <item>Un pago pendiente de confirmación puede aprobarse hasta los 30 minutos desde el inicio (<see cref="LimiteDePendienteEn"/>); pasado ese instante, o con la reserva ya expirada, es tardío.</item>
/// </list>
/// </summary>
public static class PlazosDelPago
{
    /// <summary>Vigencia de la intención de una compra con impresos: la de la reserva de CAR-10.</summary>
    public static readonly TimeSpan VigenciaConImpresos = TimeSpan.FromMinutes(15);

    /// <summary>Vigencia de la intención de una compra solo digital (sin reserva).</summary>
    public static readonly TimeSpan VigenciaSoloDigital = TimeSpan.FromMinutes(30);

    /// <summary>Un pendiente se consulta y puede aprobarse hasta 30 minutos desde el inicio del pago.</summary>
    public static readonly TimeSpan PlazoDePendiente = TimeSpan.FromMinutes(30);

    /// <summary>Cuánto se espera la respuesta de la pasarela antes de dejar el pago pendiente (PAG-07, confirmado por el responsable).</summary>
    public static readonly TimeSpan TiempoDeRespuesta = TimeSpan.FromSeconds(10);

    /// <summary>Un intento <c>iniciado</c> sin resultado tras este tiempo lo resuelve la reconciliación consultando la pasarela (PAG-07).</summary>
    public static readonly TimeSpan EsperaDeReconciliacion = TimeSpan.FromSeconds(15);

    /// <summary>La cadencia de consulta de un pago pendiente (PAG-18, D09).</summary>
    public static readonly TimeSpan IntervaloDeConsulta = TimeSpan.FromMinutes(1);

    /// <summary>Un token de intento (la clave de idempotencia firmada) se acepta hasta 30 minutos después de emitido (D04).</summary>
    public static readonly TimeSpan VigenciaDelTokenDeIntento = TimeSpan.FromMinutes(30);

    /// <summary>El contacto de entrega se elimina de la intención a los 30 días de creada, haya o no pedido (PAG-27).</summary>
    public static readonly TimeSpan RetencionDelContacto = TimeSpan.FromDays(30);

    /// <summary>Cuánto dura la reclamación de una compra aprobada mientras un orquestador la completa: otro ejecutor no compite con él.</summary>
    public static readonly TimeSpan DuracionDeLaReclamacion = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Cuándo vence la intención: 15 minutos con impresos pero nunca después de que expire la reserva, y 30 minutos en solo digital. Con impresos hace falta el instante de la
    /// reserva que dio el Inventario.
    /// </summary>
    public static DateTimeOffset VenceEn(DateTimeOffset iniciadaEn, bool tieneImpresos, DateTimeOffset? reservaExpiraEn)
    {
        if (!tieneImpresos)
        {
            return iniciadaEn + VigenciaSoloDigital;
        }

        if (reservaExpiraEn is null)
        {
            throw new ArgumentException("Una compra con impresos necesita el instante en que expira su reserva.", nameof(reservaExpiraEn));
        }

        var porVigencia = iniciadaEn + VigenciaConImpresos;
        return reservaExpiraEn.Value < porVigencia ? reservaExpiraEn.Value : porVigencia;
    }

    /// <summary>El instante hasta el que un pago pendiente puede aprobarse: 30 minutos desde el inicio.</summary>
    public static DateTimeOffset LimiteDePendienteEn(DateTimeOffset iniciadaEn) => iniciadaEn + PlazoDePendiente;

    /// <summary>La reserva está expirada desde el instante en que vence. Una compra solo digital no tiene reserva.</summary>
    public static bool ReservaExpirada(DateTimeOffset? reservaExpiraEn, DateTimeOffset ahora) => reservaExpiraEn is { } expira && ahora >= expira;

    /// <summary>
    /// Un pago aprobado es <b>tardío</b> (PAG-15) si se aprobó pasados los 30 minutos desde el inicio o con la reserva ya expirada: no se confirma el pedido y se reembolsa.
    /// A los 30 minutos exactos todavía no lo es (el pendiente puede aprobarse «hasta» ese instante).
    /// </summary>
    public static bool EsTardio(DateTimeOffset iniciadaEn, DateTimeOffset aprobadoEn, DateTimeOffset? reservaExpiraEn) =>
        aprobadoEn > LimiteDePendienteEn(iniciadaEn) || ReservaExpirada(reservaExpiraEn, aprobadoEn);
}
