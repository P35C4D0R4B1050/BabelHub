using BabelHub.Ecommerce.Application.Carrito;
using BabelHub.Ecommerce.Application.Carrito.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Carrito;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;
using BabelHub.Ecommerce.UnitTests.Web;
using CarritoDeDominio = BabelHub.Ecommerce.Domain.Carrito.Carrito;

namespace BabelHub.Ecommerce.UnitTests.Carrito;

/// <summary>El Catálogo visto por Carrito: solo <c>ObtenerEdiciones</c> (precio, formato, estado y motivo); lo demás no lo usa.</summary>
internal sealed class CatalogoDeCarritoFalso : IConsultaDeCatalogo
{
    private readonly Dictionary<Guid, EdicionConsultada> _ediciones = [];

    public bool Caido { get; set; }

    public int Consultas { get; private set; }

    public EdicionConsultada Poner(Guid edicionId, FormatoDeEdicion formato, decimal precio, string titulo = "Libro", EstadoDeEdicionConsultado estado = EstadoDeEdicionConsultado.Activa, MotivoDeNoDisponibleDeEdicion? motivo = null)
    {
        var edicion = new EdicionConsultada(edicionId, Guid.NewGuid(), titulo, formato, "9780000000019", precio, estado, motivo);
        _ediciones[edicionId] = edicion;
        return edicion;
    }

    public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        Consultas++;
        if (Caido)
        {
            return Task.FromResult(Resultado<IReadOnlyList<EdicionConsultada>>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "catalogo-no-disponible")));
        }

        IReadOnlyList<EdicionConsultada> halladas = [.. edicionIds.Where(_ediciones.ContainsKey).Select(id => _ediciones[id])];
        return Task.FromResult(Resultado<IReadOnlyList<EdicionConsultada>>.Exito(halladas));
    }

    public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

/// <summary>La disponibilidad del Catálogo: por defecto «no se puede confirmar» (la línea se acepta, D03).</summary>
internal sealed class DisponibilidadDeCarritoFalsa : IDisponibilidadDeEdiciones
{
    private readonly Dictionary<Guid, DisponibilidadDeEdicion> _datos = [];

    public bool Caida { get; set; }

    public int Consultas { get; private set; }

    public void Vendible(Guid edicionId, int vendible, FrescuraDeLaDisponibilidad frescura = FrescuraDeLaDisponibilidad.Reciente) =>
        _datos[edicionId] = new DisponibilidadDeEdicion(
            edicionId,
            vendible > 0 ? EstadoDeDisponibilidad.Disponible : EstadoDeDisponibilidad.Agotada,
            vendible,
            [],
            frescura,
            DateTimeOffset.UnixEpoch,
            null);

    public void Digital(Guid edicionId) =>
        _datos[edicionId] = new DisponibilidadDeEdicion(edicionId, EstadoDeDisponibilidad.DisponibleAlInstante, null, [], FrescuraDeLaDisponibilidad.Reciente, null, null);

    public Task<Resultado<IReadOnlyList<DisponibilidadDeEdicion>>> Consultar(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        Consultas++;
        if (Caida)
        {
            return Task.FromResult(Resultado<IReadOnlyList<DisponibilidadDeEdicion>>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "disponibilidad-no-disponible")));
        }

        IReadOnlyList<DisponibilidadDeEdicion> respuesta =
        [
            .. edicionIds.Select(id => _datos.TryGetValue(id, out var d) ? d : new DisponibilidadDeEdicion(id, EstadoDeDisponibilidad.NoSePuedeConfirmar, null, [], FrescuraDeLaDisponibilidad.NoSePuedeConfirmar, null, null)),
        ];
        return Task.FromResult(Resultado<IReadOnlyList<DisponibilidadDeEdicion>>.Exito(respuesta));
    }
}

internal sealed class PosesionFalsa : IPosesionDeEdiciones
{
    private readonly HashSet<Guid> _poseidas = [];

    public bool NoResponde { get; set; }

    public int Consultas { get; private set; }

    public void Poseer(Guid edicionId) => _poseidas.Add(edicionId);

