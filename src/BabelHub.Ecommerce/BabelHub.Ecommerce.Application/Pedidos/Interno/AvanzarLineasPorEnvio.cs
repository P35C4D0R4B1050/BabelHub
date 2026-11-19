using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>
/// El progreso de los envíos hacia las líneas del pedido (T17; PED-20, PED-29; AC-PED-20, 21, 24): lo llama Envíos <b>después de confirmar su transacción</b> (nunca dentro de ella: no hay interbloqueo envío → pedido) y lo reintenta un trabajo, así que ambas operaciones son <b>idempotentes por envío</b>.
/// <c>EnvioEnReparto</c> pasa a <b>despachadas</b> las líneas del envío y <c>EnvioEntregado</c> a <b>entregadas con la fecha</b> del envío (solo desde despachada: la entrega sin reparto previo es estado inválido); el estado del pedido se deriva <b>ignorando las líneas anuladas</b> (PED-20). Una transacción corta con el
/// pedido bloqueado: cruza que cada línea sea del pedido y de ese envío (si no, validación), omite en silencio las líneas ya anuladas, espera (reintentable) si una anulación o entrega <b>en curso</b> toca alguna, audita la compleción de cada línea entregada y el cambio de estado del pedido, y avisa <b>una vez por envío y
/// estado destino</b> (id de la operación = el <c>envioId</c>). Repetir una llamada ya aplicada responde éxito con <c>YaAplicado</c> y no audita ni avisa. No exige rol: es una llamada entre módulos de un llamador que ya autorizó la operación (logística, administración o el trabajo de «sistema»).
/// </summary>
public sealed class AvanzarLineasPorEnvio(
    IRepositorioDePedidos pedidos,
    IUnidadDeTrabajo unidad,
    IAuditoria auditoria,
    INotificador notificador,
    IReloj reloj) : IProgresoDeEnvios
{
    public Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEnReparto(EnvioEnRepartoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return Avanzar(entrada.PedidoId, entrada.EnvioId, entrada.LineaIds, EstadoDeLineaImpresa.Despachado, null, cancelacion);
    }

    public Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEntregado(EnvioEntregadoEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return Avanzar(entrada.PedidoId, entrada.EnvioId, entrada.LineaIds, EstadoDeLineaImpresa.Entregado, entrada.Fecha, cancelacion);
    }

    private async Task<Resultado<ProgresoDeEnvioAplicado>> Avanzar(Guid pedidoId, Guid envioId, IReadOnlyList<Guid>? lineaIds, EstadoDeLineaImpresa destino, DateTimeOffset? fecha, CancellationToken cancelacion)
    {
        var ids = (lineaIds ?? []).Distinct().ToList();
        if (pedidoId == Guid.Empty || envioId == Guid.Empty || ids.Count == 0 || (destino == EstadoDeLineaImpresa.Entregado && fecha is null))
        {
            return Resultado<ProgresoDeEnvioAplicado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDePedidos.ValidacionFallida, [new ErrorDeCampo("lineaIds", "invalido")]));
        }

        try
        {
            return await unidad.Ejecutar(token => Aplicar(pedidoId, envioId, ids, destino, fecha, token), cancelacion).ConfigureAwait(false);
        }
        catch (OperacionRechazadaException rechazo)
        {
            return Resultado<ProgresoDeEnvioAplicado>.Fallo(rechazo.Error);
        }
        catch (EsperaDeBloqueoDeUnPedidoAgotadaException)
        {
            return Resultado<ProgresoDeEnvioAplicado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.Conflicto));
        }
        catch (AuditoriaNoDisponibleException)
        {
            // Sin poder auditar no se aplica nada: Envíos lo reintenta (PED-29 se audita en la misma transacción).
            return Resultado<ProgresoDeEnvioAplicado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }
    }

    private async Task<Resultado<ProgresoDeEnvioAplicado>> Aplicar(Guid pedidoId, Guid envioId, IReadOnlyList<Guid> ids, EstadoDeLineaImpresa destino, DateTimeOffset? fecha, CancellationToken cancelacion)
    {
        var pedido = await pedidos.Obtener(pedidoId, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (pedido is null)
        {
            return Resultado<ProgresoDeEnvioAplicado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, ClavesDePedidos.NoEncontrado));
        }

        // El cruce: cada línea debe ser del pedido, impresa y de ese envío.
        var lineas = pedido.Lineas.Where(l => ids.Contains(l.Id)).ToList();
        if (pedido.Tipo != TipoDeEntregaDelPedido.Domicilio || lineas.Count != ids.Count || lineas.Any(l => l.EsDigital || l.EnvioId != envioId))
        {
            return Resultado<ProgresoDeEnvioAplicado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDePedidos.ValidacionFallida, [new ErrorDeCampo("lineaIds", "invalido")]));
        }

        // Una línea anulada no avanza (PED-27: el envío mantenido no la lleva, pero una repetición tardía la puede traer).
        var vigentes = lineas.Where(l => l.EstadoImpreso != EstadoDeLineaImpresa.Anulado).ToList();
        var pendientes = vigentes.Where(l => l.EstadoImpreso is { } estado && estado.Orden() < destino.Orden()).ToList();
        if (pendientes.Count == 0)
        {
            return Resultado<ProgresoDeEnvioAplicado>.Exito(new ProgresoDeEnvioAplicado(YaAplicado: true));
        }

        var idsPendientes = pendientes.Select(l => l.Id).ToList();
        var anulaciones = await pedidos.AnulacionesEnCurso(pedido.Id, cancelacion).ConfigureAwait(false);
        var entregas = await pedidos.EntregasEnCurso(pedido.Id, cancelacion).ConfigureAwait(false);
        if (anulaciones.Any(a => a.BloqueaLasLineas && a.LineaIds.Any(idsPendientes.Contains)) || entregas.Any(e => e.BloqueaLasLineas && e.Lineas.Any(l => idsPendientes.Contains(l.LineaId))))
        {
            return Resultado<ProgresoDeEnvioAplicado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        // La entrega solo se acepta desde despachada (PED-29): sin el reparto previo es estado inválido.
        if (destino == EstadoDeLineaImpresa.Entregado && pendientes.Any(l => l.EstadoImpreso != EstadoDeLineaImpresa.Despachado))
        {
            return Resultado<ProgresoDeEnvioAplicado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.EstadoInvalido));
        }

        var anterior = pedido.Estado;
        var resultados = destino == EstadoDeLineaImpresa.Despachado
            ? pedido.DespacharLineas(idsPendientes, reloj.Ahora)
            : pedido.EntregarLineas(idsPendientes, fecha!.Value);
        if (resultados.Any(r => r.Resultado != ResultadoDeOperacionDeLinea.Aplicada))
        {
            // Una línea no marcada lista no sale: la transacción se revierte y Envíos lo reporta.
            throw new OperacionRechazadaException(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDePedidos.EstadoInvalido));
        }

        await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);
        if (destino == EstadoDeLineaImpresa.Entregado)
        {
            foreach (var linea in pendientes)
            {
                await auditoria.Registrar(
                    new SolicitudDeRegistro(AccionesAuditables.LineaDePedidoEntregada)
                    {
                        ObjetoTipo = "pedido",
                        ObjetoId = pedido.Id.ToString("D"),
                        Detalle = new Dictionary<string, string?> { ["lineaId"] = linea.Id.ToString("D"), ["envioId"] = envioId.ToString("D") },
                    },
                    cancelacion).ConfigureAwait(false);
            }
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

        var estadoDelAviso = destino == EstadoDeLineaImpresa.Despachado ? "despachado" : "entregado";
        await new AvisosDePedidos(notificador).Solicitar(pedido, estadoDelAviso, envioId.ToString("N"), AvisosDePedidos.SucursalesResponsables(pedido, idsPendientes), cancelacion).ConfigureAwait(false);
        return Resultado<ProgresoDeEnvioAplicado>.Exito(new ProgresoDeEnvioAplicado(YaAplicado: false));
    }
}
