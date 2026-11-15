using BabelHub.Inventario.Application.Fuentes;
using BabelHub.Inventario.Domain.Reservas;

namespace BabelHub.Inventario.Application.Reservas.Interno;

/// <summary>
/// El destino de una entrega ya resuelto contra el directorio de fuentes: la sucursal de recojo (si es recojo), la región de la dirección (si es domicilio) y el plazo de ruta de cada fuente hacia ese destino. En
/// recojo el plazo es hacia la sucursal elegida; en domicilio, hacia la fuente más cercana de la región (0 para las de la región). La ruta sin definir queda en <see langword="null"/> y esa fuente es la menos cercana (SEL-25).
/// </summary>
internal sealed class DestinoResuelto
{
    public DestinoResuelto(
        TipoDeEntrega tipo,
        FuenteDeStock? sucursalDeRecojo,
        string? region,
        IReadOnlyDictionary<Guid, FuenteDeStock> fuentes,
        IReadOnlyDictionary<Guid, int?> plazos)
    {
        Tipo = tipo;
        SucursalDeRecojo = sucursalDeRecojo;
        Region = region;
        Fuentes = fuentes;
        Plazos = plazos;
    }

    public TipoDeEntrega Tipo { get; }

    public FuenteDeStock? SucursalDeRecojo { get; }

    public string? Region { get; }

    public IReadOnlyDictionary<Guid, FuenteDeStock> Fuentes { get; }

    public IReadOnlyDictionary<Guid, int?> Plazos { get; }

    public int? PlazoDe(Guid fuenteId) => Plazos.GetValueOrDefault(fuenteId);
}

/// <summary>
/// Elige la fuente de cada línea con la estrategia configurada, sobre las filas de stock <b>ya bloqueadas</b> y descontando en memoria lo ya asignado a otras líneas (SEL-04, SEL-26): lo comparten reservar, el reemplazo y la
/// consulta previa. No toca el stock ni escribe: solo decide. Una línea nunca se divide entre fuentes (SEL-25).
/// </summary>
internal sealed class SelectorDeFuentes(IDirectorioDeFuentes directorio, IEstrategiaDeSeleccion estrategia)
{
    /// <summary>Resuelve el destino; es <see langword="null"/> si la sucursal de recojo no existe. Lee el directorio sin bloquear nada.</summary>
    public async Task<DestinoResuelto?> Resolver(TipoDeEntregaDeReserva tipo, string? codigoDeSucursal, string? region, CancellationToken cancelacion)
    {
        var fuentes = (await directorio.Listar(cancelacion).ConfigureAwait(false)).ToDictionary(f => f.Id);
        var plazos = new Dictionary<Guid, int?>();

        if (tipo == TipoDeEntregaDeReserva.Recojo)
        {
            var sucursal = fuentes.Values.FirstOrDefault(f => string.Equals(f.Codigo, codigoDeSucursal, StringComparison.Ordinal));
            if (sucursal is null)
            {
                return null;
            }

            plazos[sucursal.Id] = 0;
            foreach (var ruta in await directorio.RutasHaciaDestino(sucursal.Id, cancelacion).ConfigureAwait(false))
            {
                plazos.TryAdd(ruta.OrigenId, ruta.PlazoEnDias);
            }

            return new DestinoResuelto(TipoDeEntrega.Recojo, sucursal, null, fuentes, plazos);
        }

        // Domicilio: las fuentes de la región tienen plazo 0 y las demás, el menor plazo de ruta hacia alguna de la región (sin ruta, null).
        var deLaRegion = fuentes.Values.Where(f => string.Equals(f.Region, region, StringComparison.Ordinal)).ToList();
        foreach (var fuente in deLaRegion)
        {
            plazos[fuente.Id] = 0;
        }

        foreach (var destino in deLaRegion)
        {
            foreach (var ruta in await directorio.RutasHaciaDestino(destino.Id, cancelacion).ConfigureAwait(false))
            {
                if (deLaRegion.Any(f => f.Id == ruta.OrigenId))
                {
                    continue;
                }

                plazos[ruta.OrigenId] = plazos.TryGetValue(ruta.OrigenId, out var actual) && actual is { } previo ? Math.Min(previo, ruta.PlazoEnDias) : ruta.PlazoEnDias;
            }
        }

        return new DestinoResuelto(TipoDeEntrega.Domicilio, null, region, fuentes, plazos);
    }

