using System.Globalization;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

public enum TipoDeResultadoDelPaso
{
    /// <summary>La reserva está hecha (o repetida con la misma clave y todavía vigente): se sigue con la intención de pago.</summary>
    Reservada,

    /// <summary>Un carrito solo digital no reserva (regla 12): se sigue sin expiración.</summary>
    SinReserva,

    /// <summary>Toda falla del paso 2 salvo las dos siguientes: se aborta de inmediato.</summary>
    Abortar,

    /// <summary><c>INV-IDEMPOTENCIA-EN-CURSO</c>: la reserva de la primera solicitud sigue; no se aborta.</summary>
    NoAbortar,

    /// <summary>Un 429 (DD-04): el Inventario conserva la reserva vigente; no se aborta ni se libera, el checkout vuelve a la revisión con su pedido.</summary>
    VolverALaRevisionPorLimite,
}

/// <summary>Lo que el Inventario informó de una línea al reservar, para guardarlo como lo nuevo informado cuando cambió el plazo o la fuente (CAR-27).</summary>
public sealed record InformadoPorLaReserva(Guid EdicionId, string? CodigoDeFuente, bool RequiereTraslado, int? PlazoDias);

public sealed record ResultadoDelPasoDeReserva(
    TipoDeResultadoDelPaso Tipo,
    DateTimeOffset? ExpiraEn = null,
    ErrorDeAplicacion? Error = null,
    string? MotivoDelAborto = null,
    IReadOnlyList<InformadoPorLaReserva>? NuevoInformado = null);

/// <summary>
/// El paso 2 de pasar al pago (D18; T18; AC-CAR-14, AC-CAR-24; CAR-17, CAR-27): <c>reservas.crear</c> en el Inventario <b>fuera de toda transacción</b>, solo para los impresos, con la clave <c>res-&lt;pedidoId&gt;-&lt;generación&gt;</c> y
/// sus traducciones: sin stock y plazo cambiado respecto de lo informado → vuelve a la revisión (aborto) con las líneas; <c>INV-IDEMPOTENCIA-EN-CURSO</c> → «en curso» sin abortar; <c>INV-IDEMPOTENCIA-EN-CONFLICTO</c> → clave mal derivada;
/// una reserva ya expirada (repetir la clave devuelve la original aunque haya vencido, DD-03) o <c>INV-RESERVA-ESTADO-INVALIDO</c> → aborto con <c>pedidoId</c> nuevo; un 429 → <b>no aborta</b> (DD-04); sin respuesta u otro rechazo → aborto.
/// </summary>
internal sealed class ReservarImpresos(ServiciosDelCheckout servicios)
{
    public async Task<ResultadoDelPasoDeReserva> Ejecutar(PasoAlPagoPreparado preparado, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(preparado);
        if (preparado.LineasImpresas.Count == 0 || preparado.Entrega is null)
        {
            return new ResultadoDelPasoDeReserva(TipoDeResultadoDelPaso.SinReserva);
        }

        var respuesta = await servicios.Inventario.Reservar(preparado.PedidoId!.Value, preparado.Generacion, preparado.Entrega, preparado.LineasImpresas, cancelacion).ConfigureAwait(false);
        var ahora = servicios.Carrito.Reloj.Ahora;
        switch (respuesta.Tipo)
        {
            case TipoDeResultadoDeReservar.Reservada:
                return Revisar(respuesta.Reserva!, preparado, ahora);
            case TipoDeResultadoDeReservar.StockInsuficiente:
                return Abortar("falta-de-stock", Rechazo([.. respuesta.SinStock!.SelectMany(l => new[] { new ErrorDeCampo("edicionId", l.EdicionId.ToString("D")), new ErrorDeCampo("maximo", l.DisponibleEnLaRed.ToString(CultureInfo.InvariantCulture)) })]));
            case TipoDeResultadoDeReservar.EnCurso:
                return new ResultadoDelPasoDeReserva(TipoDeResultadoDelPaso.NoAbortar, Error: new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso));
            case TipoDeResultadoDeReservar.ConflictoDeIdempotencia:
                // La clave está mal derivada: es un defecto, no un estado de la compra (alerta técnica en T26b); se aborta y se informa como servicio no disponible.
                return Abortar("clave-de-reserva-en-conflicto", new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible));
            case TipoDeResultadoDeReservar.EstadoInvalido:
                return Abortar("reserva-expirada", Rechazo([new ErrorDeCampo("motivo", "reserva-expirada")]));
            case TipoDeResultadoDeReservar.LimiteExcedido:
                return new ResultadoDelPasoDeReserva(
                    TipoDeResultadoDelPaso.VolverALaRevisionPorLimite,
                    Error: new ErrorDeAplicacion(CategoriaDeError.Limite, ClavesDelCarrito.LimiteExcedido, respuesta.DisponibleDesde is { } desde ? [new ErrorDeCampo("disponibleDesde", desde.ToString("O", CultureInfo.InvariantCulture))] : null));
            case TipoDeResultadoDeReservar.Rechazada:
                return Abortar("entrega-invalida", Rechazo([new ErrorDeCampo("motivo", "entrega-invalida")]));
            default:
                return Abortar("sin-respuesta", ServiciosDelCheckout.StockNoValidable());
        }
    }

    /// <summary>
    /// Lo que devolvió <c>Reservar</c>: si ya expiró (repetir la clave la devuelve aunque haya vencido, DD-03) se aborta con un pedido nuevo; si la fuente, el traslado o el plazo difieren de lo que se le informó a la persona (CAR-27), se aborta
    /// con el plazo nuevo para que lo acepte viéndolo, y lo devuelto pasa a ser lo informado.
    /// </summary>
    private static ResultadoDelPasoDeReserva Revisar(ReservaObtenida reserva, PasoAlPagoPreparado preparado, DateTimeOffset ahora)
    {
        if (reserva.ExpiraEn <= ahora)
        {
            return Abortar("reserva-expirada", Rechazo([new ErrorDeCampo("motivo", "reserva-expirada")]));
        }

        var informadas = preparado.Vista.Lineas.Where(l => l.Formato == FormatoDeEdicion.Impresa).ToDictionary(l => l.LineaId);
        var cambios = new List<InformadoPorLaReserva>();
        var errores = new List<ErrorDeCampo>();
        foreach (var seleccion in reserva.Lineas)
        {
            if (!informadas.TryGetValue(seleccion.LineaId, out var informada) || informada.Disponibilidad is DisponibilidadDeLineaDeCheckout.Disponible)
            {
                continue;
            }

            var informadoTraslado = informada.Disponibilidad == DisponibilidadDeLineaDeCheckout.PorTraslado;
            if (informadoTraslado != seleccion.RequiereTraslado || informada.PlazoInformadoDias != seleccion.PlazoEstimadoDias)
            {
                cambios.Add(new InformadoPorLaReserva(seleccion.EdicionId, seleccion.CodigoDeFuente, seleccion.RequiereTraslado, seleccion.PlazoEstimadoDias));
                errores.Add(new ErrorDeCampo("edicionId", seleccion.EdicionId.ToString("D")));
                errores.Add(new ErrorDeCampo("plazoDias", seleccion.PlazoEstimadoDias?.ToString(CultureInfo.InvariantCulture) ?? "a-confirmar"));
            }
        }

        return cambios.Count > 0
            ? Abortar("plazo-cambiado", Rechazo(errores), cambios)
            : new ResultadoDelPasoDeReserva(TipoDeResultadoDelPaso.Reservada, reserva.ExpiraEn);
    }

    private static ErrorDeAplicacion Rechazo(IReadOnlyList<ErrorDeCampo> errores) => new(CategoriaDeError.Conflicto, ClavesDelCarrito.ReservaRechazada, errores);

    private static ResultadoDelPasoDeReserva Abortar(string motivo, ErrorDeAplicacion error, IReadOnlyList<InformadoPorLaReserva>? nuevo = null) =>
        new(TipoDeResultadoDelPaso.Abortar, Error: error, MotivoDelAborto: motivo, NuevoInformado: nuevo);
}