    public Task<ResultadoDePosesion> Posee(Guid cuentaId, IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        Consultas++;
        return Task.FromResult(NoResponde ? ResultadoDePosesion.NoSePuedeConfirmar : ResultadoDePosesion.Posee([.. edicionIds.Where(_poseidas.Contains)]));
    }
}

internal sealed class EstadoDeCuentaFalso : IEstadoDeCuenta
{
    public bool Cancelada { get; set; }

    public bool NoResponde { get; set; }

    public bool NoExiste { get; set; }

    /// <summary>La cuenta existe y no está cancelada pero no puede comprar (textos pendientes, correo sin verificar o cancelación diferida): puede editar su carrito pero no guardar el consentimiento de avisos.</summary>
    public bool NoPuedeComprar { get; set; }

    public bool CorreoSinVerificar { get; set; }

    public bool TextosPendientes { get; set; }

    /// <summary>Una cancelación diferida o en ejecución: no puede comprar, pero la cuenta no está cancelada ni desactivada.</summary>
    public bool CancelacionDiferida { get; set; }

    public int Consultas { get; private set; }

    public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        Consultas++;
        if (NoResponde)
        {
            throw new InvalidOperationException("Identidad no responde.");
        }

        return Task.FromResult<EstadoDeCuenta?>(NoExiste ? null : new EstadoDeCuenta(usuarioId, !Cancelada && !NoPuedeComprar && !CorreoSinVerificar && !TextosPendientes && !CancelacionDiferida, TextosPendientes ? EstadoDeAceptacionDeLosTextos.Pendiente : EstadoDeAceptacionDeLosTextos.AlDia, !CorreoSinVerificar, Cancelada, false));
    }

    public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaAceptacion.Aceptada);
}

internal sealed class LimitadorDeCarritoFalso : ILimitadorDelCarrito
{
    public ResultadoDeLimiteDelCarrito CrearCarritos { get; set; } = ResultadoDeLimiteDelCarrito.Permitido;

    public ResultadoDeLimiteDelCarrito Editar { get; set; } = ResultadoDeLimiteDelCarrito.Permitido;

    public int CarritosCreados { get; private set; }

    public int Ediciones { get; private set; }

    public ResultadoDeLimiteDelCarrito Revisar { get; set; } = ResultadoDeLimiteDelCarrito.Permitido;

    public ResultadoDeLimiteDelCarrito Pagar { get; set; } = ResultadoDeLimiteDelCarrito.Permitido;

    public HashSet<string> ClavesDePagar { get; } = [];

    public int ConsumosDePagar { get; private set; }

    public int Revisiones { get; private set; }

    public Task<ResultadoDeLimiteDelCarrito> CrearCarritoDeVisitante(CancellationToken cancelacion = default)
    {
        CarritosCreados++;
        return Task.FromResult(CrearCarritos);
    }

    public Task<ResultadoDeLimiteDelCarrito> EditarLineas(Guid? cuentaId, CancellationToken cancelacion = default)
    {
        Ediciones++;
        return Task.FromResult(Editar);
    }

    public Task<ResultadoDeLimiteDelCarrito> RevisarCheckout(Guid cuentaId, CancellationToken cancelacion = default)
    {
        Revisiones++;
        return Task.FromResult(Revisar);
    }

    public Task<ResultadoDeLimiteDelCarrito> PasarAlPago(Guid cuentaId, string claveDeRepeticion, CancellationToken cancelacion = default)
    {
        if (ClavesDePagar.Add(claveDeRepeticion))
        {
            ConsumosDePagar++;
        }

        return Task.FromResult(Pagar);
    }
}

internal sealed class GeneradorDeIdentificadoresFalso : IGeneradorDeIdentificadores
{
    private int _contador;

    public Guid NuevoId() => new(++_contador, 0, 0, 0, 0, 0, 0, 0, 0, 0, 7);

    public string NuevaCookieDeVisitante() => $"{++_contador:D43}".Replace('0', 'A');
}

