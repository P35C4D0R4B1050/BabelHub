using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// Bloquea la intención respetando el orden global (D24): primero la marca de cancelación de la cuenta (leída con bloqueo, solo si la intención aún tiene cuenta) y después la intención. Lee la cuenta sin bloqueo, lee la marca
/// y vuelve a leer la intención con bloqueo: si la desvinculación llegó entre las dos lecturas, la cuenta ya es nula y se decide por PAG-29 sin haber leído nada de más. Solo se llama dentro de una transacción.
/// </summary>
internal sealed class BloqueoDeIntenciones(IRepositorioDePagos repositorio, ICuentaDelPagador cuentas)
{
    public async Task<(IntencionDePago? Intencion, bool CancelacionEnCurso)> Bloquear(Guid pedidoId, CancellationToken cancelacion)
    {
        var sinBloqueo = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, cancelacion).ConfigureAwait(false);
        if (sinBloqueo is null)
        {
            return (null, false);
        }

        var cancelacionEnCurso = false;
        if (sinBloqueo.CuentaId is { } cuentaId)
        {
            cancelacionEnCurso = await cuentas.CancelacionEnEjecucion(cuentaId, bloquear: true, cancelacion).ConfigureAwait(false);
        }

        var intencion = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: true, cancelacion).ConfigureAwait(false);
        return (intencion, cancelacionEnCurso);
    }
}

// ---------- Lo que el orquestador de la confirmación pide a los pasos siguientes (T15 y T16) ----------

internal enum TipoDeCompletar
{
    Completada,

    /// <summary>La compra no se puede completar y se compensa con el motivo.</summary>
    Compensar,

    /// <summary>Una falla no controlada: se reintenta y solo compensa a los 30 minutos desde <c>aprobado_en</c> (T20).</summary>
    Reintentar,
}

internal sealed record ResultadoDeCompletarCompra(TipoDeCompletar Tipo, MotivoDeCompensacion? Motivo = null);

/// <summary>El paso (c) (T15): la transacción que crea el pedido y completa la intención.</summary>
internal interface ICompletadorDeCompra
{
    Task<ResultadoDeCompletarCompra> Completar(Guid pedidoId, Guid intentoId, CancellationToken cancelacion = default);
}

/// <summary>El orquestador de la compensación (T16): termina todos los pasos pendientes de la compensación del cobro.</summary>
internal interface ICompensadorDeCompra
{
    Task Compensar(Guid pedidoId, Guid intentoId, MotivoDeCompensacion motivo, VarianteDeCompensacion variante, CancellationToken cancelacion = default);
}

// ---------- Paso (a): marcar la compra aprobada ----------

internal enum TipoDeMarcaDeAprobacion
{
    /// <summary>La compra sigue su camino: confirmar las reservas (si se marcó) y completar.</summary>
    Continuar,

    /// <summary>Hay que compensar con el motivo y la variante.</summary>
    Compensar,

    YaCompletada,

    /// <summary>La intención ya se compensó: no hay nada más que hacer con este cobro.</summary>
    YaCerrada,

    /// <summary>Otro ejecutor tiene reclamada la compra aprobada.</summary>
    EnCursoPorOtro,

    /// <summary>Otra referencia de la pasarela para el mismo intento: no se reembolsa nada solo, se alerta para conciliar.</summary>
    CargoDuplicado,

    NoEncontrada,
}

internal sealed record ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion Tipo, MotivoDeCompensacion? Motivo = null, VarianteDeCompensacion Variante = VarianteDeCompensacion.Completa, bool ConfirmarReservas = false);

