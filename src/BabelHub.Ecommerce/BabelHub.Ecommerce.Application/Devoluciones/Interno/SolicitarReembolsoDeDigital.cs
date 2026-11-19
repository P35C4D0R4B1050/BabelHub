using System.Globalization;
using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;

namespace BabelHub.Ecommerce.Application.Devoluciones.Interno;

/// <summary>
/// <c>SolicitarReembolsoDeDigital</c> (T07; DEV-04, DEV-05, DEV-09, DEV-19, DEV-20, DEV-21; plan §1): lee de Pedidos y de Biblioteca; rechaza por política (enlace entregado), por plazo (más de 14 días desde el pago) o por línea no devolvible
/// (anulada, reembolsada o con el acceso revocado o suspendido); si procede, <b>una sola transacción</b> en el orden global de bloqueo: marca de cancelación → cupo → devolución → <c>IReembolsos.Registrar</c> → <c>RevocarSiNoHayEnlace</c>
/// (un enlace ya entregado, un acceso suspendido o el tope de Pagos revierten <b>toda</b> la transacción). Después de confirmar, <c>Procesar</c>; solo si la pasarela confirma, otra transacción pasa la línea a reembolsada. Un digital nunca llama al Inventario (regla 12).
/// </summary>
internal sealed class SolicitarReembolsoDeDigital(
    AutorizadorDeDevoluciones autorizador,
    LimitesDeDevoluciones limites,
    IPedidosParaDevoluciones pedidos,
    IAccesosParaDevoluciones accesos,
    IReembolsos reembolsos,
    IMarcaDeCancelacionDeCuenta marca,
    IRepositorioDeDevoluciones repositorio,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    AvisosDeDevoluciones avisos,
    ProcesarReembolsoDeLinea procesador,
    IReloj reloj) : ICasoDeUso<PedirReembolsoDeDigitalEntrada, DevolucionVista>
{
    public async Task<Resultado<DevolucionVista>> Ejecutar(PedirReembolsoDeDigitalEntrada entrada, CancellationToken cancelacion = default)
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
        if (motivo is null)
        {
            return FallosDeDevoluciones.Fallo<DevolucionVista>(MapeoDeDevoluciones.ValidacionDeCampo("motivo", "formato-invalido"));
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

            if (linea.Estado == EstadoVisibleDeLaLinea.Anulado)
            {
                return Politica(ClavesDeDevoluciones.LineaNoDevolvible);
            }

            var acceso = await accesos.Consultar(pedido.CuentaId, pedido.PedidoId, linea.EdicionId, cancelacion).ConfigureAwait(false);
            if (!acceso.EsExito)
            {
                return FallosDeDevoluciones.NoDisponible<DevolucionVista>();
            }

            if (acceso.Valor is not { Estado: EstadoDelAccesoParaDevoluciones.Vigente } vigente)
            {
                return Politica(ClavesDeDevoluciones.LineaNoDevolvible);
            }

            if (vigente.EnlaceEntregado)
            {
                return Politica(ClavesDeDevoluciones.PoliticaDigital);
            }

            var ahora = reloj.Ahora;
            if (!PoliticaDeDevolucion.ReembolsoAutomaticoEnPlazo(pedido.CreadoEn, ahora))
            {
                return FallosDeDevoluciones.Fallo<DevolucionVista>(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.PlazoVencido, [new ErrorDeCampo("plazo-en-dias", "14")]));
            }

            var devolucion = Devolucion.Solicitar(
                pedido.PedidoId,
                TipoDeDevolucion.Digital,
                OrigenDeDevolucion.Solicitud,
                null,
                motivo,
                null,
                [new NuevaLineaDeDevolucion(linea.LineaId, linea.EdicionId, linea.Cantidad, linea.PrecioUnitario)],
                ahora,
                ResponsableDelCambio.Cliente(autorizador.Actor!.UsuarioId!.Value));
            var lineaDeDevolucion = devolucion.Lineas[0];
            var reembolsoId = await unidad.Ejecutar(token => RegistrarYRevocar(devolucion, pedido, linea, lineaDeDevolucion, ahora, token), cancelacion).ConfigureAwait(false);

            // Solo después de confirmar: la pasarela. Si confirma, otra transacción; si falla, la línea sigue aprobada con el reembolso pendiente y el acceso revocado (DEV-09, DEV-21).
            await procesador.Ejecutar(devolucion.Id, lineaDeDevolucion.LineaDePedidoId, reembolsoId, cancelacion).ConfigureAwait(false);
            var final = await repositorio.Obtener(devolucion.Id, paraActualizar: false, cancelacion).ConfigureAwait(false) ?? devolucion;
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

    private static Resultado<DevolucionVista> Politica(string clave) => FallosDeDevoluciones.De<DevolucionVista>(CategoriaDeError.ReglaDeNegocio, clave);

    private async Task<Guid> RegistrarYRevocar(Devolucion devolucion, PedidoParaDevoluciones pedido, LineaDePedidoParaDevoluciones linea, LineaDeDevolucion lineaDeDevolucion, DateTimeOffset ahora, CancellationToken token)
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

        if (await repositorio.TomarCupos([new CupoPedido(linea.LineaId, pedido.PedidoId, linea.Cantidad, linea.Cantidad)], token).ConfigureAwait(false) is not null)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.LineaNoDevolvible));
        }

        await repositorio.Agregar(devolucion, token).ConfigureAwait(false);
        devolucion.Aprobar(ahora, ResponsableDelCambio.Sistema);

        // El reembolso se registra antes de pedir a Biblioteca que revoque (el orden global: la intención antes que el acceso).
        var registrado = await reembolsos.Registrar(
            new RegistrarReembolsoEntrada(pedido.PedidoId, OrigenDelReembolso.Devolucion, ClaveDeReembolso.De(devolucion.Id, linea.LineaId), [new LineaAReembolsar(linea.LineaId, lineaDeDevolucion.Monto)], 0m),
            token).ConfigureAwait(false);
        if (!registrado.EsExito)
        {
            // El tope de Pagos (una línea ya reembolsada por una anulación) o un fallo: todo se revierte.
            throw new RechazoQueRevierteException(registrado.Error!.Categoria == CategoriaDeError.NoDisponible
                ? new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible)
                : new ErrorDeAplicacion(CategoriaDeError.ReglaDeNegocio, ClavesDeDevoluciones.LineaNoDevolvible));
        }

        devolucion.RegistrarReembolsoDeLinea(linea.LineaId, ahora);
        var revocacion = await accesos.RevocarSiNoHayEnlace(pedido.CuentaId, pedido.PedidoId, linea.EdicionId, token).ConfigureAwait(false);
        if (!revocacion.EsExito)
        {
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDeDevoluciones.ServicioNoDisponible));
        }

        if (revocacion.Valor != ResultadoDeRevocarSiNoHayEnlace.Revocado)
        {
            // Gana la primera operación que confirma: un enlace ya entregado o un acceso suspendido es política; ya revocado o sin acceso es una línea no devolvible (DEV-19, DEV-20).
            throw new RechazoQueRevierteException(new ErrorDeAplicacion(
                CategoriaDeError.ReglaDeNegocio,
                revocacion.Valor is ResultadoDeRevocarSiNoHayEnlace.EnlaceYaEntregado or ResultadoDeRevocarSiNoHayEnlace.NoDisponible ? ClavesDeDevoluciones.PoliticaDigital : ClavesDeDevoluciones.LineaNoDevolvible));
        }

        await repositorio.Guardar(devolucion, token).ConfigureAwait(false);
        await auditoria.Registrar(AuditoriaDeDevoluciones.Solicitada(devolucion), token).ConfigureAwait(false);
        await auditoria.Registrar(AuditoriaDeDevoluciones.Cambio(devolucion, EstadoDeLineaDeDevolucion.Solicitada, EstadoDeLineaDeDevolucion.Aprobada), token).ConfigureAwait(false);
        await avisos.Solicitar(devolucion, pedido.CuentaId, EstadoDeLineaDeDevolucion.Aprobada, token).ConfigureAwait(false);
        return registrado.Valor.ReembolsoId;
    }

}
