using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

using DomMedioDePago = BabelHub.Ecommerce.Domain.Pagos.MedioDePago;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>Lo que hace falta para registrar un intento: la cuenta que paga, el pedido, lo que lleva su clave firmada y el medio elegido.</summary>
internal sealed record EntradaDeRegistroDeIntento(Guid CuentaId, Guid PedidoId, DatosDeLaClaveDeIntento Clave, DomMedioDePago Medio);

/// <summary>El intento ya registrado (una copia) y, si <see cref="EraRepetido"/>, que ya existía con esa clave.</summary>
internal sealed record IntentoRegistrado(IntentoDeCobro Intento, bool EraRepetido);

/// <summary>
/// Registrar el intento de cobro (PAG-04, PAG-08, PAG-17, PAG-21, PAG-25, PAG-31; D03, D10, D24): primero, <b>fuera de toda transacción</b>, las consultas a Identidad, la posesión de las digitales, <c>reservas.consultar</c> (solo
/// con impresos) y el límite; después una transacción corta que <b>lee la marca de cancelación de la cuenta y luego bloquea la intención</b>, revalida el token (huella y total), la vigencia y a Carrito (<c>en_pago</c> con el
/// <c>pedidoId</c> de esta intención), inserta el intento <c>iniciado</c> y audita en la misma transacción. Cualquier falla antes de insertar rechaza el cobro: <b>falla cerrada, no se cobra</b>.
/// </summary>
internal sealed class RegistrarIntento(
    AutorizadorDePagos autorizador,
    IRepositorioDePagos repositorio,
    IUnidadDeTrabajo unidad,
    ICuentaDelPagador cuentas,
    IPosesionDeEdiciones posesion,
    IReservasDelPago reservas,
    ICheckoutDelCliente checkout,
    LimitadorDeCobros limitador,
    IAuditoria auditoria,
    IReloj reloj)
{
    public async Task<Resultado<IntentoRegistrado>> Registrar(EntradaDeRegistroDeIntento entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        // La intención, solo para comprobar la propiedad y saber qué consultar (sin bloqueo).
        var intencion = await unidad.Ejecutar(token => repositorio.ObtenerIntencion(entrada.PedidoId, paraActualizar: false, token), cancelacion).ConfigureAwait(false);
        if (intencion is null || !intencion.EsDeLaCuenta(entrada.CuentaId))
        {
            return Resultado<IntentoRegistrado>.Fallo(await autorizador.RechazarAjena(cancelacion).ConfigureAwait(false));
        }

        // (1) Fuera de toda transacción: Identidad, posesión, reserva y límite.
        if (await ComprobarFueraDeLaTransaccion(entrada, intencion, cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<IntentoRegistrado>.Fallo(rechazo);
        }

        // (2) La transacción corta: marca de la cuenta, luego la intención.
        try
        {
            return await unidad.Ejecutar(token => RegistrarEnLaTransaccion(entrada, token), cancelacion).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            // Sin auditoría no se cobra (AGENTS §4): la transacción se revirtió y no hay intento.
            return FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
        }
    }

    private async Task<ErrorDeAplicacion?> ComprobarFueraDeLaTransaccion(EntradaDeRegistroDeIntento entrada, IntencionDePago intencion, CancellationToken cancelacion)
    {
        EstadoDeLaCuentaParaPagar? estado;
        try
        {
            estado = await cuentas.Consultar(entrada.CuentaId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            estado = null;
        }

        if (estado is null)
        {
            return NoDisponible();
        }

        if (!estado.PuedePagar)
        {
            return CuentaNoPuedeComprar(estado.Motivo?.ToString() ?? "desconocido");
        }

        var digitales = intencion.Lineas.Where(l => l.EsDigital && l.EdicionId is not null).Select(l => l.EdicionId!.Value).ToArray();
        if (digitales.Length > 0)
        {
            ResultadoDePosesion poseidas;
            try
            {
                poseidas = await posesion.Posee(entrada.CuentaId, digitales, cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return NoDisponible();
            }

            switch (poseidas.Estado)
            {
                case EstadoDePosesion.Posee:
                    return new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePagos.DigitalYaPoseida);
                case EstadoDePosesion.NoSePuedeConfirmar:
                    return NoDisponible();
            }
        }

        if (intencion.TieneImpresos)
        {
            RespuestaDeReserva<EstadoDeLaReserva> reserva;
            try
            {
                reserva = await reservas.Consultar(entrada.PedidoId, entrada.CuentaId, cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return NoDisponible();
            }

            if (reserva.Resultado == ResultadoDeLaLlamadaDeReserva.SinRespuesta)
            {
                return NoDisponible();
            }

            if (!reserva.EsCorrecta || reserva.Valor is not { Vigente: true })
            {
                return new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePagos.ReservaExpirada);
            }
        }

        var limite = await limitador.Reservar(entrada.PedidoId, entrada.CuentaId, entrada.Clave.IntentoId, cancelacion).ConfigureAwait(false);
        return limite.Resultado == ResultadoDelLimiteDeCobro.Permitido ? null : limite.ComoFallo<IntentoRegistrado>().Error;
    }

    private async Task<Resultado<IntentoRegistrado>> RegistrarEnLaTransaccion(EntradaDeRegistroDeIntento entrada, CancellationToken token)
    {
        // Orden de bloqueo global: primero la marca de cancelación de la cuenta (con bloqueo) y después la intención.
        if (await cuentas.CancelacionEnEjecucion(entrada.CuentaId, bloquear: true, token).ConfigureAwait(false))
        {
            return Resultado<IntentoRegistrado>.Fallo(CuentaNoPuedeComprar("cancelacion-en-ejecucion"));
        }

        var intencion = await repositorio.ObtenerIntencion(entrada.PedidoId, paraActualizar: true, token).ConfigureAwait(false);
        if (intencion is null || !intencion.EsDeLaCuenta(entrada.CuentaId))
        {
            return Resultado<IntentoRegistrado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDePagos.NoEncontrado));
        }

        // Primero si se puede cobrar (cerrada, obsoleta, con la reserva vencida o con otro cobro en curso) y después si el token coincide con lo vigente.
        var ahora = reloj.Ahora;
        var validacion = intencion.ValidarCobro(ahora);
        if (validacion != ValidacionDelCobro.Permitido)
        {
            return FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.Conflicto, EmitirClaveDeIntento.Clave(validacion));
        }

        // El token lleva lo que el cliente vio: si la persona editó el carrito en otra pestaña, el total o la huella cambiaron.
        if (intencion.Total.EnCentimos != entrada.Clave.TotalEnCentimos || !string.Equals(intencion.HuellaDeInstantanea, entrada.Clave.HuellaDeInstantanea, StringComparison.Ordinal))
        {
            return FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.TotalCambio);
        }


        // Carrito en la misma transacción, sin bloqueo: solo `en_pago` con el pedido de esta intención y sin retiro pendiente.
        Resultado<CheckoutVivoParaPagar> vivo;
        try
        {
            vivo = await checkout.ObtenerParaPagar(token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Todavía no se escribió nada: sin respuesta de Carrito no se cobra (falla cerrada).
            return FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
        }

        if (!vivo.EsExito)
        {
            return vivo.Error!.Categoria == CategoriaDeError.NoEncontrado
                ? FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.InstantaneaObsoleta)
                : FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
        }

        if (vivo.Valor.PedidoId != intencion.Id)
        {
            return FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.InstantaneaObsoleta);
        }

        if (vivo.Valor.Estado != EstadoVisibleDelCheckout.EnPago)
        {
            return FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.PagoEnCurso);
        }

        var intento = IntentoDeCobro.Iniciar(entrada.Clave.IntentoId, intencion.Id, entrada.Medio, intencion.Total, ahora);
        switch (await repositorio.AgregarIntento(intento, token).ConfigureAwait(false))
        {
            case ResultadoDeAgregarIntento.ClaveRepetida:
                return FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.ClaveInvalida);
            case ResultadoDeAgregarIntento.IntentoActivoExistente:
                return FallosDePagos.De<IntentoRegistrado>(CategoriaDeError.Conflicto, ClavesDePagos.PagoEnCurso);
        }

        intencion.IniciarCobro(ahora);
        await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.CobroIniciado)
            {
                ObjetoTipo = "intento-de-cobro",
                ObjetoId = intento.Id.ToString("D"),
                Detalle = new Dictionary<string, string?> { ["medio"] = intento.Medio.ATexto(), ["importe"] = intento.Importe.ToString() },
            },
            token).ConfigureAwait(false);
        return Resultado<IntentoRegistrado>.Exito(new IntentoRegistrado(intento, false));
    }

    private static ErrorDeAplicacion NoDisponible() => new(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);

    private static ErrorDeAplicacion CuentaNoPuedeComprar(string motivo) => new(CategoriaDeError.Prohibido, ClavesDePagos.CuentaNoPuedeComprar, [new ErrorDeCampo("motivo", motivo)]);
}