/// <summary>
/// El paso (a) de la confirmación (T14, D23, D24): en <b>una transacción corta</b> lee la marca de cancelación de la cuenta, bloquea la intención, aplica la aprobación al intento (la única forma de aprobar), marca <c>aprobado_en</c> y
/// la reclamación, clasifica si la compra es tardía o si prevalecen PAG-29 o PAG-32 y, solo si va a llamar a <c>reservas.confirmar</c>, marca <c>confirmacion_iniciada_en</c> <b>antes</b> de llamar. Es idempotente: la retoma y la
/// consulta de un pendiente lo repiten. Una compensación ya iniciada se termina con su motivo.
/// </summary>
internal sealed class MarcarCompraAprobada(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, BloqueoDeIntenciones bloqueo, IAuditoria auditoria, IReloj reloj)
{
    public Task<ResultadoDeMarcarAprobada> Marcar(Guid pedidoId, Guid intentoId, string referenciaDePasarela, CancellationToken cancelacion = default) =>
        unidad.Ejecutar(token => MarcarEnLaTransaccion(pedidoId, intentoId, referenciaDePasarela, token), cancelacion);

    private async Task<ResultadoDeMarcarAprobada> MarcarEnLaTransaccion(Guid pedidoId, Guid intentoId, string referenciaDePasarela, CancellationToken token)
    {
        var (intencion, cancelacionEnCurso) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
        var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
        if (intencion is null || intento is null || intento.IntencionId != pedidoId)
        {
            return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.NoEncontrada);
        }

        // Una compensación ya iniciada se termina de inmediato y conserva su motivo aunque la cancelación de la cuenta se revierta.
        if (await repositorio.ObtenerCompensacion(intentoId, token).ConfigureAwait(false) is { } existente)
        {
            return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.Compensar, existente.Motivo, existente.Variante);
        }

        var ahora = reloj.Ahora;
        var resultado = intencion.AplicarAprobacion(intento, referenciaDePasarela, ahora);
        if (resultado == ResultadoDeAprobacion.CargoDuplicado)
        {
            return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.CargoDuplicado);
        }

        // Un cobro sobrante (de un intento rechazado o vencido, o de una intención que ya no lo acepta) solo se reembolsa: nunca reabre ni completa la intención.
        if (intento.Estado == EstadoDeIntento.AprobadoSobrante)
        {
            await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
            if (resultado == ResultadoDeAprobacion.Sobrante)
            {
                await ResolucionesDeCobros.Auditar(auditoria, intento, "aprobado", null, null, token).ConfigureAwait(false);
            }

            return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.Compensar, MotivoDelSobrante(intencion, cancelacionEnCurso), VarianteDeCompensacion.SoloReembolso);
        }

        await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
        if (resultado == ResultadoDeAprobacion.Aprobado)
        {
            await ResolucionesDeCobros.Auditar(auditoria, intento, "aprobado", null, null, token).ConfigureAwait(false);
        }

        switch (intencion.Estado)
        {
            case EstadoDeIntencion.Completada:
                return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.YaCompletada);
            case EstadoDeIntencion.Compensada:
                return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.YaCerrada);
            case not EstadoDeIntencion.Aprobada:
                throw new InvalidOperationException("Un intento aprobado con una intención que no está aprobada, completada ni compensada.");
        }

        var motivo = Compensacion.Clasificar(intencion, cancelacionEnCurso);
        if (motivo is not null)
        {
            await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
            return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.Compensar, motivo, VarianteDeCompensacion.Completa);
        }

        if (!intencion.Reclamar(ahora))
        {
            await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
            return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.EnCursoPorOtro);
        }

        // Solo se anota que se va a confirmar cuando de verdad se va a llamar a reservas.confirmar: una compra solo digital o con las reservas ya confirmadas no lo marca.
        var confirmar = intencion.ReservasConfirmadasEn is null && intencion.MarcarConfirmacionIniciada(ahora);
        await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
        return new ResultadoDeMarcarAprobada(TipoDeMarcaDeAprobacion.Continuar, ConfirmarReservas: confirmar);
    }

    /// <summary>Quien compensa la compra aprobada con el motivo; en un sobrante, por qué el cobro ya no se podía aceptar.</summary>
    private static MotivoDeCompensacion MotivoDelSobrante(IntencionDePago intencion, bool cancelacionEnCurso) =>
        intencion.EstaDesvinculada ? MotivoDeCompensacion.IntencionDesvinculada
        : cancelacionEnCurso ? MotivoDeCompensacion.CancelacionDeCuentaEnCurso
        : MotivoDeCompensacion.PagoTardio;

    /// <summary>Suelta la reclamación tras una falla que se reintenta, para que la retoma no espere a que venza.</summary>
    public Task SoltarLaReclamacion(Guid pedidoId, CancellationToken cancelacion = default) =>
        unidad.Ejecutar(
            async token =>
            {
                var (intencion, _) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
                if (intencion is { ReclamadaHasta: not null })
                {
                    intencion.SoltarReclamacion();
                    await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                }

                return true;
            },
            cancelacion);
}

