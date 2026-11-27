using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// El Inventario visto por Carrito sobre <see cref="IClienteDelInventario"/> de <c>Plataforma</c> (T25; CAR-05, CAR-06): ese cliente pone el plazo de 10 segundos, las credenciales de cada alcance y el token del usuario, y <b>este adaptador no toca
/// el token ni abre un <c>HttpClient</c></b> (regla 9 y decisión D01). Implementa <b>la disponibilidad por lotes de a lo más 50</b> (con la validación de la respuesta, SEG-13: nada fuera de lo pedido, sin repetidos, sin cantidades negativas
/// y sin edición omitida) y <b>la lista de sucursales con su última lista conocida</b> (hasta 10 minutos, R05). Y (T25b) <b>la consulta previa, <c>reservas.crear</c> (con la clave <c>res-pedido-generación</c>) y <c>reservas.liberar</c></b>, validando cada respuesta contra lo pedido (SEG-13: el
/// pedido, las líneas y cantidades exactas, una fuente y la expiración; si no, «sin respuesta» y nunca una reserva o stock inventados) y traduciendo cada código del contrato a su tipo. Una falla del Inventario nunca es una excepción. Se registra por alcance de solicitud:
/// la última lista de sucursales vive en el singleton <see cref="MemoriaDeSucursalesDelCarrito"/>.
/// </summary>
public sealed class RestInventarioDelCarrito(IClienteDelInventario cliente, IReloj reloj, MemoriaDeSucursalesDelCarrito? memoria = null) : IInventarioDelCarrito
{
    public const int MaximoDeEdicionesPorLote = 50;

    public static readonly TimeSpan VigenciaDeLaUltimaLista = TimeSpan.FromMinutes(10);

    private readonly MemoriaDeSucursalesDelCarrito _memoria = memoria ?? new MemoriaDeSucursalesDelCarrito();

    public async Task<RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        var pedidas = edicionIds.Distinct().ToList();
        if (pedidas.Count == 0)
        {
            return RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.ConValor(new StockDeVariasEdiciones([], [], reloj.Ahora));
        }

        var ediciones = new List<StockDeEdicion>();
        var noRegistradas = new List<Guid>();
        var consultadaEn = DateTimeOffset.MaxValue;
        foreach (var lote in pedidas.Chunk(MaximoDeEdicionesPorLote))
        {
            ResultadoDelInventario<DisponibilidadDeVariasEdiciones> resultado;
            try
            {
                resultado = await cliente.ConsultarDisponibilidad(lote, cancelacion).ConfigureAwait(false);
            }
            catch (Exception excepcion) when (excepcion is not OperationCanceledException)
            {
                return RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.SinRespuesta;
            }

            switch (resultado.Estado)
            {
                case EstadoDeLlamadaAlInventario.Exitosa:
                    break;
                case EstadoDeLlamadaAlInventario.Rechazada:
                    return RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.Rechazo(resultado.Codigo);
                case EstadoDeLlamadaAlInventario.LimiteExcedido:
                    return RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.Limitada(null);
                default:
                    return RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.SinRespuesta;
            }

            if (!EsValida(resultado.Valor, lote))
            {
                // Una respuesta que el contrato no admite se trata como «sin respuesta»: nunca como stock.
                return RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.SinRespuesta;
            }

            ediciones.AddRange(resultado.Valor.Ediciones.Select(e => new StockDeEdicion(e.EdicionId, e.VendibleEnLaRed, e.Fuentes.ToDictionary(f => f.CodigoDeFuente, f => f.Vendible, StringComparer.Ordinal))));
            noRegistradas.AddRange(resultado.Valor.NoRegistradas);
            consultadaEn = resultado.Valor.Ediciones.Select(e => e.ConsultadaEn > reloj.Ahora ? reloj.Ahora : e.ConsultadaEn).Append(consultadaEn).Min();
        }

