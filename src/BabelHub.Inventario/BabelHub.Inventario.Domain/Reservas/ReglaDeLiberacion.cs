namespace BabelHub.Inventario.Domain.Reservas;

/// <summary>Por qué se libera una reserva (SEL-07, SEL-17, D15). <see cref="Reemplazo"/> es interno: lo usa un reemplazo (SEL-16) y no se puede pedir por la API.</summary>
public enum MotivoDeLiberacion
{
    CheckoutCancelado,
    PagoVencido,
    PedidoAnulado,
    CanceladoPorElCliente,
    FallaPosteriorAlPago,
    NoRecogido,
    Reemplazo,
}

/// <summary>
/// Lo que decide <see cref="ReglaDeLiberacion"/> sobre las líneas afectadas: cuáles se liberan (vuelven al vendible sin tocar el físico), cuáles se marcan anuladas (las cerradas por traslado), si el motivo anula
/// el pedido (entonces se invocan los puertos de traslados y se marca <c>anulada_en</c>), y si la operación se rechaza por estado inválido o no cambia nada (una repetición).
/// </summary>
public sealed record DecisionDeLiberacion(
    IReadOnlyList<LineaDeReserva> ALiberar,
    IReadOnlyList<LineaDeReserva> AAnular,
    bool AnulaElPedido,
    bool EstadoInvalido)
{
    /// <summary>Nada que liberar ni anular y no es un rechazo: una repetición, que no cambia el stock (AC-SEL-07).</summary>
    public bool SinCambios => !EstadoInvalido && ALiberar.Count == 0 && AAnular.Count == 0;
}

/// <summary>
/// Reglas de liberación por motivo (D15), sin token: acotan qué puede liberar una credencial comprometida y sostienen SEL-28 y SEL-29.
/// <list type="bullet">
/// <item><c>checkout-cancelado</c>, <c>pago-vencido</c> y el reemplazo liberan <b>solo líneas pendientes</b>; si el pedido ya está confirmado (alguna línea confirmada o cerrada) se rechazan sin cambios y sin cancelar traslados.</item>
/// <item><c>pedido-anulado</c>, <c>cancelado-por-el-cliente</c> y <c>falla-posterior-al-pago</c> liberan pendientes y confirmadas y <b>anulan el pedido</b>.</item>
/// <item><c>no-recogido</c> libera solo confirmadas de un pedido de <b>recojo</b> y también anula el pedido.</item>
/// <item>Una línea cerrada por <b>traslado</b> no se libera ni da error: con un motivo que anula el pedido se marca anulada. Las cerradas por entrega o reparto nunca se liberan: son estado inválido cuando no hay nada más que hacer.</item>
/// <item>Sin nada que liberar y solo líneas ya liberadas o expiradas, es un éxito sin cambios (repetir no cambia el stock).</item>
/// </list>
/// </summary>
public static class ReglaDeLiberacion
{
    /// <summary>Los motivos que anulan el pedido: invocan los <c>IAlLiberarPedido</c> y marcan la venta como anulada (TRA-15, TRA-19).</summary>
    public static bool AnulaElPedido(MotivoDeLiberacion motivo) =>
        motivo is MotivoDeLiberacion.PedidoAnulado or MotivoDeLiberacion.CanceladoPorElCliente or MotivoDeLiberacion.FallaPosteriorAlPago or MotivoDeLiberacion.NoRecogido;

    /// <summary>Decide qué se libera, qué se anula y si se rechaza, sobre las líneas afectadas por la solicitud (todas las del pedido o las que acota <c>lineaIds</c>).</summary>
    public static DecisionDeLiberacion Decidir(MotivoDeLiberacion motivo, TipoDeEntrega tipoDeEntrega, IReadOnlyCollection<LineaDeReserva> lineas)
    {
        ArgumentNullException.ThrowIfNull(lineas);
        var anula = AnulaElPedido(motivo);

        if (motivo == MotivoDeLiberacion.NoRecogido && tipoDeEntrega != TipoDeEntrega.Recojo)
        {
            return Rechazo(anula);
        }

        var cerradasPorTraslado = lineas.Where(EsCerradaPorTraslado).ToList();
        var cerradasPorOtraCausa = lineas.Where(l => l.Estado == EstadoDeLinea.Cerrada && !EsCerradaPorTraslado(l)).ToList();

        List<LineaDeReserva> liberables;
        bool hayNoAdmitidas;
        switch (motivo)
        {
            case MotivoDeLiberacion.CheckoutCancelado or MotivoDeLiberacion.PagoVencido or MotivoDeLiberacion.Reemplazo:
                liberables = [.. lineas.Where(l => l.Estado == EstadoDeLinea.Pendiente)];

                // Un pedido ya confirmado (alguna línea confirmada o cerrada) no se libera por estos motivos: se rechaza todo.
                if (lineas.Any(l => l.Estado is EstadoDeLinea.Confirmada or EstadoDeLinea.Cerrada))
                {
                    return Rechazo(anula);
                }

                hayNoAdmitidas = false;
                break;

            case MotivoDeLiberacion.NoRecogido:
                liberables = [.. lineas.Where(l => l.Estado == EstadoDeLinea.Confirmada)];
                hayNoAdmitidas = lineas.Any(l => l.Estado == EstadoDeLinea.Pendiente) || cerradasPorOtraCausa.Count > 0;
                break;

            default:
                liberables = [.. lineas.Where(l => l.Estado is EstadoDeLinea.Pendiente or EstadoDeLinea.Confirmada)];
                hayNoAdmitidas = cerradasPorOtraCausa.Count > 0;
                break;
        }

        var aAnular = anula ? cerradasPorTraslado.Where(l => l.AnuladaEn is null).ToList() : [];

        // Nada que hacer: o la repetición de algo ya liberado, o líneas que este motivo no admite (entregadas, en reparto, pendientes de un no recogido).
        return liberables.Count == 0 && aAnular.Count == 0 && hayNoAdmitidas
            ? Rechazo(anula)
            : new DecisionDeLiberacion(liberables, aAnular, anula, EstadoInvalido: false);
    }

    private static bool EsCerradaPorTraslado(LineaDeReserva l) => l.Estado == EstadoDeLinea.Cerrada && l.CerradaPor == CausaDeCierre.Traslado;

    private static DecisionDeLiberacion Rechazo(bool anula) => new([], [], anula, EstadoInvalido: true);
}