// ---------- El aborto ----------

public enum DesenlaceDelAborto
{
    /// <summary>El cambio local se hizo y la liberación y la cancelación se intentaron después de confirmar.</summary>
    Abortado,

    /// <summary>El checkout ya cambió (otra solicitud lo llevó a otro estado, pedido o generación): no se hizo nada y no se liberó nada.</summary>
    NoAplica,

    /// <summary>Desde <c>en_pago</c>: hay un cobro vivo; no se aborta.</summary>
    NoCancelable,

    /// <summary>Desde <c>en_pago</c>: Pagos no respondió a <c>Cancelar</c>; no hay cambio local y el checkout sigue <c>en_pago</c>.</summary>
    PagosNoRespondio,
}

/// <summary>Lo que se aborta: el estado de origen, el <c>pedidoId</c> y la generación que la solicitud tenía al empezar (el aborto es siempre condicional a ellos) y si la intención llegó a crearse.</summary>
public sealed record SolicitudDeAborto(
    Guid CuentaId,
    EstadoDelCheckout Desde,
    Guid PedidoId,
    int Generacion,
    bool IntencionCreada,
    string Motivo,
    IReadOnlyList<InformadoPorLaReserva>? NuevoInformado);

/// <summary>
/// <c>AbortarPasoAlPago</c> (D21; T18; reutilizado por T19, T23 y el aborto desde <c>en_pago</c>): <b>siempre condicional al <c>pedidoId</c> y la generación</b> de la solicitud. Desde <c>reservando</c> hace primero el cambio local
/// (<c>en_edicion</c>, el <c>pedidoId</c> pasa a <c>pedido_descartado</c>, generación nueva y la liberación anotada) y después, <b>fuera de la transacción</b>, <c>Cancelar</c> la intención si existe y <c>liberar</c> la reserva (también una que
/// pudo crearse antes del corte); desde <c>en_pago</c> llama primero a <c>Cancelar</c> y solo con <c>Cancelada</c>, <c>Cerrada</c> o <c>NoExiste</c> hace el cambio local (con <c>NoCancelable</c> hay un cobro vivo y no aborta; si Pagos no
/// responde, no hay cambio local). La liberación confirmada (o un <c>INV-NO-ENCONTRADO</c>, que es lo mismo) limpia la anotación; un pedido ya confirmado la limpia sin reintento; una falla la deja para <c>LiberarReservasPendientes</c>.
/// </summary>
internal sealed class AbortarElPasoAlPago(ServiciosDelCheckout servicios)
{
    public async Task<DesenlaceDelAborto> Ejecutar(SolicitudDeAborto solicitud, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(solicitud);
        var carrito = servicios.Carrito;

        // Desde en_pago: primero Cancelar; el cambio local solo con una respuesta final (D21). Sin respuesta no hay cambio local.
        if (solicitud.Desde == EstadoDelCheckout.EnPago)
        {
            ResultadoDeCancelarIntencion cancelada;
            try
            {
                cancelada = await carrito.Edicion.Pagos.Cancelar(solicitud.CuentaId, solicitud.PedidoId, cancelacion).ConfigureAwait(false);
            }
            catch (Exception excepcion) when (excepcion is not OperationCanceledException)
            {
                return DesenlaceDelAborto.PagosNoRespondio;
            }

            if (cancelada == ResultadoDeCancelarIntencion.NoCancelable)
            {
                return DesenlaceDelAborto.NoCancelable;
            }
        }

        // El cambio local, condicional al pedido y la generación.
        var aplicado = await carrito.Unidad.Ejecutar(token => CambioLocal(solicitud, token), cancelacion).ConfigureAwait(false);
        if (!aplicado)
        {
            return DesenlaceDelAborto.NoAplica;
        }

        // Después de confirmar: cancelar la intención (desde reservando, si la hay) y liberar la reserva. Nada de esto interrumpe el aborto.
        if (solicitud.Desde == EstadoDelCheckout.Reservando && solicitud.IntencionCreada)
        {
            try
            {
                await carrito.Edicion.Pagos.Cancelar(solicitud.CuentaId, solicitud.PedidoId, cancelacion).ConfigureAwait(false);
            }
            catch (Exception excepcion) when (excepcion is not OperationCanceledException)
            {
                // La intención expira sola por vence_en y el trabajo de reintento la retoma.
            }
        }

        await LiberarLaReserva(solicitud, cancelacion).ConfigureAwait(false);
        return DesenlaceDelAborto.Abortado;
    }

