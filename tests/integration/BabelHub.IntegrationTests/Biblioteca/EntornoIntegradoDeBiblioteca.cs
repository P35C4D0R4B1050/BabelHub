using BabelHub.Ecommerce.Application.Auditoria;
using BabelHub.Ecommerce.Application.Biblioteca;
using BabelHub.Ecommerce.Application.Biblioteca.Interno;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Identidad;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Biblioteca;
using BabelHub.Ecommerce.Infrastructure.Plataforma;

namespace BabelHub.IntegrationTests.Biblioteca;

/// <summary>El reloj de las pruebas de integración: el instante se mueve a mano (los vencimientos y las ventanas se prueban sin esperar).</summary>
internal sealed class RelojManual(DateTimeOffset inicial) : IReloj
{
    public DateTimeOffset Ahora { get; set; } = inicial;

    public DateOnly HoyEnLima => DateOnly.FromDateTime(Ahora.ToOffset(TimeSpan.FromHours(-5)).DateTime);
}

/// <summary>El Catálogo visto por Biblioteca (título y referencia del archivo), sembrado por la prueba; una edición retirada o con la licencia vencida sigue respondiendo (BIB-09).</summary>
internal sealed class CatalogoDeBibliotecaDePrueba : IConsultaDeCatalogo, IReferenciaDeArchivosDelCatalogo
{
    private readonly Dictionary<Guid, EdicionConsultada> _ediciones = [];
    private readonly Dictionary<Guid, ReferenciaDeArchivoDigital> _referencias = [];

    public Guid Sembrar(string clave, string titulo = "Libro de prueba", EstadoDeEdicionConsultado estado = EstadoDeEdicionConsultado.Activa, MotivoDeNoDisponibleDeEdicion? motivo = null)
    {
        var id = Guid.NewGuid();
        _ediciones[id] = new EdicionConsultada(id, Guid.NewGuid(), titulo, FormatoDeEdicion.Digital, "9780000000019", 25m, estado, motivo);
        _referencias[id] = new ReferenciaDeArchivoDigital(id, clave, FormatoDeArchivoDigital.Epub, new string('a', 64), 1024);
        return id;
    }

    public Task<Resultado<IReadOnlyList<EdicionConsultada>>> ObtenerEdiciones(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default)
    {
        IReadOnlyList<EdicionConsultada> halladas = [.. edicionIds.Where(_ediciones.ContainsKey).Select(id => _ediciones[id])];
        return Task.FromResult(Resultado<IReadOnlyList<EdicionConsultada>>.Exito(halladas));
    }