// ---------- Paso (b): confirmar las reservas de los impresos ----------

internal enum TipoDeConfirmacionDeReservas
{
    Confirmadas,
    Compensar,
    Reintentar,
}

internal sealed record ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas Tipo, MotivoDeCompensacion? Motivo = null);

/// <summary>
/// El paso (b) (T14, D03, D24): llama a <c>reservas.confirmar</c> con la clave <c>conf-&lt;intentoId&gt;</c> <b>fuera de toda transacción</b> y guarda la fuente y los traslados que devuelve. Un
/// <c>INV-RESERVA-ESTADO-INVALIDO</c> equivale a «la reserva venció» (PAG-15); una respuesta que no se entiende o no coincide con las líneas es una falla posterior al pago (PAG-20); y sin respuesta del Inventario no se
/// compensa: se reintenta con la misma clave. Tras confirmar vuelve a comprobar que la intención no se desvinculó ni la cuenta se está cancelando.
/// </summary>
internal sealed class ConfirmarReservasDeLaCompra(IRepositorioDePagos repositorio, IUnidadDeTrabajo unidad, BloqueoDeIntenciones bloqueo, IReservasDelPago reservas, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public const string MotivoDeAlertaReservaInexistente = "reserva-confirmada-inexistente";
    public const string MotivoDeAlertaReservasInconsistentes = "reservas-confirmadas-inconsistentes";

    /// <summary>La clave de idempotencia de <c>reservas.confirmar</c>: la misma para todos los reintentos de un intento.</summary>
    public static string ClaveDeConfirmacion(Guid intentoId) => $"conf-{intentoId:D}";

    public async Task<ResultadoDeConfirmarReservas> Confirmar(Guid pedidoId, Guid intentoId, CancellationToken cancelacion = default)
    {
        var intencion = await unidad.Ejecutar(token => repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, token), cancelacion).ConfigureAwait(false);
        if (intencion is null)
        {
            return new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Reintentar);
        }

        RespuestaDeReserva<IReadOnlyList<LineaConfirmadaDeReserva>> respuesta;
        try
        {
            respuesta = await reservas.Confirmar(pedidoId, ClaveDeConfirmacion(intentoId), cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Reintentar);
        }

        switch (respuesta.Resultado)
        {
            case ResultadoDeLaLlamadaDeReserva.SinRespuesta:
                return new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Reintentar);
            case ResultadoDeLaLlamadaDeReserva.EstadoInvalido:
                // La reserva ya no está pendiente: venció antes de confirmar (PAG-15).
                return new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Compensar, MotivoDeCompensacion.PagoTardio);
            case ResultadoDeLaLlamadaDeReserva.NoExiste:
                await Alertar(MotivoDeAlertaReservaInexistente).ConfigureAwait(false);
                return new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Compensar, MotivoDeCompensacion.FallaPosteriorAlPago);
        }

        var lineas = Validar(intencion, respuesta.Valor);
        if (lineas is null)
        {
            await Alertar(MotivoDeAlertaReservasInconsistentes).ConfigureAwait(false);
            return new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Compensar, MotivoDeCompensacion.FallaPosteriorAlPago);
        }

        return await unidad.Ejecutar(token => AnotarLasReservas(pedidoId, lineas, token), cancelacion).ConfigureAwait(false);
    }

    private async Task<ResultadoDeConfirmarReservas> AnotarLasReservas(Guid pedidoId, IReadOnlyList<ReservaConfirmadaDeLinea> lineas, CancellationToken token)
    {
        var (intencion, cancelacionEnCurso) = await bloqueo.Bloquear(pedidoId, token).ConfigureAwait(false);
        if (intencion is null || intencion.Estado != EstadoDeIntencion.Aprobada)
        {
            // Otro ejecutor la completó o la compensó mientras tanto: el siguiente paso lo verá.
            return new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Reintentar);
        }

        intencion.MarcarReservasConfirmadas(reloj.Ahora, intencion.EstaDesvinculada ? null : lineas);
        await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);

        // Tras confirmar se vuelve a comprobar: la desvinculación (PAG-29) o la cancelación de la cuenta en ejecución (PAG-32) prevalecen y se libera lo confirmado.
        if (intencion.EstaDesvinculada)
        {
            return new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Compensar, MotivoDeCompensacion.IntencionDesvinculada);
        }

        return cancelacionEnCurso
            ? new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Compensar, MotivoDeCompensacion.CancelacionDeCuentaEnCurso)
            : new ResultadoDeConfirmarReservas(TipoDeConfirmacionDeReservas.Confirmadas);
    }

    /// <summary>Las líneas confirmadas deben ser exactamente las impresas de la intención, cada una con su fuente y, si requiere traslado, el traslado; si no, es una respuesta inconsistente (SEG-13).</summary>
    private static IReadOnlyList<ReservaConfirmadaDeLinea>? Validar(IntencionDePago intencion, IReadOnlyList<LineaConfirmadaDeReserva>? devueltas)
    {
        var esperadas = intencion.Lineas.Where(l => !l.EsDigital).Select(l => l.LineaId).ToHashSet();
        if (devueltas is null || devueltas.Count != esperadas.Count)
        {
            return null;
        }

        var vistas = new HashSet<Guid>();
        var resultado = new List<ReservaConfirmadaDeLinea>(devueltas.Count);
        foreach (var linea in devueltas)
        {
            if (!esperadas.Contains(linea.LineaId) || !vistas.Add(linea.LineaId) || string.IsNullOrWhiteSpace(linea.CodigoDeFuente) || linea.RequiereTraslado != (linea.TrasladoId is not null))
            {
                return null;
            }

            resultado.Add(new ReservaConfirmadaDeLinea(linea.LineaId, linea.CodigoDeFuente.Trim(), linea.RequiereTraslado, linea.TrasladoId, linea.FechaEstimadaDeLlegada));
        }

        return resultado;
    }

    private async Task Alertar(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe la compra.
        }
    }
}