    /// <summary>
    /// Selecciona la fuente de una línea. <paramref name="filas"/> son las filas bloqueadas de <b>esa edición</b> (todas las fuentes) y <paramref name="asignado"/> lo ya concedido a otras líneas en esta solicitud, por
    /// pareja (fuente, edición); el llamador lo actualiza con la fuente elegida.
    /// </summary>
    public Seleccion Seleccionar(DestinoResuelto destino, int cantidad, IEnumerable<ExistenciaBloqueada> filas, IReadOnlyDictionary<(Guid FuenteId, Guid EdicionId), int> asignado)
    {
        var candidatas = new List<CandidataDeFuente>();
        foreach (var fila in filas)
        {
            if (!destino.Fuentes.TryGetValue(fila.FuenteId, out var fuente))
            {
                continue;
            }

            var yaAsignado = asignado.GetValueOrDefault((fila.FuenteId, fila.EdicionId));
            var vendible = Math.Max(0, fila.Fisico - fila.Reservado) - yaAsignado;
            candidatas.Add(new CandidataDeFuente(
                fuente.Id,
                fuente.Codigo,
                Math.Max(0, vendible),
                EsElDestino: destino.SucursalDeRecojo?.Id == fuente.Id,
                EsDeLaRegion: destino.Tipo == TipoDeEntrega.Domicilio && string.Equals(fuente.Region, destino.Region, StringComparison.Ordinal),
                PlazoHaciaElDestinoEnDias: destino.PlazoDe(fuente.Id)));
        }

        return estrategia.Seleccionar(new SolicitudDeSeleccion(destino.Tipo, cantidad, candidatas));
    }
}

/// <summary>Convierte entre los tipos del contrato público y los del Dominio.</summary>
internal static class ConversionesDeReservas
{
    public static TipoDeEntrega ADominio(TipoDeEntregaDeReserva tipo) => tipo == TipoDeEntregaDeReserva.Recojo ? TipoDeEntrega.Recojo : TipoDeEntrega.Domicilio;

    public static MotivoDeLiberacion ADominio(MotivoDeLiberacionDeReserva motivo) => motivo switch
    {
        MotivoDeLiberacionDeReserva.CheckoutCancelado => MotivoDeLiberacion.CheckoutCancelado,
        MotivoDeLiberacionDeReserva.PagoVencido => MotivoDeLiberacion.PagoVencido,
        MotivoDeLiberacionDeReserva.PedidoAnulado => MotivoDeLiberacion.PedidoAnulado,
        MotivoDeLiberacionDeReserva.CanceladoPorElCliente => MotivoDeLiberacion.CanceladoPorElCliente,
        MotivoDeLiberacionDeReserva.FallaPosteriorAlPago => MotivoDeLiberacion.FallaPosteriorAlPago,
        MotivoDeLiberacionDeReserva.NoRecogido => MotivoDeLiberacion.NoRecogido,
        _ => throw new ArgumentOutOfRangeException(nameof(motivo)),
    };

    public static EstadoDeReserva AContrato(EstadoDeLinea estado) => estado switch
    {
        EstadoDeLinea.Pendiente => EstadoDeReserva.Pendiente,
        EstadoDeLinea.Confirmada => EstadoDeReserva.Confirmada,
        EstadoDeLinea.Cerrada => EstadoDeReserva.Cerrada,
        EstadoDeLinea.Liberada => EstadoDeReserva.Liberada,
        EstadoDeLinea.Expirada => EstadoDeReserva.Expirada,
        _ => throw new ArgumentOutOfRangeException(nameof(estado)),
    };

