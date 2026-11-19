using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Devoluciones.Interno;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Pagos;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Devoluciones;
using BabelHub.Ecommerce.UnitTests.Auditoria;
using BabelHub.Ecommerce.UnitTests.Carrito;
using BabelHub.Ecommerce.UnitTests.Catalogo;
using BabelHub.Ecommerce.UnitTests.Envios;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Devoluciones;

/// <summary>La resolución de sucursal de las pruebas: el código de cada sucursal y si el Inventario no responde.</summary>
internal sealed class ResolucionDeSucursalDoble : IResolucionDeSucursal
{
    public Dictionary<Guid, string> Codigos { get; } = [];

    public bool NoResponde { get; set; }

    public int Consultas { get; private set; }

    public Task<string?> CodigoDe(Guid sucursalId, CancellationToken cancelacion = default)
    {
        Consultas++;
        if (NoResponde)
        {
            throw new InvalidOperationException("El Inventario no responde.");
        }

        return Task.FromResult(Codigos.GetValueOrDefault(sucursalId));
    }
}

/// <summary>La lectura de Pedidos para Devoluciones: los pedidos sembrados; uno que no se sembró es «no encontrado» y <see cref="NoResponde"/> es «no disponible».</summary>
internal sealed class PedidosParaDevolucionesDoble : IPedidosParaDevoluciones
{
    private readonly Dictionary<Guid, PedidoParaDevoluciones> _pedidos = [];

    public bool NoResponde { get; set; }

    public int Lecturas { get; private set; }

    public void Sembrar(PedidoParaDevoluciones pedido) => _pedidos[pedido.PedidoId] = pedido;

    public Task<Resultado<PedidoParaDevoluciones>> Obtener(Guid pedidoId, CancellationToken cancelacion = default)
    {
        Lecturas++;
        if (NoResponde)
        {
            return Task.FromResult(Resultado<PedidoParaDevoluciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        return Task.FromResult(_pedidos.TryGetValue(pedidoId, out var pedido)
            ? Resultado<PedidoParaDevoluciones>.Exito(pedido)
            : Resultado<PedidoParaDevoluciones>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "pedido-no-encontrado")));
    }

    public Task<Resultado<PaginaDePedidosDeUnaCuenta>> ListarDeUnaCuenta(Guid cuentaId, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var ids = _pedidos.Values.Where(p => p.CuentaId == cuentaId).Select(p => p.PedidoId).ToList();
        return Task.FromResult(Resultado<PaginaDePedidosDeUnaCuenta>.Exito(new PaginaDePedidosDeUnaCuenta([.. ids.Skip((pagina - 1) * tamano).Take(tamano)], pagina, tamano, ids.Count)));
    }
}

/// <summary>El Inventario de Devoluciones: las sucursales activas y el resultado del reingreso (T10), con las cargas que recibió.</summary>
internal sealed class InventarioDeDevolucionesDoble(UnidadDeTrabajoReversible unidad) : IInventarioDeDevoluciones
{
    public HashSet<string> Sucursales { get; } = ["lima-central", "arequipa"];

    public bool NoResponde { get; set; }

    public bool Lanza { get; set; }

    public ResultadoDelReingreso Resultado { get; set; } = new ResultadoDelReingreso.Reingresado();

    /// <summary>El adaptador lanza una excepción con texto interno al reingresar (se trata como incierto).</summary>
    public bool LanzaAlReingresar { get; set; }

    public List<ReingresoPorDevolucion> Reingresos { get; } = [];

    public int ConsultasDeSucursales { get; private set; }

    public int LlamadasDentroDeLaTransaccion { get; private set; }

    public Task<ResultadoDeSucursalesActivas> ListarSucursalesActivas(CancellationToken cancelacion = default)
    {
        ConsultasDeSucursales++;
        if (unidad.EnCurso)
        {
            LlamadasDentroDeLaTransaccion++;
        }

        if (Lanza)
        {
            throw new InvalidOperationException("El Inventario falló.");
        }

        return Task.FromResult<ResultadoDeSucursalesActivas>(NoResponde ? new ResultadoDeSucursalesActivas.NoDisponible() : new ResultadoDeSucursalesActivas.Lista(Sucursales));
    }

    public Task<ResultadoDelReingreso> Reingresar(ReingresoPorDevolucion reingreso, CancellationToken cancelacion = default)
    {
        if (unidad.EnCurso)
        {
            LlamadasDentroDeLaTransaccion++;
        }

        Reingresos.Add(reingreso);
        if (LanzaAlReingresar)
        {
            throw new InvalidOperationException("TEXTO-INTERNO-DEL-INVENTARIO");
        }

        return Task.FromResult(Resultado);
    }
}