    private async Task<bool> CambioLocal(SolicitudDeAborto solicitud, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var ahora = carrito.Reloj.Ahora;
        var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(solicitud.CuentaId, conBloqueo: true, cancelacion).ConfigureAwait(false);
        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(solicitud.CuentaId, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (checkout is null || delCarrito is null)
        {
            return false;
        }

        var efecto = checkout.AbortarPasoAlPago(solicitud.Desde, solicitud.PedidoId, solicitud.Generacion, delCarrito, ahora);
        if (efecto.Efecto != EfectoDeTransicion.Aplicada)
        {
            return false;
        }

        // Un cambio de plazo o de fuente: lo devuelto por Reservar es lo nuevo informado, y un segundo intento avanza (CAR-27).
        foreach (var nuevo in solicitud.NuevoInformado ?? [])
        {
            checkout.RegistrarLoInformado(nuevo.EdicionId, nuevo.CodigoDeFuente, nuevo.RequiereTraslado, nuevo.PlazoDias, ahora);
        }

        await carrito.Checkouts.Guardar(checkout, cancelacion).ConfigureAwait(false);
        if (efecto.Retiradas.Count > 0)
        {
            await carrito.Carritos.Guardar(delCarrito, cancelacion).ConfigureAwait(false);
            await carrito.Carritos.AgregarAvisos(solicitud.CuentaId, carrito.AvisosDeRetiro(efecto.Retiradas, new Dictionary<Guid, EdicionConsultada>(), ahora), cancelacion).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>
    /// <c>liberar</c> direccionando por el <c>pedidoId</c> descartado, después de confirmar: liberada, ya liberada o inexistente limpia la anotación; un pedido ya confirmado la limpia sin reintento (alerta técnica en T26b); sin respuesta
    /// la deja anotada y la reserva expira sola a los 15 minutos (<c>LiberarReservasPendientes</c>, T20, la reintenta).
    /// </summary>
    private async Task LiberarLaReserva(SolicitudDeAborto solicitud, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        ResultadoDeLiberar liberada;
        try
        {
            liberada = await servicios.Inventario.Liberar(solicitud.PedidoId, "checkout-cancelado", cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return;
        }

        if (liberada == ResultadoDeLiberar.NoSePudo)
        {
            return;
        }

        await carrito.Unidad.Ejecutar(
            async token =>
            {
                var checkout = await carrito.Checkouts.ObtenerPorPedido(solicitud.PedidoId, conBloqueo: true, token).ConfigureAwait(false);
                if (checkout is { } c && c.PedidoDescartado == solicitud.PedidoId && c.ReservaALiberarDesde is not null)
                {
                    c.LiberacionConfirmada();
                    await carrito.Checkouts.Guardar(c, token).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion).ConfigureAwait(false);
    }
}
