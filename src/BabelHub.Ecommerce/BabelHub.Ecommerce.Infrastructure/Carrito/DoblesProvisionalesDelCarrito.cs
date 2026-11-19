using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BabelHub.Ecommerce.Infrastructure.Carrito;

/// <summary>Lo que lanzan los dobles provisionales que fallan cerrado: el tercero real aún no existe, así que la operación no se puede hacer (nunca se asume un éxito).</summary>
public sealed class IntegracionProvisionalNoDisponibleException(string modulo) : InvalidOperationException($"El módulo {modulo} aún no existe y su doble provisional falla cerrado.")
{
    public string Modulo { get; } = modulo;
}

/// <summary>
/// <see cref="IPosesionDeEdiciones"/> hasta que exista Biblioteca (D07, R02): <b>el único doble «abierto»</b>: responde «no posee» y la compra sigue. Solo existe en desarrollo y pruebas: la guarda de arranque
/// impide registrarlo en producción. Pagos repite la comprobación al iniciar el cobro y Biblioteca concede una sola vez.
/// </summary>
public sealed class PosesionDeEdicionesSinBiblioteca : IPosesionDeEdiciones
{
    public Task<ResultadoDePosesion> Posee(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult(ResultadoDePosesion.NoPosee);
}

/// <summary>
/// <see cref="ICreadorDeIntencionesDePago"/> hasta que exista Pagos: <b>falla cerrado</b> (lanza), de modo que no se pasa al pago sin un cobro real. Los tres métodos lanzan: quien llama lo trata como «Pagos no respondió».
/// </summary>
public sealed class CreadorDeIntencionesSinPagos : ICreadorDeIntencionesDePago
{
    public Task<ResultadoDeCrearIntencion> Crear(Guid cuentaId, InstantaneaDeCompra instantanea, string huellaDeInstantanea, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default) =>
        throw new IntegracionProvisionalNoDisponibleException("Pagos");

    public Task<ResultadoDeReabrirIntencion> Reabrir(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default) =>
        throw new IntegracionProvisionalNoDisponibleException("Pagos");

    public Task<ResultadoDeCancelarIntencion> Cancelar(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default) =>
        throw new IntegracionProvisionalNoDisponibleException("Pagos");
}

/// <summary>
/// <see cref="IEstadoDeCuenta"/> hasta que exista Identidad (los contratos de la cuenta que Carrito necesita —lectura con bloqueo de la marca de cancelación, versión vigente de cada texto— los amplía el carril A): falla
/// cerrado, «si Identidad no responde, nadie puede comprar» (CAR-37). No lleva datos de nadie.
/// </summary>
public sealed class EstadoDeCuentaDelCarritoSinIdentidad : IEstadoDeCuenta
{
    public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) => throw new IntegracionProvisionalNoDisponibleException("Identidad");

    public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaAceptacion.NoDisponible);
}

/// <summary>
/// El Inventario simulado fiel a los contratos v1 (<c>docs/03-diseno-de-software/08-contratos-entre-servicios-v1.md</c>), para el desarrollo y las pruebas hasta que exista el cliente real: stock por edición configurable,
/// reservas con la clave de idempotencia (repetir la misma clave devuelve la original aunque haya expirado; una clave nueva sobre una reserva expirada o liberada es <c>INV-RESERVA-ESTADO-INVALIDO</c>; el cuarto
/// reemplazo de un pedido es 429 y conserva la vigente, SEL-36), expiración de 15 minutos, <c>liberar</c> idempotente (un pedido desconocido es <c>INV-NO-ENCONTRADO</c> y uno confirmado,
/// <c>INV-RESERVA-ESTADO-INVALIDO</c>) y un interruptor de «caído». No es un doble de producción: la guarda de arranque lo impide.
/// </summary>
public sealed class InventarioSimuladoDelCarrito(IReloj reloj) : IInventarioDelCarrito
{
    private const int MaximoDeEdicionesPorConsulta = 50;
    private const int MaximoDeReemplazos = 3;
    private static readonly TimeSpan DuracionDeLaReserva = TimeSpan.FromMinutes(15);

    private readonly Lock _candado = new();
    private readonly Dictionary<Guid, int> _stock = [];
    private readonly Dictionary<Guid, ReservaSimulada> _reservas = [];
    private readonly Dictionary<Guid, (string Fuente, bool Traslado, int? Plazo)> _fuentes = [];
    private List<SucursalDeEntrega> _sucursales = [new("sede-central", "Sede central", "ayacucho")];

