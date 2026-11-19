using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// El orquestador de la entrega de un pedido de recojo (T16; PED-11, PED-25, PED-29; AC-PED-09): <b>tres tiempos</b>, porque el Inventario no es revertible y no se llama dentro de una transacción. <b>Tiempo 1</b> (transacción corta): bloquea el pedido, valida el alcance, las líneas y que ninguna otra operación las tenga en curso, <b>audita la operación
/// como «iniciada»</b> con el actor de la solicitud (si no se puede auditar, no hay fila ni llamada al Inventario) y deja la fila <c>entregas</c> <c>en_curso</c> con la clave de cierre de cada línea. <b>Tiempo 2</b> (fuera de toda transacción): <c>cerrar</c> <b>una línea por llamada</b> con su clave (repetirla no descuenta dos veces), y justo tras
/// cada respuesta una transacción mínima marca <b>el cierre confirmado</b>. <b>Tiempo 3</b> (transacción corta): vuelve a bloquear y revalida, pasa a <b>entregadas con su fecha</b> solo las líneas con cierre exitoso, audita cada una y el cambio de estado, completa la fila y avisa una vez. Si el Inventario no responde o rechaza, la línea
/// sigue como estaba, la fila queda fallida (si ninguna línea llegó a cerrarse) y el encargado reintenta con la misma clave; con un cierre parcial la fila sigue en curso para que la reanudación aplique lo local. Una línea que ya salió por Envíos (<c>INV-RESERVA-ESTADO-INVALIDO</c>) queda fuera. Si el tiempo 3 falla (por ejemplo, la
/// auditoría), se revierte y la fila sobrevive con el cierre confirmado: <c>CompletarOperacionesEnCurso</c> repite solo lo local.
/// </summary>
internal sealed class EntregarLineasDelPedido(
    AutorizadorDePedidos autorizador,
    ControlesDeGestionDePedidos controles,
    IRepositorioDePedidos pedidos,
    IUnidadDeTrabajo unidad,
    IReservasDelPedido reservas,
    IAuditoria auditoria,
    INotificador notificador,
    IGeneradorDeIdentificadoresDePedidos identificadores,
    IReloj reloj)
{
    public async Task<Resultado<LineasOperadas>> Entregar(string codigoDeSucursal, OperarLineasEntrada entrada, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var quien = await autorizador.ExigirRol(OperacionDePedidos.GestionDeRecojo, cancelacion).ConfigureAwait(false);
        if (!quien.EsExito)
        {
            return Resultado<LineasOperadas>.Fallo(quien.Error!);
        }

        if (await autorizador.ExigirSucursalDeLaRuta(quien.Valor, codigoDeSucursal ?? string.Empty, cancelacion).ConfigureAwait(false) is { } fueraDeSucursal)
        {
            return Resultado<LineasOperadas>.Fallo(fueraDeSucursal);
        }

        if (await controles.LimiteDeGestion(quien.Valor, cancelacion).ConfigureAwait(false) is { } limite)
        {
            return Resultado<LineasOperadas>.Fallo(limite);
        }

        var lineaIds = (entrada.LineaIds ?? []).Distinct().ToList();
        if (lineaIds.Count is 0 or > OperarLineasDelPedido.MaximoDeLineasPorOperacion)
        {
            return Resultado<LineasOperadas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDePedidos.ValidacionFallida, [new ErrorDeCampo("lineaIds", lineaIds.Count == 0 ? "obligatorio" : "demasiado-largo")]));
        }

        // Tiempo 1.
        Entrega entrega;
        try
        {
            var inicio = await unidad.Ejecutar(token => IniciarLaEntrega(quien.Valor, entrada.PedidoId, lineaIds, token), cancelacion).ConfigureAwait(false);
            if (!inicio.EsExito)
            {
                return Resultado<LineasOperadas>.Fallo(inicio.Error!);
            }

            entrega = inicio.Valor;
        }
        catch (EsperaDeBloqueoDeUnPedidoAgotadaException)
        {
            return Resultado<LineasOperadas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.Conflicto));
        }
        catch (AuditoriaNoDisponibleException)
        {
            // Sin auditoría no hay fila ni llamada al Inventario.
            return Resultado<LineasOperadas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        // Tiempo 2 y 2b.
        var fallo = await CerrarCadaLinea(entrega, cancelacion).ConfigureAwait(false);
        if (fallo is not null)
        {
            return Resultado<LineasOperadas>.Fallo(fallo);
        }

        // Tiempo 3.
        try
        {
            return await unidad.Ejecutar(token => CompletarLaEntrega(entrega, token), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is AuditoriaNoDisponibleException or EsperaDeBloqueoDeUnPedidoAgotadaException)
        {
            // El tiempo 3 se revirtió: la fila sigue en curso con el cierre confirmado y la reanudación repite solo lo local.
            return Resultado<LineasOperadas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }
    }

    /// <summary>
    /// La reanudación (T22): repite <b>solo el tiempo 3</b> de una entrega en curso (nunca <c>cerrar</c>) para las líneas con el cierre ya confirmado. La ejecuta el trabajo de «sistema», así que la auditoría la registra el sistema con el iniciador guardado en la fila en <c>iniciadaPor</c> (nunca se reconstruye un actor de persona).
    /// Cualquier falla (auditoría, bloqueo u otra) deja la fila en curso para reintentarla; si otra instancia ya la resolvió, lo dice para no tocarla.
    /// </summary>
    public async Task<ResultadoDeReanudarEntrega> Reanudar(Entrega entrega, CancellationToken cancelacion)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        try
        {
            var resultado = await unidad.Ejecutar(token => CompletarLaEntrega(entrega, token), cancelacion).ConfigureAwait(false);
            return resultado.EsExito ? ResultadoDeReanudarEntrega.Aplicada : resultado.Error!.Clave == ClavesDePedidos.OperacionYaResuelta ? ResultadoDeReanudarEntrega.YaResuelta : ResultadoDeReanudarEntrega.Reintentar;
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeReanudarEntrega.Reintentar;
        }
    }

    private async Task<Resultado<Entrega>> IniciarLaEntrega(SolicitanteDePedidos solicitante, Guid pedidoId, IReadOnlyList<Guid> lineaIds, CancellationToken cancelacion)
    {
        var pedido = await pedidos.Obtener(pedidoId, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (await autorizador.ComprobarAlcance(pedido, solicitante, cancelacion).ConfigureAwait(false) is { } ajeno)
        {
            return Resultado<Entrega>.Fallo(ajeno);
        }

        if (await autorizador.ComprobarLineas(pedido!, lineaIds, cancelacion).ConfigureAwait(false) is { } lineaAjena)
        {
            return Resultado<Entrega>.Fallo(lineaAjena);
        }

        if (await controles.ExigirSinOperacionEnCurso(pedido!.Id, lineaIds, cancelacion).ConfigureAwait(false) is { } enCurso)
        {
            return Resultado<Entrega>.Fallo(enCurso);
        }

        // Se entrega lo que está listo para recojo (o no recogido): cualquier otro estado es inválido y no se llama al Inventario.
        if (pedido.Lineas.Where(l => lineaIds.Contains(l.Id)).Any(l => l.EsDigital || l.EstadoImpreso is not (EstadoDeLineaImpresa.ListoParaRecojo or EstadoDeLineaImpresa.NoRecogido)))
        {
            return Resultado<Entrega>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.EstadoInvalido));
        }

        var iniciador = new IniciadorDeOperacion(solicitante.UsuarioId, solicitante.Rol, solicitante.SucursalId);
        await auditoria.Registrar(
            new SolicitudDeRegistro(AccionesAuditables.OperacionDePedidoIniciada)
            {
                ObjetoTipo = "pedido",
                ObjetoId = pedido.Id.ToString("D"),
                Detalle = new Dictionary<string, string?> { ["operacion"] = "entrega", ["iniciadaPor"] = solicitante.UsuarioId.ToString("D") },
            },
            cancelacion).ConfigureAwait(false);

        var entrega = Entrega.Iniciar(identificadores.Nuevo(), pedido.Id, lineaIds, iniciador, reloj.Ahora);
        await pedidos.AgregarEntrega(entrega, cancelacion).ConfigureAwait(false);
        return Resultado<Entrega>.Exito(entrega);
    }

    /// <summary>El contrato dice que una falla del Inventario nunca es una excepción; si un adaptador la lanza igual, se trata como un resultado incierto (reintentable con la misma clave).</summary>
    private async Task<ResultadoDeCerrarReserva> Cerrar(Guid pedidoId, LineaDeEntrega linea, CancellationToken cancelacion)
    {
        try
        {
            return await reservas.Cerrar(pedidoId, linea.LineaId, linea.ClaveDeCierre, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeCerrarReserva.Incierto;
        }
    }

    private async Task<ErrorDeAplicacion?> CerrarCadaLinea(Entrega entrega, CancellationToken cancelacion)
    {
        foreach (var linea in entrega.Lineas.ToList())
        {
            var resultado = await Cerrar(entrega.PedidoId, linea, cancelacion).ConfigureAwait(false);
            var confirmado = resultado switch
            {
                ResultadoDeCerrarReserva.Cerrada => ResultadoDelCierre.Cerrada,
                ResultadoDeCerrarReserva.YaCerrada => ResultadoDelCierre.YaCerrada,
                ResultadoDeCerrarReserva.YaSalio => ResultadoDelCierre.YaSalio,
                _ => (ResultadoDelCierre?)null,
            };
            if (confirmado is null)
            {
                // Sin respuesta o rechazado: la línea no cambia. Si ninguna llegó a cerrarse la fila queda fallida (libera el bloqueo por línea) y el encargado reintenta con la misma clave.
                await unidad.Ejecutar(
                    async token =>
                    {
                        if (entrega.LineasCerradas.Count == 0)
                        {
                            entrega.Fallar(reloj.Ahora);
                        }
                        else
                        {
                            entrega.RegistrarIntento(reloj.Ahora);
                        }

                        await pedidos.ActualizarEntrega(entrega, token).ConfigureAwait(false);
                        return true;
                    },
                    cancelacion).ConfigureAwait(false);
                return new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible);
            }

            // 2b: el paso se marca justo tras la respuesta del Inventario.
            await unidad.Ejecutar(
                async token =>
                {
                    entrega.ConfirmarCierre(linea.LineaId, confirmado.Value, reloj.Ahora);
                    await pedidos.ActualizarEntrega(entrega, token).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }

        return null;
    }

    private async Task<Resultado<LineasOperadas>> CompletarLaEntrega(Entrega entrega, CancellationToken cancelacion)
    {
        var pedido = await pedidos.Obtener(entrega.PedidoId, conBloqueo: true, cancelacion).ConfigureAwait(false)
            ?? throw new InvalidOperationException("El pedido de una entrega en curso no existe.");

        // Se revalida bajo el bloqueo: si otra instancia o la reanudación ya resolvió la fila, no se aplica nada dos veces.
        if ((await pedidos.EntregasEnCurso(pedido.Id, cancelacion).ConfigureAwait(false)).All(e => e.Id != entrega.Id))
        {
            return Resultado<LineasOperadas>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.OperacionYaResuelta));
        }

        var ahora = reloj.Ahora;
        var anterior = pedido.Estado;
        var cerradas = entrega.LineasCerradas;
        var aplicadas = pedido.EntregarLineas(cerradas, ahora).Where(r => r.Resultado == ResultadoDeOperacionDeLinea.Aplicada).Select(r => r.LineaId).ToList();
        await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);
        foreach (var lineaId in aplicadas)
        {
            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.LineaDePedidoEntregada)
                {
                    ObjetoTipo = "pedido",
                    ObjetoId = pedido.Id.ToString("D"),
                    Detalle = new Dictionary<string, string?> { ["lineaId"] = lineaId.ToString("D"), ["iniciadaPor"] = entrega.Iniciador.EstaAnonimizado ? null : entrega.Iniciador.UsuarioId.ToString("D") },
                },
                cancelacion).ConfigureAwait(false);
        }

        if (pedido.Estado != anterior)
        {
            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.EstadoDePedidoCambiado)
                {
                    ObjetoTipo = "pedido",
                    ObjetoId = pedido.Id.ToString("D"),
                    Detalle = new Dictionary<string, string?> { ["estadoAnterior"] = AvisosDePedidos.NombreDelEstado(anterior), ["estadoNuevo"] = AvisosDePedidos.NombreDelEstado(pedido.Estado) },
                },
                cancelacion).ConfigureAwait(false);
        }

        entrega.Completar(ahora);
        await pedidos.ActualizarEntrega(entrega, cancelacion).ConfigureAwait(false);
        if (aplicadas.Count > 0)
        {
            await new AvisosDePedidos(notificador).Solicitar(pedido, "entregado", entrega.Id.ToString("N"), AvisosDePedidos.SucursalesResponsables(pedido, aplicadas), cancelacion).ConfigureAwait(false);
        }

        return Resultado<LineasOperadas>.Exito(new LineasOperadas(
            pedido.Id,
            ProyeccionesDePedidos.AEstadoVisible(pedido.Estado),
            [.. entrega.Lineas.Select(l => new ResultadoDeLineaOperada(l.LineaId, aplicadas.Contains(l.LineaId) ? ResultadoVisibleDeLinea.Aplicada : ResultadoVisibleDeLinea.EstadoInvalido))]));
    }
}

/// <summary>El generador de identificadores de las operaciones de Pedidos (entregas y anulaciones): aleatorios, sin patrón; un puerto para que las pruebas sean deterministas.</summary>
public interface IGeneradorDeIdentificadoresDePedidos
{
    Guid Nuevo();
}

/// <summary>Cómo terminó la reanudación de una entrega: aplicada, a reintentar en la próxima pasada o ya resuelta por otra instancia (no se toca).</summary>
internal enum ResultadoDeReanudarEntrega
{
    Aplicada,
    Reintentar,
    YaResuelta,
}
