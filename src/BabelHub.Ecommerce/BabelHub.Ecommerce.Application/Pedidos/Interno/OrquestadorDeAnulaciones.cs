using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>Lo que dejó una anulación o cancelación aplicada: el pedido, su estado, lo reembolsado y si el reembolso sigue pendiente (PED-24).</summary>
internal sealed record AnulacionAplicada(Guid PedidoId, EstadoDePedido Estado, decimal Monto, bool ReembolsoPendiente);

/// <summary>
/// El motor de tres tiempos de la cancelación del cliente y la anulación de la administración (T19, T20; D03, D04, D10, D21): <b>sin transacción</b>, porque libera en el Inventario y reembolsa, y eso no se revierte. Lo comparten <c>CancelarPedidoDelCliente</c>, <c>AnularPedido</c> y la reanudación (T22).
/// <list type="number">
/// <item><b>Tiempo 1</b> (<see cref="Iniciar"/>, una transacción): pedido con bloqueo y alcance, ninguna operación en curso, el plan (un rechazo se audita y se explica con su alternativa), <b>la cancelación de los envíos que no salieron</b> (los mantenidos dejan fuera sus líneas; un <c>Fallo</c> o una excepción de Envíos revierte todo), la auditoría «iniciada» y la fila <c>en_curso</c>.</item>
/// <item><b>Tiempo 2</b> (<see cref="Continuar"/>, fuera de transacción): <c>liberar</c> <b>una línea por llamada</b>, solo las que no salieron; un 409 deja la línea fuera y recalcula el plan (el envío y el monto); el paso se marca justo después (2b).</item>
/// <item><b>Tiempo 3</b> (una transacción): <b>registra el reembolso antes de revocar los accesos</b> (orden de bloqueo), anula las líneas, completa la fila, audita sin datos personales y avisa; <b>después de confirmar</b>, procesa el reembolso con la pasarela (un fallo lo deja pendiente y visible, el pedido sigue anulado).</item>
/// </list>
/// Si un tiempo falla, la fila sobrevive y la reanudación repite solo lo local y <c>liberar</c> (nunca <c>cerrar</c>).
/// </summary>
internal sealed class OrquestadorDeAnulaciones(
    AutorizadorDePedidos autorizador,
    ControlesDeGestionDePedidos controles,
    IRepositorioDePedidos pedidos,
    IUnidadDeTrabajo unidad,
    IDespachoDeEnvios despacho,
    IReservasDelPedido reservas,
    IReembolsos reembolsos,
    IAccesosDeBiblioteca accesos,
    IAuditoria auditoria,
    INotificador notificador,
    IPublicadorDeAlertasTecnicas alertas,
    IGeneradorDeIdentificadoresDePedidos identificadores,
    IReloj reloj)
{
    public const string MotivoDeLiberacionDeLaAdministracion = "pedido-anulado";
    public const string AlertaLiberacionYaCerrada = "pedido-liberacion-de-anulacion-ya-cerrada";

    public static string ClaveDelReembolso(OrigenDeLaAnulacion origen, Guid pedidoId) =>
        string.Concat(origen == OrigenDeLaAnulacion.Cliente ? "canc:" : "anul:", pedidoId.ToString("D"));

    /// <summary>El tiempo 1: planea, cancela los envíos que no salieron, audita y deja la fila en curso.</summary>
    public async Task<Resultado<Anulacion>> Iniciar(SolicitanteDePedidos solicitante, Guid pedidoId, OrigenDeLaAnulacion origen, MotivoDeAnulacion motivo, CancellationToken cancelacion)
    {
        try
        {
            return await unidad.Ejecutar(token => IniciarEnTransaccion(solicitante, pedidoId, origen, motivo, token), cancelacion).ConfigureAwait(false);
        }
        catch (OperacionRechazadaException rechazo)
        {
            return Resultado<Anulacion>.Fallo(rechazo.Error);
        }
        catch (EsperaDeBloqueoDeUnPedidoAgotadaException)
        {
            return Resultado<Anulacion>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.Conflicto));
        }
        catch (AuditoriaNoDisponibleException)
        {
            // Sin auditoría no hay fila ni llamada al Inventario.
            return Resultado<Anulacion>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }
    }

    private async Task<Resultado<Anulacion>> IniciarEnTransaccion(SolicitanteDePedidos solicitante, Guid pedidoId, OrigenDeLaAnulacion origen, MotivoDeAnulacion motivo, CancellationToken cancelacion)
    {
        var pedido = await pedidos.Obtener(pedidoId, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (await autorizador.ComprobarAlcance(pedido, solicitante, cancelacion).ConfigureAwait(false) is { } ajeno)
        {
            return Resultado<Anulacion>.Fallo(ajeno);
        }

        // La primera operación que confirma gana: cualquier anulación o entrega en curso del pedido bloquea esta.
        if (await controles.ExigirSinOperacionEnCurso(pedido!.Id, null, cancelacion).ConfigureAwait(false) is { } enCurso)
        {
            return Resultado<Anulacion>.Fallo(enCurso);
        }

        var plan = Planear(pedido, origen, motivo, null);
        if (plan.Rechazo is { } rechazo)
        {
            return await Rechazar(pedido, solicitante, origen, rechazo, cancelacion).ConfigureAwait(false);
        }

        // Los envíos que no salieron se cancelan en el tiempo 1; los mantenidos dejan fuera sus líneas y el plan se recalcula sin ellas, sin el envío y sin las digitales.
        if (origen == OrigenDeLaAnulacion.Administracion && pedido.Tipo == TipoDeEntregaDelPedido.Domicilio)
        {
            var mantenidas = await CancelarLosEnvios(pedido.Id, cancelacion).ConfigureAwait(false);

            // Repetir una anulación fallida: Envíos informa «mantenido» también un envío ya cancelado; las líneas de una fila fallida anterior ya pasaron por la cancelación de envíos, así que no salieron.
            var yaCanceladas = (await pedidos.AnulacionesFallidas(pedido.Id, cancelacion).ConfigureAwait(false)).SelectMany(a => a.LineaIds).ToHashSet();
            mantenidas = mantenidas.Where(l => !yaCanceladas.Contains(l)).ToHashSet();
            if (mantenidas.Count > 0)
            {
                plan = Planear(pedido, origen, motivo, mantenidas);
                if (plan.Rechazo is { } sinLineas)
                {
                    return await Rechazar(pedido, solicitante, origen, sinLineas, cancelacion).ConfigureAwait(false);
                }
            }
        }

        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.OperacionDePedidoIniciada)
            {
                ObjetoTipo = "pedido",
                ObjetoId = pedido.Id.ToString("D"),
                Detalle = DetalleDelIniciador(solicitante, new Dictionary<string, string?> { ["operacion"] = origen == OrigenDeLaAnulacion.Cliente ? "cancelacion" : "anulacion" }),
            },
            cancelacion).ConfigureAwait(false);

        var anulacion = Anulacion.Iniciar(identificadores.Nuevo(), pedido.Id, plan.Plan!, new IniciadorDeOperacion(solicitante.UsuarioId, solicitante.Rol, solicitante.SucursalId), reloj.Ahora);
        anulacion.MarcarPaso(PasoDeAnulacion.EnviosCancelados, reloj.Ahora);
        await pedidos.AgregarAnulacion(anulacion, cancelacion).ConfigureAwait(false);
        return Resultado<Anulacion>.Exito(anulacion);
    }

    private static ResultadoDePlanDeAnulacion Planear(Pedido pedido, OrigenDeLaAnulacion origen, MotivoDeAnulacion motivo, IReadOnlySet<Guid>? lineasQueSalieron) =>
        origen == OrigenDeLaAnulacion.Cliente ? pedido.PlanearCancelacionDelCliente() : pedido.PlanearAnulacionDeLaAdministracion(motivo, lineasQueSalieron);

    /// <summary>Cancela los envíos del pedido que no salieron y devuelve las líneas de los que se mantienen. Un <c>Fallo</c> o una excepción de Envíos revierte el tiempo 1 entero.</summary>
    private async Task<IReadOnlySet<Guid>> CancelarLosEnvios(Guid pedidoId, CancellationToken cancelacion)
    {
        Resultado<IReadOnlyList<ResultadoDeCancelarEnvio>> cancelados;
        try
        {
            cancelados = await despacho.Cancelar(pedidoId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not (OperationCanceledException or OperacionRechazadaException))
        {
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        if (!cancelados.EsExito)
        {
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        return cancelados.Valor.Where(e => e.Destino == DestinoDelEnvioAlCancelar.Mantenido).SelectMany(e => e.LineaIds).ToHashSet();
    }

    /// <summary>
    /// Un rechazo se audita con la acción propia <c>AnulacionDePedidoRechazada</c> (la operación, el motivo de rechazo cerrado, el estado y el iniciador; sin datos personales de entrega) y se explica con su alternativa en el error (PED-18, PED-23).
    /// </summary>
    private async Task<Resultado<Anulacion>> Rechazar(Pedido pedido, SolicitanteDePedidos solicitante, OrigenDeLaAnulacion origen, RechazoDeAnulacion rechazo, CancellationToken cancelacion)
    {
        var codigo = rechazo switch
        {
            RechazoDeAnulacion.MotivoNoCorresponde => "motivo-no-corresponde",
            RechazoDeAnulacion.PedidoYaAnulado => "pedido-ya-anulado",
            RechazoDeAnulacion.NoQuedaImpresaPorSalir => "sin-impresas-por-salir",
            RechazoDeAnulacion.IncluyeDigital => "incluye-digital",
            _ => "impreso-en-camino",
        };
        var detalle = DetalleDelIniciador(solicitante, new Dictionary<string, string?> { ["operacion"] = origen == OrigenDeLaAnulacion.Cliente ? "cancelacion" : "anulacion", ["motivoDeRechazo"] = codigo, ["estadoAnterior"] = AvisosDePedidos.NombreDelEstado(pedido.Estado) });
        await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.AnulacionDePedidoRechazada) { ObjetoTipo = "pedido", ObjetoId = pedido.Id.ToString("D"), Detalle = detalle, Resultado = ResultadoAuditado.Rechazado }, cancelacion).ConfigureAwait(false);

        return Resultado<Anulacion>.Fallo(rechazo switch
        {
            RechazoDeAnulacion.MotivoNoCorresponde => new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDePedidos.ValidacionFallida, [new ErrorDeCampo("motivo", "invalido")]),
            RechazoDeAnulacion.PedidoYaAnulado => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.EstadoInvalido),
            RechazoDeAnulacion.NoQuedaImpresaPorSalir => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.SinLineasAnulables, [new ErrorDeCampo("alternativa", "devolucion")]),
            RechazoDeAnulacion.IncluyeDigital => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.NoCancelable, [new ErrorDeCampo("alternativa", "politica-de-reembolso-digital")]),
            _ => new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.NoCancelable, [new ErrorDeCampo("alternativa", "devolucion")]),
        });
    }

    /// <summary>
    /// Los tiempos 2 y 3 de una fila en curso. <paramref name="reanudacion"/> indica que la ejecuta el trabajo de «sistema»: la compleción se audita como «sistema» con el iniciador en el detalle <c>iniciadaPor</c> y solo se repite lo local y <c>liberar</c>.
    /// </summary>
    public async Task<Resultado<AnulacionAplicada>> Continuar(Anulacion anulacion, bool reanudacion, CancellationToken cancelacion)
    {
        var liberadas = await LiberarLasReservas(anulacion, reanudacion, cancelacion).ConfigureAwait(false);
        if (liberadas is not null)
        {
            return Resultado<AnulacionAplicada>.Fallo(liberadas);
        }

        Resultado<AnulacionAplicada> aplicada;
        try
        {
            aplicada = await unidad.Ejecutar(token => Completar(anulacion, reanudacion, token), cancelacion).ConfigureAwait(false);
        }
        catch (OperacionRechazadaException rechazo)
        {
            return Resultado<AnulacionAplicada>.Fallo(rechazo.Error);
        }
        catch (Exception excepcion) when (excepcion is AuditoriaNoDisponibleException or EsperaDeBloqueoDeUnPedidoAgotadaException)
        {
            // El tiempo 3 se revirtió: la fila sigue en curso y la reanudación repite solo lo local.
            return Resultado<AnulacionAplicada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        if (!aplicada.EsExito || anulacion.ReembolsoId is not { } reembolsoId)
        {
            return aplicada;
        }

        // Ya confirmado: el reembolso se procesa con la pasarela, fuera de transacción. Si falla, el pedido sigue anulado con el reembolso pendiente.
        var pendiente = !await ProcesarElReembolso(reembolsoId, cancelacion).ConfigureAwait(false);
        return Resultado<AnulacionAplicada>.Exito(aplicada.Valor with { ReembolsoPendiente = pendiente });
    }

    /// <summary>El tiempo 2: <c>liberar</c> una línea por llamada con el motivo de la operación, solo las impresas que no salieron; el paso se marca al terminar.</summary>
    private async Task<ErrorDeAplicacion?> LiberarLasReservas(Anulacion anulacion, bool reanudacion, CancellationToken cancelacion)
    {
        if (anulacion.PasoConfirmadoEn(PasoDeAnulacion.ReservasLiberadas) is not null)
        {
            return null;
        }

        var pedido = await pedidos.Obtener(anulacion.PedidoId, conBloqueo: false, cancelacion).ConfigureAwait(false);
        if (pedido is null)
        {
            return new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDePedidos.NoEncontrado);
        }

        var motivo = anulacion.Origen == OrigenDeLaAnulacion.Cliente ? MotivosDeAnulacion.Codigo(MotivoDeAnulacion.CanceladoPorElCliente) : MotivoDeLiberacionDeLaAdministracion;
        var impresas = pedido.Lineas.Where(l => !l.EsDigital && anulacion.LineaIds.Contains(l.Id)).Select(l => l.Id).ToList();
        var salieron = new HashSet<Guid>();
        var sinRespuesta = false;
        foreach (var lineaId in impresas)
        {
            var resultado = await Liberar(anulacion.PedidoId, motivo, lineaId, cancelacion).ConfigureAwait(false);
            if (resultado == ResultadoDeLiberarReserva.YaCerrada)
            {
                salieron.Add(lineaId);
            }
            else if (resultado == ResultadoDeLiberarReserva.NoSePudo)
            {
                sinRespuesta = true;
            }
        }

        if (sinRespuesta)
        {
            // La reanudación anota ella misma el intento (una vez por pasada); la solicitud original lo anota aquí.
            if (!reanudacion)
            {
                await unidad.Ejecutar(
                    async token =>
                    {
                        anulacion.RegistrarIntento(reloj.Ahora);
                        await pedidos.ActualizarAnulacion(anulacion, token).ConfigureAwait(false);
                        return true;
                    },
                    cancelacion).ConfigureAwait(false);
            }

            return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible);
        }

        if (salieron.Count > 0)
        {
            // Con el plan ajustado se vuelve a liberar solo lo que queda (cada pasada deja fuera al menos una línea: termina).
            return await AjustarPorLasQueSalieron(anulacion, salieron, cancelacion).ConfigureAwait(false)
                ?? await LiberarLasReservas(anulacion, reanudacion, cancelacion).ConfigureAwait(false);
        }

        // 2b: el paso se marca justo tras la última respuesta del Inventario.
        await unidad.Ejecutar(
            async token =>
            {
                anulacion.MarcarPaso(PasoDeAnulacion.ReservasLiberadas, reloj.Ahora);
                await pedidos.ActualizarAnulacion(anulacion, token).ConfigureAwait(false);
                return true;
            },
            cancelacion).ConfigureAwait(false);
        return null;
    }

    private async Task<ResultadoDeLiberarReserva> Liberar(Guid pedidoId, string motivo, Guid lineaId, CancellationToken cancelacion)
    {
        try
        {
            return await reservas.Liberar(pedidoId, motivo, [lineaId], cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeLiberarReserva.NoSePudo;
        }
    }

    /// <summary>
    /// Un 409 de <c>liberar</c> revela que una línea ya salió (una entrega o un envío ganó): queda fuera y se recalcula el plan (el envío, el monto y las digitales, D10) bajo el bloqueo del pedido; sin líneas la operación queda descartada. La cancelación del cliente no tiene plan alternativo (todas sus líneas están
    /// confirmadas): queda fallida con una alerta técnica. Devuelve <see langword="null"/> si el plan quedó ajustado y la operación sigue en curso (se vuelve a liberar lo que queda).
    /// </summary>
    private async Task<ErrorDeAplicacion?> AjustarPorLasQueSalieron(Anulacion anulacion, HashSet<Guid> salieron, CancellationToken cancelacion)
    {
        await PublicarAlerta(AlertaLiberacionYaCerrada, cancelacion).ConfigureAwait(false);
        try
        {
            return await unidad.Ejecutar(
                async token =>
                {
                    var pedido = await pedidos.Obtener(anulacion.PedidoId, conBloqueo: true, token).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("El pedido de una anulación en curso no existe.");
                    if (anulacion.Origen == OrigenDeLaAnulacion.Cliente)
                    {
                        anulacion.Fallar(reloj.Ahora);
                        await pedidos.ActualizarAnulacion(anulacion, token).ConfigureAwait(false);
                        return new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.EstadoInvalido);
                    }

                    // Las líneas que salieron: las del 409 y las que el plan dejó fuera antes (envíos mantenidos) y que el pedido aún cuenta como por salir.
                    var fuera = pedido.Lineas.Where(l => !l.EsDigital && !anulacion.LineaIds.Contains(l.Id) && l.EstadoImpreso is { } e && e.SeAnula()).Select(l => l.Id).Concat(salieron).ToHashSet();
                    var plan = pedido.PlanearAnulacionDeLaAdministracion(anulacion.Motivo, fuera);
                    anulacion.AjustarAlPlan(plan.Plan, reloj.Ahora);
                    await pedidos.ActualizarAnulacion(anulacion, token).ConfigureAwait(false);
                    return anulacion.Estado == EstadoDeOperacion.Descartada
                        ? new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.SinLineasAnulables)
                        : null;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoDeUnPedidoAgotadaException)
        {
            return new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.Conflicto);
        }
    }

    /// <summary>El tiempo 3: reembolso registrado antes de revocar los accesos, líneas anuladas, fila completada, auditoría y aviso, todo en una transacción.</summary>
    private async Task<Resultado<AnulacionAplicada>> Completar(Anulacion anulacion, bool reanudacion, CancellationToken cancelacion)
    {
        var pedido = await pedidos.Obtener(anulacion.PedidoId, conBloqueo: true, cancelacion).ConfigureAwait(false)
            ?? throw new InvalidOperationException("El pedido de una anulación en curso no existe.");

        // Se revalida bajo el bloqueo: si otra instancia o la reanudación ya resolvió la fila, no se aplica nada dos veces.
        if ((await pedidos.AnulacionesEnCurso(pedido.Id, cancelacion).ConfigureAwait(false)).All(a => a.Id != anulacion.Id))
        {
            return Resultado<AnulacionAplicada>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.OperacionYaResuelta));
        }

        var ahora = reloj.Ahora;
        var anterior = pedido.Estado;
        var lineas = pedido.Lineas.Where(l => anulacion.LineaIds.Contains(l.Id)).ToList();

        // 1. El reembolso se registra primero (clave natural única por pedido: repetir no reembolsa dos veces). Una digital que Devoluciones ya reembolsó (su acceso se revocó por reembolso) no se reembolsa otra vez: el tope de Pagos dejaría la anulación fallida.
        var aReembolsar = new List<LineaDePedido>();
        foreach (var linea in lineas)
        {
            if (!linea.EsDigital || !await YaReembolsadaPorDevolucion(pedido, linea, cancelacion).ConfigureAwait(false))
            {
                aReembolsar.Add(linea);
            }
        }

        var registrado = await RegistrarElReembolso(pedido, anulacion, aReembolsar, cancelacion).ConfigureAwait(false);
        anulacion.RegistrarReembolso(registrado, ahora);

        // 2. Los accesos digitales se revocan aunque se hayan descargado.
        foreach (var digital in lineas.Where(l => l.EsDigital))
        {
            await RevocarElAcceso(pedido.Id, digital.EdicionId, cancelacion).ConfigureAwait(false);
        }

        if (anulacion.HayDigitales)
        {
            anulacion.MarcarPaso(PasoDeAnulacion.AccesosRevocados, ahora);
        }

        // 3. Las líneas se anulan y el pedido se recalcula.
        var plan = new PlanDeAnulacion(
            anulacion.Origen,
            anulacion.Motivo,
            [.. lineas.Where(l => !l.EsDigital).Select(l => l.Id)],
            [.. lineas.Where(l => l.EsDigital).Select(l => l.Id)],
            anulacion.IncluyeEnvio,
            anulacion.Monto,
            []);
        pedido.AplicarAnulacion(plan, ahora);
        await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);
        if (!anulacion.Completar(ahora))
        {
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        await pedidos.ActualizarAnulacion(anulacion, cancelacion).ConfigureAwait(false);
        await AuditarLaCompleccion(pedido, anulacion, anterior, reanudacion, cancelacion).ConfigureAwait(false);
        await new AvisosDePedidos(notificador).Solicitar(pedido, "anulado", anulacion.Id.ToString("N"), AvisosDePedidos.SucursalesResponsables(pedido, anulacion.LineaIds), cancelacion).ConfigureAwait(false);
        return Resultado<AnulacionAplicada>.Exito(new AnulacionAplicada(pedido.Id, pedido.Estado, anulacion.Monto, ReembolsoPendiente: false));
    }

    private async Task<Guid> RegistrarElReembolso(Pedido pedido, Anulacion anulacion, IReadOnlyList<LineaDePedido> lineas, CancellationToken cancelacion)
    {
        Resultado<ReembolsoRegistrado> registrado;
        try
        {
            registrado = await reembolsos.Registrar(
                new RegistrarReembolsoEntrada(
                    pedido.Id,
                    anulacion.Origen == OrigenDeLaAnulacion.Cliente ? OrigenDelReembolso.CancelacionDelCliente : OrigenDelReembolso.Anulacion,
                    ClaveDelReembolso(anulacion.Origen, pedido.Id),
                    [.. lineas.Select(l => new LineaAReembolsar(l.Id, l.Importe))],
                    anulacion.IncluyeEnvio ? pedido.CostoDeEnvio : 0m),
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not (OperationCanceledException or OperacionRechazadaException))
        {
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        if (!registrado.EsExito)
        {
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        return registrado.Valor.ReembolsoId;
    }

    private async Task<bool> YaReembolsadaPorDevolucion(Pedido pedido, LineaDePedido digital, CancellationToken cancelacion)
    {
        Resultado<bool> revocado;
        try
        {
            revocado = await accesos.EstaRevocadoPorReembolso(pedido.CuentaId, pedido.Id, digital.EdicionId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not (OperationCanceledException or OperacionRechazadaException))
        {
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        // Falla cerrado: sin saberlo no se reembolsa (el reintento de la operación en curso lo vuelve a preguntar).
        return revocado.EsExito ? revocado.Valor : throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
    }

    private async Task RevocarElAcceso(Guid pedidoId, Guid edicionId, CancellationToken cancelacion)
    {
        Resultado<ResultadoDeRevocarAcceso> revocado;
        try
        {
            revocado = await accesos.Revocar(new RevocarAccesoEntrada(pedidoId, edicionId, MotivoDeRevocacionDeAcceso.PedidoAnulado), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not (OperationCanceledException or OperacionRechazadaException))
        {
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        if (!revocado.EsExito)
        {
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }
    }

    /// <summary>
    /// La compleción, sin datos personales y solo con las claves que declara el catálogo: la acción propia de la operación (<c>PedidoCanceladoPorElCliente</c> o <c>PedidoAnulado</c> con la causa = el motivo cerrado) y, si el estado del pedido cambió o la ejecuta la reanudación, <c>EstadoDePedidoCambiado</c>. Una reanudación se audita como
    /// «sistema» y nombra al iniciador guardado en la fila en <c>iniciadaPor</c> (el catálogo aún no declara su rol ni su sucursal: hallazgo para el carril de auditoría).
    /// </summary>
    private async Task AuditarLaCompleccion(Pedido pedido, Anulacion anulacion, EstadoDePedido anterior, bool reanudacion, CancellationToken cancelacion)
    {
        var anteriorNombre = AvisosDePedidos.NombreDelEstado(anterior);

        // Una reanudación se audita como «sistema» y nombra al iniciador guardado en la fila (identificador, rol y sucursal); una solicitud directa ya lleva su actor.
        Dictionary<string, string?> ConElIniciador(Dictionary<string, string?> detalle)
        {
            if (reanudacion && !anulacion.Iniciador.EstaAnonimizado)
            {
                detalle["iniciadaPor"] = anulacion.Iniciador.UsuarioId.ToString("D");
                detalle["rolDelIniciador"] = anulacion.Iniciador.Rol;
                detalle["sucursalDelIniciador"] = anulacion.Iniciador.SucursalId?.ToString("D");
            }

            return detalle;
        }

        var propia = anulacion.Origen == OrigenDeLaAnulacion.Cliente
            ? new SolicitudDeRegistro(AccionesAuditables.PedidoCanceladoPorElCliente) { Detalle = ConElIniciador(new() { ["estadoAnterior"] = anteriorNombre }) }
            : new SolicitudDeRegistro(AccionesAuditables.PedidoAnulado) { Detalle = ConElIniciador(new() { ["estadoAnterior"] = anteriorNombre, ["causa"] = MotivosDeAnulacion.Codigo(anulacion.Motivo) }) };
        await auditoria.Registrar(propia with { ObjetoTipo = "pedido", ObjetoId = pedido.Id.ToString("D") }, cancelacion).ConfigureAwait(false);

        if (pedido.Estado != anterior || reanudacion)
        {
            var detalle = ConElIniciador(new() { ["estadoAnterior"] = anteriorNombre, ["estadoNuevo"] = AvisosDePedidos.NombreDelEstado(pedido.Estado) });
            await auditoria.Registrar(new SolicitudDeRegistro(AccionesAuditables.EstadoDePedidoCambiado) { ObjetoTipo = "pedido", ObjetoId = pedido.Id.ToString("D"), Detalle = detalle }, cancelacion).ConfigureAwait(false);
        }
    }

    /// <summary>El actor de la solicitud en el detalle: su identificador, su rol y su sucursal (las claves personales las anonimiza la depuración).</summary>
    private static Dictionary<string, string?> DetalleDelIniciador(SolicitanteDePedidos solicitante, Dictionary<string, string?> detalle)
    {
        detalle["iniciadaPor"] = solicitante.UsuarioId.ToString("D");
        detalle["rolDelIniciador"] = solicitante.Rol;
        detalle["sucursalDelIniciador"] = solicitante.SucursalId?.ToString("D");
        return detalle;
    }

    /// <summary>Procesa el reembolso con la pasarela: verdadero si quedó reembolsado; cualquier otra cosa (o una excepción) lo deja pendiente y visible.</summary>
    private async Task<bool> ProcesarElReembolso(Guid reembolsoId, CancellationToken cancelacion)
    {
        try
        {
            var procesado = await reembolsos.Procesar(reembolsoId, cancelacion).ConfigureAwait(false);
            return procesado.EsExito && procesado.Valor == EstadoDelReembolso.Reembolsado;
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return false;
        }
    }

    private async Task PublicarAlerta(string motivo, CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Una alerta que no se publica nunca interrumpe la operación.
        }
    }
}