        // Sin ninguna edición registrada no hay instante de consulta: la frescura es la de esta llamada.
        return RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.ConValor(new StockDeVariasEdiciones(ediciones, noRegistradas, consultadaEn == DateTimeOffset.MaxValue ? reloj.Ahora : consultadaEn));
    }

    /// <summary>La respuesta debe traer exactamente lo pedido: cada edición una sola vez, en <c>ediciones</c> o en <c>noRegistradas</c>, nunca algo ajeno y sin cantidades negativas (SEG-13).</summary>
    private static bool EsValida(DisponibilidadDeVariasEdiciones respuesta, Guid[] lote)
    {
        if (respuesta.Ediciones is null || respuesta.NoRegistradas is null)
        {
            return false;
        }

        var pedidas = lote.ToHashSet();
        var vistas = new HashSet<Guid>();
        foreach (var edicion in respuesta.Ediciones)
        {
            if (edicion is null || !pedidas.Contains(edicion.EdicionId) || !vistas.Add(edicion.EdicionId) || edicion.VendibleEnLaRed < 0 || !SonValidas(edicion.Fuentes))
            {
                return false;
            }
        }

        foreach (var id in respuesta.NoRegistradas)
        {
            if (!pedidas.Contains(id) || !vistas.Add(id))
            {
                return false;
            }
        }

        return vistas.Count == pedidas.Count;
    }
    /// <summary>El desglose por sucursal debe venir, con cada sucursal una sola vez y sin cantidades negativas (SEG-13).</summary>
    private static bool SonValidas(IReadOnlyList<FuenteConVendible>? fuentes) =>
        fuentes is not null && fuentes.All(f => f is { CodigoDeFuente.Length: > 0, Vendible: >= 0 }) && fuentes.Select(f => f.CodigoDeFuente).Distinct(StringComparer.Ordinal).Count() == fuentes.Count;


    public async Task<ListaDeSucursales?> ListarSucursales(CancellationToken cancelacion = default)
    {
        ResultadoDelInventario<IReadOnlyList<SucursalPublica>> resultado;
        try
        {
            resultado = await cliente.ListarSucursales(cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return DeRespaldo();
        }

        if (resultado.Exitosa && resultado.Valor is { } sucursales && sucursales.All(s => s is { Codigo.Length: > 0, Nombre.Length: > 0, Region.Length: > 0 }))
        {
            var ahora = reloj.Ahora;
            var lista = sucursales.Select(s => new SucursalDeEntrega(s.Codigo, s.Nombre, s.Region)).ToList();
            _memoria.Guardar(lista, ahora);

            return new ListaDeSucursales(lista, ahora, EsDeRespaldo: false);
        }

        return DeRespaldo();
    }

    /// <summary>La última lista conocida si tiene a lo más 10 minutos; si no, <see langword="null"/> (no hay lista ni respaldo).</summary>
    private ListaDeSucursales? DeRespaldo()
    {
        return _memoria.Leer() is { } ultima && reloj.Ahora - ultima.ObtenidaEn <= VigenciaDeLaUltimaLista ? new ListaDeSucursales(ultima.Lista, ultima.ObtenidaEn, EsDeRespaldo: true) : null;
    }

    // ---------- T25b: consulta previa, reservar y liberar ----------

    public async Task<RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>> ConsultarPrevia(EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        ArgumentNullException.ThrowIfNull(lineas);
        ResultadoDelInventario<RespuestaDeConsultaPrevia> resultado;
        try
        {
            resultado = await cliente.ConsultarPrevia(new ConsultaPrevia(Tipo(entrega.Tipo), null, entrega.RegionDeEntrega, [.. lineas.Select(l => new LineaDeSolicitud(l.LineaId, l.EdicionId, l.Cantidad))]), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.SinRespuesta;
        }

        switch (resultado.Estado)
        {
            case EstadoDeLlamadaAlInventario.Exitosa:
                break;
            case EstadoDeLlamadaAlInventario.Rechazada:
                return RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.Rechazo(resultado.Codigo);
            case EstadoDeLlamadaAlInventario.LimiteExcedido:
                return RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.Limitada(resultado.Limite?.DisponibleDesde);
            default:
                return RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.SinRespuesta;
        }

        // Una línea de más, de menos, repetida o con una edición distinta de la pedida se trata como «sin respuesta»: nunca como stock (SEG-13).
        var respuesta = resultado.Valor;
        if (respuesta.Lineas is null || respuesta.Lineas.Count != lineas.Count || respuesta.Lineas.Any(l => l is null || l.DisponibleEnLaRed < 0 || (l.Disponible && string.IsNullOrWhiteSpace(l.CodigoDeFuente)))
            || !CoincidenConLoPedido(lineas, respuesta.Lineas.Select(l => (l.LineaId, l.EdicionId))))
        {
            return RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.SinRespuesta;
        }

        var consultadaEn = respuesta.ConsultadaEn > reloj.Ahora ? reloj.Ahora : respuesta.ConsultadaEn;
        return RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.ConValor(new ConsultaPreviaDelInventario(
            [.. respuesta.Lineas.Select(l => new SeleccionDeLinea(l.LineaId, l.EdicionId, l.Disponible, l.CodigoDeFuente, l.RequiereTraslado, l.PlazoEstimadoDias, l.DisponibleEnLaRed))],
            consultadaEn));
    }

    public async Task<ResultadoDeReservar> Reservar(Guid pedidoId, int generacionDeReserva, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        ArgumentNullException.ThrowIfNull(lineas);
        ResultadoDelInventario<ReservaDelInventario> resultado;
        try
        {
            var solicitud = new SolicitudDeReservaDeStock(pedidoId, Tipo(entrega.Tipo), null, entrega.RegionDeEntrega, [.. lineas.Select(l => new LineaDeSolicitud(l.LineaId, l.EdicionId, l.Cantidad))]);
            resultado = await cliente.Reservar(solicitud, ClaveDeReserva.Para(pedidoId, generacionDeReserva), cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeReservar.De(TipoDeResultadoDeReservar.NoDisponible);
        }

        switch (resultado.Estado)
        {
            case EstadoDeLlamadaAlInventario.Exitosa:
                return TraducirReserva(resultado.Valor, pedidoId, lineas);
            case EstadoDeLlamadaAlInventario.LimiteExcedido:
                return ResultadoDeReservar.De(TipoDeResultadoDeReservar.LimiteExcedido, resultado.Codigo, resultado.Limite?.DisponibleDesde);
            case EstadoDeLlamadaAlInventario.Rechazada:
                return TraducirRechazoDeReservar(resultado);
            default:
                return ResultadoDeReservar.De(TipoDeResultadoDeReservar.NoDisponible);
        }
    }

    public async Task<ResultadoDeLiberar> Liberar(Guid pedidoId, string motivo, CancellationToken cancelacion = default)
    {
        ResultadoDelInventario<ReservaDelInventario> resultado;
        try
        {
            resultado = await cliente.Liberar(pedidoId, motivo, null, cancelacion).ConfigureAwait(false);
        }
        catch (Exception excepcion) when (excepcion is not OperationCanceledException)
        {
            return ResultadoDeLiberar.NoSePudo;
        }

        return resultado.Estado switch
        {
            EstadoDeLlamadaAlInventario.Exitosa => ResultadoDeLiberar.Liberada,
            EstadoDeLlamadaAlInventario.Rechazada when resultado.Codigo == CodigosDelInventario.NoEncontrado => ResultadoDeLiberar.NoExistia,
            EstadoDeLlamadaAlInventario.Rechazada when resultado.Codigo == CodigosDelInventario.ReservaEstadoInvalido => ResultadoDeLiberar.PedidoConfirmado,
            _ => ResultadoDeLiberar.NoSePudo,
        };
    }

    private static TipoDeEntregaDeLaReserva Tipo(TipoDeEntrega tipo) =>
        tipo == TipoDeEntrega.Domicilio ? TipoDeEntregaDeLaReserva.Domicilio : TipoDeEntregaDeLaReserva.Recojo;

    private static bool CoincidenConLoPedido(IReadOnlyList<LineaParaElInventario> pedidas, IEnumerable<(Guid LineaId, Guid EdicionId)> devueltas)
    {
        var esperadas = pedidas.ToDictionary(l => l.LineaId, l => l.EdicionId);
        var vistas = new HashSet<Guid>();
        foreach (var (lineaId, edicionId) in devueltas)
        {
            if (!esperadas.TryGetValue(lineaId, out var edicion) || edicion != edicionId || !vistas.Add(lineaId))
            {
                return false;
            }
        }

        return vistas.Count == esperadas.Count;
    }

    /// <summary>Una reserva válida es del pedido, trae su expiración y exactamente las líneas y cantidades pedidas, cada una con una fuente; cualquier otra cosa es «sin respuesta» (SEG-13).</summary>
    private static ResultadoDeReservar TraducirReserva(ReservaDelInventario reserva, Guid pedidoId, IReadOnlyList<LineaParaElInventario> pedidas)
    {
        if (reserva is null || reserva.PedidoId != pedidoId || reserva.ExpiraEn is not { } expiraEn || reserva.Lineas is null || reserva.Lineas.Count != pedidas.Count
            || reserva.Lineas.Any(l => l is null || string.IsNullOrWhiteSpace(l.CodigoDeFuente))
            || !CoincidenConLoPedido(pedidas, reserva.Lineas.Select(l => (l.LineaId, l.EdicionId))))
        {
            return ResultadoDeReservar.De(TipoDeResultadoDeReservar.NoDisponible);
        }

        var cantidades = pedidas.ToDictionary(l => l.LineaId, l => l.Cantidad);
        if (reserva.Lineas.Any(l => l.Cantidad != cantidades[l.LineaId]))
        {
            return ResultadoDeReservar.De(TipoDeResultadoDeReservar.NoDisponible);
        }

        // El contrato no distingue la repetición de la creación: la expiración que devuelve es la que manda (DD-03).
        return ResultadoDeReservar.Reservada(new ReservaObtenida(
            reserva.PedidoId,
            expiraEn,
            [.. reserva.Lineas.Select(l => new SeleccionDeLinea(l.LineaId, l.EdicionId, true, l.CodigoDeFuente, l.RequiereTraslado, l.PlazoEstimadoDias, l.Cantidad))],
            EsRepeticion: false));
    }

    private static ResultadoDeReservar TraducirRechazoDeReservar(ResultadoDelInventario resultado) => resultado.Codigo switch
    {
        CodigosDelInventario.StockInsuficiente when resultado.LineasSinStock is { Count: > 0 } sin =>
            ResultadoDeReservar.Sin([.. sin.Select(l => new LineaSinStock(l.LineaId, l.EdicionId, l.Solicitada, l.DisponibleEnLaRed))]),
        CodigosDelInventario.IdempotenciaEnCurso => ResultadoDeReservar.De(TipoDeResultadoDeReservar.EnCurso, resultado.Codigo),
        CodigosDelInventario.IdempotenciaEnConflicto => ResultadoDeReservar.De(TipoDeResultadoDeReservar.ConflictoDeIdempotencia, resultado.Codigo),
        CodigosDelInventario.ReservaEstadoInvalido => ResultadoDeReservar.De(TipoDeResultadoDeReservar.EstadoInvalido, resultado.Codigo),
        _ => ResultadoDeReservar.De(TipoDeResultadoDeReservar.Rechazada, resultado.Codigo),
    };

    private static class CodigosDelInventario
    {
        public const string NoEncontrado = "INV-NO-ENCONTRADO";
        public const string StockInsuficiente = "INV-STOCK-INSUFICIENTE";
        public const string IdempotenciaEnCurso = "INV-IDEMPOTENCIA-EN-CURSO";
        public const string IdempotenciaEnConflicto = "INV-IDEMPOTENCIA-EN-CONFLICTO";
        public const string ReservaEstadoInvalido = "INV-RESERVA-ESTADO-INVALIDO";
    }
}
