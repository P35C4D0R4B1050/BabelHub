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
    private readonly Dictionary<Guid, Dictionary<string, int>> _stock = [];
    private readonly Dictionary<Guid, ReservaSimulada> _reservas = [];
    private readonly Dictionary<Guid, (string Fuente, bool Traslado, int? Plazo)> _fuentes = [];
    private List<SucursalDeEntrega> _sucursales = [new("sede-central", "Sede central", "ayacucho")];

    /// <summary>Mientras es verdadero, toda llamada responde «sin respuesta».</summary>
    public bool Caido { get; set; }

    /// <summary>Fija lo vendible de una edición en una sucursal (por omisión, la sede central).</summary>
    public void FijarStock(Guid edicionId, int vendible, string codigoDeSucursal = "sede-central")
    {
        lock (_candado)
        {
            if (!_stock.TryGetValue(edicionId, out var porSucursal))
            {
                _stock[edicionId] = porSucursal = new Dictionary<string, int>(StringComparer.Ordinal);
            }

            porSucursal[codigoDeSucursal] = vendible;
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
            var conocidas = edicionIds.Distinct().Where(_stock.ContainsKey).Select(id => StockDe(id, null)).ToList();
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
            var selecciones = Seleccionar(lineas, null);
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

        if (!EntregaValida(entrega) || lineas.Count == 0 || lineas.Any(l => l.Cantidad is < 1 or > 20))
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

            var asignadas = Seleccionar(lineas, pedidoId);
            var sinStock = asignadas
                .Where(s => !s.HayStock)
                .Select(s => new LineaSinStock(s.LineaId, s.EdicionId, lineas.First(l => l.LineaId == s.LineaId).Cantidad, s.DisponibleEnLaRed, s.DisponibleEnLaSucursal))
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
            nueva.Selecciones = asignadas;
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
        TipoDeEntrega.Recojo => entrega.RegionDeEntrega is null,
        TipoDeEntrega.Domicilio => !string.IsNullOrWhiteSpace(entrega.RegionDeEntrega),
        _ => false,
    };

    private static string CargaCanonica(Guid pedidoId, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas) =>
        $"{pedidoId:D}|{entrega.Tipo}|{entrega.RegionDeEntrega}|{string.Join(",", lineas.OrderBy(l => l.LineaId).Select(l => $"{l.LineaId:D}:{l.EdicionId:D}:{l.Cantidad}:{l.CodigoDeSucursal}"))}";

    private StockDeEdicion StockDe(Guid edicionId, Guid? salvoElPedido)
    {
        var porSucursal = _stock[edicionId].Keys.ToDictionary(s => s, s => Vendible(edicionId, s, salvoElPedido), StringComparer.Ordinal);
        return new StockDeEdicion(edicionId, porSucursal.Values.Sum(), porSucursal);
    }

    /// <summary>Lo vendible de una edición en una sucursal: su stock menos lo reservado allí por los demás pedidos (los pendientes no vencidos y los confirmados).</summary>
    private int Vendible(Guid edicionId, string codigoDeSucursal, Guid? salvoElPedido)
    {
        var ahora = reloj.Ahora;
        var reservado = _reservas
            .Where(r => r.Key != salvoElPedido && (r.Value.Estado == EstadoSimulado.Confirmada || (r.Value.Estado == EstadoSimulado.Pendiente && r.Value.ExpiraEn > ahora)))
            .Sum(r => r.Value.Selecciones.Where(s => s.EdicionId == edicionId && s.CodigoDeFuente == codigoDeSucursal).Sum(s => r.Value.Lineas.First(l => l.LineaId == s.LineaId).Cantidad));
        var hay = _stock.TryGetValue(edicionId, out var porSucursal) ? porSucursal.GetValueOrDefault(codigoDeSucursal) : 0;
        return Math.Max(0, hay - reservado);
    }

    /// <summary>Cada línea en la sucursal que declara (sin dividirse), sumando las del mismo libro y sucursal en el orden pedido; una línea sin sucursal (la forma anterior) sale de la fuente fijada para su edición.</summary>
    private List<SeleccionDeLinea> Seleccionar(IReadOnlyList<LineaParaElInventario> lineas, Guid? salvoElPedido)
    {
        var apartado = new Dictionary<(Guid, string), int>();
        var selecciones = new List<SeleccionDeLinea>(lineas.Count);
        foreach (var linea in lineas)
        {
            var fijada = _fuentes.GetValueOrDefault(linea.EdicionId, ("sede-central", false, (int?)null));
            var sucursal = linea.CodigoDeSucursal ?? fijada.Item1;
            var (traslado, plazo) = linea.CodigoDeSucursal is null ? (fijada.Item2, fijada.Item3) : (false, (int?)null);
            var disponible = Vendible(linea.EdicionId, linea.CodigoDeSucursal is null ? "sede-central" : sucursal, salvoElPedido);
            var yaApartado = apartado.GetValueOrDefault((linea.EdicionId, sucursal));
            var alcanza = yaApartado + linea.Cantidad <= disponible;
            if (alcanza)
            {
                apartado[(linea.EdicionId, sucursal)] = yaApartado + linea.Cantidad;
            }

            var enLaRed = _stock.ContainsKey(linea.EdicionId) ? StockDe(linea.EdicionId, salvoElPedido).VendibleEnLaRed : 0;
            selecciones.Add(new SeleccionDeLinea(linea.LineaId, linea.EdicionId, alcanza, alcanza ? sucursal : null, traslado, plazo, enLaRed, linea.CodigoDeSucursal is null ? null : disponible));
        }

        return selecciones;
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

/// <summary>
/// <see cref="ILimitadorDelCarrito"/> hasta que <c>Plataforma</c> tenga las políticas de Carrito (carril de plataforma): <b>falla cerrado en lo que reserva stock</b> (pasar al pago) y permite el resto, que solo edita el carrito; la guarda de arranque
/// impide publicarlo (nunca hay carritos sin límite de uso en producción, SEG-05).
/// </summary>
public sealed class LimitadorDelCarritoProvisional : ILimitadorDelCarrito
{
    public Task<ResultadoDeLimiteDelCarrito> CrearCarritoDeVisitante(CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDelCarrito.Permitido);

    public Task<ResultadoDeLimiteDelCarrito> EditarLineas(Guid? cuentaId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDelCarrito.Permitido);

    public Task<ResultadoDeLimiteDelCarrito> RevisarCheckout(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDelCarrito.Permitido);

    public Task<ResultadoDeLimiteDelCarrito> PasarAlPago(Guid cuentaId, string claveDeRepeticion, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDelCarrito.NoDisponible);

    public Task<ResultadoDeLimiteDelCarrito> VolverOReiniciar(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLimiteDelCarrito.Permitido);
}

/// <summary>
/// <see cref="IAuditoriaDelCarrito"/> hasta que <c>AccionesAuditables</c> tenga las acciones de Carrito (carril de auditoría): <b>no audita nada</b>. Solo sirve para desarrollo y pruebas; la guarda de arranque impide publicarlo, porque en producción
/// una operación sensible sin auditoría debe fallar (AGENTS §4).
/// </summary>
public sealed class AuditoriaDelCarritoProvisional : IAuditoriaDelCarrito
{
    public Task FusionAplicada(Guid cuentaId, int lineasDelVisitante, IReadOnlyList<MotivoDeAjusteDeAviso> ajustes, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task EdicionesRetiradas(int ediciones, int carritosAfectados, int lineasQuitadas, int lineasAnotadas, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task CheckoutTerminado(string motivo, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task CarritoEliminadoPorIdentidad(string motivo, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task PurgaRealizada(string trabajo, int cantidad, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task PasoAlPagoIniciado(bool conImpresos, bool aDomicilio, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task PasoAlPagoAbortado(string motivo, CancellationToken cancelacion = default) => Task.CompletedTask;
}

/// <summary><see cref="IMarcaDeCancelacion"/> hasta que Identidad la exponga: <b>falla cerrado</b> (lanza), de modo que nadie pasa al pago sin saber si la cuenta se está cancelando (AC-CAR-27).</summary>
public sealed class MarcaDeCancelacionProvisional : IMarcaDeCancelacion
{
    public Task<EstadoDeLaMarcaDeCancelacion> LeerConBloqueo(Guid cuentaId, CancellationToken cancelacion = default) =>
        throw new IntegracionProvisionalNoDisponibleException("Identidad");
}

/// <summary><see cref="IConsentimientoDeAvisos"/> hasta que Identidad lo exponga: no se ofrece el consentimiento y no se guarda (el pago nunca depende de él).</summary>
public sealed class ConsentimientoDeAvisosProvisional : IConsentimientoDeAvisos
{
    public Task<OfertaDeConsentimiento> Consultar(Guid cuentaId, CancellationToken cancelacion = default) => Task.FromResult(OfertaDeConsentimiento.NoSeOfrece);

    public Task<bool> Guardar(Guid cuentaId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default) => Task.FromResult(false);
}

/// <summary><see cref="IAlertasDelCarrito"/> hasta que Auditoría exponga el canal de alertas técnicas de Carrito: no publica nada. La guarda de arranque impide publicarlo.</summary>
public sealed class AlertasDelCarritoProvisional : IAlertasDelCarrito
{
    public Task Publicar(TipoDeAlertaDelCarrito tipo, int cantidad, CancellationToken cancelacion = default) => Task.CompletedTask;
}

public static class ComposicionProvisionalDelCarrito
{
    /// <summary>
    /// Registra los dobles provisionales de lo que aún no existe (Biblioteca, Pagos, Identidad, el cliente real del Inventario y lo que espera al carril de plataforma y auditoría: límites, auditoría y alertas) con <c>TryAdd</c>: <b>un registro real anterior se respeta</b>. Con
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
        servicios.TryAddSingleton<ILimitadorDelCarrito, LimitadorDelCarritoProvisional>();
        servicios.TryAddSingleton<IAuditoriaDelCarrito, AuditoriaDelCarritoProvisional>();
        servicios.TryAddSingleton<IMarcaDeCancelacion, MarcaDeCancelacionProvisional>();
        servicios.TryAddSingleton<IConsentimientoDeAvisos, ConsentimientoDeAvisosProvisional>();
        servicios.TryAddSingleton<IAlertasDelCarrito, AlertasDelCarritoProvisional>();

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
        (typeof(ILimitadorDelCarrito), typeof(LimitadorDelCarritoProvisional)),
        (typeof(IAuditoriaDelCarrito), typeof(AuditoriaDelCarritoProvisional)),
        (typeof(IMarcaDeCancelacion), typeof(MarcaDeCancelacionProvisional)),
        (typeof(IConsentimientoDeAvisos), typeof(ConsentimientoDeAvisosProvisional)),
        (typeof(IAlertasDelCarrito), typeof(AlertasDelCarritoProvisional)),
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
