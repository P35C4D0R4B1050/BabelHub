using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

internal sealed class ValidadorDeConsultarSeleccion : IValidador<ConsultarSeleccionEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarSeleccionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeReservas.Entrega(errores, entrada.TipoDeEntrega, entrada.CodigoDeSucursalDeRecojo, entrada.RegionDeEntrega, FormaDeLaSolicitud.AlgunaDeclaraSucursal(entrada.Lineas));
        ReglasDeEntradaDeReservas.Lineas(errores, entrada.Lineas);
        return ReglasDeEntradaDeReservas.Resultado(errores);
    }
}

internal sealed class ValidadorDeConsultarReserva : IValidador<ConsultarReservaEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarReservaEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        return entrada.PedidoId == Guid.Empty
            ? ResultadoDeValidacion.Invalido([new ErrorDeCampo("pedidoId", ConstructorDeValidacion.Obligatorio)])
            : ResultadoDeValidacion.Valido;
    }
}

/// <summary>
/// La consulta previa de la selección de fuentes de un carrito (SEL-20): para cada <b>línea impresa</b>, la fuente que se seleccionaría, si implica traslado y su plazo, o la falta de stock; por sucursal, si la declarada la cubre (SEL-44). <b>Solo lee</b>: no
/// reserva, no libera vencidas (las lee como vendible, FUE-03), no cuenta para los límites de reservas y no toma bloqueos. Las líneas digitales se ignoran. La llamada llega sin token del cliente (la sesión se exige solo al
/// pagar, CAR-16) con la credencial de servicio de lectura y el límite general; aquí basta un actor con contexto. <b>No expone cantidades de la red</b> (<c>DisponibleEnLaRed</c> va en cero): un recorrido de esta
/// consulta no debe servir para medir el inventario de la red.
/// </summary>
internal sealed class ConsultarSeleccion(
    AutorizadorDeReservas autorizador,
    IValidador<ConsultarSeleccionEntrada> validador,
    IFichasDeEdiciones fichas,
    IExistencias existencias,
    SelectorDeFuentes selector,
    IReloj reloj) : ICasoDeUso<ConsultarSeleccionEntrada, SeleccionConsultada>
{
    public async Task<Resultado<SeleccionConsultada>> Ejecutar(ConsultarSeleccionEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirContexto(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<SeleccionConsultada>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<SeleccionConsultada>(validacion);
        }

        // Solo las impresas: una digital no tiene stock y se ignora; una desconocida es no encontrada.
        var impresas = new List<LineaSolicitada>();
        foreach (var linea in entrada.Lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores))
        {
            var ficha = await fichas.PorId(linea.EdicionId, cancelacion).ConfigureAwait(false);
            if (ficha is null)
            {
                return FallosDeReservas.De<SeleccionConsultada>(CategoriaDeError.NoEncontrado, ClavesDeReservas.EdicionNoEncontrada);
            }

            if (ficha.EsImpresa)
            {
                impresas.Add(linea);
            }
        }

        if (FormaDeLaSolicitud.Comprobar(entrada.TipoDeEntrega, entrada.CodigoDeSucursalDeRecojo, impresas, out var porSucursal) is { } errorDeForma)
        {
            return FallosDeReservas.Validacion<SeleccionConsultada>(ResultadoDeValidacion.Invalido([errorDeForma]));
        }

        // Sin líneas impresas no hay nada que seleccionar: la sucursal que declare una digital se ignora (SEL-20).
        if (impresas.Count == 0 && FormaDeLaSolicitud.AlgunaDeclaraSucursal(entrada.Lineas))
        {
            return Resultado<SeleccionConsultada>.Exito(new SeleccionConsultada([], reloj.Ahora));
        }

        if (porSucursal)
        {
            // La asignación sigue el orden de la solicitud (SEL-43), no el de las ediciones.
            var identificadores = impresas.Select(l => l.LineaId).ToHashSet();
            return await PorSucursal([.. entrada.Lineas.Where(l => identificadores.Contains(l.LineaId))], cancelacion).ConfigureAwait(false);
        }

        var destino = await selector.Resolver(entrada.TipoDeEntrega, entrada.CodigoDeSucursalDeRecojo, entrada.RegionDeEntrega, cancelacion).ConfigureAwait(false);
        if (destino is null)
        {
            return FallosDeReservas.Validacion<SeleccionConsultada>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("codigoDeSucursalDeRecojo", "inexistente")]));
        }

        var filasPorEdicion = await Vendibles(impresas, cancelacion).ConfigureAwait(false);

        var lineas = new List<SeleccionDeLinea>(impresas.Count);
        var asignado = new Dictionary<(Guid FuenteId, Guid EdicionId), int>();
        foreach (var linea in impresas)
        {
            var seleccion = selector.Seleccionar(destino, linea.Cantidad, filasPorEdicion[linea.EdicionId], asignado);
            if (seleccion.FuenteId is { } fuenteId)
            {
                asignado[(fuenteId, linea.EdicionId)] = asignado.GetValueOrDefault((fuenteId, linea.EdicionId)) + linea.Cantidad;
            }

            lineas.Add(new SeleccionDeLinea(linea.LineaId, linea.EdicionId, seleccion.HayStock, seleccion.Codigo, seleccion.ImplicaTraslado, seleccion.PlazoEnDias, DisponibleEnLaRed: 0));
        }

        return Resultado<SeleccionConsultada>.Exito(new SeleccionConsultada(lineas, reloj.Ahora));
    }

    /// <summary>Por sucursal (SEL-44): si la sucursal de cada línea la cubre, con la misma asignación en orden de reservar; sin traslado ni plazo, y la falta de stock con el vendible de esa sucursal.</summary>
    private async Task<Resultado<SeleccionConsultada>> PorSucursal(IReadOnlyList<LineaSolicitada> impresas, CancellationToken cancelacion)
    {
        if (await selector.Sucursales(impresas.Select(l => l.CodigoDeSucursal!), cancelacion).ConfigureAwait(false) is not { } sucursales)
        {
            return FallosDeReservas.Validacion<SeleccionConsultada>(ResultadoDeValidacion.Invalido([FormaDeLaSolicitud.SucursalInexistente]));
        }

        var filas = (await Vendibles(impresas, cancelacion).ConfigureAwait(false)).SelectMany(f => f.Value);
        var lineas = impresas.Zip(SelectorDeFuentes.AsignarPorSucursal(impresas, sucursales, filas))
            .Select(par => new SeleccionDeLinea(
                par.First.LineaId,
                par.First.EdicionId,
                par.Second.Cubierta,
                par.Second.Cubierta ? par.First.CodigoDeSucursal : null,
                RequiereTraslado: false,
                PlazoEstimadoEnDias: null,
                DisponibleEnLaRed: 0,
                DisponibleEnLaSucursal: par.Second.Cubierta ? null : par.Second.VendibleEnLaSucursal))
            .ToList();

        return Resultado<SeleccionConsultada>.Exito(new SeleccionConsultada(lineas, reloj.Ahora));
    }

    /// <summary>El vendible de cada edición por fuente, como filas sin reservado para reutilizar la selección de reservar; ya cuenta lo vencido sin barrer (FUE-03).</summary>
    private async Task<IReadOnlyDictionary<Guid, IReadOnlyList<ExistenciaBloqueada>>> Vendibles(IReadOnlyList<LineaSolicitada> impresas, CancellationToken cancelacion)
    {
        var ediciones = impresas.Select(l => l.EdicionId).Distinct().ToList();
        if (ediciones.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<ExistenciaBloqueada>>();
        }

        var disponibilidad = (await existencias.Disponibilidad(ediciones, cancelacion).ConfigureAwait(false)).ToDictionary(d => d.EdicionId);
        return ediciones.ToDictionary(
            e => e,
            e => (IReadOnlyList<ExistenciaBloqueada>)[.. disponibilidad.GetValueOrDefault(e)?.PorFuente.Select(f => new ExistenciaBloqueada(f.FuenteId, e, f.Vendible, 0)) ?? []]);
    }
}

