using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>Lo que varios casos de uso de Pagos anotan o auditan al resolver un intento: el mismo texto y las mismas claves en todos, sin datos de pago.</summary>
internal static class ResolucionesDeCobros
{
    /// <summary>El resultado de un cobro en la auditoría: aprobado, rechazado o pendiente, con su motivo de la lista cerrada y, si lo hubo, qué de un aprobado no coincidió (AM11).</summary>
    public static Task Auditar(IAuditoria auditoria, IntentoDeCobro intento, string resultado, string? motivo, MotivoDeDuda? duda, CancellationToken token)
    {
        var detalle = new Dictionary<string, string?> { ["resultado"] = resultado };
        if (motivo is not null)
        {
            detalle["motivo"] = motivo;
        }

        if (duda is { } d)
        {
            detalle["duda"] = d switch
            {
                MotivoDeDuda.ReferenciaDistinta => "referencia",
                MotivoDeDuda.MonedaDistinta => "moneda",
                _ => "importe",
            };
        }

        return auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.CobroResuelto)
            {
                ObjetoTipo = "intento-de-cobro",
                ObjetoId = intento.Id.ToString("D"),
                Detalle = detalle,
            },
            token);
    }

    /// <summary>Anota, en la misma transacción de la transición, el aviso a Carrito y el aviso del estado del pago al cliente (idempotentes por su clave).</summary>
    public static async Task AnotarLosAvisos(IRepositorioDePagos repositorio, Guid intencionId, Guid intentoId, TransicionDeAviso transicion, EventoDeAvisoDePago evento, bool retenido, DateTimeOffset ahora, CancellationToken token)
    {
        await repositorio.AgregarAvisoACarrito(AvisoACarrito.Crear(intencionId, intentoId, transicion, retenido, ahora), token).ConfigureAwait(false);
        await repositorio.AgregarAvisoDePago(AvisoDePago.Crear(intencionId, intentoId, evento, ahora), token).ConfigureAwait(false);
    }
}

/// <summary>
/// El aviso entrante de la pasarela (T19, D20, PAG-19): <b>solo adelanta la consulta</b>. Lo lee sin red, y <c>Plataforma</c> verifica su firma y su marca de tiempo y lo deduplica; la firma falsa se rechaza antes de todo efecto
/// y sin llamadas de red (un 401 uniforme). Una entrada aceptada marca <c>consulta_inmediata</c> en el intento y audita <b>en la misma transacción</b> que reclama el aviso: si algo falla, la transacción se revierte, el reclamo
/// se libera y responde «no disponible» (503) para que la pasarela reintente. Un cargo desconocido se acepta sin efecto. La consulta y la aprobación las hace <see cref="ConsultarPagosPendientes"/> fuera de transacción.
/// </summary>
internal sealed class RegistrarAvisoDeLaPasarela(
    IProcesadorDeEntradasDeIntegracion procesador,
    IPasarelaDePago pasarela,
    IRepositorioDePagos repositorio,
    IUnidadDeTrabajo unidad,
    BloqueoDeIntenciones bloqueo,
    IAuditoria auditoria,
    IReloj reloj) : IAvisosDeLaPasarela
{
    /// <summary>El proveedor en la lista blanca y los secretos de <c>Plataforma</c>.</summary>
    public const string Proveedor = "culqi";

    public async Task<Resultado<bool>> Recibir(AvisoDeLaPasarelaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        AvisoDeLaPasarela? aviso;
        try
        {
            aviso = pasarela.InterpretarAviso(entrada.Cuerpo);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            aviso = null;
        }

        try
        {
            return await unidad.Ejecutar(token => RecibirEnLaTransaccion(aviso, entrada, token), cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La transacción se revirtió (también el reclamo del aviso): la pasarela lo reenvía.
            return FallosDePagos.De<bool>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
        }
    }

    private async Task<Resultado<bool>> RecibirEnLaTransaccion(AvisoDeLaPasarela? aviso, AvisoDeLaPasarelaEntrada entrada, CancellationToken token)
    {
        // Sin un identificador de evento el procesador lo rechaza y deja su evento de seguridad: un 401 uniforme.
        var idDeEntrada = aviso?.IdDeEntrada ?? string.Empty;
        var recibida = await procesador.Recibir(new EntradaDeIntegracion(Proveedor, idDeEntrada, entrada.MarcaDeTiempo, entrada.Cuerpo, entrada.Firma), token).ConfigureAwait(false);
        switch (recibida.Estado)
        {
            case EstadoDeEntradaDeIntegracion.Rechazada:
                return FallosDePagos.De<bool>(CategoriaDeError.NoAutenticado, ClavesDePagos.NoAutenticado);
            case EstadoDeEntradaDeIntegracion.Repetida:
                return Resultado<bool>.Exito(true);
            case EstadoDeEntradaDeIntegracion.EnCurso:
                return FallosDePagos.De<bool>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
        }

        await AdelantarLaConsulta(aviso!.ReferenciaOpaca, token).ConfigureAwait(false);
        await procesador.Completar(Proveedor, idDeEntrada, new ResultadoMinimo("consulta-adelantada"), token).ConfigureAwait(false);
        return Resultado<bool>.Exito(true);
    }

    private async Task AdelantarLaConsulta(Guid referenciaOpaca, CancellationToken token)
    {
        var existente = await repositorio.ObtenerIntento(referenciaOpaca, token).ConfigureAwait(false);
        if (existente is null)
        {
            // Un cargo sin intento coincidente: se acepta sin efecto (no se revela nada) y sin llamadas a la pasarela.
            return;
        }

        // El mismo orden de bloqueo (D24): la marca de cancelación de la cuenta y después la intención.
        await bloqueo.Bloquear(existente.IntencionId, token).ConfigureAwait(false);
        var intento = await repositorio.ObtenerIntento(referenciaOpaca, token).ConfigureAwait(false);
        if (intento is null || !intento.MarcarConsultaInmediata(reloj.Ahora))
        {
            return;
        }

        await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.AvisoDeLaPasarelaRecibido)
            {
                ObjetoTipo = "intento-de-cobro",
                ObjetoId = intento.Id.ToString("D"),
                Detalle = new Dictionary<string, string?> { ["resultado"] = "consulta-adelantada" },
            },
            token).ConfigureAwait(false);
    }
}