/// <summary>Los carritos en memoria: guarda el objeto del Dominio y deja una bitácora de cada lectura con bloqueo, para comprobar el orden de bloqueo (checkout antes que carrito, D10).</summary>
internal sealed class RepositorioDeCarritosEnMemoria(BitacoraDeBloqueos bitacora) : IRepositorioDeCarritos
{
    private readonly List<CarritoDeDominio> _carritos = [];
    private readonly List<(Guid CuentaId, AvisoDelCarrito Aviso, bool Visto)> _avisos = [];

    public IReadOnlyList<CarritoDeDominio> Todos => _carritos;

    /// <summary>Se ejecuta una sola vez al agregar: simula a otro dispositivo que crea el carrito a la vez (puede lanzar <c>CarritoYaExisteException</c>).</summary>
    public Action? AntesDeAgregar { get; set; }

    /// <summary>Se ejecuta una sola vez justo antes de una lectura con bloqueo por id: simula otra solicitud que cambia el carrito mientras tanto.</summary>
    public Action? AlBloquear { get; set; }

    public int Guardados { get; private set; }

    public void Sembrar(CarritoDeDominio carrito) => _carritos.Add(carrito);

    public Task<CarritoDeDominio?> ObtenerDeCuenta(Guid cuentaId, bool conBloqueo, CancellationToken cancelacion = default)
    {
        if (conBloqueo && AlBloquear is { } alBloquear)
        {
            AlBloquear = null;
            alBloquear();
        }

        bitacora.Anotar(conBloqueo, "carrito");
        return Task.FromResult(_carritos.FirstOrDefault(c => c.Propietario.CuentaId == cuentaId));
    }

    public Task<CarritoDeDominio?> ObtenerDeVisitante(byte[] huellaDeLaCookie, bool conBloqueo, CancellationToken cancelacion = default)
    {
        bitacora.Anotar(conBloqueo, "carrito");
        return Task.FromResult(_carritos.FirstOrDefault(c => c.Propietario.EsDeVisitante && c.Propietario.HuellaDelVisitante.SequenceEqual(huellaDeLaCookie)));
    }

    public Task<CarritoDeDominio?> Obtener(Guid carritoId, bool conBloqueo, CancellationToken cancelacion = default)
    {
        if (conBloqueo && AlBloquear is { } alBloquear)
        {
            AlBloquear = null;
            alBloquear();
        }

        bitacora.Anotar(conBloqueo, $"carrito:{carritoId}");
        return Task.FromResult(_carritos.FirstOrDefault(c => c.Id == carritoId));
    }

    public Task Agregar(CarritoDeDominio carrito, CancellationToken cancelacion = default)
    {
        if (AntesDeAgregar is { } antes)
        {
            AntesDeAgregar = null;
            antes();
        }

        _carritos.Add(carrito);
        Guardados++;
        return Task.CompletedTask;
    }