/// <summary>
/// La reserva de un pedido (SEL-20): la ve el <b>cliente dueño</b> o la administración central. El personal de sucursal o de logística recibe 403 (con su evento) antes de leer nada; el cliente de otro pedido,
/// «no encontrado» con el evento de recurso ajeno (no se revela que existe). Una pendiente vencida que nadie barrió se muestra como expirada. No lleva los traslados (los da <c>Traslados</c>).
/// </summary>
internal sealed class ConsultarReserva(
    AutorizadorDeReservas autorizador,
    IValidador<ConsultarReservaEntrada> validador,
    IRepositorioDeReservas reservas,
    ConstructorDeLaReserva constructor,
    IReloj reloj) : ICasoDeUso<ConsultarReservaEntrada, ReservaDelPedido>
{
    public async Task<Resultado<ReservaDelPedido>> Ejecutar(ConsultarReservaEntrada entrada, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrada);

        if (await autorizador.ExigirClienteOAdministracion(cancelacion).ConfigureAwait(false) is { } rechazo)
        {
            return FallosDeReservas.Fallo<ReservaDelPedido>(rechazo);
        }

        var validacion = validador.Validar(entrada);
        if (!validacion.EsValido)
        {
            return FallosDeReservas.Validacion<ReservaDelPedido>(validacion);
        }

        var reserva = await reservas.UltimaDelPedido(entrada.PedidoId, cancelacion).ConfigureAwait(false);
        if (reserva is null)
        {
            return FallosDeReservas.De<ReservaDelPedido>(CategoriaDeError.NoEncontrado, ClavesDeReservas.ReservaNoEncontrada);
        }

        // El cliente solo ve lo suyo; la administración ve todo.
        var actor = autorizador.Actor!;
        if (!actor.EsAdministracionCentral() && reserva.ClienteId != actor.UsuarioId)
        {
            return FallosDeReservas.Fallo<ReservaDelPedido>(await autorizador.PedidoAjeno(cancelacion).ConfigureAwait(false));
        }

        return Resultado<ReservaDelPedido>.Exito(await constructor.Construir(reserva, esRepeticion: false, cancelacion, ahora: reloj.Ahora).ConfigureAwait(false));
    }
}
