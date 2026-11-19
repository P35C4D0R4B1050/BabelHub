using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>
/// Lo que Pagos hace cuando se aprueba un cobro (T14): <b>el único punto de entrada del estado aprobado</b>. Lo llaman el cobro en línea, la consulta de un pendiente, el aviso y la retoma; es un orquestador sin transacción y
/// nunca se llama desde una transacción. Devuelve lo que ve el cliente (aprobado, o pendiente si la compra no se pudo completar todavía).
/// </summary>
internal interface IConfirmadorDeCompraAprobada
{
    Task<ResultadoDelCobroDelCliente> Confirmar(Guid pedidoId, Guid intentoId, string referenciaDePasarela, CancellationToken cancelacion = default);
}

/// <summary>Los valores del cobro que la configuración ajusta: el tiempo de respuesta de la pasarela (10 segundos, PAG-07) y si la pasarela exige el correo del titular (PAG-30).</summary>
internal sealed record OpcionesDelCobro(TimeSpan TiempoDeRespuesta, bool EnviarElCorreoDelTitular = false)
{
    public static OpcionesDelCobro PorDefecto { get; } = new(PlazosDelPago.TiempoDeRespuesta);
}

/// <summary>
/// El orquestador del cobro (PAG-01 a PAG-09, PAG-11; D03): sin transacción, en tres tiempos. <b>Paso 0</b>: si el <c>intentoId</c> ya existe, tras validar la firma del token, que su cuenta es la del actor y que el intento
/// es de la intención del token, devuelve el resultado original sin más comprobaciones ni cupo (AC-PAG-21). <b>Paso 1</b>: <see cref="RegistrarIntento"/>. <b>Paso 2</b>: la pasarela, de hasta 10 segundos, <b>fuera de toda
/// transacción</b>, enviando solo el importe, la moneda, la referencia opaca y, si se exige, el correo (PAG-30). <b>Paso 3</b>: registra el resultado en una transacción corta con el mismo orden de bloqueo; ante un aprobado
/// <b>no cambia el intento ni la intención</b> (lo marca el paso (a) de la confirmación) y <see cref="IniciarCobro"/>, ya fuera de la transacción, llama a <see cref="IConfirmadorDeCompraAprobada"/>.
/// </summary>
internal sealed class IniciarCobro(
    AutorizadorDePagos autorizador,
    FirmadorDeClavesDeIntento firmador,
    IRepositorioDePagos repositorio,
    IUnidadDeTrabajo unidad,
    RegistrarIntento registrar,
    IPasarelaDePago pasarela,
    ICuentaDelPagador cuentas,
    IReservasDelPago reservas,
    IConfirmadorDeCompraAprobada confirmador,
    IAuditoria auditoria,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj,
    OpcionesDelCobro opciones) : ICasoDeUso<CobrarEntrada, ResultadoDelCobroDelCliente>
{
    public const string MotivoDeAlertaCobroDudoso = "cobro-dudoso-para-conciliar";
    public const string MotivoDeAlertaAuditoriaNoDisponible = "auditoria-no-disponible";
    public const string MotivoDeAlertaRegistroDelCobroFallido = "registro-del-cobro-fallido";

    public async Task<Resultado<ResultadoDelCobroDelCliente>> Ejecutar(CobrarEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirSesion(cancelacion).ConfigureAwait(false) is { } sinSesion)
        {
            return Resultado<ResultadoDelCobroDelCliente>.Fallo(sinSesion);
        }

        // La forma de la entrada se valida antes de gastar nada: un token del medio ausente o un medio fuera de la lista no llegan a la pasarela.
        if (entrada.TokenDelMedio is null || !Enum.IsDefined(entrada.Medio) || entrada.PedidoId == Guid.Empty)
        {
            return FallosDePagos.De<ResultadoDelCobroDelCliente>(CategoriaDeError.Validacion, ClavesDePagos.ValidacionFallida, [new ErrorDeCampo(entrada.TokenDelMedio is null ? "tokenDelMedio" : "medio", "invalido")]);
        }

        var cuentaId = autorizador.CuentaId!.Value;
        var verificacion = firmador.Verificar(entrada.ClaveDeIntento, reloj.Ahora, out var clave);
        if (verificacion != ResultadoDeVerificarClave.Valida)
        {
            return FallosDePagos.De<ResultadoDelCobroDelCliente>(CategoriaDeError.Conflicto, ClavesDePagos.ClaveInvalida);
        }

        if (clave!.CuentaId != cuentaId || clave.PedidoId != entrada.PedidoId)
        {
            // Un token firmado por el servidor para otra cuenta u otro pedido es una señal de robo: la respuesta es uniforme pero queda el acceso denegado (SEG-09).
            await autorizador.RegistrarClaveAjena(cancelacion).ConfigureAwait(false);
            return FallosDePagos.De<ResultadoDelCobroDelCliente>(CategoriaDeError.Conflicto, ClavesDePagos.ClaveInvalida);
        }

        // Paso 0: la clave ya existe: el resultado original, sin consultas ni cupo.
        var (existente, ajeno, completada) = await unidad.Ejecutar(
            async token =>
            {
                var intento = await repositorio.ObtenerIntento(clave.IntentoId, token).ConfigureAwait(false);
                if (intento is null)
                {
                    return ((IntentoDeCobro?)null, false, false);
                }

                var intencion = await repositorio.ObtenerIntencion(intento.IntencionId, paraActualizar: false, token).ConfigureAwait(false);
                return (intento, intento.IntencionId != entrada.PedidoId || intencion is null || !intencion.EsDeLaCuenta(cuentaId), intencion?.Estado == EstadoDeIntencion.Completada);
            },
            cancelacion).ConfigureAwait(false);
        if (existente is not null)
        {
            if (ajeno)
            {
                await autorizador.RegistrarClaveAjena(cancelacion).ConfigureAwait(false);
                return FallosDePagos.De<ResultadoDelCobroDelCliente>(CategoriaDeError.Conflicto, ClavesDePagos.ClaveInvalida);
            }

            return Resultado<ResultadoDelCobroDelCliente>.Exito(ResultadoOriginal(existente, completada));
        }

        var medio = entrada.Medio.ADominio();

        // Si la pasarela exige el correo del titular, se resuelve ANTES de registrar el intento: sin él no se cobra (falla cerrada), no se llama a la pasarela sin el dato que pide.
        string? correo = null;
        if (opciones.EnviarElCorreoDelTitular)
        {
            correo = await ObtenerElCorreo(cuentaId, cancelacion).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(correo))
            {
                return FallosDePagos.De<ResultadoDelCobroDelCliente>(CategoriaDeError.NoDisponible, ClavesDePagos.ServicioNoDisponible);
            }
        }

        // Paso 1: registrar el intento (Identidad, posesión, reservas, límite y la transacción corta).
        var registrado = await registrar.Registrar(new EntradaDeRegistroDeIntento(cuentaId, entrada.PedidoId, clave, medio), cancelacion).ConfigureAwait(false);
        if (!registrado.EsExito)
        {
            return Resultado<ResultadoDelCobroDelCliente>.Fallo(registrado.Error!);
        }

        var intentoRegistrado = registrado.Valor.Intento;
        if (registrado.Valor.EraRepetido)
        {
            // Otra solicitud simultánea con la misma clave ya hizo el cobro: este es el resultado original.
            var intencionDelCobro = await unidad.Ejecutar(token => repositorio.ObtenerIntencion(entrada.PedidoId, paraActualizar: false, token), cancelacion).ConfigureAwait(false);
            return Resultado<ResultadoDelCobroDelCliente>.Exito(ResultadoOriginal(intentoRegistrado, intencionDelCobro?.Estado == EstadoDeIntencion.Completada));
        }

        // Desde aquí el intento existe y la pasarela puede haber cobrado: nada de lo que sigue depende de que el cliente siga conectado (cerrar la pestaña no deja un cobro sin rastro).
        // Paso 2: la pasarela, fuera de toda transacción y cortada solo por el plazo de respuesta.
        var enviadoEn = reloj.Ahora;
        var resultadoDeLaPasarela = await Cobrar(new SolicitudDeCobroEnPasarela(intentoRegistrado.ReferenciaOpaca, intentoRegistrado.Importe, medio, entrada.TokenDelMedio, correo)).ConfigureAwait(false);

        // Paso 3: registrar el resultado en una transacción corta (con el mismo orden de bloqueo).
        var cierre = await RegistrarElResultado(intentoRegistrado.Id, entrada.PedidoId, resultadoDeLaPasarela, enviadoEn).ConfigureAwait(false);
        switch (cierre.Tipo)
        {
            case TipoDeCierre.Aprobado:
                // Fuera de la transacción: el único punto de entrada del estado aprobado.
                return Resultado<ResultadoDelCobroDelCliente>.Exito(await confirmador.Confirmar(entrada.PedidoId, intentoRegistrado.Id, cierre.Referencia!, CancellationToken.None).ConfigureAwait(false));
            case TipoDeCierre.Rechazado:
                return Resultado<ResultadoDelCobroDelCliente>.Exito(new ResultadoDelCobroDelCliente(EstadoDelCobro.Rechazado, MapeoDeMotivos.APublico(cierre.Motivo), intentoRegistrado.Id));
            default:
                if (cierre.PasoAPendiente)
                {
                    await ExtenderLaReservaSiHaceFalta(entrada.PedidoId).ConfigureAwait(false);
                }

                return Resultado<ResultadoDelCobroDelCliente>.Exito(new ResultadoDelCobroDelCliente(EstadoDelCobro.PendienteDeConfirmacion, null, intentoRegistrado.Id));
        }
    }

    /// <summary>
    /// El resultado de un intento que ya existe. Un cobro aprobado solo es «aprobado» para el cliente si la compra quedó completada; si no (se está confirmando, se compensó o fue un cobro sobrante que se reembolsa) el cliente
    /// ve «estamos confirmando tu pago» y la pantalla de estado dice la verdad.
    /// </summary>
    private static ResultadoDelCobroDelCliente ResultadoOriginal(IntentoDeCobro intento, bool intencionCompletada) => intento.Estado switch
    {
        EstadoDeIntento.Aprobado when intencionCompletada => new ResultadoDelCobroDelCliente(EstadoDelCobro.Aprobado, null, intento.Id),
        EstadoDeIntento.Rechazado => new ResultadoDelCobroDelCliente(EstadoDelCobro.Rechazado, MapeoDeMotivos.APublico(intento.MotivoDeRechazo), intento.Id),
        EstadoDeIntento.Vencido => new ResultadoDelCobroDelCliente(EstadoDelCobro.Rechazado, MotivoDeRechazoDePago.Otro, intento.Id),
        _ => new ResultadoDelCobroDelCliente(EstadoDelCobro.PendienteDeConfirmacion, null, intento.Id),
    };

    private async Task<string?> ObtenerElCorreo(Guid cuentaId, CancellationToken cancelacion)
    {
        try
        {
            return await cuentas.CorreoDelTitular(cuentaId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Llama a la pasarela con el plazo de respuesta (PAG-07): sin respuesta a tiempo, o ante una falla, el resultado es «sin respuesta» y el cobro queda pendiente. La llamada <b>no</b> se enlaza con la cancelación de la
    /// solicitud: si el cliente se desconecta mientras la pasarela cobra, igual se registra lo que respondió; solo la corta su plazo.
    /// </summary>
    private async Task<ResultadoDeCobro> Cobrar(SolicitudDeCobroEnPasarela solicitud)
    {
        using var plazo = new CancellationTokenSource(opciones.TiempoDeRespuesta);
        try
        {
            return await pasarela.Cobrar(solicitud, plazo.Token).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Sin respuesta a tiempo o una falla del adaptador (también una cancelación interna de su cliente HTTP): el cobro queda pendiente y se consulta después.
            return ResultadoDeCobro.SinRespuesta();
        }
    }

    private enum TipoDeCierre
    {
        Aprobado,
        Rechazado,
        Pendiente,
    }

    private sealed record Cierre(TipoDeCierre Tipo, string? Referencia = null, MotivoDeRechazo? Motivo = null, bool PasoAPendiente = false);

    /// <summary>
    /// Registra el resultado en una transacción corta, <b>sin depender de la cancelación de la solicitud</b>. Cualquier falla (auditoría, bloqueo, conexión) tras un cobro que la pasarela pudo aplicar deja el intento <c>iniciado</c>
    /// (la reconciliación lo recoge), publica una alerta técnica y el cliente ve «estamos confirmando tu pago»: nunca «no se cobró».
    /// </summary>
    private async Task<Cierre> RegistrarElResultado(Guid intentoId, Guid pedidoId, ResultadoDeCobro resultado, DateTimeOffset enviadoEn)
    {
        ResultadoValidado? validado = null;
        try
        {
            return await unidad.Ejecutar(
                async token =>
                {
                    var intento = await repositorio.ObtenerIntento(intentoId, token).ConfigureAwait(false);
                    var intencionSinBloqueo = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, token).ConfigureAwait(false);
                    if (intento is null || intencionSinBloqueo is null)
                    {
                        return new Cierre(TipoDeCierre.Pendiente);
                    }

                    // El mismo orden de bloqueo: la marca de cancelación de la cuenta (si la intención aún la tiene) y después la intención.
                    if (intencionSinBloqueo.CuentaId is { } cuentaId)
                    {
                        await cuentas.CancelacionEnEjecucion(cuentaId, bloquear: true, token).ConfigureAwait(false);
                    }

                    var intencion = (await repositorio.ObtenerIntencion(pedidoId, paraActualizar: true, token).ConfigureAwait(false))!;
                    validado = resultado.Validar(intento);
                    intento.MarcarEnviado(enviadoEn);

                    switch (validado.Tipo)
                    {
                        case TipoDeResultadoDeCobro.Aprobado:
                            // No cambia el intento ni la intención: el paso (a) de la confirmación los marca. Si el proceso cae aquí, la reconciliación recoge el intento iniciado.
                            return new Cierre(TipoDeCierre.Aprobado, validado.ReferenciaDePasarela);
                        case TipoDeResultadoDeCobro.Rechazado:
                            if (!intento.MarcarRechazado(validado.Motivo ?? MotivoDeRechazo.Otro, reloj.Ahora))
                            {
                                return new Cierre(TipoDeCierre.Pendiente);
                            }

                            if (intencion.Estado == EstadoDeIntencion.ConCobro)
                            {
                                intencion.ResolverIntentoSinCobro(reloj.Ahora);
                            }

                            await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
                            await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                            await Auditar(intento, "rechazado", (validado.Motivo ?? MotivoDeRechazo.Otro).ATexto(), null, token).ConfigureAwait(false);
                            return new Cierre(TipoDeCierre.Rechazado, Motivo: validado.Motivo ?? MotivoDeRechazo.Otro);
                        default:
                            if (intento.MarcarPendiente(reloj.Ahora) && intencion.Estado == EstadoDeIntencion.ConCobro)
                            {
                                intencion.PasarAPendiente();
                                await repositorio.GuardarIntento(intento, token).ConfigureAwait(false);
                                await repositorio.GuardarIntencion(intencion, token).ConfigureAwait(false);
                                await Auditar(intento, "pendiente", null, validado.Duda, token).ConfigureAwait(false);
                                return new Cierre(TipoDeCierre.Pendiente, PasoAPendiente: true);
                            }

                            return new Cierre(TipoDeCierre.Pendiente);
                    }
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (AuditoriaNoDisponibleException)
        {
            await Alertar(MotivoDeAlertaAuditoriaNoDisponible).ConfigureAwait(false);
            return new Cierre(TipoDeCierre.Pendiente);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Alertar(MotivoDeAlertaRegistroDelCobroFallido).ConfigureAwait(false);
            return new Cierre(TipoDeCierre.Pendiente);
        }
        finally
        {
            if (validado is { EsDudoso: true })
            {
                await Alertar(MotivoDeAlertaCobroDudoso).ConfigureAwait(false);
            }
        }
    }

    private Task Auditar(IntentoDeCobro intento, string resultado, string? motivo, MotivoDeDuda? duda, CancellationToken token)
    {
        var detalle = new Dictionary<string, string?> { ["resultado"] = resultado };
        if (motivo is not null)
        {
            detalle["motivo"] = motivo;
        }

        if (duda is { } d)
        {
            // El aprobado dudoso queda identificable en la auditoría (AM11): referencia, moneda o importe distintos del intento.
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

    private async Task Alertar(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe el cobro.
        }
    }

    /// <summary>Con impresos, el pago pendiente prolonga la reserva una sola vez hasta los 30 minutos desde el inicio (PAG-13), <b>fuera de toda transacción</b>; si falla, la retoma el trabajo de consulta.</summary>
    private async Task ExtenderLaReservaSiHaceFalta(Guid pedidoId)
    {
        try
        {
            var intencion = await unidad.Ejecutar(token => repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, token), CancellationToken.None).ConfigureAwait(false);
            if (intencion is not { TieneImpresos: true, ReservaExtendida: false })
            {
                return;
            }

            var extension = await reservas.Extender(pedidoId, CancellationToken.None).ConfigureAwait(false);
            if (!extension.EsCorrecta || extension.Valor is null)
            {
                return;
            }

            await unidad.Ejecutar(
                async token =>
                {
                    var actual = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: true, token).ConfigureAwait(false);
                    if (actual is not null && actual.ExtenderReserva(extension.Valor.ExpiraEn))
                    {
                        await repositorio.GuardarIntencion(actual, token).ConfigureAwait(false);
                    }

                    return true;
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // El inventario o la base no respondieron: la reserva se extiende en la siguiente consulta del trabajo.
        }
    }
}

/// <summary>Traduce el motivo de rechazo del Dominio al público (mismos nombres, lo comprueba una prueba).</summary>
internal static class MapeoDeMotivos
{
    public static MotivoDeRechazoDePago APublico(MotivoDeRechazo? motivo) =>
        motivo is { } m ? Enum.Parse<MotivoDeRechazoDePago>(m.ToString()) : MotivoDeRechazoDePago.Otro;
}
