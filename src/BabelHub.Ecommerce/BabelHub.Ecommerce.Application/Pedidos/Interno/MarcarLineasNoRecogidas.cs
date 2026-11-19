using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Notificaciones;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pedidos;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>Lo que dejó una corrida de la revisión de plazos: pedidos revisados, líneas marcadas no recogidas, pedidos que fallaron (se reintentan en la próxima hora) y liberaciones hechas y pendientes.</summary>
public sealed record ResultadoDeLaRevisionDePlazos(int PedidosRevisados, int LineasMarcadas, int PedidosConFallo, int LiberacionesHechas, int LiberacionesPendientes);

/// <summary>
/// La revisión horaria de plazos de recojo (T18; PED-15, PED-16; AC-PED-13, 14): la corre el trabajo de fondo como actor «sistema». Por cada pedido de recojo con una línea que lleva <b>más de 360 horas</b> en listo para recojo abre <b>una transacción corta bajo su bloqueo</b>, <b>omite las líneas con una fila <c>en_curso</c></b> (entrega o anulación: gana la primera que confirma), pasa a no recogida
/// las vencidas, <b>anota la liberación de su reserva</b> (<c>liberaciones_de_reserva</c>, motivo <c>no-recogido</c>), audita el cambio de estado del pedido si lo hay y solicita el aviso «no recogido» al cliente y a la sucursal con la huella de las líneas como identificador (una repetición no avisa dos veces). Sin poder auditar, el pedido se revierte y se reintenta a la hora
/// siguiente. <b>Después</b> de cada pedido, fuera de transacción, pide al Inventario liberar las reservas anotadas: una liberación <c>Liberada</c> o <c>NoExistia</c> borra la fila; <c>YaCerrada</c> (una entrega ganó) se trata como cierre confirmado, borra la fila y publica una alerta técnica; si el Inventario no responde la fila queda con un intento más y se repite en
/// la corrida siguiente (también las de corridas anteriores). Nunca interrumpe la corrida por un pedido: cada fallo se cuenta y sigue con el siguiente.
/// </summary>
internal sealed class MarcarLineasNoRecogidas(
    AutorizadorDePedidos autorizador,
    IRepositorioDePedidos pedidos,
    IUnidadDeTrabajo unidad,
    IReservasDelPedido reservas,
    IAuditoria auditoria,
    INotificador notificador,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj)
{
    public const string MotivoDeLiberacion = "no-recogido";
    public const string AlertaLiberacionYaCerrada = "pedido-liberacion-no-recogido-ya-cerrada";
    public const int MaximoDePedidosPorCorrida = 200;
    public const int MaximoDeLiberacionesPorCorrida = 500;

    public async Task<Resultado<ResultadoDeLaRevisionDePlazos>> Revisar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResultadoDeLaRevisionDePlazos>.Fallo(rechazo);
        }

        IReadOnlyList<Guid> candidatos;
        try
        {
            candidatos = await pedidos.PedidosConRecojoVencido(reloj.Ahora, MaximoDePedidosPorCorrida, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return Resultado<ResultadoDeLaRevisionDePlazos>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }

        int marcadas = 0, conFallo = 0;
        foreach (var pedidoId in candidatos)
        {
            try
            {
                marcadas += await unidad.Ejecutar(token => MarcarUnPedido(pedidoId, token), cancelacion).ConfigureAwait(false);
            }
            catch (Exception excepcion) when (excepcion is AuditoriaNoDisponibleException or EsperaDeBloqueoDeUnPedidoAgotadaException)
            {
                // Se revierte entero y se reintenta en la próxima corrida.
                conFallo++;
            }
        }

        var (hechas, pendientes) = await LiberarLasReservas(cancelacion).ConfigureAwait(false);
        return Resultado<ResultadoDeLaRevisionDePlazos>.Exito(new ResultadoDeLaRevisionDePlazos(candidatos.Count, marcadas, conFallo, hechas, pendientes));
    }

    private async Task<int> MarcarUnPedido(Guid pedidoId, CancellationToken cancelacion)
    {
        var pedido = await pedidos.Obtener(pedidoId, conBloqueo: true, cancelacion).ConfigureAwait(false);
        if (pedido is null)
        {
            return 0;
        }

        // Una línea con una operación en curso no se toca: gana la primera que confirma (PED-21).
        var enCurso = new HashSet<Guid>();
        foreach (var anulacion in await pedidos.AnulacionesEnCurso(pedido.Id, cancelacion).ConfigureAwait(false))
        {
            enCurso.UnionWith(anulacion.LineaIds);
        }

        foreach (var entrega in await pedidos.EntregasEnCurso(pedido.Id, cancelacion).ConfigureAwait(false))
        {
            enCurso.UnionWith(entrega.Lineas.Select(l => l.LineaId));
        }

        var ahora = reloj.Ahora;
        var anterior = pedido.Estado;
        var marcadas = pedido.MarcarNoRecogidas(ahora, enCurso);
        if (marcadas.Count == 0)
        {
            return 0;
        }

        // Se audita primero: sin poder auditar no se guarda nada y la transacción se revierte entera. Cada línea marcada deja su registro (aunque el estado del pedido no cambie): afecta al stock.
        foreach (var lineaId in marcadas)
        {
            await auditoria.Registrar(
                new SolicitudDeRegistro(AccionesAuditables.LineaDePedidoMarcadaNoRecogida)
                {
                    ObjetoTipo = "pedido",
                    ObjetoId = pedido.Id.ToString("D"),
                    Detalle = new Dictionary<string, string?> { ["lineaId"] = lineaId.ToString("D") },
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

        await pedidos.Actualizar(pedido, cancelacion).ConfigureAwait(false);
        await pedidos.RegistrarLiberacionesDeReserva(pedido.Id, marcadas, MotivoDeLiberacion, ahora, cancelacion).ConfigureAwait(false);

        await new AvisosDePedidos(notificador).Solicitar(pedido, "no-recogido", $"no-recogido-{AvisosDePedidos.HuellaDeLineas(marcadas)}", AvisosDePedidos.SucursalesResponsables(pedido, marcadas), cancelacion).ConfigureAwait(false);
        return marcadas.Count;
    }

    /// <summary>Pide al Inventario liberar las reservas anotadas (las de esta corrida y las que quedaron de antes), agrupadas por pedido y motivo, fuera de toda transacción.</summary>
    private async Task<(int Hechas, int Pendientes)> LiberarLasReservas(CancellationToken cancelacion)
    {
        IReadOnlyList<LiberacionDeReservaPendiente> anotadas;
        try
        {
            anotadas = await pedidos.LiberacionesDeReservaPendientes(MaximoDeLiberacionesPorCorrida, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return (0, 0);
        }

        int hechas = 0, pendientes = 0;
        foreach (var grupo in anotadas.GroupBy(l => (l.PedidoId, l.Motivo)))
        {
            var lineas = grupo.Select(l => l.LineaId).ToList();
            var resultado = await Liberar(grupo.Key.PedidoId, grupo.Key.Motivo, lineas, cancelacion).ConfigureAwait(false);
            switch (resultado)
            {
                case ResultadoDeLiberarReserva.Liberada or ResultadoDeLiberarReserva.NoExistia:
                    await Anotar(() => pedidos.QuitarLiberacionesDeReserva(lineas, cancelacion)).ConfigureAwait(false);
                    hechas += lineas.Count;
                    break;
                case ResultadoDeLiberarReserva.YaCerrada:
                    // Una entrega ganó: la reserva ya se cerró. Es un cierre confirmado, no hay nada más que liberar.
                    await Anotar(() => pedidos.QuitarLiberacionesDeReserva(lineas, cancelacion)).ConfigureAwait(false);
                    await PublicarAlerta(AlertaLiberacionYaCerrada, cancelacion).ConfigureAwait(false);
                    hechas += lineas.Count;
                    break;
                default:
                    await Anotar(() => pedidos.AnotarIntentoDeLiberacion(lineas, cancelacion)).ConfigureAwait(false);
                    pendientes += lineas.Count;
                    break;
            }
        }

        return (hechas, pendientes);
    }

    private async Task<ResultadoDeLiberarReserva> Liberar(Guid pedidoId, string motivo, IReadOnlyList<Guid> lineas, CancellationToken cancelacion)
    {
        try
        {
            return await reservas.Liberar(pedidoId, motivo, lineas, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeLiberarReserva.NoSePudo;
        }
    }

    private static async Task Anotar(Func<Task> anotacion)
    {
        try
        {
            await anotacion().ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Si no se puede anotar, la fila queda y la próxima corrida repite (la liberación es idempotente por estado).
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
            // Una alerta que no se publica nunca interrumpe la revisión.
        }
    }
}