    /// <summary>Mientras es verdadero, toda llamada responde «sin respuesta».</summary>
    public bool Caido { get; set; }

    public void FijarStock(Guid edicionId, int vendible)
    {
        lock (_candado)
        {
            _stock[edicionId] = vendible;
        }
    }

    public void FijarFuente(Guid edicionId, string codigoDeFuente, bool requiereTraslado, int? plazoEnDias)
    {
        lock (_candado)
        {
            _fuentes[edicionId] = (codigoDeFuente, requiereTraslado, plazoEnDias);
        }
    }

    public void FijarSucursales(IEnumerable<SucursalDeEntrega> sucursales)
    {
        lock (_candado)
        {
            _sucursales = [.. sucursales];
        }
    }

    /// <summary>Marca como confirmada la reserva de un pedido (lo que haría el cobro aprobado).</summary>
    public void Confirmar(Guid pedidoId)
    {
        lock (_candado)
        {
            _reservas[pedidoId].Estado = EstadoSimulado.Confirmada;
        }
    }

    public int Reservado(Guid edicionId)
    {
        lock (_candado)
        {
            return _reservas.Values.Where(r => r.Estado != EstadoSimulado.Liberada).Sum(r => r.Lineas.Where(l => l.EdicionId == edicionId).Sum(l => l.Cantidad));
        }
    }

    public Task<RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(edicionIds);
        if (Caido)
        {
            return Task.FromResult(RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.SinRespuesta);
        }

        if (edicionIds.Count is 0 or > MaximoDeEdicionesPorConsulta)
        {
            return Task.FromResult(RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.Rechazo("INV-VALIDACION-INVALIDA"));
        }