// ---------- El orquestador ----------

/// <summary>
/// <c>ConfirmarCompraAprobada</c> (T14): <b>el único punto de entrada del estado aprobado</b> (D23). Orquesta, sin transacción propia, el paso (a) <see cref="MarcarCompraAprobada"/>, el paso (b)
/// <see cref="ConfirmarReservasDeLaCompra"/> y el paso (c) <see cref="ICompletadorDeCompra"/>, y compensa por <see cref="ICompensadorDeCompra"/> cuando la compra no se puede completar. Devuelve «aprobado» solo cuando la compra
/// quedó completada; en cualquier otro caso (reclamada por otro, en reintento o compensada) el cliente ve «estamos confirmando tu pago» y la pantalla de estado dice la verdad. Una falla inesperada deja la compra aprobada
/// para que la retome el trabajo y publica una alerta técnica: nunca interrumpe al llamador.
/// </summary>
internal sealed class ConfirmarCompraAprobada(
    MarcarCompraAprobada marcar,
    ConfirmarReservasDeLaCompra confirmarReservas,
    ICompletadorDeCompra completador,
    ICompensadorDeCompra compensador,
    IAvisadorACarrito avisador,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj) : IConfirmadorDeCompraAprobada
{
    public const string MotivoDeAlertaCargoDuplicado = "cargo-duplicado-para-conciliar";
    public const string MotivoDeAlertaConfirmacionFallida = "confirmacion-de-compra-fallida";

    public async Task<ResultadoDelCobroDelCliente> Confirmar(Guid pedidoId, Guid intentoId, string referenciaDePasarela, CancellationToken cancelacion = default)
    {
        try
        {
            return await ConfirmarLaCompra(pedidoId, intentoId, referenciaDePasarela, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La compra queda aprobada o con el intento sin resolver: la retoma el trabajo (T19, T20). El cliente no ve un error.
            await Alertar(MotivoDeAlertaConfirmacionFallida).ConfigureAwait(false);
            return Pendiente(intentoId);
        }
    }

    private async Task<ResultadoDelCobroDelCliente> ConfirmarLaCompra(Guid pedidoId, Guid intentoId, string referenciaDePasarela, CancellationToken cancelacion)
    {
        var marca = await marcar.Marcar(pedidoId, intentoId, referenciaDePasarela, cancelacion).ConfigureAwait(false);
        switch (marca.Tipo)
        {
            case TipoDeMarcaDeAprobacion.YaCompletada:
                // Si el proceso cayó entre confirmar la transacción y avisar a Carrito, la retoma lo avisa (idempotente).
                await avisador.Enviar(pedidoId, cancelacion).ConfigureAwait(false);
                return new ResultadoDelCobroDelCliente(EstadoDelCobro.Aprobado, null, intentoId);
            case TipoDeMarcaDeAprobacion.CargoDuplicado:
                await Alertar(MotivoDeAlertaCargoDuplicado).ConfigureAwait(false);
                return Pendiente(intentoId);
            case TipoDeMarcaDeAprobacion.Compensar:
                await compensador.Compensar(pedidoId, intentoId, marca.Motivo!.Value, marca.Variante, cancelacion).ConfigureAwait(false);
                return Pendiente(intentoId);
            case TipoDeMarcaDeAprobacion.Continuar:
                break;
            default:
                return Pendiente(intentoId);
        }

        if (marca.ConfirmarReservas)
        {
            var reservas = await confirmarReservas.Confirmar(pedidoId, intentoId, cancelacion).ConfigureAwait(false);
            switch (reservas.Tipo)
            {
                case TipoDeConfirmacionDeReservas.Compensar:
                    await compensador.Compensar(pedidoId, intentoId, reservas.Motivo!.Value, VarianteDeCompensacion.Completa, cancelacion).ConfigureAwait(false);
                    return Pendiente(intentoId);
                case TipoDeConfirmacionDeReservas.Reintentar:
                    await marcar.SoltarLaReclamacion(pedidoId, cancelacion).ConfigureAwait(false);
                    return Pendiente(intentoId);
            }
        }

        var completada = await completador.Completar(pedidoId, intentoId, cancelacion).ConfigureAwait(false);
        switch (completada.Tipo)
        {
            case TipoDeCompletar.Completada:
                // Después de confirmar la transacción que anotó el aviso PagoAprobado: Carrito se entera y una falla suya no cambia nada.
                await avisador.Enviar(pedidoId, cancelacion).ConfigureAwait(false);
                return new ResultadoDelCobroDelCliente(EstadoDelCobro.Aprobado, null, intentoId);
            case TipoDeCompletar.Compensar:
                await compensador.Compensar(pedidoId, intentoId, completada.Motivo!.Value, VarianteDeCompensacion.Completa, cancelacion).ConfigureAwait(false);
                return Pendiente(intentoId);
            default:
                await marcar.SoltarLaReclamacion(pedidoId, cancelacion).ConfigureAwait(false);
                return Pendiente(intentoId);
        }
    }

    private static ResultadoDelCobroDelCliente Pendiente(Guid intentoId) => new(EstadoDelCobro.PendienteDeConfirmacion, null, intentoId);

    private async Task Alertar(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe la compra.
        }
    }
}
