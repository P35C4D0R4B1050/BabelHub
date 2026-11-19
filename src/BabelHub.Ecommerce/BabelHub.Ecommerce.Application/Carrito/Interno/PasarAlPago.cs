using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.Application.Carrito.Interno;

/// <summary>
/// <c>PasarAlPago</c>, el orquestador <b>sin transacción</b> (D18, D19; T19b; AC-CAR-09, 14, 21, 24, 27; CAR-10, CAR-23): encadena <c>PrepararPasoAlPago</c> (1) → <c>Reservar</c> en el Inventario (2, fuera de toda transacción) → <c>SolicitarIntencionDePago</c>
/// (3a) → <c>ConfirmarEnPago</c> (3b), cada uno en su propia transacción corta. <b>Ninguna llamada de red va dentro de una transacción y ninguna retiene el bloqueo del checkout mientras llama a Pagos.</b> Toda falla del paso 2 salvo «en curso» y el 429
/// (DD-04), y toda del paso 3 que no sea un duplicado, reutiliza el aborto condicional de T18 (<see cref="AbortarElPasoAlPago"/>): <c>Cancelar</c> la intención, descartar el <c>pedidoId</c>, subir la generación y liberar la reserva después de confirmar.
/// Con el checkout ya <c>en_pago</c> y la misma huella llama primero a <c>Crear</c> (idempotente) y solo si devolvió <c>Creada</c> repite <c>Reservar</c>; con un retiro pendiente en 3b reabre fuera de la transacción y hace el cambio local condicional.
/// </summary>
internal sealed class PasarAlPago(ServiciosDelCheckout servicios) : ICasoDeUso<PasarAlPagoEntrada, ResultadoDePasarAlPago>
{
    public async Task<Resultado<ResultadoDePasarAlPago>> Ejecutar(PasarAlPagoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        // 1. Preparar: comprobaciones en orden, congelar y reservando. Sin crear reserva ni intención en ninguna falla.
        var preparacion = await new PrepararPasoAlPago(servicios).Ejecutar(entrada, cancelacion).ConfigureAwait(false);
        if (!preparacion.EsExito)
        {
            return Resultado<ResultadoDePasarAlPago>.Fallo(preparacion.Error!);
        }

        var preparado = preparacion.Valor;
        if (preparado.Desenlace == DesenlaceDePrepararPaso.RevisionPorRetiro)
        {
            return Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(null, ServiciosDelCheckout.DeEstado(preparado.EstadoDelCheckout), preparado.Vista, false));
        }