    public Task Guardar(CarritoDeDominio carrito, CancellationToken cancelacion = default)
    {
        Guardados++;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AvisoDelCarrito>> AvisosSinVer(Guid cuentaId, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<AvisoDelCarrito>>([.. _avisos.Where(a => a.CuentaId == cuentaId && !a.Visto).Select(a => a.Aviso).OrderByDescending(a => a.CreadoEn)]);

    public Task AgregarAvisos(Guid cuentaId, IReadOnlyList<AvisoDelCarrito> avisos, CancellationToken cancelacion = default)
    {
        _avisos.AddRange(avisos.Select(a => (cuentaId, a, false)));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CarritoConEdicion>> BuscarConEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<CarritoConEdicion>>([.. _carritos.Where(c => c.Lineas.Any(l => edicionIds.Contains(l.EdicionId))).Select(c => new CarritoConEdicion(c.Id, c.Propietario.CuentaId))]);

    public List<(byte[] Huella, Guid CarritoId)> Fusiones { get; } = [];

    public Task RegistrarFusion(byte[] huellaDeLaCookie, Guid carritoDeCuentaId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        Fusiones.Add((huellaDeLaCookie, carritoDeCuentaId));
        return Task.CompletedTask;
    }

    public Task MarcarAvisosComoVistos(Guid cuentaId, IReadOnlyCollection<Guid> avisoIds, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        for (var i = 0; i < _avisos.Count; i++)
        {
            if (_avisos[i].CuentaId == cuentaId && avisoIds.Contains(_avisos[i].Aviso.Id))
            {
                _avisos[i] = (cuentaId, _avisos[i].Aviso, true);
            }
        }

        return Task.CompletedTask;
    }
}

internal sealed class RepositorioDeCheckoutsEnMemoria(BitacoraDeBloqueos bitacora) : IRepositorioDeCheckouts
{
    private readonly List<Checkout> _checkouts = [];

    public IReadOnlyList<Checkout> Todos => _checkouts;

    public void Sembrar(Checkout checkout) => _checkouts.Add(checkout);

    /// <summary>Se ejecuta una sola vez al agregar: simula a otra solicitud que crea el checkout a la vez (puede lanzar <c>CheckoutYaExisteException</c>).</summary>
    public Action? AntesDeAgregar { get; set; }

    private static bool EstaVivo(Checkout c) => c.Estado is EstadoDelCheckout.EnEdicion or EstadoDelCheckout.Reservando or EstadoDelCheckout.EnPago or EstadoDelCheckout.PagoPendiente;

    public Task<Checkout?> ObtenerVivoDeCuenta(Guid cuentaId, bool conBloqueo, CancellationToken cancelacion = default)
    {
        bitacora.Anotar(conBloqueo, "checkout");
        return Task.FromResult(_checkouts.FirstOrDefault(c => c.CuentaId == cuentaId && EstaVivo(c)));
    }

    public Task<Checkout?> ObtenerPorPedido(Guid pedidoId, bool conBloqueo, CancellationToken cancelacion = default)
    {
        bitacora.Anotar(conBloqueo, "checkout");
        return Task.FromResult(_checkouts.FirstOrDefault(c => c.PedidoId == pedidoId || c.PedidoDescartado == pedidoId));
    }

    public Task<IReadOnlyList<Checkout>> ObtenerVivosDeCuentasConBloqueo(IReadOnlyCollection<Guid> cuentaIds, CancellationToken cancelacion = default)
    {
        var vivos = _checkouts.Where(c => cuentaIds.Contains(c.CuentaId) && EstaVivo(c)).OrderBy(c => c.Id).ToList();
        foreach (var checkout in vivos)
        {
            bitacora.Anotar(true, $"checkout:{checkout.Id}");
        }

        return Task.FromResult<IReadOnlyList<Checkout>>(vivos);
    }

    public Task<EstadoDelCheckout?> LeerEstadoSinBloqueo(Guid cuentaId, CancellationToken cancelacion = default) =>
        Task.FromResult<EstadoDelCheckout?>(_checkouts.FirstOrDefault(c => c.CuentaId == cuentaId && EstaVivo(c))?.Estado);

    public Task Agregar(Checkout checkout, CancellationToken cancelacion = default)
    {
        if (AntesDeAgregar is { } antes)
        {
            AntesDeAgregar = null;
            antes();
        }

        _checkouts.Add(checkout);
        return Task.CompletedTask;
    }

    public List<Checkout> Guardados { get; } = [];

    public Task Guardar(Checkout checkout, CancellationToken cancelacion = default)
    {
        Guardados.Add(checkout);
        return Task.CompletedTask;
    }
}

/// <summary>Quién bloqueó qué y en qué orden (solo cuenta las lecturas con bloqueo).</summary>
internal sealed class BitacoraDeBloqueos
{
    public List<string> Bloqueos { get; } = [];

    /// <summary>Un hecho que importa para el orden (una llamada a Pagos) sin ser un bloqueo.</summary>
    public void Evento(string nombre) => Bloqueos.Add(nombre);

    public void Anotar(bool conBloqueo, string recurso)
    {
        if (conBloqueo)
        {
            Bloqueos.Add(recurso);
        }
    }
}

/// <summary>Pagos visto por Carrito: cuenta las llamadas y responde lo que se le indique; puede lanzar (Pagos no responde) o hacer algo justo al reabrir (una carrera).</summary>
internal sealed class PagosFalso(BitacoraDeBloqueos bitacora) : ICreadorDeIntencionesDePago
{
    public ResultadoDeReabrirIntencion AlReabrir { get; set; } = ResultadoDeReabrirIntencion.Reabierta;

    public ResultadoDeCancelarIntencion AlCancelar { get; set; } = ResultadoDeCancelarIntencion.Cancelada;

    public ResultadoDeCrearIntencion AlCrear { get; set; } = ResultadoDeCrearIntencion.Creada;

    public bool NoResponde { get; set; }

    /// <summary>Se ejecuta justo después de que Pagos responde a <c>Reabrir</c>: simula otra solicitud que cambia el checkout mientras tanto.</summary>
    public Action? EnLaCarreraDeReabrir { get; set; }

    /// <summary>Se ejecuta una sola vez justo antes de responder a <c>Crear</c>: simula otra solicitud que cambia el carrito entre 3a y 3b.</summary>
    public Action? AlCrearAccion { get; set; }

    public int Reaperturas { get; private set; }

    public int Cancelaciones { get; private set; }

    public int Creaciones { get; private set; }

    public Task<ResultadoDeCrearIntencion> Crear(Guid cuentaId, InstantaneaDeCompra instantanea, string huellaDeInstantanea, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default)
    {
        Creaciones++;
        if (AlCrearAccion is { } accion)
        {
            AlCrearAccion = null;
            accion();
        }

        return NoResponde ? throw new InvalidOperationException("Pagos no responde.") : Task.FromResult(AlCrear);
    }

    public Task<ResultadoDeReabrirIntencion> Reabrir(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default)
    {
        Reaperturas++;
        bitacora.Evento("reabrir");
        if (NoResponde)
        {
            throw new InvalidOperationException("Pagos no responde.");
        }

        EnLaCarreraDeReabrir?.Invoke();
        return Task.FromResult(AlReabrir);
    }

    public Task<ResultadoDeCancelarIntencion> Cancelar(Guid cuentaId, Guid pedidoId, CancellationToken cancelacion = default)
    {
        Cancelaciones++;
        bitacora.Evento("cancelar");
        return NoResponde ? throw new InvalidOperationException("Pagos no responde.") : Task.FromResult(AlCancelar);
    }
}

/// <summary>Todo lo que un caso de uso de Carrito necesita, armado con dobles; <c>Cuenta</c> es el cliente que pide.</summary>
internal sealed class EntornoDelCarrito
{
    public static readonly DateTimeOffset Inicio = new(2026, 11, 18, 15, 0, 0, TimeSpan.Zero);

    public EntornoDelCarrito(ActorDePrueba actor)
    {
        Actor = actor;
        Reloj = new RelojFijo(Inicio);
        Bitacora = new BitacoraDeBloqueos();
        Carritos = new RepositorioDeCarritosEnMemoria(Bitacora);
        Checkouts = new RepositorioDeCheckoutsEnMemoria(Bitacora);
        Pagos = new PagosFalso(Bitacora);
        Marca = new MarcaDeCancelacionFalsa(Bitacora);
    }

    public ActorDePrueba Actor { get; }

    public RelojFijo Reloj { get; }

    public BitacoraDeBloqueos Bitacora { get; }

    public RepositorioDeCarritosEnMemoria Carritos { get; }

    public RepositorioDeCheckoutsEnMemoria Checkouts { get; }

    public CatalogoDeCarritoFalso Catalogo { get; } = new();

    public DisponibilidadDeCarritoFalsa Disponibilidad { get; } = new();

    public PosesionFalsa Posesion { get; } = new();

    public PagosFalso Pagos { get; }

    public InventarioDelCarritoFalso Inventario { get; } = new();

    public CostoDeEnvioFijo Costo { get; } = new(12m);

    public TextosLegalesFalsos Textos { get; } = new();

    public ConsentimientoFalso Consentimiento { get; } = new();

    public MarcaDeCancelacionFalsa Marca { get; }

    public BabelHub.Ecommerce.UnitTests.Identidad.CalculadorDeHuellaDePrueba Huellas { get; } = new();

    public AuditoriaDelCarritoEspia Auditoria { get; } = new();

    public EstadoDeCuentaFalso Cuenta { get; } = new();

    public LimitadorDeCarritoFalso Limites { get; } = new();

    public GeneradorDeIdentificadoresFalso Identificadores { get; } = new();

    public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

    public UnidadDeTrabajoSimple Unidad { get; } = new();

    /// <summary>Los servicios compartidos de los casos de uso, armados con los dobles de este entorno.</summary>
    public ServiciosDelCarrito Servicios() =>
        new(new AutorizadorDelCarrito(Actor, Denegaciones), Catalogo, Disponibilidad, Posesion, Cuenta, Limites, Carritos, Checkouts, Identificadores, Reloj, Unidad, new EdicionConCheckoutVivo(Pagos, Checkouts), Auditoria);

    /// <summary>Los servicios del checkout: los del carrito más el Inventario y el costo de envío.</summary>
    public ServiciosDelCheckout ServiciosDelCheckout() => new(Servicios(), Inventario, Costo, Textos, Consentimiento, Marca, Huellas);

    public static EntornoDelCarrito DeCliente(Guid? cuenta = null) => new(ActorDePrueba.DeCliente(cuenta));

    public static EntornoDelCarrito DeVisitante() => new(ActorDePrueba.Anonimo());

    /// <summary>Registra una edición en el Catálogo y en la disponibilidad: un impreso con stock vendible o una digital disponible al instante.</summary>
    public Guid Impreso(int vendible, decimal precio = 50m, string titulo = "Libro")
    {
        var id = Guid.NewGuid();
        Catalogo.Poner(id, FormatoDeEdicion.Impresa, precio, titulo);
        Disponibilidad.Vendible(id, vendible);
        Inventario.FijarStock(id, vendible);
        return id;
    }

    public Guid Digital(decimal precio = 20m, string titulo = "Digital")
    {
        var id = Guid.NewGuid();
        Catalogo.Poner(id, FormatoDeEdicion.Digital, precio, titulo);
        Disponibilidad.Digital(id);
        return id;
    }
}

/// <summary>La auditoría de Carrito que guarda lo registrado y puede fallar como una auditoría caída (el caso de uso entonces falla).</summary>
internal sealed class AuditoriaDelCarritoEspia : IAuditoriaDelCarrito
{
    public List<(Guid CuentaId, int LineasDelVisitante, IReadOnlyList<MotivoDeAjusteDeAviso> Ajustes)> Fusiones { get; } = [];

    public List<(int Ediciones, int Carritos, int Quitadas, int Anotadas)> Retiros { get; } = [];

    public List<(bool ConImpresos, bool ADomicilio)> PasosAlPago { get; } = [];

    public bool Caida { get; set; }

    public Task FusionAplicada(Guid cuentaId, int lineasDelVisitante, IReadOnlyList<MotivoDeAjusteDeAviso> ajustes, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            throw new InvalidOperationException("La auditoría no está disponible.");
        }

        Fusiones.Add((cuentaId, lineasDelVisitante, ajustes));
        return Task.CompletedTask;
    }

    public Task PasoAlPagoIniciado(bool conImpresos, bool aDomicilio, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            throw new InvalidOperationException("La auditoría no está disponible.");
        }

        PasosAlPago.Add((conImpresos, aDomicilio));
        return Task.CompletedTask;
    }

    public Task EdicionesRetiradas(int ediciones, int carritosAfectados, int lineasQuitadas, int lineasAnotadas, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            throw new InvalidOperationException("La auditoría no está disponible.");
        }

        Retiros.Add((ediciones, carritosAfectados, lineasQuitadas, lineasAnotadas));
        return Task.CompletedTask;
    }
}

