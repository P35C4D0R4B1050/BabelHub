using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// El orquestador de la compensación (T16, D07): la <b>única ruta</b> de PAG-15, PAG-20, PAG-29 y PAG-32 y del cobro sobrante. Es un orquestador sin transacción con <b>un paso corto por transacción</b> bajo el bloqueo de la
/// intención, y cada paso se marca y no se repite: (0) abre la compensación (una por cobro) y cierra la intención; (1) libera las reservas <b>fuera de transacción</b>; (2) registra el reembolso del cargo completo y, después de
/// confirmar, llama a <see cref="ProcesarReembolso"/> fuera de transacción; (3) deja el aviso <c>compensado</c> para Carrito. Sin crear pedido y, en PAG-29 y PAG-32, sin avisar ni leer el correo. Una compensación ya iniciada se
/// termina de inmediato con su motivo; una falla en cualquier paso la deja para que la retome el trabajo (T20), con una alerta técnica.
/// </summary>
internal sealed class CompensarCompra(
    IRepositorioDePagos repositorio,
    IUnidadDeTrabajo unidad,
    BloqueoDeIntenciones bloqueo,
    IReservasDelPago reservas,
    RegistrarReembolso registrarReembolso,
    ProcesarReembolso procesarReembolso,
    IAvisadorACarrito avisador,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj) : ICompensadorDeCompra
{
    public const string MotivoDeAlertaCompensacionFallida = "compensacion-fallida";
    public const string MotivoDeAlertaCompensacionNoAplicable = "compensacion-no-aplicable";
    public const string MotivoDeAlertaReservaConfirmadaSinLiberar = "reserva-confirmada-sin-liberar";

    public async Task Compensar(Guid pedidoId, Guid intentoId, MotivoDeCompensacion motivo, VarianteDeCompensacion variante, CancellationToken cancelacion = default)
    {
        try
        {
            var (compensacion, noAplicable) = await unidad.Ejecutar(token => Iniciar(pedidoId, intentoId, motivo, variante, token), cancelacion).ConfigureAwait(false);
            if (noAplicable)
            {
                // Compensar una compra completada o un intento que no es un cobro sobrante sería reembolsar lo que se entregó: no se hace y se alerta.
                await Alertar(MotivoDeAlertaCompensacionNoAplicable).ConfigureAwait(false);
                return;
            }

            if (compensacion is null || compensacion.EstaTerminada)
            {
                return;
            }

            if (compensacion.SiguientePaso == PasoDeCompensacion.LiberarReservas && !await LiberarLasReservas(pedidoId, intentoId, cancelacion).ConfigureAwait(false))
            {
                return;
            }

            compensacion = await Leer(intentoId, cancelacion).ConfigureAwait(false);
            if (compensacion.SiguientePaso == PasoDeCompensacion.RegistrarReembolso)
            {
                var reembolsoId = await unidad.Ejecutar(token => RegistrarElReembolso(pedidoId, intentoId, token), cancelacion).ConfigureAwait(false);
                if (reembolsoId is { } id)
                {
                    await ProcesarSinInterrumpir(id).ConfigureAwait(false);
                }
            }

            compensacion = await Leer(intentoId, cancelacion).ConfigureAwait(false);
            if (compensacion.SiguientePaso == PasoDeCompensacion.CerrarCheckout)
            {
                await unidad.Ejecutar(token => CerrarElCheckout(pedidoId, intentoId, token), cancelacion).ConfigureAwait(false);

                // Después de confirmar la transacción que anotó el aviso «compensado»: Carrito cierra el checkout y una falla suya no cambia la compensación.
                await avisador.Enviar(pedidoId, cancelacion).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Los pasos hechos quedaron marcados: la retoma sigue desde donde quedó.
            await Alertar(MotivoDeAlertaCompensacionFallida).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// El paso (0): abre la compensación de este cobro (una sola) y, en la variante completa, pasa la intención aprobada a <c>compensada</c> (elimina el contacto) y omite los avisos de pendiente que ya no tienen sentido. Si ya existe,
    /// la devuelve tal cual: conserva su motivo aunque la cancelación de la cuenta se haya revertido.
    /// </summary>
    private async Task<(Compensacion? Compensacion, bool NoAplicable)> Iniciar(Guid pedidoId, Guid intentoId, MotivoDeCompensacion motivo, VarianteDeCompensacion variante, CancellationToken token)
    {
        var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
        var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
        if (intencion is null || intento is null || intento.IntencionId != pedidoId)
        {
            return (null, false);
        }

        if (await repositorio.ObtenerCompensacion(intentoId, token).ConfigureAwait(false) is { } existente)
        {
            return (existente, false);
        }

        var ahora = reloj.Ahora;
        if (variante == VarianteDeCompensacion.Completa)
        {
            if (intencion.Estado is not (EstadoDeIntencion.Aprobada or EstadoDeIntencion.Compensada))
            {
                return (null, true);
            }
        }
        else if (intento.Estado != EstadoDeIntento.AprobadoSobrante)
        {
            return (null, true);
        }

        var compensacion = Compensacion.Iniciar(pedidoId, intentoId, motivo, variante, tieneReservas: intencion.TieneImpresos, ahora);
        if (!await repositorio.AgregarCompensacion(compensacion, token).ConfigureAwait(false))
        {
            // Otra solicitud la abrió primero: se sigue con la suya.
            return (await repositorio.ObtenerCompensacion(intentoId, token).ConfigureAwait(false), false);
        }

        // Sin aviso al cliente (PAG-29 y PAG-32, también en un cobro sobrante) no queda ningún aviso sin enviar que lea su correo; y una compensación completa supera los avisos de «pendiente».
        if (variante == VarianteDeCompensacion.Completa || !compensacion.Avisar)
        {
            await OmitirLosAvisosQueYaNoTienenSentido(pedidoId, todos: !compensacion.Avisar, token).ConfigureAwait(false);
        }

        if (variante == VarianteDeCompensacion.Completa && intencion.Estado == EstadoDeIntencion.Aprobada)
        {
            intencion.Compensar(ahora);
            await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
        }

        await Auditar(pedidoId, "abrir", motivo, token).ConfigureAwait(false);

        return (compensacion, false);
    }

    /// <summary>Los avisos de «pendiente» (a Carrito y al cliente) de esta intención quedan superados por la compensación: pasan a <c>omitido</c>; si la compensación no avisa (PAG-29 y PAG-32), <b>todo</b> aviso sin enviar pasa a omitido para no leer el correo.</summary>
    private async Task OmitirLosAvisosQueYaNoTienenSentido(Guid pedidoId, bool todos, CancellationToken token)
    {
        foreach (var aviso in await repositorio.ListarAvisosACarritoDeLaIntencion(pedidoId, token).ConfigureAwait(false))
        {
            if ((todos || aviso.Transicion == TransicionDeAviso.Pendiente) && aviso.Omitir())
            {
                await repositorio.GuardarAvisoACarrito(aviso, token).ConfigureAwait(false);
            }
        }

        foreach (var aviso in await repositorio.ListarAvisosDePagoDeLaIntencion(pedidoId, token).ConfigureAwait(false))
        {
            if ((todos || aviso.Evento == EventoDeAvisoDePago.Pendiente) && aviso.Omitir())
            {
                await repositorio.GuardarAvisoDePago(aviso, token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// El paso (1): libera las reservas <b>fuera de transacción</b>. Con <c>falla-posterior-al-pago</c> si consta que se intentó confirmar (también libera lo confirmado) y con <c>pago-vencido</c> si no. Liberar una reserva ya
    /// expirada o liberada es un éxito sin cambios; un <c>INV-RESERVA-ESTADO-INVALIDO</c> con <c>pago-vencido</c> es una reserva ya confirmada: sin reintento y con alerta técnica. Sin respuesta del Inventario, se detiene aquí y
    /// se retoma. Devuelve si el paso quedó hecho.
    /// </summary>
    private async Task<bool> LiberarLasReservas(Guid pedidoId, Guid intentoId, CancellationToken cancelacion)
    {
        var intencion = await unidad.Ejecutar(token => repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, token), cancelacion).ConfigureAwait(false);
        if (intencion is null)
        {
            return false;
        }

        var motivo = Compensacion.MotivoDeLiberacionPara(intencion);
        RespuestaDeReserva<bool> respuesta;
        try
        {
            respuesta = await reservas.Liberar(pedidoId, motivo, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }

        if (respuesta.Resultado == ResultadoDeLaLlamadaDeReserva.SinRespuesta)
        {
            return false;
        }

        if (respuesta.Resultado == ResultadoDeLaLlamadaDeReserva.EstadoInvalido && motivo == MotivoDeLiberacion.PagoVencido)
        {
            await Alertar(MotivoDeAlertaReservaConfirmadaSinLiberar).ConfigureAwait(false);
        }

        await unidad.Ejecutar(
            async token =>
            {
                await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
                var compensacion = (await repositorio.ObtenerCompensacion(intentoId, token).ConfigureAwait(false))!;
                if (compensacion.MarcarReservasLiberadas(motivo, reloj.Ahora))
                {
                    await repositorio.GuardarCompensacion(compensacion, token).ConfigureAwait(false);
                    await Auditar(pedidoId, "liberar-reservas", compensacion.Motivo, token).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion).ConfigureAwait(false);
        return true;
    }

    /// <summary>El paso (2): inserta el reembolso del cargo completo (idempotente por <c>comp:&lt;intentoId&gt;</c>), crea el aviso de «reembolso automático» si corresponde y marca el paso.</summary>
    private async Task<Guid?> RegistrarElReembolso(Guid pedidoId, Guid intentoId, CancellationToken token)
    {
        var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
        var compensacion = (await repositorio.ObtenerCompensacion(intentoId, token).ConfigureAwait(false))!;
        if (compensacion.ReembolsoId is not null || intencion is null)
        {
            return null;
        }

        var intento = (await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false))!;
        var reembolso = await registrarReembolso.RegistrarDeCompensacion(intento, token).ConfigureAwait(false);
        var ahora = reloj.Ahora;
        compensacion.MarcarReembolsoRegistrado(reembolso.Id, ahora);

        // PAG-29 y PAG-32 no avisan; y una intención desvinculada ya no tiene a quién avisar (no se lee el correo ni nada de la persona).
        if (compensacion.Avisar && intencion.CuentaId is not null)
        {
            await repositorio.AgregarAvisoDePago(AvisoDePago.Crear(pedidoId, intentoId, EventoDeAvisoDePago.ReembolsoAutomatico, ahora), token).ConfigureAwait(false);
        }

        await repositorio.GuardarCompensacion(compensacion, token).ConfigureAwait(false);
        await Auditar(pedidoId, "registrar-reembolso", compensacion.Motivo, token).ConfigureAwait(false);
        return reembolso.Id;
    }

    /// <summary>El paso (3): deja el aviso <c>compensado</c> para Carrito (que lo recibe después de confirmar) y marca el checkout como cerrado.</summary>
    private async Task<bool> CerrarElCheckout(Guid pedidoId, Guid intentoId, CancellationToken token)
    {
        await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
        var compensacion = (await repositorio.ObtenerCompensacion(intentoId, token).ConfigureAwait(false))!;
        if (compensacion.CheckoutCerradoEn is not null)
        {
            return false;
        }

        var ahora = reloj.Ahora;
        await repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(pedidoId, intentoId, TransicionDeAviso.Compensado, retenido: false, ahora), token).ConfigureAwait(false);
        compensacion.MarcarCheckoutCerrado(ahora);
        await repositorio.GuardarCompensacion(compensacion, token).ConfigureAwait(false);
        await Auditar(pedidoId, "cerrar-checkout", compensacion.Motivo, token).ConfigureAwait(false);
        return true;
    }

/// <summary>Audita un paso de la compensación en la misma transacción, con su motivo de lista cerrada y sin datos de pago ni de la persona.</summary>    private Task Auditar(Guid pedidoId, string paso, MotivoDeCompensacion motivo, CancellationToken token) =>        auditoria.Registrar(            new SolicitudDeRegistro(AccionesAuditables.PasoDeCompensacionHecho)            {                ObjetoTipo = "pedido",                ObjetoId = pedidoId.ToString("D"),                Detalle = new Dictionary<string, string?>                {                    ["paso"] = paso,                    ["motivo"] = motivo switch                    {                        MotivoDeCompensacion.PagoTardio => "pago-tardio",                        MotivoDeCompensacion.FallaPosteriorAlPago => "falla-posterior-al-pago",                        MotivoDeCompensacion.IntencionDesvinculada => "intencion-desvinculada",                        _ => "cancelacion-de-cuenta-en-curso",                    },                },            },            token);
    private Task<Compensacion> Leer(Guid intentoId, CancellationToken cancelacion) =>
        unidad.Ejecutar(async token => (await repositorio.ObtenerCompensacion(intentoId, token).ConfigureAwait(false))!, cancelacion);

    /// <summary>Pide el reembolso a la pasarela fuera de toda transacción; si falla, el reembolso queda pendiente para su reintento y la compensación sigue.</summary>
    private async Task ProcesarSinInterrumpir(Guid reembolsoId)
    {
        try
        {
            await procesarReembolso.Ejecutar(reembolsoId, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El reembolso quedó registrado y pendiente: lo reintenta el trabajo.
        }
    }

    /// <summary>Audita un paso de la compensación en la misma transacción, con su motivo de lista cerrada y sin datos de pago ni de la persona.</summary>
    private Task Auditar(Guid pedidoId, string paso, MotivoDeCompensacion motivo, CancellationToken token) =>
        auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.PasoDeCompensacionHecho)
            {
                ObjetoTipo = "pedido",
                ObjetoId = pedidoId.ToString("D"),
                Detalle = new Dictionary<string, string?>
                {
                    ["paso"] = paso,
                    ["motivo"] = motivo switch
                    {
                        MotivoDeCompensacion.PagoTardio => "pago-tardio",
                        MotivoDeCompensacion.FallaPosteriorAlPago => "falla-posterior-al-pago",
                        MotivoDeCompensacion.IntencionDesvinculada => "intencion-desvinculada",
                        _ => "cancelacion-de-cuenta-en-curso",
                    },
                },
            },
            token);

    private async Task Alertar(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe la compensación.
        }
    }
}