/// <summary>La marca de cancelación de la cuenta leída con bloqueo (DEV-26): la cuenta sembrada o la marca por omisión «libre».</summary>
internal sealed class MarcaDeCancelacionDoble : IMarcaDeCancelacionDeCuenta
{
    public EstadoDeLaMarca Estado { get; set; } = EstadoDeLaMarca.Libre;

    public bool NoResponde { get; set; }

    public int Lecturas { get; private set; }

    public Task<EstadoDeLaMarca> LeerConBloqueo(Guid usuarioId, CancellationToken cancelacion = default)
    {
        Lecturas++;
        if (NoResponde)
        {
            throw new InvalidOperationException("Identidad no responde.");
        }

        return Task.FromResult(Estado);
    }
}

/// <summary>La auditoría de las pruebas de Devoluciones: guarda lo registrado, puede fallar a pedido y se revierte con la transacción.</summary>
internal sealed class AuditoriaDeDevolucionesEspia : IAuditoria, IInstantanea
{
    private List<SolicitudDeRegistro> _solicitudes = [];

    public IReadOnlyList<SolicitudDeRegistro> Solicitudes => _solicitudes;

    public bool Caida { get; set; }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            return Task.FromException(new AuditoriaNoDisponibleException("sin auditoría"));
        }

        _solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }

    public object Capturar() => _solicitudes.ToList();

    public void Restaurar(object instantanea) => _solicitudes = (List<SolicitudDeRegistro>)instantanea;
}

/// <summary>El repositorio de devoluciones y cupos en memoria: se revierte con la transacción y toma los cupos en orden de línea.</summary>
internal sealed class RepositorioDeDevolucionesEnMemoria : IRepositorioDeDevoluciones, IInstantanea
{
    private Dictionary<Guid, Devolucion> _devoluciones = [];
    private Dictionary<Guid, CupoDeLinea> _cupos = [];

    public IReadOnlyCollection<Devolucion> Todas => _devoluciones.Values;

    public IReadOnlyDictionary<Guid, CupoDeLinea> Cupos => _cupos;

    public bool NoDisponible { get; set; }

    public List<Guid> OrdenDeCupos { get; } = [];

    public Task<Devolucion?> Obtener(Guid devolucionId, bool paraActualizar, CancellationToken cancelacion = default)
    {
        Comprobar();
        return Task.FromResult(_devoluciones.TryGetValue(devolucionId, out var guardada) ? Clonar(guardada) : null);
    }

    public Task Agregar(Devolucion devolucion, CancellationToken cancelacion = default)
    {
        Comprobar();
        _devoluciones[devolucion.Id] = Clonar(devolucion);
        return Task.CompletedTask;
    }

    public Task Guardar(Devolucion devolucion, CancellationToken cancelacion = default)
    {
        Comprobar();
        _devoluciones[devolucion.Id] = Clonar(devolucion);
        return Task.CompletedTask;
    }

    public Task<Guid?> TomarCupos(IReadOnlyList<CupoPedido> cupos, CancellationToken cancelacion = default)
    {
        Comprobar();
        foreach (var cupo in cupos.OrderBy(c => c.LineaId))
        {
            OrdenDeCupos.Add(cupo.LineaId);
            if (!_cupos.TryGetValue(cupo.LineaId, out var actual))
            {
                actual = CupoDeLinea.Crear(cupo.LineaId, cupo.CantidadComprada);
                _cupos[cupo.LineaId] = actual;
            }

            if (!actual.Comprometer(cupo.Unidades))
            {
                return Task.FromResult<Guid?>(cupo.LineaId);
            }
        }

        return Task.FromResult<Guid?>(null);
    }

    public Task DevolverCupos(IReadOnlyList<(Guid LineaId, int Unidades)> cupos, CancellationToken cancelacion = default)
    {
        Comprobar();
        foreach (var (lineaId, unidades) in cupos.OrderBy(c => c.LineaId))
        {
            _cupos[lineaId].Devolver(unidades);
        }

        return Task.CompletedTask;
    }

    public Task<PaginaDeDevolucionesDelDominio> ListarDePedidos(IReadOnlyCollection<Guid> pedidoIds, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var todas = _devoluciones.Values.Where(d => pedidoIds.Contains(d.PedidoId)).OrderByDescending(d => d.CreadaEn).ToList();
        return Task.FromResult(new PaginaDeDevolucionesDelDominio([.. todas.Skip((pagina - 1) * tamano).Take(tamano)], todas.Count));
    }