/// <summary>El costo de envío configurado de las pruebas.</summary>
internal sealed class CostoDeEnvioFijo(decimal soles) : ICostoDeEnvioConfigurado
{
    public CostoDeEnvio Valor { get; set; } = CostoDeEnvio.Crear(soles);
}

/// <summary>El Inventario visto por Carrito: stock y fuente por edición configurables, con contadores de consultas y un interruptor de caído. Reservar y liberar se configuran con sus resultados (T18, T20).</summary>
internal sealed class InventarioDelCarritoFalso : IInventarioDelCarrito
{
    private readonly Dictionary<Guid, int> _stock = [];
    private readonly Dictionary<Guid, (string Fuente, bool Traslado, int? Plazo)> _fuentes = [];

    public bool Caido { get; set; }

    public bool PreviaCaida { get; set; }

    public int ConsultasDeDisponibilidad { get; private set; }

    public int ConsultasPrevias { get; private set; }

    public List<IReadOnlyList<Guid>> EdicionesConsultadas { get; } = [];

    public EntregaParaElInventario? UltimaEntregaPrevia { get; private set; }

    public ResultadoDeReservar AlReservar { get; set; } = ResultadoDeReservar.De(TipoDeResultadoDeReservar.NoDisponible);

    public ResultadoDeLiberar AlLiberar { get; set; } = ResultadoDeLiberar.Liberada;