        return preparado.EstadoDelCheckout == EstadoDelCheckout.EnPago
            ? await RepetirDesdeEnPago(preparado, cancelacion).ConfigureAwait(false)
            : await DesdeReservando(preparado, cancelacion).ConfigureAwait(false);
    }

    // ---------- Desde reservando: los pasos 2, 3a y 3b ----------

    private async Task<Resultado<ResultadoDePasarAlPago>> DesdeReservando(PasoAlPagoPreparado preparado, CancellationToken cancelacion)
    {
        // 2. Reservar (solo los impresos).
        var paso = await new ReservarImpresos(servicios).Ejecutar(preparado, cancelacion).ConfigureAwait(false);
        switch (paso.Tipo)
        {
            case TipoDeResultadoDelPaso.Abortar:
                await Abortar(preparado, EstadoDelCheckout.Reservando, false, paso.MotivoDelAborto!, paso.NuevoInformado, cancelacion).ConfigureAwait(false);
                return Resultado<ResultadoDePasarAlPago>.Fallo(paso.Error!);
            case TipoDeResultadoDelPaso.NoAbortar:
                return Resultado<ResultadoDePasarAlPago>.Fallo(paso.Error!);
            case TipoDeResultadoDelPaso.VolverALaRevisionPorLimite:
                await VolverALaRevisionPorLimite(preparado, cancelacion).ConfigureAwait(false);
                return Resultado<ResultadoDePasarAlPago>.Fallo(paso.Error!);
        }

        // 3a. La intención de pago.
        var intencion = await new SolicitarIntencionDePago(servicios).Ejecutar(preparado, paso.ExpiraEn, cancelacion).ConfigureAwait(false);
        switch (intencion.Desenlace)
        {
            case DesenlaceDeLaIntencion.Abortar:
                await Abortar(preparado, EstadoDelCheckout.Reservando, intencion.IntencionCreada, intencion.MotivoDelAborto!, null, cancelacion).ConfigureAwait(false);
                return Resultado<ResultadoDePasarAlPago>.Fallo(intencion.Error!);
            case DesenlaceDeLaIntencion.Cerrada:
                await Abortar(preparado, EstadoDelCheckout.Reservando, false, "intencion-cerrada", null, cancelacion).ConfigureAwait(false);
                return Resultado<ResultadoDePasarAlPago>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.ReservaRechazada, [new ErrorDeCampo("motivo", "intencion-cerrada")]));
            case DesenlaceDeLaIntencion.YaEnCurso:
                return Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(preparado.PedidoId, EstadoVisibleDelCheckout.EnPago, null, PagoEnCurso: true));
            case DesenlaceDeLaIntencion.Conflicto:
                return await EstadoActual(preparado, cancelacion).ConfigureAwait(false);
        }

        // 3b. Confirmar en pago.
        var confirmacion = await new ConfirmarEnPago(servicios).Ejecutar(preparado, paso.ExpiraEn, cancelacion).ConfigureAwait(false);
        switch (confirmacion.Desenlace)
        {
            case DesenlaceDeLaConfirmacion.EnPago:
                return Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(preparado.PedidoId, EstadoVisibleDelCheckout.EnPago));
            case DesenlaceDeLaConfirmacion.ConRetiroPendiente:
                return await ReabrirPorRetiro(preparado, cancelacion).ConfigureAwait(false);
            default:
                // La intención ya se creó en 3a: el aborto la cancela y libera la reserva.
                await Abortar(preparado, EstadoDelCheckout.Reservando, true, confirmacion.MotivoDelAborto!, null, cancelacion).ConfigureAwait(false);
                return Resultado<ResultadoDePasarAlPago>.Fallo(confirmacion.Error!);
        }
    }

    // ---------- Con el checkout ya en pago: Crear primero, Reservar solo si Creada ----------

    private async Task<Resultado<ResultadoDePasarAlPago>> RepetirDesdeEnPago(PasoAlPagoPreparado preparado, CancellationToken cancelacion)
    {
        var intencion = await new SolicitarIntencionDePago(servicios).Ejecutar(preparado, preparado.ReservaExpiraEn, cancelacion).ConfigureAwait(false);
        switch (intencion.Desenlace)
        {
            case DesenlaceDeLaIntencion.YaEnCurso:
                return Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(preparado.PedidoId, EstadoVisibleDelCheckout.EnPago, null, PagoEnCurso: true));
            case DesenlaceDeLaIntencion.Conflicto:
                return await EstadoActual(preparado, cancelacion).ConfigureAwait(false);
            case DesenlaceDeLaIntencion.Cerrada:
                return await AbortarDesdeEnPago(preparado, "intencion-cerrada", new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.ReservaRechazada, [new ErrorDeCampo("motivo", "intencion-cerrada")]), cancelacion).ConfigureAwait(false);
            case DesenlaceDeLaIntencion.Abortar:
                return await AbortarDesdeEnPago(preparado, intencion.MotivoDelAborto!, intencion.Error!, cancelacion).ConfigureAwait(false);
        }

        // Creada: repite Reservar (idempotente con la misma clave: devuelve la original o detecta que expiró).
        var paso = await new ReservarImpresos(servicios).Ejecutar(preparado, cancelacion).ConfigureAwait(false);
        return paso.Tipo switch
        {
            TipoDeResultadoDelPaso.Reservada or TipoDeResultadoDelPaso.SinReserva => Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(preparado.PedidoId, EstadoVisibleDelCheckout.EnPago)),
            TipoDeResultadoDelPaso.Abortar => await AbortarDesdeEnPago(preparado, paso.MotivoDelAborto!, paso.Error!, cancelacion).ConfigureAwait(false),
            _ => Resultado<ResultadoDePasarAlPago>.Fallo(paso.Error!),
        };
    }

    /// <summary>
    /// El aborto desde <c>en_pago</c>: llama primero a <c>Cancelar</c> y solo con una respuesta final hace el cambio local; con <c>NoCancelable</c> hay un cobro vivo y <b>no aborta</b> (la reserva sigue y se informa el pago pendiente); si Pagos
    /// no responde no hay cambio local.
    /// </summary>
    private async Task<Resultado<ResultadoDePasarAlPago>> AbortarDesdeEnPago(PasoAlPagoPreparado preparado, string motivo, ErrorDeAplicacion error, CancellationToken cancelacion)
    {
        var desenlace = await Abortar(preparado, EstadoDelCheckout.EnPago, true, motivo, null, cancelacion).ConfigureAwait(false);
        return desenlace switch
        {
            DesenlaceDelAborto.NoCancelable => Resultado<ResultadoDePasarAlPago>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.PagoPendiente)),
            DesenlaceDelAborto.PagosNoRespondio => Resultado<ResultadoDePasarAlPago>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDelCarrito.ServicioNoDisponible)),
            _ => Resultado<ResultadoDePasarAlPago>.Fallo(error),
        };
    }

    // ---------- Piezas ----------

    private Task<DesenlaceDelAborto> Abortar(PasoAlPagoPreparado preparado, EstadoDelCheckout desde, bool intencionCreada, string motivo, IReadOnlyList<InformadoPorLaReserva>? nuevoInformado, CancellationToken cancelacion) =>
        new AbortarElPasoAlPago(servicios).Ejecutar(new SolicitudDeAborto(preparado.CuentaId, desde, preparado.PedidoId!.Value, preparado.Generacion, intencionCreada, motivo, nuevoInformado), cancelacion);

    /// <summary>El 429 (DD-04): vuelve a la revisión conservando el pedido, en una transacción corta y condicional al pedido y la generación.</summary>
    private async Task VolverALaRevisionPorLimite(PasoAlPagoPreparado preparado, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        await carrito.Unidad.Ejecutar(
            async token =>
            {
                var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(preparado.CuentaId, conBloqueo: true, token).ConfigureAwait(false);
                if (checkout is not null && checkout.VolverALaRevisionPorLimite(preparado.PedidoId!.Value, preparado.Generacion, carrito.Reloj.Ahora).Efecto == EfectoDeTransicion.Aplicada)
                {
                    await carrito.Checkouts.Guardar(checkout, token).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion).ConfigureAwait(false);
    }

    /// <summary>
    /// Con un retiro pendiente en 3b: <c>Reabrir</c> <b>fuera de la transacción</b> y, solo con <c>Reabierta</c>, el cambio local condicional <c>reservando → en_edicion</c> conservando el pedido, subiendo la generación y aplicando el retiro. Si <c>Reabrir</c>
    /// responde otra cosa o falla, el checkout se queda <c>reservando</c> y lo recoge <c>RecuperarCheckoutsReservando</c>.
    /// </summary>
    private async Task<Resultado<ResultadoDePasarAlPago>> ReabrirPorRetiro(PasoAlPagoPreparado preparado, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        ResultadoDeReabrirIntencion reabierta;
        try
        {
            reabierta = await carrito.Edicion.Pagos.Reabrir(preparado.CuentaId, preparado.PedidoId!.Value, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return Resultado<ResultadoDePasarAlPago>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso));
        }

        if (reabierta != ResultadoDeReabrirIntencion.Reabierta)
        {
            return Resultado<ResultadoDePasarAlPago>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDelCarrito.EnCurso));
        }

        var vista = await carrito.Unidad.Ejecutar(
            async token =>
            {
                var ahora = carrito.Reloj.Ahora;
                var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(preparado.CuentaId, conBloqueo: true, token).ConfigureAwait(false);
                var delCarrito = await carrito.Carritos.ObtenerDeCuenta(preparado.CuentaId, conBloqueo: true, token).ConfigureAwait(false);
                if (checkout is null || delCarrito is null)
                {
                    return (VistaDelCheckout?)null;
                }

                var efecto = checkout.VolverALaEdicionPorRetiroPendiente(preparado.PedidoId!.Value, preparado.Generacion, delCarrito, ahora);
                if (efecto.Efecto == EfectoDeTransicion.Aplicada)
                {
                    await carrito.Checkouts.Guardar(checkout, token).ConfigureAwait(false);
                    await carrito.Carritos.Guardar(delCarrito, token).ConfigureAwait(false);
                    if (efecto.Retiradas.Count > 0)
                    {
                        await carrito.Carritos.AgregarAvisos(preparado.CuentaId, carrito.AvisosDeRetiro(efecto.Retiradas, new Dictionary<Guid, EdicionConsultada>(), ahora), token).ConfigureAwait(false);
                    }
                }

                return servicios.Vista(checkout, delCarrito, null) with { CarritoCambio = true };
            },
            cancelacion).ConfigureAwait(false);

        return vista is null
            ? Resultado<ResultadoDePasarAlPago>.Fallo(carrito.Autorizador.NoEncontrado())
            : Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(null, ServiciosDelCheckout.DeEstado(EstadoDelCheckout.EnEdicion), vista, false));
    }

    /// <summary>Con un intento activo (<c>Conflicto</c>) se devuelve el estado actual del checkout, sin abortar.</summary>
    private async Task<Resultado<ResultadoDePasarAlPago>> EstadoActual(PasoAlPagoPreparado preparado, CancellationToken cancelacion)
    {
        var carrito = servicios.Carrito;
        var checkout = await carrito.Checkouts.ObtenerVivoDeCuenta(preparado.CuentaId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (checkout is null)
        {
            return Resultado<ResultadoDePasarAlPago>.Fallo(carrito.Autorizador.NoEncontrado());
        }

        var delCarrito = await carrito.Carritos.ObtenerDeCuenta(preparado.CuentaId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDePasarAlPago>.Exito(new ResultadoDePasarAlPago(checkout.PedidoId, ServiciosDelCheckout.DeEstado(checkout.Estado), servicios.Vista(checkout, delCarrito, null), false));
    }
}
