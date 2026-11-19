using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// <c>ReportarArchivoDefectuoso</c> (T08; DEV-06, DEV-22, DEV-26): el cliente reporta un archivo defectuoso de un acceso suyo <b>vigente</b> dentro de 30 días desde el pago. Un acceso revocado, suspendido o con una solicitud abierta se rechaza sin crear otro
/// reporte (el cupo de la línea lo garantiza también ante la carrera). Una transacción lee con bloqueo la marca de cancelación, compromete el cupo, registra el reporte con su texto redactable y <c>Suspender</c> las descargas. La decide solo la administración central.
/// Un digital nunca llama al Inventario (regla 12).
/// </summary>
internal sealed class ReportarArchivoDefectuoso(
    AutorizadorDeDevoluciones autorizador,
    LimitesDeDevoluciones limites,
    IPedidosParaDevoluciones pedidos,
    IAccesosParaDevoluciones accesos,
    IMarcaDeCancelacionDeCuenta marca,
    IRepositorioDeDevoluciones repositorio,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj) : ICasoDeUso<ReportarArchivoDefectuosoEntrada, DevolucionVista>
{
    public async Task<Resultado<DevolucionVista>> Ejecutar(ReportarArchivoDefectuosoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(rechazo);
        }

        if (await limites.Operacion(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(excedido);
        }

        if (entrada.PedidoId == Guid.Empty || entrada.LineaId == Guid.Empty)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(MapeoDeDevoluciones.ValidacionDeCampo(entrada.PedidoId == Guid.Empty ? "pedidoId" : "lineaId", "obligatorio"));
        }

        var motivo = TextoRedactado.Crear(entrada.Motivo?.Valor, TextoRedactado.LongitudMaximaDelMotivo);
        var texto = TextoRedactado.Crear(entrada.Texto?.Valor, TextoRedactado.LongitudMaximaDelTexto);
        if (motivo is null || texto is null)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(MapeoDeDevoluciones.ValidacionDeCampo(motivo is null ? "motivo" : "texto", "formato-invalido"));
        }

        try
        {
            var lectura = await pedidos.Obtener(entrada.PedidoId, cancelacion).ConfigureAwait(false);
            if (!lectura.EsExito)
            {
                return lectura.Error!.Categoria == CategoriaDeError.NoEncontrado
                    ? FallosDeDevoluciones.Fallo<DevolucionVista>(await autorizador.NoEncontrado(MotivosDeDenegacionDeDevoluciones.RecursoAjeno, cancelacion).ConfigureAwait(false))
                    : FallosDeDevoluciones.NoDisponible<DevolucionVista>();
            }

            var pedido = lectura.Valor;
            if (await autorizador.ExigirQueElPedidoEsDelCliente(pedido.CuentaId, cancelacion).ConfigureAwait(false) is { } ajeno)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(ajeno);
            }

            var linea = pedido.Lineas.FirstOrDefault(l => l.LineaId == entrada.LineaId);
            if (linea is null || !linea.EsDigital)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(MapeoDeDevoluciones.ValidacionDeCampo("lineaId", linea is null ? "no-es-del-pedido" : "no-es-digital"));
            }

            var acceso = await accesos.Consultar(pedido.CuentaId, pedido.PedidoId, linea.EdicionId, cancelacion).ConfigureAwait(false);
            if (!acceso.EsExito)
            {
                return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
            }

            if (linea.Estado == EstadoVisibleDeLaLinea.Anulado || acceso.Valor is not { Estado: EstadoDelAccesoParaDevoluciones.Vigente } vigente)
            {
                return FallosDeDevoluciones.De<DevolucionVista>(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.ReporteNoAdmitido);
            }

            var ahora = reloj.Ahora;
            if (!PoliticaDeDevolucion.ReporteEnPlazo(pedido.CreadoEn, ahora))
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.PlazoVencido, [new ErrorDeCampo("plazo-en-dias", "30")]));
            }

            var devolucion = Devolucion.Solicitar(
                pedido.PedidoId,
                TipoDeDevolucion.Digital,
                OrigenDeDevolucion.Reporte,
                null,
                motivo,
                texto,
                [new NuevaLineaDeDevolucion(linea.LineaId, linea.EdicionId, linea.Cantidad, linea.PrecioUnitario, vigente.AccesoId)],
                ahora,
                ResponsableDelCambio.Cliente(autorizador.Actor!.UsuarioId!.Value));
            await unidad.Ejecutar(
                async token =>
                {
                    await Registrar(devolucion, pedido, linea, vigente.AccesoId, token).ConfigureAwait(false);
                    return true;
                },
                cancelacion).ConfigureAwait(false);
            return Resultado<DevolucionVista>.Exito(MapeoDeDevoluciones.Vista(devolucion));
        }
        catch (RechazoQueRevierteException revertido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(revertido.Error);
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
        }
    }

    private async Task Registrar(Devolucion devolucion, PedidoParaDevoluciones pedido, LineaDePedidoParaDevoluciones linea, Guid accesoId, CancellationToken token)
    {
        EstadoDeLaMarca estado;
        try
        {
            estado = await marca.LeerConBloqueo(pedido.CuentaId, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible));
        }

        if (estado is EstadoDeLaMarca.EnEjecucion or EstadoDeLaMarca.Ejecutada)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeDevoluciones.CuentaEnCancelacion));
        }

        // El cupo de la línea garantiza un solo reporte o solicitud abierta aun ante la carrera (DEV-22).
        if (await repositorio.TomarCupos([new CupoPedido(linea.LineaId, pedido.PedidoId, linea.Cantidad, linea.Cantidad)], token).ConfigureAwait(false) is not null)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.ReporteNoAdmitido));
        }

        await repositorio.Agregar(devolucion, token).ConfigureAwait(false);
        var suspension = await accesos.Suspender(pedido.CuentaId, accesoId, token).ConfigureAwait(false);
        if (!suspension.EsExito)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible));
        }

        if (suspension.Valor != ResultadoDeSuspenderAcceso.Suspendido)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.ReporteNoAdmitido));
        }

        await auditoria.Registrar(AuditoriaDeDevoluciones.Solicitada(devolucion), token).ConfigureAwait(false);
    }
}