    public List<(Guid PedidoId, int Generacion, IReadOnlyList<LineaParaElInventario> Lineas)> Reservas { get; } = [];

    public List<(Guid PedidoId, string Motivo)> Liberaciones { get; } = [];

    public ListaDeSucursales? Sucursales { get; set; } = new([new("sede-central", "Sede central", "ayacucho"), new("cusco", "Cusco", "cusco")], EntornoDelCarrito.Inicio, false);

    public void FijarStock(Guid edicionId, int vendible) => _stock[edicionId] = vendible;

    /// <summary>El Inventario no conoce la edición: sale en <c>noRegistradas</c>.</summary>
    public void Olvidar(Guid edicionId) => _stock.Remove(edicionId);

    public void FijarFuente(Guid edicionId, string fuente, bool traslado, int? plazo) => _fuentes[edicionId] = (fuente, traslado, plazo);

    public Task<RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>> ConsultarDisponibilidad(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        ConsultasDeDisponibilidad++;
        EdicionesConsultadas.Add([.. edicionIds]);
        if (Caido)
        {
            return Task.FromResult(RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.SinRespuesta);
        }

        var conocidas = edicionIds.Where(_stock.ContainsKey).Select(id => new StockDeEdicion(id, _stock[id])).ToList();
        var noRegistradas = edicionIds.Where(id => !_stock.ContainsKey(id)).ToList();
        return Task.FromResult(RespuestaDelInventarioDelCarrito<StockDeVariasEdiciones>.ConValor(new StockDeVariasEdiciones(conocidas, noRegistradas, EntornoDelCarrito.Inicio)));
    }