    public Task<Resultado<ReferenciaDeArchivoDigital>> ObtenerDeEdicion(Guid edicionId, CancellationToken cancelacion = default) =>
        Task.FromResult(_referencias.TryGetValue(edicionId, out var r)
            ? Resultado<ReferenciaDeArchivoDigital>.Exito(r)
            : Resultado<ReferenciaDeArchivoDigital>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "edicion-no-encontrada")));

    public Task<Resultado<ObraAlAzar?>> ObtenerObraAlAzar(CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<PaginaDeObras>> BuscarObras(BusquedaDeObras busqueda, CancellationToken cancelacion = default) => throw new NotSupportedException();

    public Task<Resultado<ResumenDeObra>> ObtenerObra(Guid obraId, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

internal sealed class CuentasDeBibliotecaDePrueba : IEstadoDeCuenta
{
    public Task<EstadoDeCuenta?> Consultar(Guid usuarioId, CancellationToken cancelacion = default) =>
        Task.FromResult<EstadoDeCuenta?>(new EstadoDeCuenta(usuarioId, true, EstadoDeAceptacionDeLosTextos.AlDia, true, false, false));

    public Task<ResultadoDeLaAceptacion> AceptarLosTextos(Guid usuarioId, string versionMostrada, CancellationToken cancelacion = default) => throw new NotSupportedException();
}

/// <summary>Los límites en memoria: un tope por política, idempotencia por clave del llamador (la repetición no cuenta) y un almacén de contadores que puede estar caído (falla cerrado).</summary>
internal sealed class LimitesDeBibliotecaDePrueba : IControlDeLimites
{
    private readonly object _candado = new();
    private readonly Dictionary<string, int> _usadas = [];
    private readonly HashSet<string> _claves = [];

    public int Maximo { get; set; } = int.MaxValue;

    public bool Caido { get; set; }

    public int Usadas(string politica)
    {
        lock (_candado)
        {
            return _usadas.GetValueOrDefault(politica);
        }
    }

    public Task<ResultadoDeLimite> Reservar(ClaveDePolitica politica, string? sujeto = null, string? claveDelLlamador = null, HuellaDeSujeto? huellaDelLlamador = null, ClaveDeIdempotencia? idempotencia = null, CancellationToken cancelacion = default)
    {
        if (Caido)
        {
            return Task.FromResult(ResultadoDeLimite.NoDisponible());
        }

        lock (_candado)
        {
            var reserva = new ReservaDeLimite(politica.Valor, "usuario", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
            if (claveDelLlamador is not null && !_claves.Add($"{politica.Valor}|{sujeto}|{claveDelLlamador}"))
            {
                return Task.FromResult(ResultadoDeLimite.Permitido(reserva));
            }

            var usadas = _usadas.GetValueOrDefault(politica.Valor);
            if (usadas >= Maximo)
            {
                return Task.FromResult(ResultadoDeLimite.Excedido(politica.Valor, 1800, DateTimeOffset.UtcNow.AddMinutes(30)));
            }

            _usadas[politica.Valor] = usadas + 1;
            return Task.FromResult(ResultadoDeLimite.Permitido(reserva));
        }
    }

    public Task Liberar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(ReservaDeLimite reserva, ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}

internal sealed class DenegacionesDeBibliotecaDePrueba : IDenegacionDeAcceso
{
    private int _total;

    public int Total => _total;

    public Task Registrar(DenegacionDeAcceso denegacion, CancellationToken cancelacion = default)
    {
        Interlocked.Increment(ref _total);
        return Task.CompletedTask;
    }
}

internal sealed class AuditoriaDeBibliotecaDePrueba : IAuditoria
{
    private readonly List<SolicitudDeRegistro> _registradas = [];

    public bool Caida { get; set; }

    public IReadOnlyList<SolicitudDeRegistro> Registradas
    {
        get
        {
            lock (_registradas)
            {
                return [.. _registradas];
            }
        }
    }

    public Task Registrar(SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        if (Caida)
        {
            return Task.FromException(new AuditoriaNoDisponibleException("sin auditoría"));
        }

        lock (_registradas)
        {
            _registradas.Add(solicitud);
        }

        return Task.CompletedTask;
    }
}

internal sealed class AlertasDeBibliotecaDePrueba : IPublicadorDeAlertasTecnicas
{
    public List<AlertaTecnica> Alertas { get; } = [];

    public Task Publicar(AlertaTecnica alerta, CancellationToken cancelacion = default)
    {
        lock (Alertas)
        {
            Alertas.Add(alerta);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Los casos de uso <b>reales</b> de Biblioteca sobre PostgreSQL real (la fixture) y el almacén que se indique, con el resto de los módulos en dobles mínimos. Cada llamada concurrente fija su propio actor (el <see cref="ActorDelFlujo"/> es por flujo asíncrono), de modo que varias
/// solicitudes simultáneas de distintas cuentas o de la administración conviven.
/// </summary>
internal sealed class EntornoIntegradoDeBiblioteca
{
    public EntornoIntegradoDeBiblioteca(PostgresDeBibliotecaFixture bd, IEntregaDeArchivos entrega, DateTimeOffset? ahora = null)
    {
        Reloj = new RelojManual(ahora ?? DateTimeOffset.UtcNow);
        Actor = new ActorDelFlujo();
        Catalogo = new CatalogoDeBibliotecaDePrueba();
        Limites = new LimitesDeBibliotecaDePrueba();
        Auditoria = new AuditoriaDeBibliotecaDePrueba();
        Denegaciones = new DenegacionesDeBibliotecaDePrueba();
        Alertas = new AlertasDeBibliotecaDePrueba();
        Bd = bd;
        Entrega = entrega;

        Autorizador = new AutorizadorDeBiblioteca(Actor, Denegaciones, new CuentasDeBibliotecaDePrueba());
        Proyector = new ProyectorDeAccesos(Catalogo, Catalogo, bd.Descargas);
        Consumir = new ConsumirDescarga(bd.Accesos, bd.Descargas, entrega, bd.Unidad);
        Descarga = new OrquestadorDeDescargas(Autorizador, Limites, bd.Accesos, bd.Descargas, Catalogo, Catalogo, entrega, Consumir, Alertas, Reloj);
        Conceder = new ConcederAcceso(bd.Accesos, Reloj);
        Revocar = new RevocarAcceso(bd.Accesos, Auditoria, Reloj);
        ParaDevoluciones = new AccesosParaDevolucionesDeLaAplicacion(bd.Accesos, Auditoria, Reloj);
        Cancelacion = new EliminarBibliotecaDeUnaCuenta(bd.Accesos);
        Listar = new ListarMiBiblioteca(Autorizador, bd.Accesos, Proyector);
        Consulta = new ConsultarBibliotecaDeUnCliente(Autorizador, bd.Accesos, Proyector, Auditoria, bd.Unidad);
        PedirAyuda = new PedirAyuda(Autorizador, bd.Accesos, bd.Descargas, bd.Unidad, Reloj);
        ListarAyudas = new ListarAyudas(Autorizador, bd.Descargas);
        Restituir = new RestituirDescargas(Autorizador, bd.Accesos, bd.Descargas, Proyector, Auditoria, bd.Unidad, Reloj);
        DepurarRegistros = new DepurarRegistrosDeDescargas(Actor, bd.Descargas, Auditoria, bd.Unidad, Reloj);
        DepurarAyudas = new DepurarAyudas(Actor, bd.Descargas, Auditoria, bd.Unidad, Reloj);
    }

    public RelojManual Reloj { get; }

    public ActorDelFlujo Actor { get; }

    public CatalogoDeBibliotecaDePrueba Catalogo { get; }

    public LimitesDeBibliotecaDePrueba Limites { get; }

    public AuditoriaDeBibliotecaDePrueba Auditoria { get; }

    public DenegacionesDeBibliotecaDePrueba Denegaciones { get; }

    public AlertasDeBibliotecaDePrueba Alertas { get; }

    public PostgresDeBibliotecaFixture Bd { get; }

    public IEntregaDeArchivos Entrega { get; }

    public AutorizadorDeBiblioteca Autorizador { get; }

    public ProyectorDeAccesos Proyector { get; }

    public ConsumirDescarga Consumir { get; }

    public OrquestadorDeDescargas Descarga { get; }

    public ConcederAcceso Conceder { get; }

    public RevocarAcceso Revocar { get; }

    public AccesosParaDevolucionesDeLaAplicacion ParaDevoluciones { get; }

    public EliminarBibliotecaDeUnaCuenta Cancelacion { get; }

    public ListarMiBiblioteca Listar { get; }

    public ConsultarBibliotecaDeUnCliente Consulta { get; }

    public PedirAyuda PedirAyuda { get; }

    public ListarAyudas ListarAyudas { get; }

    public RestituirDescargas Restituir { get; }

    public DepurarRegistrosDeDescargas DepurarRegistros { get; }

    public DepurarAyudas DepurarAyudas { get; }

    /// <summary>Concede un libro digital a la cuenta (por Pagos, dentro de su transacción) y devuelve el identificador del acceso, la edición y el pedido.</summary>
    public async Task<(Guid Acceso, Guid Edicion, Guid Pedido)> Comprar(Guid cuenta, string claveDeArchivo, string titulo = "Libro de prueba", EstadoDeEdicionConsultado estado = EstadoDeEdicionConsultado.Activa, MotivoDeNoDisponibleDeEdicion? motivo = null)
    {
        var edicion = Catalogo.Sembrar(claveDeArchivo, titulo, estado, motivo);
        var pedido = Guid.NewGuid();
        var resultado = await Bd.Unidad.Ejecutar(async token => await Conceder.Ejecutar(new ConcederAccesoEntrada(cuenta, pedido, edicion), token));
        Assert.Equal(ResultadoDeConcederAcceso.Concedido, resultado.Valor);
        var acceso = (await Bd.Unidad.Ejecutar(token => Bd.Accesos.ObtenerDeLaCompra(cuenta, pedido, edicion, token)))!;
        return (acceso.Id, edicion, pedido);
    }

    /// <summary>Corre algo como un cliente (su cuenta sale del actor del flujo, nunca de la solicitud).</summary>
    public async Task<T> ComoCliente<T>(Guid cuenta, Func<Task<T>> trabajo)
    {
        Actor.Establecer(ActorActual.DeUsuario(cuenta, "cliente"));
        try
        {
            return await trabajo();
        }
        finally
        {
            Actor.Limpiar();
        }
    }

    public async Task<T> ComoAdministracion<T>(Func<Task<T>> trabajo)
    {
        Actor.Establecer(ActorActual.DeUsuario(Guid.NewGuid(), "administrador-central"));
        try
        {
            return await trabajo();
        }
        finally
        {
            Actor.Limpiar();
        }
    }

    public async Task<T> ComoSistema<T>(Func<Task<T>> trabajo)
    {
        Actor.Establecer(ActorActual.DelSistema());
        try
        {
            return await trabajo();
        }
        finally
        {
            Actor.Limpiar();
        }
    }

    public Task<Resultado<EnlaceDeDescarga>> Pedir(Guid cuenta, Guid acceso, string clave) =>
        Task.Run(() => ComoCliente(cuenta, () => Descarga.SolicitarEnlace(acceso, clave)));
}
