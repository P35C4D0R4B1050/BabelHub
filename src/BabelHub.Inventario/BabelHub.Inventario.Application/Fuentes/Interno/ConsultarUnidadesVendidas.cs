using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Fuentes;

namespace BabelHub.Inventario.Application.Fuentes.Interno;

/// <summary>
/// Las unidades vendidas de los últimos 30 días (FUE-21, FUE-22): de una fuente (el encargado, la suya) o de toda la red (solo la administración central). No se guardan
/// como un contador: salen de las ventas que entrega <see cref="IOrigenDeUnidadesVendidas"/> (las de pedidos pagados, sin anulados, atribuidas a una sola fuente: la que
/// despacha el pedido a domicilio o la sucursal que lo entrega en recojo, aunque el libro llegue por traslado) menos lo que volvió por devolución
/// (<see cref="IReingresosPorDevolucion"/>). Cada pedido y edición cuenta <b>una sola vez</b> y lo devuelto nunca deja un valor negativo.
/// </summary>
internal sealed class ConsultarUnidadesVendidas(
    AutorizadorDeFuentes autorizador,
    IValidador<ConsultarUnidadesVendidasEntrada> validador,
    IRepositorioDeFuentes fuentes,
    IOrigenDeUnidadesVendidas origen,
    IReingresosPorDevolucion reingresos,
    IReloj reloj) : ICasoDeUso<ConsultarUnidadesVendidasEntrada, UnidadesVendidasConsultadas>
{
    /// <summary>La ventana de FUE-21, en días.</summary>
    public const int DiasDeLaVentana = 30;

    public async Task<Resultado<UnidadesVendidasConsultadas>> Ejecutar(ConsultarUnidadesVendidasEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        Fuente? pedida = null;
        ErrorDeAplicacion? rechazo;
        if (entrada.CodigoDeFuente is null)
        {
            rechazo = await autorizador.ExigirRedCompleta(cancelacion).ConfigureAwait(false);
        }
        else
        {
            // Primero el rol, sin leer nada; la fuente se resuelve solo para comparar el alcance fino.
            rechazo = await autorizador.ExigirAdministracionOEncargado(cancelacion).ConfigureAwait(false);
            if (rechazo is null)
            {
                pedida = Fuente.EsCodigoValido(entrada.CodigoDeFuente)
                    ? await fuentes.PorCodigo(entrada.CodigoDeFuente, cancelacion).ConfigureAwait(false)
                    : null;
                rechazo = await autorizador.ExigirFuente(pedida?.Id, cancelacion).ConfigureAwait(false);
            }
        }

        if (rechazo is not null)
        {
            return FallosDeFuentes.Fallo<UnidadesVendidasConsultadas>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeFuentes.Validacion<UnidadesVendidasConsultadas>(validacion);
        }

        if (entrada.CodigoDeFuente is not null && pedida is null)
        {
            return FallosDeFuentes.NoEncontrada<UnidadesVendidasConsultadas>();
        }

        var hasta = reloj.Ahora;
        var desde = hasta.AddDays(-DiasDeLaVentana);
        var ventas = await origen.Ventas(pedida?.Id, desde, hasta, cancelacion).ConfigureAwait(false);

        // Una vez por pedido y edición (FUE-22): si el puerto repite una pareja, se cuenta la primera, en un orden estable.
        var unicas = ventas
            .Where(v => v.Unidades > 0)
            .OrderBy(v => v.PedidoId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(v => v.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(v => v.FuenteId, OrdenDeBloqueo.DeIdentificadores)
            .ThenByDescending(v => v.Unidades)
            .GroupBy(v => (v.PedidoId, v.EdicionId))
            .Select(g => g.First())
            .ToList();

        var devueltas = unicas.Count == 0
            ? new Dictionary<(Guid, Guid), long>()
            : (await reingresos.PorPedidos([.. unicas.Select(v => v.PedidoId).Distinct()], cancelacion).ConfigureAwait(false))
                .GroupBy(r => (r.PedidoId, r.EdicionId))
                .ToDictionary(g => g.Key, g => g.Sum(r => (long)Math.Max(0, r.Unidades)));

        var porFuente = unicas
            .GroupBy(v => v.FuenteId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(v => Math.Max(0L, v.Unidades - devueltas.GetValueOrDefault((v.PedidoId, v.EdicionId)))));

        var fuentesDeLaConsulta = pedida is not null
            ? [pedida]
            : (await fuentes.Listar(cancelacion).ConfigureAwait(false)).OrderBy(f => f.Codigo, StringComparer.Ordinal).ToList();

        var detalle = fuentesDeLaConsulta
            .Select(f => new UnidadesVendidasDeFuente(f.Codigo, Tope(porFuente.GetValueOrDefault(f.Id))))
            .ToList();

        return Resultado<UnidadesVendidasConsultadas>.Exito(new UnidadesVendidasConsultadas(DiasDeLaVentana, Tope(detalle.Sum(d => (long)d.Unidades)), detalle));
    }

    private static int Tope(long valor) => (int)Math.Min(int.MaxValue, valor);
}
