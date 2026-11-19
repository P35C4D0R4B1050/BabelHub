using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Application.Pedidos.Interno;

/// <summary>Lo que dejó la revisión: líneas no recogidas sin anular, líneas impresas sin estado final y estados de traslado purgados.</summary>
public sealed record ResultadoDeLaRevisionSinEstadoFinal(int NoRecogidasSinAnular, int ImpresasSinEstadoFinal, int EstadosDeTrasladoPurgados);

/// <summary>
/// La revisión diaria de lo que no se resuelve y la purga de los estados de traslado (T27; PED-15, AUD-31): la corre el trabajo de fondo como actor «sistema». Publica una <b>alerta técnica</b> (sin datos del pedido) si una línea <b>no recogida</b> lleva más de 30 días sin anularse o entregarse, o si una línea impresa lleva más de 90 días desde que se creó el pedido sin estado final: la retención del contacto
/// no tiene plazo si el pedido nunca llega a entregado o anulado. Y <b>purga a los 90 días</b> los estados de traslado recibidos (<c>estados_de_traslado_recibidos</c>), que solo sirven para la idempotencia de la entrada. Nunca modifica un pedido; repetirla no cambia nada.
/// </summary>
internal sealed class RevisarPedidosSinEstadoFinal(AutorizadorDePedidos autorizador, IRepositorioDePedidos pedidos, IPublicadorDeAlertasTecnicas alertas, IReloj reloj)
{
    public static readonly TimeSpan NoRecogidaSinAnularDurante = TimeSpan.FromDays(30);

    public static readonly TimeSpan SinEstadoFinalDurante = TimeSpan.FromDays(90);

    public static readonly TimeSpan RetencionDeEstadosDeTraslado = TimeSpan.FromDays(90);

    /// <summary>El motivo catalogado más cercano: el catálogo no tiene uno propio de estas dos condiciones.</summary>
    public const string AlertaOperacionPendiente = "pedido-operacion-fallida";

    public async Task<Resultado<ResultadoDeLaRevisionSinEstadoFinal>> Revisar(CancellationToken cancelacion = default)
    {
        if (await autorizador.ExigirSistema(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return Resultado<ResultadoDeLaRevisionSinEstadoFinal>.Fallo(rechazo);
        }

        var ahora = reloj.Ahora;
        try
        {
            var noRecogidas = await pedidos.ContarNoRecogidasSinAnularDesde(ahora - NoRecogidaSinAnularDurante, cancelacion).ConfigureAwait(false);
            var sinFinal = await pedidos.ContarImpresasSinEstadoFinalDesde(ahora - SinEstadoFinalDurante, cancelacion).ConfigureAwait(false);
            var purgados = await pedidos.PurgarEstadosDeTraslado(ahora - RetencionDeEstadosDeTraslado, cancelacion).ConfigureAwait(false);
            if (noRecogidas > 0 || sinFinal > 0)
            {
                await Alertar(cancelacion).ConfigureAwait(false);
            }

            return Resultado<ResultadoDeLaRevisionSinEstadoFinal>.Exito(new ResultadoDeLaRevisionSinEstadoFinal(noRecogidas, sinFinal, purgados));
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return Resultado<ResultadoDeLaRevisionSinEstadoFinal>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, ClavesDePedidos.ServicioNoDisponible));
        }
    }

    private async Task Alertar(CancellationToken cancelacion)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(AlertaOperacionPendiente, reloj.Ahora), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            // Una alerta que no se publica nunca interrumpe la revisión.
        }
    }
}