        lock (_candado)
        {
            var conocidas = edicionIds.Distinct().Where(_stock.ContainsKey).Select(id => new StockDeEdicion(id, Vendible(id, null))).ToList();
            var noRegistradas = edicionIds.Distinct().Where(id => !_stock.ContainsKey(id)).ToList();
            return Task.FromResult(RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.ConValor(new StockDeVariasEdiciones(conocidas, noRegistradas, reloj.Ahora)));
        }
    }

    public Task<RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>> ConsultarPrevia(EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        ArgumentNullException.ThrowIfNull(lineas);
        if (Caido)
        {
            return Task.FromResult(RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.SinRespuesta);
        }

        if (!EntregaValida(entrega) || lineas.Count == 0)
        {
            return Task.FromResult(RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.Rechazo("INV-VALIDACION-INVALIDA"));
        }

        lock (_candado)
        {
            var selecciones = lineas.Select(l => Seleccionar(l, null)).ToList();
            return Task.FromResult(RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.ConValor(new ConsultaPreviaDelInventario(selecciones, reloj.Ahora)));
        }
    }

    public Task<ListaDeSucursales?> ListarSucursales(CancellationToken cancelacion = default)
    {
        if (Caido)
        {
            return Task.FromResult<ListaDeSucursales?>(null);
        }

        lock (_candado)
        {
            return Task.FromResult<ListaDeSucursales?>(new ListaDeSucursales([.. _sucursales], reloj.Ahora, EsDeRespaldo: false));
        }
    }

    public Task<ResultadoDeReservar> Reservar(Guid pedidoId, int generacionDeReserva, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default)
    {
        ArgumentNullException.ThrowIfNull(entrega);
        ArgumentNullException.ThrowIfNull(lineas);
        if (Caido)
        {
            return Task.FromResult(ResultadoDeReservar.De(TipoDeResultadoDeReservar.NoDisponible));
        }

        if (!EntregaValida(entrega) || lineas.Count == 0 || lineas.Any(l => l.Cantidad is < 1 or > 99))
        {
            return Task.FromResult(ResultadoDeReservar.De(TipoDeResultadoDeReservar.Rechazada, "INV-VALIDACION-INVALIDA"));
        }

        var clave = ClaveDeReserva.Para(pedidoId, generacionDeReserva);
        var carga = CargaCanonica(pedidoId, entrega, lineas);
        var ahora = reloj.Ahora;

        lock (_candado)
        {
            _reservas.TryGetValue(pedidoId, out var existente);

            if (existente is not null && existente.Claves.TryGetValue(clave, out var cargaGuardada))
            {
                return Task.FromResult(cargaGuardada == carga
                    ? ResultadoDeReservar.Reservada(existente.Original(pedidoId))
                    : ResultadoDeReservar.De(TipoDeResultadoDeReservar.ConflictoDeIdempotencia, "INV-IDEMPOTENCIA-EN-CONFLICTO"));
            }

            if (existente is not null)
            {
                if (existente.Estado != EstadoSimulado.Pendiente || existente.ExpiraEn <= ahora)
                {
                    return Task.FromResult(ResultadoDeReservar.De(TipoDeResultadoDeReservar.EstadoInvalido, "INV-RESERVA-ESTADO-INVALIDO"));
                }

                if (existente.Reemplazos >= MaximoDeReemplazos)
                {
                    return Task.FromResult(ResultadoDeReservar.De(TipoDeResultadoDeReservar.LimiteExcedido, "INV-LIMITE-EXCEDIDO", existente.ExpiraEn));
                }
            }

            var sinStock = lineas
                .Where(l => !_stock.ContainsKey(l.EdicionId) || Vendible(l.EdicionId, pedidoId) < l.Cantidad)
                .Select(l => new LineaSinStock(l.LineaId, l.EdicionId, l.Cantidad, _stock.ContainsKey(l.EdicionId) ? Vendible(l.EdicionId, pedidoId) : 0))
                .ToList();
            if (sinStock.Count > 0)
            {
                return Task.FromResult(ResultadoDeReservar.Sin(sinStock));
            }

            var nueva = existente ?? new ReservaSimulada();
            nueva.Reemplazos = existente is null ? 0 : existente.Reemplazos + 1;
            nueva.Estado = EstadoSimulado.Pendiente;
            nueva.ExpiraEn = ahora + DuracionDeLaReserva;
            nueva.Lineas = [.. lineas];
            nueva.Selecciones = [.. lineas.Select(l => Seleccionar(l, pedidoId))];
            nueva.Claves[clave] = carga;
            _reservas[pedidoId] = nueva;
            return Task.FromResult(ResultadoDeReservar.Reservada(new ReservaObtenida(pedidoId, nueva.ExpiraEn, nueva.Selecciones, EsRepeticion: false)));
        }
    }

    public Task<ResultadoDeLiberar> Liberar(Guid pedidoId, string motivo, CancellationToken cancelacion = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(motivo);
        if (Caido)
        {
            return Task.FromResult(ResultadoDeLiberar.NoSePudo);
        }

        lock (_candado)
        {
            if (!_reservas.TryGetValue(pedidoId, out var reserva))
            {
                return Task.FromResult(ResultadoDeLiberar.NoExistia);
            }

            if (reserva.Estado == EstadoSimulado.Confirmada)
            {
                return Task.FromResult(ResultadoDeLiberar.PedidoConfirmado);
            }

            reserva.Estado = EstadoSimulado.Liberada;
            return Task.FromResult(ResultadoDeLiberar.Liberada);
        }
    }

    private static bool EntregaValida(EntregaParaElInventario entrega) => entrega.Tipo switch
    {
        TipoDeEntrega.Recojo => !string.IsNullOrWhiteSpace(entrega.CodigoDeSucursalDeRecojo) && entrega.RegionDeEntrega is null,
        TipoDeEntrega.Domicilio => !string.IsNullOrWhiteSpace(entrega.RegionDeEntrega) && entrega.CodigoDeSucursalDeRecojo is null,
        _ => false,
    };

    private static string CargaCanonica(Guid pedidoId, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas) =>
        $"{pedidoId:D}|{entrega.Tipo}|{entrega.CodigoDeSucursalDeRecojo}|{entrega.RegionDeEntrega}|{string.Join(",", lineas.OrderBy(l => l.LineaId).Select(l => $"{l.LineaId:D}:{l.EdicionId:D}:{l.Cantidad}"))}";

    /// <summary>Lo vendible de una edición: el stock menos lo reservado por los demás pedidos (los pendientes no vencidos y los confirmados).</summary>
    private int Vendible(Guid edicionId, Guid? salvoElPedido)
    {
        var ahora = reloj.Ahora;
        var reservado = _reservas
            .Where(r => r.Key != salvoElPedido && (r.Value.Estado == EstadoSimulado.Confirmada || (r.Value.Estado == EstadoSimulado.Pendiente && r.Value.ExpiraEn > ahora)))
            .Sum(r => r.Value.Lineas.Where(l => l.EdicionId == edicionId).Sum(l => l.Cantidad));
        return Math.Max(0, _stock.GetValueOrDefault(edicionId) - reservado);
    }

    private SeleccionDeLinea Seleccionar(LineaParaElInventario linea, Guid? salvoElPedido)
    {
        var disponible = _stock.ContainsKey(linea.EdicionId) ? Vendible(linea.EdicionId, salvoElPedido) : 0;
        var (fuente, traslado, plazo) = _fuentes.GetValueOrDefault(linea.EdicionId, ("sede-central", false, (int?)null));
        return new SeleccionDeLinea(linea.LineaId, linea.EdicionId, disponible >= linea.Cantidad, disponible >= linea.Cantidad ? fuente : null, traslado, plazo, disponible);
    }

    private enum EstadoSimulado
    {
        Pendiente,
        Confirmada,
        Liberada,
    }

    private sealed class ReservaSimulada
    {
        public EstadoSimulado Estado { get; set; }

        public DateTimeOffset ExpiraEn { get; set; }

        public int Reemplazos { get; set; }

        public IReadOnlyList<LineaParaElInventario> Lineas { get; set; } = [];

        public IReadOnlyList<SeleccionDeLinea> Selecciones { get; set; } = [];

        public Dictionary<string, string> Claves { get; } = [];

        /// <summary>La reserva original de una clave ya usada: la misma, aunque haya expirado (repetir la clave no revisa la expiración).</summary>
        public ReservaObtenida Original(Guid pedidoId) => new(pedidoId, ExpiraEn, Selecciones, EsRepeticion: true);
    }
}

