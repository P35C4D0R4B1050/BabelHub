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

    public int Consultas { get; private set; }

    public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default)
    {
        Consultas++;
        if (NoResponde)
        {
            throw new InvalidOperationException("Identidad no responde.");
        }

        return Task.FromResult<EstadoDeCuenta?>(NoExiste ? null : new EstadoDeCuenta(usuarioId, !Cancelada, EstadoDeAceptacionDeLosTextos.AlDia, true, Cancelada, false));
    }

    public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => Task.FromResult(ResultadoDeLaAceptacion.Aceptada);
}

internal sealed class LimitadorDeCarritoFalso : ILimitadorDelCarrito
{
    public ResultadoDeLimiteDelCarrito CrearCarritos { get; set; } = ResultadoDeLimiteDelCarrito.Permitido;

    public ResultadoDeLimiteDelCarrito Editar { get; set; } = ResultadoDeLimiteDelCarrito.Permitido;

    public int CarritosCreados { get; private set; }

    public int Ediciones { get; private set; }

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

    public int Guardados { get; private set; }

    public void Sembrar(CarritoDeDominio carrito) => _carritos.Add(carrito);

    public Task<CarritoDeDominio?> ObtenerDeCuenta(Guid cuentaId, bool conBloqueo, CancellationToken cancelacion = default)
    {
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
        bitacora.Anotar(conBloqueo, $"carrito:{carritoId}");
        return Task.FromResult(_carritos.FirstOrDefault(c => c.Id == carritoId));
    }

    public Task Agregar(CarritoDeDominio carrito, CancellationToken cancelacion = default)
    {
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

    public Task<EstadoDelCheckout?> LeerEstadoSinBloqueo(Guid cuentaId, CancellationToken cancelacion = default) =>
        Task.FromResult<EstadoDelCheckout?>(_checkouts.FirstOrDefault(c => c.CuentaId == cuentaId && EstaVivo(c))?.Estado);

    public Task Agregar(Checkout checkout, CancellationToken cancelacion = default)
    {
        _checkouts.Add(checkout);
        return Task.CompletedTask;
    }

    public Task Guardar(Checkout checkout, CancellationToken cancelacion = default) => Task.CompletedTask;
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

    public int Reaperturas { get; private set; }

    public int Cancelaciones { get; private set; }

    public int Creaciones { get; private set; }

    public Task<ResultadoDeCrearIntencion> Crear(Guid cuentaId, InstantaneaDeCompra instantanea, string huellaDeInstantanea, DateTimeOffset reservaExpiraEn, CancellationToken cancelacion = default)
    {
        Creaciones++;
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

    public AuditoriaDelCarritoEspia Auditoria { get; } = new();

    public EstadoDeCuentaFalso Cuenta { get; } = new();

    public LimitadorDeCarritoFalso Limites { get; } = new();

    public GeneradorDeIdentificadoresFalso Identificadores { get; } = new();

    public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

    public UnidadDeTrabajoSimple Unidad { get; } = new();

    /// <summary>Los servicios compartidos de los casos de uso, armados con los dobles de este entorno.</summary>
    public ServiciosDelCarrito Servicios() =>
        new(new AutorizadorDelCarrito(Actor, Denegaciones), Catalogo, Disponibilidad, Posesion, Cuenta, Limites, Carritos, Checkouts, Identificadores, Reloj, Unidad, new EdicionConCheckoutVivo(Pagos, Checkouts), Auditoria);

    public static EntornoDelCarrito DeCliente(Guid? cuenta = null) => new(ActorDePrueba.DeCliente(cuenta));

    public static EntornoDelCarrito DeVisitante() => new(ActorDePrueba.Anonimo());

    /// <summary>Registra una edición en el Catálogo y en la disponibilidad: un impreso con stock vendible o una digital disponible al instante.</summary>
    public Guid Impreso(int vendible, decimal precio = 50m, string titulo = "Libro")
    {
        var id = Guid.NewGuid();
        Catalogo.Poner(id, FormatoDeEdicion.Impresa, precio, titulo);
        Disponibilidad.Vendible(id, vendible);
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
}