    /// <summary>
    /// El estado de la reserva entera a partir de los de sus líneas (con las vencidas mostradas como expiradas): el de las líneas cuando coinciden y, si difieren (una liberación parcial, un cierre por líneas), el de la línea
    /// más «viva»: pendiente, luego confirmada, cerrada, expirada y por último liberada.
    /// </summary>
    public static EstadoDeReserva EstadoDeLaReserva(IEnumerable<EstadoDeReserva> estados)
    {
        var presentes = estados.ToHashSet();
        foreach (var estado in new[] { EstadoDeReserva.Pendiente, EstadoDeReserva.Confirmada, EstadoDeReserva.Cerrada, EstadoDeReserva.Expirada, EstadoDeReserva.Liberada })
        {
            if (presentes.Contains(estado))
            {
                return estado;
            }
        }

        throw new InvalidOperationException("Una reserva necesita al menos una línea.");
    }
}

/// <summary>
/// Arma la respuesta pública de una reserva guardada: el código de la fuente de cada línea, si implica traslado (la fuente no es la sucursal de recojo) y su plazo estimado (<see langword="null"/> si la ruta no está
/// definida, «a confirmar», FUE-13). A domicilio nunca hay traslado (SEL-24).
/// </summary>
internal sealed class ConstructorDeLaReserva(IDirectorioDeFuentes directorio)
{
    public async Task<ReservaDelPedido> Construir(ReservaDePedido reserva, bool esRepeticion, CancellationToken cancelacion, IReadOnlyList<TrasladoVinculadoAlPedido>? traslados = null, DateTimeOffset? ahora = null)
    {
        ArgumentNullException.ThrowIfNull(reserva);

        var fuentes = (await directorio.Listar(cancelacion).ConfigureAwait(false)).ToDictionary(f => f.Id);
        // Un orden estable (edición y línea del pedido): la respuesta de una repetición, leída de la base, es la misma que la original.
        var lineas = new List<LineaReservada>(reserva.Lineas.Count);
        foreach (var linea in reserva.Lineas.OrderBy(l => l.EdicionId, OrdenDeBloqueo.DeIdentificadores).ThenBy(l => l.LineaId, OrdenDeBloqueo.DeIdentificadores))
        {
            var fuente = fuentes.GetValueOrDefault(linea.FuenteId)
                ?? throw new InvalidOperationException("La línea de la reserva nombra una fuente que no existe.");
            var requiereTraslado = reserva.EsDeRecojo && reserva.SucursalDeRecojoId is { } sucursal && sucursal != linea.FuenteId;
            int? plazo = requiereTraslado
                ? await directorio.PlazoDeRuta(linea.FuenteId, reserva.SucursalDeRecojoId!.Value, cancelacion).ConfigureAwait(false)
                : null;
            // Una pendiente vencida que nadie barrió todavía se muestra como expirada (SEL-27): no es una reserva activa aunque la fila siga pendiente.
            var estadoDeLaLinea = ahora is { } instante && linea.Estado == EstadoDeLinea.Pendiente && reserva.EstaVencida(instante) ? EstadoDeReserva.Expirada : ConversionesDeReservas.AContrato(linea.Estado);
            lineas.Add(new LineaReservada(linea.LineaId, linea.EdicionId, linea.Cantidad, estadoDeLaLinea, fuente.Codigo, requiereTraslado, plazo));
        }

        return new ReservaDelPedido(
            reserva.PedidoId,
            ConversionesDeReservas.EstadoDeLaReserva(lineas.Select(l => l.Estado)),
            reserva.IniciadaEn,
            reserva.FechaDeExpiracion,
            lineas,
            traslados ?? [],
            esRepeticion);
    }
}