    public Task<RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>> ConsultarPrevia(EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default)
    {
        ConsultasPrevias++;
        UltimaEntregaPrevia = entrega;
        if (Caido || PreviaCaida)
        {
            return Task.FromResult(RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.SinRespuesta);
        }

        var selecciones = lineas.Select(l =>
        {
            var (fuente, traslado, plazo) = _fuentes.GetValueOrDefault(l.EdicionId, ("sede-central", false, (int?)null));
            var hay = _stock.GetValueOrDefault(l.EdicionId) >= l.Cantidad;
            return new SeleccionDeLinea(l.LineaId, l.EdicionId, hay, hay ? fuente : null, traslado, plazo, _stock.GetValueOrDefault(l.EdicionId));
        }).ToList();
        return Task.FromResult(RespuestaDelInventarioDelCarrito<ConsultaPreviaDelInventario>.ConValor(new ConsultaPreviaDelInventario(selecciones, EntornoDelCarrito.Inicio)));
    }

    public Task<ListaDeSucursales?> ListarSucursales(CancellationToken cancelacion = default) => Task.FromResult(Caido ? null : Sucursales);

    public Task<ResultadoDeReservar> Reservar(Guid pedidoId, int generacionDeReserva, EntregaParaElInventario entrega, IReadOnlyList<LineaParaElInventario> lineas, CancellationToken cancelacion = default)
    {
        Reservas.Add((pedidoId, generacionDeReserva, lineas));
        return Task.FromResult(AlReservar);
    }

    public Task<ResultadoDeLiberar> Liberar(Guid pedidoId, string motivo, CancellationToken cancelacion = default)
    {
        Liberaciones.Add((pedidoId, motivo));
        return Task.FromResult(AlLiberar);
    }
}

