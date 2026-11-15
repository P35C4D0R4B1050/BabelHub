using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;
using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>Los fallos esperados del módulo como <see cref="Resultado{T}"/> (la Api los traduce a los códigos <c>INV-…</c>).</summary>
internal static class FallosDeReservas
{
    public static Resultado<T> Fallo<T>(ErrorDeAplicacion error) => Resultado<T>.Fallo(error);

    public static Resultado<T> Validacion<T>(ResultadoDeValidacion validacion) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Validacion, ClavesDeReservas.ValidacionFallida, validacion.Errores));

    public static Resultado<T> De<T>(CategoriaDeError categoria, string clave) => Resultado<T>.Fallo(new ErrorDeAplicacion(categoria, clave));

    public static Resultado<T> BloqueoOcupado<T>() => De<T>(CategoriaDeError.NoDisponible, ClavesDeReservas.BloqueoOcupado);

    public static Resultado<T> StockInsuficiente<T>(IEnumerable<LineaSinStock> lineas) =>
        Resultado<T>.Fallo(new ErrorDeAplicacion(CategoriaDeError.Conflicto, ClavesDeReservas.StockInsuficiente, [.. lineas.Select(DetalleDeFaltaDeStock.Codificar)]));
}

/// <summary>
/// Reserva los impresos de un pedido: todas las líneas o ninguna (SEL-01, SEL-05). En una sola transacción y en el orden de la jerarquía de bloqueos (<c>inventario-fuentes</c> D04): bloquea al cliente y al pedido,
/// valida que toda edición exista y sea impresa (SEL-12), pide <c>Bloquear</c> las filas de <b>todas las fuentes</b> de las ediciones (que libera las vencidas), elige la fuente de cada línea con la estrategia sobre esas
/// filas ya confirmadas y aplica <c>Aplicar(0, +cantidad)</c> agregado y ordenado por (edición, fuente). Si alguna línea no tiene stock termina en fallo y <c>TransaccionPorCasoDeUso</c> deshace todo: ninguna otra línea
/// queda reservada (AC-SEL-05). El cliente sale del actor verificado: nunca del cuerpo (D16). Este es el núcleo: la idempotencia, los límites y el reemplazo de una reserva pendiente los agregan T11 a T14; hasta
/// entonces un pedido que ya tiene reserva se rechaza con <c>reserva-existente</c>.
/// </summary>
internal sealed class ReservarUnidades(
    AutorizadorDeReservas autorizador,
    IValidador<ReservarEntrada> validador,
    IBloqueosAsesores bloqueos,
    IRepositorioDeReservas reservas,
    IFichasDeEdiciones fichas,
    IMovimientosDeStock stock,
    SelectorDeFuentes selector,
    ConstructorDeLaReserva constructor,
    IReloj reloj) : ICasoDeUso<ReservarEntrada, ReservaDelPedido>
{
    public async Task<Resultado<ReservaDelPedido>> Ejecutar(ReservarEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirCliente(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ReservaDelPedido>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<ReservaDelPedido>(validacion);
        }

        // ExigirCliente garantizó el identificador: sale del actor, nunca de la entrada (D16).
        var clienteId = autorizador.Actor!.UsuarioId!.Value;

        try
        {
            return await Reservar(entrada, clienteId, cancelacion).ConfigureAwait(false);
        }
        catch (EsperaDeBloqueoAgotadaException)
        {
            return FallosDeReservas.BloqueoOcupado<ReservaDelPedido>();
        }
        catch (ReservaDuplicadaException)
        {
            // Una cabecera o una línea activa concurrente que el bloqueo del pedido ya debería impedir: conflicto, nunca un error del servidor.
            return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaExistente);
        }
    }

    private async Task<Resultado<ReservaDelPedido>> Reservar(ReservarEntrada entrada, Guid clienteId, CancellationToken cancelacion)
    {
        // Jerarquía D04: cliente → pedido → filas de stock. La reserva de otro cliente sobre el mismo pedido se detecta ya con el bloqueo del pedido tomado.
        await bloqueos.Cliente(clienteId, cancelacion).ConfigureAwait(false);
        await bloqueos.Pedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);

        var anterior = await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (anterior is not null)
        {
            return anterior.ClienteId != clienteId
                ? FallosDeReservas.Fallo<ReservaDelPedido>(await autorizador.PedidoAjeno(cancelacion).ConfigureAwait(false))
                : FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.Conflicto, ClavesDeReservas.ReservaExistente);
        }

        if (await ValidarEdiciones(entrada, cancelacion).ConfigureAwait(false) is { } falloDeEdicion)
        {
            return falloDeEdicion;
        }

        var destino = await selector.Resolver(entrada.TipoDeEntrega, entrada.CodigoDeSucursalDeRecojo, entrada.RegionDeEntrega, cancelacion).ConfigureAwait(false);
        if (destino is null)
        {
            return FallosDeReservas.Validacion<ReservaDelPedido>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("codigoDeSucursalDeRecojo", "inexistente")]));
        }

        var lineas = entrada.Lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores).ToList();
        var bloqueo = await stock.Bloquear([.. lineas.Select(l => l.EdicionId)], cancelacion).ConfigureAwait(false);
        if (!bloqueo.EstaBloqueado)
        {
            // La ficha cambió entre la comprobación y el bloqueo: se trata como edición no impresa.
            return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.ReglaDeNegocio, ClavesDeReservas.EdicionNoImpresa);
        }

        var seleccionadas = new List<(LineaSolicitada Linea, Guid FuenteId)>(lineas.Count);
        var sinStock = new List<LineaSinStock>();
        var asignado = new Dictionary<(Guid FuenteId, Guid EdicionId), int>();
        foreach (var linea in lineas)
        {
            var seleccion = selector.Seleccionar(destino, linea.Cantidad, bloqueo.Filas.Where(f => f.EdicionId == linea.EdicionId), asignado);
            if (seleccion.FuenteId is not { } fuenteId)
            {
                sinStock.Add(new LineaSinStock(linea.LineaId, linea.EdicionId, linea.Cantidad, seleccion.DisponibleEnLaRed));
                continue;
            }

            asignado[(fuenteId, linea.EdicionId)] = asignado.GetValueOrDefault((fuenteId, linea.EdicionId)) + linea.Cantidad;
            seleccionadas.Add((linea, fuenteId));
        }

        if (sinStock.Count > 0)
        {
            // Nada se aplicó todavía: el fallo basta para que la transacción deje todo como estaba (SEL-05).
            return FallosDeReservas.StockInsuficiente<ReservaDelPedido>(sinStock);
        }

        if (await AplicarLosMovimientos(seleccionadas, bloqueo, cancelacion).ConfigureAwait(false) is { } falloDeAplicacion)
        {
            return falloDeAplicacion;
        }

        var ahora = reloj.Ahora;
        var reserva = ReservaDePedido.Iniciar(
            Guid.NewGuid(),
            entrada.PedidoId,
            clienteId,
            ConversionesDeReservas.ADominio(entrada.TipoDeEntrega),
            destino.SucursalDeRecojo?.Id,
            entrada.RegionDeEntrega,
            ahora,
            seleccionadas.Select(s => LineaDeReserva.Pendiente(Guid.NewGuid(), s.Linea.LineaId, s.Linea.EdicionId, s.FuenteId, s.Linea.Cantidad, ahora)));

        await reservas.Insertar(reserva, new DatosDeLaSolicitud(entrada.ClaveDeIdempotencia, CargaCanonicaDeReservar.Huella(entrada), null), cancelacion).ConfigureAwait(false);
        return Resultado<ReservaDelPedido>.Exito(await constructor.Construir(reserva, esRepeticion: false, cancelacion).ConfigureAwait(false));
    }

    /// <summary>Toda edición debe existir y ser impresa (SEL-12): una desconocida es «no encontrada» y una digital se rechaza porque los libros digitales no se reservan.</summary>
    private async Task<Resultado<ReservaDelPedido>?> ValidarEdiciones(ReservarEntrada entrada, CancellationToken cancelacion)
    {
        foreach (var linea in entrada.Lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores))
        {
            var ficha = await fichas.PorId(linea.EdicionId, cancelacion).ConfigureAwait(false);
            if (ficha is null)
            {
                return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.NoEncontrado, ClavesDeReservas.EdicionNoEncontrada);
            }

            if (!ficha.EsImpresa)
            {
                return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.ReglaDeNegocio, ClavesDeReservas.EdicionNoImpresa);
            }
        }

        return null;
    }

    /// <summary>Aplica <c>Aplicar(0, +cantidad)</c> por pareja (fuente, edición), en el orden único de bloqueo: con las filas ya bloqueadas ninguna debería fallar, pero la sentencia es la última defensa.</summary>
    private async Task<Resultado<ReservaDelPedido>?> AplicarLosMovimientos(
        List<(LineaSolicitada Linea, Guid FuenteId)> seleccionadas,
        BloqueoDeStock bloqueo,
        CancellationToken cancelacion)
    {
        var movimientos = seleccionadas
            .GroupBy(s => (s.FuenteId, s.Linea.EdicionId))
            .Select(g => new MovimientoDeStock(g.Key.FuenteId, g.Key.EdicionId, 0, checked(g.Sum(s => s.Linea.Cantidad))))
            .OrderBy(m => m.EdicionId, OrdenDeBloqueo.DeIdentificadores)
            .ThenBy(m => m.FuenteId, OrdenDeBloqueo.DeIdentificadores);

        foreach (var movimiento in movimientos)
        {
            var resultado = await stock.Aplicar(movimiento, cancelacion).ConfigureAwait(false);
            switch (resultado.Resultado)
            {
                case ResultadoDeMovimiento.Aplicado:
                    continue;

                case ResultadoDeMovimiento.DeniegadoPorAlcance:
                    return FallosDeReservas.Fallo<ReservaDelPedido>(await autorizador.DenegadoPorAlcance(cancelacion).ConfigureAwait(false));

                case ResultadoDeMovimiento.EdicionInexistenteONoImpresa:
                    return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.ReglaDeNegocio, ClavesDeReservas.EdicionNoImpresa);

                default:
                    var fila = bloqueo.Fila(movimiento.FuenteId, movimiento.EdicionId);
                    var linea = seleccionadas.First(s => s.FuenteId == movimiento.FuenteId && s.Linea.EdicionId == movimiento.EdicionId).Linea;
                    return FallosDeReservas.StockInsuficiente<ReservaDelPedido>([new LineaSinStock(linea.LineaId, linea.EdicionId, linea.Cantidad, Math.Max(0, fila.Fisico - fila.Reservado))]);
            }
        }

        return null;
    }
}