    public Task<PaginaDeDevolucionesDelDominio> ListarDeLaSucursal(string codigoDeSucursal, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var todas = _devoluciones.Values.Where(d => d.Tipo == TipoDeDevolucion.Impreso && d.Origen == OrigenDeDevolucion.Solicitud && d.CodigoDeSucursalReceptora == codigoDeSucursal).OrderByDescending(d => d.CreadaEn).ToList();
        return Task.FromResult(new PaginaDeDevolucionesDelDominio([.. todas.Skip((pagina - 1) * tamano).Take(tamano)], todas.Count));
    }

    public Task<IReadOnlyList<(Guid DevolucionId, Guid LineaDePedidoId)>> ObtenerConReembolsoPendiente(int maximo, CancellationToken cancelacion = default)
    {
        Comprobar();
        IReadOnlyList<(Guid, Guid)> pendientes = [.. _devoluciones.Values.OrderBy(d => d.CreadaEn).SelectMany(d => d.Lineas.Where(l => d.Tipo == TipoDeDevolucion.Impreso ? l.Estado == EstadoDeLineaDeDevolucion.Recibida : l.Estado == EstadoDeLineaDeDevolucion.Aprobada).Select(l => (d.Id, l.LineaDePedidoId))).Take(maximo)];
        return Task.FromResult(pendientes);
    }

    /// <summary>Las devoluciones que otra transacción tiene bloqueadas: <c>SKIP LOCKED</c> las salta.</summary>
    public HashSet<Guid> Bloqueadas { get; } = [];

    public Task<Devolucion?> ObtenerParaActualizarSinEsperar(Guid devolucionId, CancellationToken cancelacion = default)
    {
        Comprobar();
        return Task.FromResult(!Bloqueadas.Contains(devolucionId) && _devoluciones.TryGetValue(devolucionId, out var guardada) ? Clonar(guardada) : null);
    }

    public Task<IReadOnlyList<Guid>> ObtenerConRecepcionVencida(DateTimeOffset aprobadasAntesDe, int maximo, CancellationToken cancelacion = default)
    {
        Comprobar();
        IReadOnlyList<Guid> ids = [.. _devoluciones.Values
            .Where(d => d.Tipo == TipoDeDevolucion.Impreso && d.ReingresoIniciadoEn is null && d.Lineas.All(l => l.Estado == EstadoDeLineaDeDevolucion.Aprobada) && d.Lineas.Min(l => l.AprobadaEn)!.Value < aprobadasAntesDe)
            .OrderBy(d => d.Lineas.Min(l => l.AprobadaEn))
            .Select(d => d.Id)
            .Take(maximo)];
        return Task.FromResult(ids);
    }

    public Task<IReadOnlyList<Guid>> ObtenerConReingresoEstancado(DateTimeOffset iniciadoAntesDe, int maximo, CancellationToken cancelacion = default)
    {
        Comprobar();
        IReadOnlyList<Guid> ids = [.. _devoluciones.Values
            .Where(d => d.ReingresoIniciadoEn is { } iniciado && iniciado < iniciadoAntesDe && d.Lineas.All(l => l.Estado == EstadoDeLineaDeDevolucion.Aprobada))
            .OrderBy(d => d.ReingresoIniciadoEn)
            .Select(d => d.Id)
            .Take(maximo)];
        return Task.FromResult(ids);
    }

    public Task<PaginaDeDevolucionesDelDominio> ListarDeLaAdministracion(FiltroDeLaAdministracion filtro, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var todas = _devoluciones.Values.Where(d => (filtro.Tipo is null || d.Tipo == filtro.Tipo) && (!filtro.SoloReportes || d.Origen == OrigenDeDevolucion.Reporte)).OrderByDescending(d => d.CreadaEn).ToList();
        return Task.FromResult(new PaginaDeDevolucionesDelDominio([.. todas.Skip((pagina - 1) * tamano).Take(tamano)], todas.Count));
    }

    private void Comprobar()
    {
        if (NoDisponible)
        {
            throw new DevolucionesNoDisponiblesException("El almacén de Devoluciones no responde.");
        }
    }

    /// <summary>Una copia profunda: lo que la transacción cambie sobre un objeto leído no altera lo guardado hasta que se guarde, y revertir descarta los cambios.</summary>
    private static Devolucion Clonar(Devolucion d) =>
        Devolucion.Rehidratar(
            d.Id,
            d.PedidoId,
            d.Tipo,
            d.Origen,
            d.CodigoDeSucursalReceptora,
            d.Motivo,
            d.Texto,
            d.CreadaEn,
            d.RedactadaEn,
            d.ReingresoIniciadoEn,
            d.ReingresoIncertoEn,
            d.ReingresoConfirmadoEn,
            [.. d.Lineas.Select(l => new LineaRehidratada(l.Id, l.LineaDePedidoId, l.EdicionId, l.Cantidad, l.PrecioUnitario, l.Monto, l.AccesoId, [.. l.Cambios], l.ReembolsoRegistradoEn))]);