public static class ComposicionProvisionalDelCarrito
{
    /// <summary>
    /// Registra los dobles provisionales de lo que aún no existe (Biblioteca, Pagos, Identidad y el cliente real del Inventario) con <c>TryAdd</c>: <b>un registro real anterior se respeta</b>. Con
    /// <paramref name="esProduccion"/>, si cualquiera de los puertos que declara Carrito termina resuelto por un doble provisional, el arranque falla nombrando cuál (D07, R02): nunca se publica con un cobro
    /// simulado, un Inventario simulado ni una posesión de ediciones «abierta». Se llama <b>después</b> de registrar las implementaciones reales.
    /// </summary>
    public static IServiceCollection AgregarDoblesProvisionalesDelCarrito(this IServiceCollection servicios, bool esProduccion)
    {
        ArgumentNullException.ThrowIfNull(servicios);
        servicios.TryAddSingleton<IPosesionDeEdiciones, PosesionDeEdicionesSinBiblioteca>();
        servicios.TryAddSingleton<ICreadorDeIntencionesDePago, CreadorDeIntencionesSinPagos>();
        servicios.TryAddSingleton<IInventarioDelCarrito, InventarioSimuladoDelCarrito>();
        servicios.TryAddSingleton<IEstadoDeCuenta, EstadoDeCuentaDelCarritoSinIdentidad>();

        if (esProduccion)
        {
            ExigirQueNingunoSeaProvisional(servicios);
        }

        return servicios;
    }

    private static readonly (Type Puerto, Type Doble)[] Dobles =
    [
        (typeof(IPosesionDeEdiciones), typeof(PosesionDeEdicionesSinBiblioteca)),
        (typeof(ICreadorDeIntencionesDePago), typeof(CreadorDeIntencionesSinPagos)),
        (typeof(IInventarioDelCarrito), typeof(InventarioSimuladoDelCarrito)),
        (typeof(IEstadoDeCuenta), typeof(EstadoDeCuentaDelCarritoSinIdentidad)),
    ];

    private static void ExigirQueNingunoSeaProvisional(IServiceCollection servicios)
    {
        var provisionales = Dobles
            .Where(d => servicios.Any(s => s.ServiceType == d.Puerto && (s.ImplementationType == d.Doble || s.ImplementationInstance?.GetType() == d.Doble)))
            .Select(d => d.Puerto.Name)
            .ToList();
        if (provisionales.Count > 0)
        {
            throw new InvalidOperationException(
                $"En producción no se permiten dobles provisionales de Carrito. Falta la implementación real de: {string.Join(", ", provisionales)}.");
        }
    }
}