/// <summary>
/// El trabajo de consulta de pagos pendientes (T19, D03, PAG-07, PAG-15, PAG-18, PAG-19): cada minuto, hasta los 30 minutos, consulta a la pasarela <b>fuera de toda transacción</b> el estado de los intentos sin resolver
/// (antes, si el aviso entrante lo adelantó) y valida lo que responde contra el intento. Un aprobado va por <see cref="IConfirmadorDeCompraAprobada"/>; un rechazo cierra el intento; los intentos <c>iniciado</c> sin respuesta a
/// los 15 segundos pasan a <c>pendiente_de_confirmacion</c> y <b>nunca</b> a rechazado por la simple ausencia del cargo (para no abrir un segundo cobro); y pasado el límite de 30 minutos vencen. Cuando un pendiente con impresos
/// no pudo extender su reserva, la reextiende y libera el aviso retenido. Una falla con un intento no detiene a los demás.
/// </summary>
internal sealed class ConsultarPagosPendientes(
    IRepositorioDePagos repositorio,
    IUnidadDeTrabajo unidad,
    BloqueoDeIntenciones bloqueo,
    IPasarelaDePago pasarela,
    IConfirmadorDeCompraAprobada confirmador,
    ExtenderReservaDelPago extender,
    IAvisadorACarrito avisador,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelCobro opciones)
{
    public const string MotivoDeAlertaCobroDudoso = IniciarCobro.MotivoDeAlertaCobroDudoso;
    public const string MotivoDeAlertaConsultaFallida = "consulta-de-pago-fallida";

    /// <summary>Revisa hasta <paramref name="limite"/> intentos sin resolver y devuelve cuántos revisó.</summary>
    public async Task<int> Ejecutar(int limite, CancellationToken cancelacion = default)
    {
        var ids = await unidad.Ejecutar(token => repositorio.ListarIntentosParaConsultar(reloj.Ahora, limite, token), cancelacion).ConfigureAwait(false);
        foreach (var id in ids)
        {
            try
            {
                await Revisar(id, cancelacion).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Un intento que falla no detiene a los demás: la próxima corrida lo reintenta.
                await Alertar(MotivoDeAlertaConsultaFallida).ConfigureAwait(false);
            }
        }

        return ids.Count;
    }

    private async Task Revisar(Guid intentoId, CancellationToken cancelacion)
    {
        var (intento, intencion) = await Leer(intentoId, cancelacion).ConfigureAwait(false);
        if (intento is null || intencion is null)
        {
            return;
        }

        // Un intento ya vencido o rechazado puede aprobarse tarde: se consulta (el aviso lo adelanta, y tras vencer unos minutos más) y un aprobado entra como sobrante que se reembolsa (D23, R02).
        if (intento.EsConciliable(reloj.Ahora))
        {
            if (intento.DebeConsultar(reloj.Ahora))
            {
                await ConsultarUnCobroYaResuelto(intento, cancelacion).ConfigureAwait(false);
            }

            return;
        }

        if (intento.Estado is not (EstadoDeIntento.Iniciado or EstadoDeIntento.PendienteDeConfirmacion))
        {
            return;
        }

        if (intento.DebeConsultar(reloj.Ahora) && await ConsultarElEstado(intento, intencion, cancelacion).ConfigureAwait(false))
        {
            // Aprobado o rechazado: ya se resolvió por su propia vía.
            return;
        }

        (intento, intencion) = await Leer(intentoId, cancelacion).ConfigureAwait(false);
        if (intento is null || intencion is null)
        {
            return;
        }

        if (intento.DebeVencer(intencion.LimitePendienteEn, reloj.Ahora))
        {
            await Vencer(intento.IntencionId, intentoId, cancelacion).ConfigureAwait(false);
        }
        else if (intento.Estado == EstadoDeIntento.PendienteDeConfirmacion && intencion is { TieneImpresos: true, ReservaExtendida: false })
        {
            // La primera extensión falló: se reintenta y, si termina, sale el aviso retenido.
            if (await extender.Extender(intento.IntencionId).ConfigureAwait(false))
            {
                await avisador.LiberarElPendienteRetenido(intento.IntencionId, intentoId, CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    /// <summary>Consulta un intento ya resuelto: solo un aprobado válido hace algo (entra por el único punto de entrada del estado aprobado y sigue la variante de cobro sobrante); lo demás no cambia nada.</summary>
    private async Task ConsultarUnCobroYaResuelto(IntentoDeCobro intento, CancellationToken cancelacion)
    {
        var validado = (await Consultar(intento.ReferenciaOpaca).ConfigureAwait(false)).Validar(intento);
        await unidad.Ejecutar(token => AnotarLaConsulta(intento.Id, token), cancelacion).ConfigureAwait(false);
        if (validado.EsDudoso)
        {
            await Alertar(MotivoDeAlertaCobroDudoso).ConfigureAwait(false);
        }

        if (validado.Tipo == TipoDeResultadoDeCobro.Aprobado)
        {
            await confirmador.Confirmar(intento.IntencionId, intento.Id, validado.ReferenciaDePasarela!, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Consulta, valida y actúa. Devuelve si el intento quedó resuelto (aprobado o rechazado).</summary>
    private async Task<bool> ConsultarElEstado(IntentoDeCobro intento, IntencionDePago intencion, CancellationToken cancelacion)
    {
        var resultado = await Consultar(intento.ReferenciaOpaca).ConfigureAwait(false);
        var validado = resultado.Validar(intento);
        await unidad.Ejecutar(token => AnotarLaConsulta(intento.Id, token), cancelacion).ConfigureAwait(false);
        if (validado.EsDudoso)
        {
            await Alertar(MotivoDeAlertaCobroDudoso).ConfigureAwait(false);
        }

        switch (validado.Tipo)
        {
            case TipoDeResultadoDeCobro.Aprobado:
                await confirmador.Confirmar(intento.IntencionId, intento.Id, validado.ReferenciaDePasarela!, CancellationToken.None).ConfigureAwait(false);
                return true;
            case TipoDeResultadoDeCobro.Rechazado:
                await Rechazar(intento.IntencionId, intento.Id, validado.Motivo ?? MotivoDeRechazo.Otro, cancelacion).ConfigureAwait(false);
                return true;
        }

        // Sin respuesta (o sin el cargo): un iniciado que lleva 15 segundos sin resultado pasa a pendiente; nunca a rechazado por la ausencia del cargo.
        if (intento.Estado == EstadoDeIntento.Iniciado && intento.DebeResolverseSinRespuesta(reloj.Ahora))
        {
            await PasarAPendiente(intencion.Id, intento.Id, cancelacion).ConfigureAwait(false);
        }

        return false;
    }

    private async Task<ResultadoDeCobro> Consultar(Guid referenciaOpaca)
    {
        using var plazo = new CancellationTokenSource(opciones.TiempoDeRespuesta);
        try
        {
            return await pasarela.ConsultarEstado(referenciaOpaca, plazo.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return ResultadoDeCobro.SinRespuesta();
        }
    }

    private async Task<bool> AnotarLaConsulta(Guid intentoId, CancellationToken token)
    {
        var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
        if (intento is not null)
        {
            intento.RegistrarConsulta(reloj.Ahora);
            await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
        }

        return true;
    }

    private async Task PasarAPendiente(Guid pedidoId, Guid intentoId, CancellationToken cancelacion)
    {
        var paso = await unidad.Ejecutar(
            async token =>
            {
                var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
                var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
                if (intencion is null || intento is null || !intento.MarcarPendiente(reloj.Ahora))
                {
                    return false;
                }

                if (intencion.Estado == EstadoDeIntencion.ConCobro)
                {
                    intencion.PasarAPendiente();
                    await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                }

                await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
                await ResolucionesDeCobros.Auditar(auditoria, intento, "pendiente", null, null, token).ConfigureAwait(false);
                await ResolucionesDeCobros.AnotarLosAvisos(repositorio, pedidoId, intentoId, TransicionDeAviso.Pendiente, EventoDeAvisoDePago.Pendiente, retenido: intencion.TieneImpresos, reloj.Ahora, token).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
        if (paso && await extender.Extender(pedidoId).ConfigureAwait(false))
        {
            await avisador.LiberarElPendienteRetenido(pedidoId, intentoId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task Rechazar(Guid pedidoId, Guid intentoId, MotivoDeRechazo motivo, CancellationToken cancelacion)
    {
        var paso = await unidad.Ejecutar(
            async token =>
            {
                var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
                var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
                if (intencion is null || intento is null || !intento.MarcarRechazado(motivo, reloj.Ahora))
                {
                    return false;
                }

                if (intencion.Estado == EstadoDeIntencion.ConCobro)
                {
                    intencion.ResolverIntentoSinCobro(reloj.Ahora);
                    await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                }

                await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
                await ResolucionesDeCobros.Auditar(auditoria, intento, "rechazado", motivo.ATexto(), null, token).ConfigureAwait(false);
                await ResolucionesDeCobros.AnotarLosAvisos(repositorio, pedidoId, intentoId, TransicionDeAviso.Rechazado, EventoDeAvisoDePago.Rechazado, retenido: false, reloj.Ahora, token).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
        if (paso)
        {
            await avisador.Enviar(pedidoId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>Pasado el límite de 30 minutos el intento vence (PAG-18): la intención vuelve a abierta si su reserva sigue viva o se cierra como «rechazada sin reintento».</summary>
    private async Task Vencer(Guid pedidoId, Guid intentoId, CancellationToken cancelacion)
    {
        var paso = await unidad.Ejecutar(
            async token =>
            {
                var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
                var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
                if (intencion is null || intento is null || !intento.Vencer(reloj.Ahora))
                {
                    return false;
                }

                if (intencion.Estado == EstadoDeIntencion.ConCobro)
                {
                    intencion.ResolverIntentoSinCobro(reloj.Ahora);
                    await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                }

                await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
                await ResolucionesDeCobros.Auditar(auditoria, intento, "rechazado", "vencido", null, token).ConfigureAwait(false);
                await ResolucionesDeCobros.AnotarLosAvisos(repositorio, pedidoId, intentoId, TransicionDeAviso.Vencido, EventoDeAvisoDePago.Vencido, retenido: false, reloj.Ahora, token).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
        if (paso)
        {
            await avisador.Enviar(pedidoId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Task<(IntentoDeCobro? Intento, IntencionDePago? Intencion)> Leer(Guid intentoId, CancellationToken cancelacion) =>
        unidad.Ejecutar(
            async token =>
            {
                var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
                var intencion = intento is null ? null : await repositorio.ObtenerIntencion(intento.IntencionId, paraActualizar: false, token).ConfigureAwait(false);
                return (intento, intencion);
            },
            cancelacion);

    private async Task Alertar(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe la consulta.
        }
    }
}
