using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Plataforma;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>
/// El Inventario visto por Carrito sobre <see cref="IClienteDelInventario"/> de <c>Plataforma</c> (T25; CAR-05, CAR-06): ese cliente pone el plazo de 10 segundos, las credenciales de cada alcance y el token del usuario, y <b>este adaptador no toca
/// el token ni abre un <c>HttpClient</c></b> (regla 9 y decisión D01). Hoy implementa de verdad <b>la disponibilidad por lotes de a lo más 50</b> (con la validación de la respuesta, SEG-13: nada fuera de lo pedido, sin repetidos, sin cantidades negativas
/// y sin edición omitida) y <b>la lista de sucursales con su última lista conocida</b> (hasta 10 minutos, R05). La <b>consulta previa, <c>reservas.crear</c> y <c>reservas.liberar</c> aún no existen en <see cref="IClienteDelInventario"/></b> (los agrega el carril de
/// plataforma): hasta entonces fallan cerrado —«sin respuesta» y «no se pudo»— y el checkout no pasa al pago, nunca simula stock; el hallazgo está en <c>progress/hallazgos.md</c>. Una falla del Inventario nunca es una excepción. Se registra por alcance de solicitud:
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

            ediciones.AddRange(resultado.Valor.Ediciones.Select(e => new StockDeEdicion(e.EdicionId, e.VendibleEnLaRed)));
            noRegistradas.AddRange(resultado.Valor.NoRegistradas);
            consultadaEn = resultado.Valor.Ediciones.Select(e => e.ConsultadaEn).Append(consultadaEn).Min();
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
            if (edicion is null || !pedidas.Contains(edicion.EdicionId) || !vistas.Add(edicion.EdicionId) || edicion.VendibleEnLaRed < 0)
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

    // La consulta previa, reservas.crear y reservas.liberar faltan en IClienteDelInventario (carril de plataforma): fallan cerrado.

    public Task<RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>> ConsultarPrevia(EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default) =>
        Task.FromResult(RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.SinRespuesta);

    public Task<ResultadoDeReservar> Reservar(Guid pedidoId, int generacionDeReserva, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDeReservar.De(TipoDeResultadoDeReservar.NoDisponible));

    public Task<ResultadoDeLiberar> Liberar(Guid pedidoId, string motivo, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDeLiberar.NoSePudo);
}
