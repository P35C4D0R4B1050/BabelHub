using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Pagos;

namespace BabelHub.Ecommerce.Application.Pagos.Interno;

/// <summary>Cuánto retomó cada corrida de <see cref="ReanudarComprasAprobadas"/>.</summary>
internal sealed record ResultadoDeReanudar(int Compensaciones, int Compras, int Reembolsos);

/// <summary>
/// La retoma (T20, D23, PAG-18, PAG-20): lo que quedó a medias cuando un proceso cayó o una falla se reintenta. (1) Toda <b>compensación ya iniciada</b> se termina de inmediato, sin esperar los 30 minutos. (2) Toda intención
/// <c>aprobada</c> sin completar se retoma por el único punto de entrada del estado aprobado, <b>respetando la reclamación</b> (no compite con un orquestador en vuelo: una reclamación vigente se salta) y, si pasados 30 minutos
/// desde <c>aprobado_en</c> sigue sin completarse, se compensa como falla posterior al pago con una alerta técnica. (3) Todo reembolso <c>solicitado</c> huérfano se reanuda consultando a la pasarela qué aplicó. Una falla con un
/// elemento no detiene a los demás.
/// </summary>
internal sealed class ReanudarComprasAprobadas(
    IRepositorioDePagos repositorio,
    IRepositorioDeReembolsos reembolsos,
    IUnidadDeTrabajo unidad,
    IConfirmadorDeCompraAprobada confirmador,
    ICompensadorDeCompra compensador,
    ProcesarReembolso procesarReembolso,
    IPublicadorDeAlertasTecnicas alertas,
    IReloj reloj)
{
    /// <summary>Pasado este tiempo desde <c>aprobado_en</c>, una compra que no se pudo completar se compensa.</summary>
    public static readonly TimeSpan EsperaAntesDeCompensar = PlazosDelPago.PlazoDePendiente;

    public const string MotivoDeAlertaCompraSinCompletar = "compra-aprobada-sin-completar";
    public const string MotivoDeAlertaRetomaFallida = "retoma-de-pagos-fallida";
    public const string MotivoDeAlertaCompraSinReferencia = "compra-aprobada-sin-referencia";

    public async Task<ResultadoDeReanudar> Ejecutar(int limite, CancellationToken cancelacion = default)
    {
        var compensaciones = await ReanudarCompensaciones(limite, cancelacion).ConfigureAwait(false);
        var compras = await ReanudarCompras(limite, cancelacion).ConfigureAwait(false);
        var huerfanos = await ReanudarReembolsos(limite, cancelacion).ConfigureAwait(false);
        return new ResultadoDeReanudar(compensaciones, compras, huerfanos);
    }

    private async Task<int> ReanudarCompensaciones(int limite, CancellationToken cancelacion)
    {
        var pendientes = await unidad.Ejecutar(token => repositorio.ListarCompensacionesSinTerminar(limite, token), cancelacion).ConfigureAwait(false);
        foreach (var compensacion in pendientes)
        {
            await Intentar(() => compensador.Compensar(compensacion.IntencionId, compensacion.IntentoId, compensacion.Motivo, compensacion.Variante, cancelacion)).ConfigureAwait(false);
        }

        return pendientes.Count;
    }

    private async Task<int> ReanudarCompras(int limite, CancellationToken cancelacion)
    {
        var ids = await unidad.Ejecutar(token => repositorio.ListarIntencionesAprobadas(limite, token), cancelacion).ConfigureAwait(false);
        foreach (var pedidoId in ids)
        {
            await Intentar(() => ReanudarUna(pedidoId, cancelacion)).ConfigureAwait(false);
        }

        return ids.Count;
    }

    private async Task ReanudarUna(Guid pedidoId, CancellationToken cancelacion)
    {
        var (intencion, intento) = await Leer(pedidoId, cancelacion).ConfigureAwait(false);
        if (intencion is not { Estado: EstadoDeIntencion.Aprobada } || intento is null)
        {
            return;
        }

        if (intento.ReferenciaDePasarela is not { } referencia)
        {
            // Un intento aprobado sin la referencia del cargo no se puede retomar: se alerta para conciliar a mano.
            await Alertar(MotivoDeAlertaCompraSinReferencia).ConfigureAwait(false);
            return;
        }

        // Un orquestador en vuelo la tiene reclamada: no se compite con él.
        if (intencion.EstaReclamada(reloj.Ahora))
        {
            return;
        }

        // Por el único punto de entrada del estado aprobado: clasifica (tardía, desvinculada, cancelación), confirma reservas, completa o compensa.
        await confirmador.Confirmar(pedidoId, intento.Id, referencia, CancellationToken.None).ConfigureAwait(false);

        (intencion, _) = await Leer(pedidoId, cancelacion).ConfigureAwait(false);
        // Si otro ejecutor la reclamó mientras tanto (la está completando), no se compensa por encima de él.
        if (intencion is { Estado: EstadoDeIntencion.Aprobada, AprobadoEn: { } aprobadoEn } && !intencion.EstaReclamada(reloj.Ahora) && reloj.Ahora - aprobadoEn >= EsperaAntesDeCompensar)
        {
            // Sigue sin completarse a los 30 minutos: se compensa y se reembolsa (PAG-20).
            await Alertar(MotivoDeAlertaCompraSinCompletar).ConfigureAwait(false);
            await compensador.Compensar(pedidoId, intento.Id, MotivoDeCompensacion.FallaPosteriorAlPago, VarianteDeCompensacion.Completa, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task<int> ReanudarReembolsos(int limite, CancellationToken cancelacion)
    {
        var ids = await unidad.Ejecutar(token => reembolsos.ListarHuerfanos(reloj.Ahora, limite, token), cancelacion).ConfigureAwait(false);
        foreach (var id in ids)
        {
            await Intentar(() => procesarReembolso.Reanudar(id, cancelacion)).ConfigureAwait(false);
        }

        return ids.Count;
    }

    private Task<(IntencionDePago? Intencion, IntentoDeCobro? Intento)> Leer(Guid pedidoId, CancellationToken cancelacion) =>
        unidad.Ejecutar(
            async token =>
            {
                var intencion = await repositorio.ObtenerIntencion(pedidoId, paraActualizar: false, token).ConfigureAwait(false);
                var intento = intencion is null ? null : await repositorio.ObtenerIntentoActivo(pedidoId, token).ConfigureAwait(false);
                return (intencion, intento);
            },
            cancelacion);

    /// <summary>Una falla con un elemento no detiene a los demás: se alerta y la próxima corrida lo reintenta.</summary>
    private async Task Intentar(Func<Task> accion)
    {
        try
        {
            await accion().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await Alertar(MotivoDeAlertaRetomaFallida).ConfigureAwait(false);
        }
    }

    private async Task Alertar(string motivo)
    {
        try
        {
            await alertas.Publicar(new AlertaTecnica(motivo, reloj.Ahora), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Una alerta que no sale no interrumpe la retoma.
        }
    }
}