/// <summary>
/// <c>DecidirReporte</c> (T08; DEV-06, DEV-09, DEV-18, D12): <b>solo la administración central</b> y solo sobre una línea <c>solicitada</c> de un reporte. <b>Aprobar</b>: línea aprobada, <c>IReembolsos.Registrar</c> y luego <c>IAccesosDeBiblioteca.Revocar</c> (si el
/// acceso ya estaba revocado por una anulación de Pedidos, o el tope de Pagos rechaza, revierte y responde <c>ECO-DEV-LINEA-NO-DEVOLVIBLE</c>); después de confirmar, <c>Procesar</c> y reembolsada solo si la pasarela confirma. <b>Rechazar</b>: línea rechazada, cupo
/// devuelto y <c>Reanudar</c> (tolera que el acceso ya no esté suspendido o esté revocado por una anulación); repetible dentro de los 30 días (DEV-18).
/// </summary>
internal sealed class DecidirReporte(
    AutorizadorDeDevoluciones autorizador,
    LimitesDeDevoluciones limites,
    IPedidosParaDevoluciones pedidos,
    IAccesosParaDevoluciones accesos,
    IAccesosDeBiblioteca biblioteca,
    IReembolsos reembolsos,
    IRepositorioDeDevoluciones repositorio,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    IReloj reloj)
{
    public Task<Resultado<DevolucionVista>> Aprobar(Guid devolucionId, CancellationToken cancelacion = default) => Decidir(devolucionId, aprobar: true, cancelacion);

    public Task<Resultado<DevolucionVista>> Rechazar(Guid devolucionId, CancellationToken cancelacion = default) => Decidir(devolucionId, aprobar: false, cancelacion);

    private async Task<Resultado<DevolucionVista>> Decidir(Guid devolucionId, bool aprobar, CancellationToken cancelacion)
    {
        if (await autorizador.ExigirAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(rechazo);
        }

        if (await limites.Operacion(cancelacion).ConfigureAwait(false) is { } excedido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(excedido);
        }

        try
        {
            var inicial = await repositorio.Obtener(devolucionId, paraActualizar: false, cancelacion).ConfigureAwait(false);
            if (inicial is null || inicial.Origen != OrigenDeDevolucion.Reporte)
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(await autorizador.NoEncontrado(MotivosDeDenegacionDeDevoluciones.RecursoAjeno, cancelacion).ConfigureAwait(false));
            }

            // La cuenta y la edición salen del contrato de lectura de Pedidos, nunca de la tabla ni del actor.
            var lectura = await pedidos.Obtener(inicial.PedidoId, cancelacion).ConfigureAwait(false);
            if (!lectura.EsExito)
            {
                return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
            }

            var pedido = lectura.Valor;
            var responsable = ResponsableDelCambio.Personal(autorizador.Actor!.UsuarioId!.Value);
            var ahora = reloj.Ahora;
            Guid? reembolsoId = null;
            Devolucion? decidida = null;
            await unidad.Ejecutar(
                async token =>
                {
                    var devolucion = await repositorio.Obtener(devolucionId, paraActualizar: true, token).ConfigureAwait(false) ?? throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDeDevoluciones.NoEncontrado));
                    var linea = devolucion.Lineas[0];
                    if (aprobar)
                    {
                        reembolsoId = await AprobarDentro(devolucion, linea, pedido, responsable, ahora, token).ConfigureAwait(false);
                    }
                    else
                    {
                        await RechazarDentro(devolucion, linea, pedido, responsable, ahora, token).ConfigureAwait(false);
                    }

                    decidida = devolucion;
                    return true;
                },
                cancelacion).ConfigureAwait(false);

            if (aprobar && reembolsoId is { } id)
            {
                await ProcesarElReembolso(decidida!.Id, decidida.Lineas[0].LineaDePedidoId, id, cancelacion).ConfigureAwait(false);
            }

            var final = await repositorio.Obtener(devolucionId, paraActualizar: false, cancelacion).ConfigureAwait(false) ?? decidida!;
            return Resultado<DevolucionVista>.Exito(MapeoDeDevoluciones.Vista(final));
        }
        catch (RechazoQueRevierteException revertido)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(revertido.Error);
        }
        catch (DevolucionesNoDisponiblesException)
        {
            return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
        }
    }

    private async Task<Guid> AprobarDentro(Devolucion devolucion, LineaDeDevolucion linea, PedidoParaDevoluciones pedido, ResponsableDelCambio responsable, DateTimeOffset ahora, CancellationToken token)
    {
        if (devolucion.Aprobar(ahora, responsable) != ResultadoDeTransicionDeDevolucion.Aplicada)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeDevoluciones.EstadoInvalido));
        }

        var registrado = await reembolsos.Registrar(
            new RegistrarReembolsoEntrada(pedido.PedidoId, OrigenDelReembolso.Devolucion, $"dev:{devolucion.Id:D}:{linea.LineaDePedidoId:D}", [new LineaAReembolsar(linea.LineaDePedidoId, linea.Monto)], 0m),
            token).ConfigureAwait(false);
        if (!registrado.EsExito)
        {
            throw new RechazoQueRevierteException(registrado.Error!.Categoria == CategoriaDeError.NoDisponible
                ? new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible)
                : new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.LineaNoDevolvible));
        }

        devolucion.RegistrarReembolsoDeLinea(linea.LineaDePedidoId, ahora);
        var revocacion = await biblioteca.Revocar(new RevocarAccesoEntrada(pedido.PedidoId, linea.EdicionId, MotivoDeRevocacionDeAcceso.ReporteAprobado), token).ConfigureAwait(false);
        if (!revocacion.EsExito)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible));
        }

        if (revocacion.Valor == ResultadoDeRevocarAcceso.YaRevocado)
        {
            // Una anulación de Pedidos ya revocó y reembolsó la línea mientras el reporte seguía abierto: se revierte y la administración lo cierra rechazándolo.
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.LineaNoDevolvible));
        }

        await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
        await auditoria.Registrar(AuditoriaDeDevoluciones.Cambio(devolucion, EstadoDeLineaDeDevolucion.Solicitada, EstadoDeLineaDeDevolucion.Aprobada), token).ConfigureAwait(false);
        return registrado.Valor.ReembolsoId;
    }

    private async Task RechazarDentro(Devolucion devolucion, LineaDeDevolucion linea, PedidoParaDevoluciones pedido, ResponsableDelCambio responsable, DateTimeOffset ahora, CancellationToken token)
    {
        if (devolucion.Rechazar(ahora, responsable) != ResultadoDeTransicionDeDevolucion.Aplicada)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeDevoluciones.EstadoInvalido));
        }

        await repositorio.DevolverCupos([(linea.LineaDePedidoId, linea.Cantidad)], token).ConfigureAwait(false);
        if (linea.AccesoId is { } accesoId)
        {
            // Rechazar tolera que el acceso ya no esté suspendido o esté revocado por una anulación: el reporte debe poder cerrarse siempre.
            try
            {
                await accesos.Reanudar(pedido.CuentaId, accesoId, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Sin Biblioteca no se reanuda, pero el reporte se cierra: la administración lo ve y Biblioteca lo concilia.
            }
        }

        await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
        await auditoria.Registrar(AuditoriaDeDevoluciones.Cambio(devolucion, EstadoDeLineaDeDevolucion.Solicitada, EstadoDeLineaDeDevolucion.Rechazada), token).ConfigureAwait(false);
    }

    private async Task ProcesarElReembolso(Guid devolucionId, Guid lineaDePedidoId, Guid reembolsoId, CancellationToken cancelacion)
    {
        Resultado<EstadoDelReembolso> procesado;
        try
        {
            procesado = await reembolsos.Procesar(reembolsoId, cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }

        if (!procesado.EsExito || procesado.Valor != EstadoDelReembolso.Reembolsado)
        {
            return;
        }

        try
        {
            await unidad.Ejecutar(
                async token =>
                {
                    var devolucion = await repositorio.Obtener(devolucionId, paraActualizar: true, token).ConfigureAwait(false);
                    if (devolucion is not null && devolucion.Reembolsar(lineaDePedidoId, reloj.Ahora, ResponsableDelCambio.Sistema) == ResultadoDeTransicionDeDevolucion.Aplicada)
                    {
                        await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
                        await auditoria.Registrar(AuditoriaDeDevoluciones.Cambio(devolucion, EstadoDeLineaDeDevolucion.Aprobada, EstadoDeLineaDeDevolucion.Reembolsada), token).ConfigureAwait(false);
                    }

                    return true;
                },
                cancelacion).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // La pasarela ya confirmó: la conciliación (T11) pasa la línea a reembolsada sin repetir el reembolso.
        }
    }
}