/// <summary>Los textos legales con su versión vigente (de Identidad, por el contrato público de la web): configurables y con un interruptor de caído.</summary>
internal sealed class TextosLegalesFalsos : BabelHub.Ecommerce.Application.Web.ITextosLegales
{
    private readonly Dictionary<BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal, string> _versiones = new()
    {
        [BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal.Terminos] = "t-1",
        [BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal.Privacidad] = "p-1",
        [BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal.Reembolso] = "r-1",
    };

    public bool Caido { get; set; }

    public int Consultas { get; private set; }

    public void Vigente(BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal tipo, string version) => _versiones[tipo] = version;

    public Task<Resultado<BabelHub.Ecommerce.Application.Web.TextoLegal>> Obtener(BabelHub.Ecommerce.Application.Web.TipoDeTextoLegal tipo, CancellationToken cancelacion = default)
    {
        Consultas++;
        return Task.FromResult(Caido
            ? Resultado<BabelHub.Ecommerce.Application.Web.TextoLegal>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "texto-no-disponible"))
            : Resultado<BabelHub.Ecommerce.Application.Web.TextoLegal>.Exito(new BabelHub.Ecommerce.Application.Web.TextoLegal(tipo, "Texto", _versiones[tipo], new DateOnly(2026, 1, 1), false, "Resumen", [])));
    }
}

/// <summary>El consentimiento de WhatsApp: ofrece o no según se configure y guarda o falla; cuenta lo que se le pidió.</summary>
internal sealed class ConsentimientoFalso : IConsentimientoDeAvisos
{
    public OfertaDeConsentimiento Oferta { get; set; } = new(EstadoDeLaOfertaDeConsentimiento.SeOfrece, "•••• •• 321");

    public bool GuardaBien { get; set; } = true;

    public List<(Guid Cuenta, string Huella)> Guardados { get; } = [];

    public int Consultas { get; private set; }

    public Task<OfertaDeConsentimiento> Consultar(Guid cuentaId, CancellationToken cancelacion = default)
    {
        Consultas++;
        return Task.FromResult(Oferta);
    }

    public Task<bool> Guardar(Guid cuentaId, string huellaEnmascaradaDelNumero, CancellationToken cancelacion = default)
    {
        Guardados.Add((cuentaId, huellaEnmascaradaDelNumero));
        return Task.FromResult(GuardaBien);
    }
}

/// <summary>La marca de cancelación de la cuenta: se lee con bloqueo (queda en la bitácora) y puede cambiar a mitad de la solicitud o fallar.</summary>
internal sealed class MarcaDeCancelacionFalsa(BitacoraDeBloqueos bitacora) : IMarcaDeCancelacion
{
    public EstadoDeLaMarcaDeCancelacion Estado { get; set; } = EstadoDeLaMarcaDeCancelacion.Libre;

    public bool Falla { get; set; }

    /// <summary>Después de esta cantidad de lecturas la marca pasa a <see cref="EstadoTrasLasLecturas"/>: simula una cancelación que empieza entre los pasos.</summary>
    public int? LecturasHastaCambiar { get; set; }

    public EstadoDeLaMarcaDeCancelacion EstadoTrasLasLecturas { get; set; } = EstadoDeLaMarcaDeCancelacion.EnEjecucion;

    public int Lecturas { get; private set; }

    /// <summary>Se ejecuta justo antes de leer la marca: simula que la cancelación empieza entre la revisión y el pago.</summary>
    public Action? AntesDeLeer { get; set; }

    public Task<EstadoDeLaMarcaDeCancelacion> LeerConBloqueo(Guid cuentaId, CancellationToken cancelacion = default)
    {
        if (AntesDeLeer is { } antes)
        {
            AntesDeLeer = null;
            antes();
        }

        bitacora.Anotar(true, "marca");
        Lecturas++;
        if (LecturasHastaCambiar is { } hasta && Lecturas > hasta)
        {
            Estado = EstadoTrasLasLecturas;
        }

        return Falla ? throw new InvalidOperationException("Identidad no responde.") : Task.FromResult(Estado);
    }
}
