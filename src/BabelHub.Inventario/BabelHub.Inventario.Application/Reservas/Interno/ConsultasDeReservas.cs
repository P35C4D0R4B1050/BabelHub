using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Application.Plataforma;

namespace BabelHub.Inventario.Application.Reservas.Interno;

internal sealed class ValidadorDeConsultarSeleccion : IValidador<ConsultarSeleccionEntrada>
{
    public ResultadoDeValidacion Validar(ConsultarSeleccionEntrada entrada)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        var errores = new List<ErrorDeCampo>();
        ReglasDeEntradaDeReservas.Entrega(errores, entrada.TipoDeEntrega, entrada.CodigoDeSucursalDeRecojo, entrada.RegionDeEntrega);
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
/// La consulta previa de la selección de fuentes de un carrito (SEL-20): para cada <b>línea impresa</b>, la fuente que se seleccionaría, si implica traslado y su plazo, o la falta de stock. <b>Solo lee</b>: no
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

        var destino = await selector.Resolver(entrada.TipoDeEntrega, entrada.CodigoDeSucursalDeRecojo, entrada.RegionDeEntrega, cancelacion).ConfigureAwait(false);
        if (destino is null)
        {
            return FallosDeReservas.Validacion<SeleccionConsultada>(ResultadoDeValidacion.Invalido([new ErrorDeCampo("codigoDeSucursalDeRecojo", "inexistente")]));
        }

        var disponibilidad = impresas.Count == 0
            ? []
            : (await existencias.Disponibilidad([.. impresas.Select(l => l.EdicionId)], cancelacion).ConfigureAwait(false)).ToDictionary(d => d.EdicionId);

        var lineas = new List<SeleccionDeLinea>(impresas.Count);
        var asignado = new Dictionary<(Guid FuenteId, Guid EdicionId), int>();
        foreach (var linea in impresas)
        {
            // El vendible ya incluye lo vencido sin barrer; se presenta como filas sin reservado para reutilizar la selección de reservar.
            var filas = disponibilidad.GetValueOrDefault(linea.EdicionId)?.PorFuente.Select(f => new ExistenciaBloqueada(f.FuenteId, linea.EdicionId, f.Vendible, 0)) ?? [];
            var seleccion = selector.Seleccionar(destino, linea.Cantidad, filas, asignado);
            if (seleccion.FuenteId is { } fuenteId)
            {
                asignado[(fuenteId, linea.EdicionId)] = asignado.GetValueOrDefault((fuenteId, linea.EdicionId)) + linea.Cantidad;
            }

            lineas.Add(new SeleccionDeLinea(linea.LineaId, linea.EdicionId, seleccion.HayStock, seleccion.Codigo, seleccion.ImplicaTraslado, seleccion.PlazoEnDias, DisponibleEnLaRed: 0));
        }

        return Resultado<SeleccionConsultada>.Exito(new SeleccionConsultada(lineas, reloj.Ahora));
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