    public object Capturar() => (_devoluciones.ToDictionary(x => x.Key, x => Clonar(x.Value)), _cupos.ToDictionary(c => c.Key, c => CupoDeLinea.Rehidratar(c.Value.LineaId, c.Value.CantidadComprada, c.Value.CantidadComprometida)));

    public void Restaurar(object instantanea)
    {
        var (devoluciones, cupos) = ((Dictionary<Guid, Devolucion>, Dictionary<Guid, CupoDeLinea>))instantanea;
        _devoluciones = devoluciones;
        _cupos = cupos;
    }
}

/// <summary>El entorno de las pruebas de Devoluciones: los casos de uso reales con repositorios y contratos de otros módulos en memoria. Crece con cada tarea.</summary>
internal sealed class EntornoDeDevoluciones
{
    public static readonly DateTimeOffset AhoraInicial = new(2026, 11, 20, 15, 0, 0, TimeSpan.Zero);

    public EntornoDeDevoluciones(ActorDePrueba? actor = null, DateTimeOffset? ahora = null)
    {
        Actor = actor ?? ActorDePrueba.DeCliente();
        Reloj = new RelojFijo(ahora ?? AhoraInicial);
        Repositorio = new RepositorioDeDevolucionesEnMemoria();
        Auditoria = new AuditoriaDeDevolucionesEspia();
        Reembolsos = new ReembolsosDoble();
        Accesos = new AccesosParaDevolucionesDoble();
        Unidad = new UnidadDeTrabajoReversible(Repositorio, Auditoria, Reembolsos, Accesos);
        Reembolsos.Unidad = Unidad;
        Denegaciones = new DenegacionesDelCatalogoEspia();
        Cuentas = new EstadoDeCuentaFalso();
        Sucursales = new ResolucionDeSucursalDoble();
        Limites = new LimitesDeEnviosDoble();
        Pedidos = new PedidosParaDevolucionesDoble();
        Inventario = new InventarioDeDevolucionesDoble(Unidad);
        Marca = new MarcaDeCancelacionDoble();
        Alertas = new BabelHub.Ecommerce.UnitTests.Auditoria.AlertasTecnicasEspia();
        Biblioteca = new BibliotecaParaDevolucionesDoble();
        Armar();
    }

    /// <summary>El mismo estado (almacén, dobles y auditoría) visto por otro actor: por ejemplo, la administración central decidiendo lo que un cliente reportó.</summary>
    public EntornoDeDevoluciones ConLosDatosDe(EntornoDeDevoluciones otro) =>
        new(this, otro);

    private EntornoDeDevoluciones(EntornoDeDevoluciones actor, EntornoDeDevoluciones compartido)
    {
        Actor = actor.Actor;
        Reloj = compartido.Reloj;
        Repositorio = compartido.Repositorio;
        Auditoria = compartido.Auditoria;
        Reembolsos = compartido.Reembolsos;
        Unidad = compartido.Unidad;
        Accesos = compartido.Accesos;
        Denegaciones = compartido.Denegaciones;
        Cuentas = compartido.Cuentas;
        Sucursales = compartido.Sucursales;
        Limites = compartido.Limites;
        Pedidos = compartido.Pedidos;
        Inventario = compartido.Inventario;
        Marca = compartido.Marca;
        Biblioteca = compartido.Biblioteca;
        Alertas = compartido.Alertas;
        Armar();
    }

    private void Armar()
    {
        Autorizador = new AutorizadorDeDevoluciones(Actor, Denegaciones, Cuentas, Sucursales);
        LimitesDeUso = new LimitesDeDevoluciones(Limites, Actor);
        Procesador = new ProcesarReembolsoDeLinea(Reembolsos, Repositorio, Unidad, Auditoria, Reloj);
        SolicitarImpreso = new SolicitarDevolucionDeImpreso(Autorizador, LimitesDeUso, Pedidos, Inventario, Marca, Repositorio, Unidad, Auditoria, Reloj);
        SolicitarDigital = new SolicitarReembolsoDeDigital(Autorizador, LimitesDeUso, Pedidos, Accesos, Reembolsos, Marca, Repositorio, Unidad, Auditoria, Procesador, Reloj);
        Reportar = new ReportarArchivoDefectuoso(Autorizador, LimitesDeUso, Pedidos, Accesos, Marca, Repositorio, Unidad, Auditoria, Reloj);
        Decidir = new DecidirReporte(Autorizador, LimitesDeUso, Pedidos, Accesos, Biblioteca, Reembolsos, Repositorio, Unidad, Auditoria, Procesador, Reloj);
        Gestion = new AprobarYRechazarDevolucion(Autorizador, LimitesDeUso, Repositorio, Unidad, Auditoria, Reloj);
        Recepcion = new ConfirmarRecepcion(Autorizador, LimitesDeUso, Inventario, Reembolsos, Repositorio, Unidad, Auditoria, Alertas, Procesador, Reloj);
        Conciliacion = new ConciliarReembolsosDeDevoluciones(Autorizador, Repositorio, Reembolsos, Unidad, Procesador, Reloj);
        Vencidas = new RechazarDevolucionesVencidas(Autorizador, Repositorio, Unidad, Auditoria, Reloj);
        Estancadas = new RevisarDevolucionesEstancadas(Autorizador, Repositorio, Alertas, Reloj);
    }

    public RechazarDevolucionesVencidas Vencidas { get; private set; } = null!;

    public RevisarDevolucionesEstancadas Estancadas { get; private set; } = null!;

    public ActorDePrueba Actor { get; private set; }

    public RelojFijo Reloj { get; private set; }

    public RepositorioDeDevolucionesEnMemoria Repositorio { get; private set; }

    public AuditoriaDeDevolucionesEspia Auditoria { get; private set; }

    public UnidadDeTrabajoReversible Unidad { get; private set; }

    public DenegacionesDelCatalogoEspia Denegaciones { get; private set; }

    public EstadoDeCuentaFalso Cuentas { get; private set; }

    public ResolucionDeSucursalDoble Sucursales { get; private set; }

    public LimitesDeEnviosDoble Limites { get; private set; }

    public PedidosParaDevolucionesDoble Pedidos { get; private set; }

    public InventarioDeDevolucionesDoble Inventario { get; private set; }

    public MarcaDeCancelacionDoble Marca { get; private set; }

    public AutorizadorDeDevoluciones Autorizador { get; private set; } = null!;

    public LimitesDeDevoluciones LimitesDeUso { get; private set; } = null!;

    public SolicitarDevolucionDeImpreso SolicitarImpreso { get; private set; } = null!;

    public SolicitarReembolsoDeDigital SolicitarDigital { get; private set; } = null!;

    public BibliotecaParaDevolucionesDoble Biblioteca { get; private set; }

    public ReportarArchivoDefectuoso Reportar { get; private set; } = null!;

    public DecidirReporte Decidir { get; private set; } = null!;

    public AprobarYRechazarDevolucion Gestion { get; private set; } = null!;

    public ConfirmarRecepcion Recepcion { get; private set; } = null!;

    public ProcesarReembolsoDeLinea Procesador { get; private set; } = null!;

    public ConciliarReembolsosDeDevoluciones Conciliacion { get; private set; } = null!;

    public BabelHub.Ecommerce.UnitTests.Auditoria.AlertasTecnicasEspia Alertas { get; private set; } = null!;

    public ReembolsosDoble Reembolsos { get; private set; }

    public AccesosParaDevolucionesDoble Accesos { get; private set; }

    /// <summary>Siembra un pedido de la cuenta del actor con las líneas dadas y lo devuelve.</summary>
    public PedidoParaDevoluciones SembrarPedido(params LineaDePedidoParaDevoluciones[] lineas)
    {
        var pedido = new PedidoParaDevoluciones(Guid.NewGuid(), "PED-2026-0001", Actor.Actual!.UsuarioId!.Value, Reloj.Ahora.AddDays(-30), lineas);
        Pedidos.Sembrar(pedido);
        return pedido;
    }

    public static LineaDePedidoParaDevoluciones Entregada(DateTimeOffset entregadaEn, int cantidad = 3, decimal precio = 50m, EstadoVisibleDeLaLinea estado = EstadoVisibleDeLaLinea.Entregado) =>
        new(Guid.NewGuid(), Guid.NewGuid(), false, cantidad, precio, cantidad * precio, estado, estado == EstadoVisibleDeLaLinea.Entregado ? entregadaEn : null);

    public static LineaDePedidoParaDevoluciones Digital() => new(Guid.NewGuid(), Guid.NewGuid(), true, 1, 30m, 30m, EstadoVisibleDeLaLinea.DisponibleEnLaBiblioteca, null);
}
