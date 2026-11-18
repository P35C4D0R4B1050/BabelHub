using System.Security.Cryptography;
using BabelHub.Ecommerce.Application.Catalogo;
using BabelHub.Ecommerce.Application.Catalogo.Interno;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Application.Plataforma.Interno;
using BabelHub.Ecommerce.Domain.Catalogo;
using BabelHub.Ecommerce.UnitTests.Plataforma;
using BabelHub.Ecommerce.UnitTests.Soporte;

namespace BabelHub.Ecommerce.UnitTests.Catalogo;

/// <summary>Algo con estado que la unidad de trabajo de prueba puede capturar y restaurar: así un fallo del caso de uso o de la auditoría <b>revierte</b> lo escrito en los repositorios en memoria (SEG-04, AUD-04).</summary>
internal interface IInstantanea
{
    object Capturar();

    void Restaurar(object instantanea);
}

/// <summary>Una unidad de trabajo con reversión: ejecuta el trabajo y, si lanza, restaura los repositorios en memoria al estado previo (como una transacción de base de datos).</summary>
internal sealed class UnidadDeTrabajoReversible(params IInstantanea[] repositorios) : IUnidadDeTrabajo
{
    public int Ejecuciones { get; private set; }

    public async Task<T> Ejecutar<T>(Func<CancellationToken, Task<T>> trabajo, CancellationToken cancelacion = default)
    {
        Ejecuciones++;
        var capturas = repositorios.Select(r => r.Capturar()).ToArray();
        try
        {
            return await trabajo(cancelacion).ConfigureAwait(false);
        }
        catch
        {
            for (var i = 0; i < repositorios.Length; i++)
            {
                repositorios[i].Restaurar(capturas[i]);
            }

            throw;
        }
    }

    public Task EjecutarComo(EsquemaDeModulo esquema, Func<CancellationToken, Task> bloque, CancellationToken cancelacion = default) => bloque(cancelacion);
}

internal sealed class RepositorioDeObrasEnMemoria : IRepositorioDeObras, IInstantanea
{
    private Dictionary<Guid, Obra> _obras = [];

    public IReadOnlyCollection<Obra> Todas => _obras.Values;

    public static Obra Clonar(Obra o) =>
        Obra.Rehidratar(
            o.Id,
            new DatosDeObra(o.Titulo, o.Autor, o.Editorial, o.Categoria),
            o.Portada,
            o.Version,
            o.Estado,
            o.DadaDeBajaEn,
            o.CreadaEn,
            o.Ediciones.Select(e => Edicion.Rehidratar(e.Id, e.Tipo, e.Isbn, e.Precio, e.Estado, e.MotivoDeRetiro, e.RetiradaEn, e.DadaDeBajaEn, e.Licencia, e.Archivo)));

    public Task<Obra?> Obtener(Guid obraId, bool paraActualizar, CancellationToken cancelacion = default) =>
        Task.FromResult(_obras.TryGetValue(obraId, out var obra) ? Clonar(obra) : null);

    public Task<Obra?> ObtenerPorEdicion(Guid edicionId, bool paraActualizar, CancellationToken cancelacion = default) =>
        Task.FromResult(_obras.Values.FirstOrDefault(o => o.BuscarEdicion(edicionId) is not null) is { } obra ? Clonar(obra) : null);

    public Task<ResultadoDeEscrituraDeObra> Agregar(Obra obra, CancellationToken cancelacion = default)
    {
        if (UsaUnIsbnAjeno(obra))
        {
            return Task.FromResult(ResultadoDeEscrituraDeObra.IsbnDuplicado);
        }

        _obras[obra.Id] = Clonar(obra);
        return Task.FromResult(ResultadoDeEscrituraDeObra.Escrita);
    }

    public Task<ResultadoDeEscrituraDeObra> Guardar(Obra obra, long versionLeida, CancellationToken cancelacion = default)
    {
        if (!_obras.TryGetValue(obra.Id, out var guardada) || guardada.Version != versionLeida)
        {
            return Task.FromResult(ResultadoDeEscrituraDeObra.ConflictoDeVersion);
        }

        if (UsaUnIsbnAjeno(obra))
        {
            return Task.FromResult(ResultadoDeEscrituraDeObra.IsbnDuplicado);
        }

        _obras[obra.Id] = Clonar(obra);
        return Task.FromResult(ResultadoDeEscrituraDeObra.Escrita);
    }

    public Task<string?> TituloDeLaEdicionConIsbn(string isbn13, CancellationToken cancelacion = default) =>
        Task.FromResult(_obras.Values.FirstOrDefault(o => o.Ediciones.Any(e => e.Isbn.Isbn13 == isbn13))?.Titulo);

    /// <summary>Prepara una obra ya guardada (sin pasar por un caso de uso).</summary>
    public void Sembrar(Obra obra) => _obras[obra.Id] = Clonar(obra);

    public object Capturar() => _obras.ToDictionary(p => p.Key, p => Clonar(p.Value));

    public void Restaurar(object instantanea) => _obras = (Dictionary<Guid, Obra>)instantanea;

    private bool UsaUnIsbnAjeno(Obra obra) =>
        obra.Ediciones.Any(e => _obras.Values.Any(o => o.Id != obra.Id && o.Ediciones.Any(x => x.Isbn.Isbn13 == e.Isbn.Isbn13)));
}

internal sealed class RepositorioDeReferenciasEnMemoria : IRepositorioDeReferenciasDeArchivo, IInstantanea
{
    private Dictionary<Guid, ReferenciaDeArchivo> _referencias = [];

    public IReadOnlyCollection<ReferenciaDeArchivo> Todas => _referencias.Values;

    public static ReferenciaDeArchivo Clonar(ReferenciaDeArchivo r) =>
        ReferenciaDeArchivo.Rehidratar(r.Id, r.UsuarioId, r.Finalidad, r.ClaveDelAlmacen, r.Estado, r.TipoDetectado, r.TamanoEnBytes, r.Huella, r.CreadoEn, r.VenceEn, r.ConsumidoEn);

    public Task<ReferenciaDeArchivo?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default) =>
        Task.FromResult(_referencias.TryGetValue(id, out var r) ? Clonar(r) : null);

    public Task Agregar(ReferenciaDeArchivo referencia, CancellationToken cancelacion = default)
    {
        _referencias[referencia.Id] = Clonar(referencia);
        return Task.CompletedTask;
    }

    public Task Guardar(ReferenciaDeArchivo referencia, CancellationToken cancelacion = default)
    {
        _referencias[referencia.Id] = Clonar(referencia);
        return Task.CompletedTask;
    }

    public Task EncolarDescarte(string claveEnAlmacen, FinalidadDeArchivo finalidad, Guid usuarioId, DateTimeOffset ahora, CancellationToken cancelacion = default)
    {
        var existente = _referencias.Values.FirstOrDefault(r => r.ClaveDelAlmacen == claveEnAlmacen);
        if (existente is not null)
        {
            existente.PasarADescarte();
            return Task.CompletedTask;
        }

        var id = Guid.NewGuid();
        _referencias[id] = ReferenciaDeArchivo.Rehidratar(id, usuarioId, finalidad, claveEnAlmacen, EstadoDeReferencia.Descarte, null, null, null, ahora, ahora, null);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ReferenciaDeArchivo>> Depurables(DateTimeOffset ahora, TimeSpan margenDeSubida, int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<ReferenciaDeArchivo>>(_referencias.Values
            .Where(r => r.Estado switch
            {
                EstadoDeReferencia.Subiendo => r.CreadoEn + margenDeSubida <= ahora,
                EstadoDeReferencia.Lista => r.VenceEn <= ahora,
                EstadoDeReferencia.Descarte => true,
                _ => r.ConsumidoEn is { } consumida && consumida + PoliticaDeArchivos.RetencionDeReferenciasConsumidas <= ahora,
            })
            .Take(maximo)
            .Select(Clonar)
            .ToList());

    public Task Eliminar(Guid id, CancellationToken cancelacion = default)
    {
        _referencias.Remove(id);
        return Task.CompletedTask;
    }

    /// <summary>Una referencia ya lista de un usuario (subida antes y aún sin consumir).</summary>
    public Guid SembrarLista(Guid usuarioId, FinalidadDeArchivo finalidad, TipoDeArchivo tipo, DateTimeOffset creadaEn, long tamano = 1_000)
    {
        var referencia = ReferenciaDeArchivo.Iniciar(Guid.NewGuid(), usuarioId, finalidad, creadaEn);
        referencia.MarcarLista(tipo, tamano, HuellaDeArchivo.Desde(SHA256.HashData(Guid.NewGuid().ToByteArray())));
        _referencias[referencia.Id] = Clonar(referencia);
        return referencia.Id;
    }

    public object Capturar() => _referencias.ToDictionary(p => p.Key, p => Clonar(p.Value));

    public void Restaurar(object instantanea) => _referencias = (Dictionary<Guid, ReferenciaDeArchivo>)instantanea;
}

internal sealed class RepositorioDeIngresosEnMemoria : IRepositorioDeIngresos, IInstantanea
{
    private Dictionary<Guid, IngresoSolicitado> _ingresos = [];

    public IReadOnlyCollection<IngresoSolicitado> Todos => _ingresos.Values;

    public static IngresoSolicitado Clonar(IngresoSolicitado i) =>
        IngresoSolicitado.Rehidratar(i.Id, i.EdicionId, i.ClaveDeIdempotencia, i.UsuarioId, i.Estado, i.IngresoId, i.Cantidad, i.Referencia, i.Nota, i.SolicitadoEn, i.Intentos, i.UltimoIntentoEn, i.ResultadoEn);

    public Task<bool> Agregar(IngresoSolicitado ingreso, CancellationToken cancelacion = default)
    {
        if (_ingresos.Values.Any(i => i.ClaveDeIdempotencia == ingreso.ClaveDeIdempotencia)
            || (ingreso.EstaPendiente && _ingresos.Values.Any(i => i.EdicionId == ingreso.EdicionId && i.EstaPendiente)))
        {
            return Task.FromResult(false);
        }

        _ingresos[ingreso.Id] = Clonar(ingreso);
        return Task.FromResult(true);
    }

    public Task<IngresoSolicitado?> Obtener(Guid ingresoSolicitadoId, bool paraActualizar, CancellationToken cancelacion = default) =>
        Task.FromResult(_ingresos.TryGetValue(ingresoSolicitadoId, out var i) ? Clonar(i) : null);

    public Task<IngresoSolicitado?> PorClave(Guid claveDeIdempotencia, CancellationToken cancelacion = default) =>
        Task.FromResult(_ingresos.Values.FirstOrDefault(i => i.ClaveDeIdempotencia == claveDeIdempotencia) is { } i ? Clonar(i) : null);

    public Task<IngresoSolicitado?> PendienteDeLaEdicion(Guid edicionId, CancellationToken cancelacion = default) =>
        Task.FromResult(_ingresos.Values.FirstOrDefault(i => i.EdicionId == edicionId && i.EstaPendiente) is { } i ? Clonar(i) : null);

    public Task<bool> GuardarTransicion(IngresoSolicitado ingreso, CancellationToken cancelacion = default)
    {
        if (!_ingresos.TryGetValue(ingreso.Id, out var guardado) || !guardado.EstaPendiente)
        {
            return Task.FromResult(false);
        }

        _ingresos[ingreso.Id] = Clonar(ingreso);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<IngresoSolicitado>> DeLaEdicion(Guid edicionId, int maximo, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<IngresoSolicitado>>(_ingresos.Values.Where(i => i.EdicionId == edicionId).OrderByDescending(i => i.SolicitadoEn).Take(maximo).Select(Clonar).ToList());

    public Task<IReadOnlySet<Guid>> IngresosYaConstatados(IReadOnlyCollection<Guid> ingresoIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(_ingresos.Values.Where(i => i.Estado == EstadoDeIngreso.Registrado && i.IngresoId is { } id && ingresoIds.Contains(id)).Select(i => i.IngresoId!.Value).ToHashSet());

    public object Capturar() => _ingresos.ToDictionary(p => p.Key, p => Clonar(p.Value));

    public void Restaurar(object instantanea) => _ingresos = (Dictionary<Guid, IngresoSolicitado>)instantanea;
}

/// <summary>
/// Todo el módulo Catálogo montado sobre dobles (T07 a T13): repositorios en memoria con reversión, el Inventario simulado, la auditoría espía, el reloj fijo y el actor. Cada caso de uso se
/// arma con <see cref="TransaccionPorCasoDeUso{TEntrada, TSalida}"/> alrededor, como en la composición real, de modo que un fallo (también una auditoría caída) revierte lo escrito.
/// </summary>
internal sealed class EntornoDelCatalogo
{
    public static readonly DateTimeOffset InicioDelDia = new(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-5));

    public EntornoDelCatalogo(ActorDePrueba? actor = null)
    {
        Actor = actor ?? ActorDePrueba.DeAdministradorCentral();
        Reloj = new RelojFijo(InicioDelDia);
        Unidad = new UnidadDeTrabajoReversible(Obras, Referencias, Ingresos);
        Autorizador = new AutorizadorDelCatalogo(Actor, Denegaciones);
        ConsumirReferencia = new ConsumirReferenciaDeArchivo(Referencias, Reloj);
        Constructor = new ConstructorDeEdiciones(ConsumirReferencia, Reloj);
        IngresosIniciales = new SolicitudDeIngresosIniciales(Ingresos, Emisor, Reloj);
        Lecturas = new LecturasDelCatalogoEnMemoria(Obras);
        Resolutor = new ResolutorDeDisponibilidad(Inventario, Memoria, Reloj);
        Consulta = new ConsultaDeCatalogoDeLaAplicacion(Lecturas, Resolutor, Reloj, Lecturas);
    }

    public ActorDePrueba Actor { get; }

    public RelojFijo Reloj { get; }

    public DenegacionesDelCatalogoEspia Denegaciones { get; } = new();

    public AuditoriaDelCatalogoEspia Auditoria { get; } = new();

    public RepositorioDeObrasEnMemoria Obras { get; } = new();

    public RepositorioDeReferenciasEnMemoria Referencias { get; } = new();

    public RepositorioDeIngresosEnMemoria Ingresos { get; } = new();

    public EmisorDeClavesDeIngresoDoble Emisor { get; } = new();

    public InventarioDelCatalogoDoble Inventario { get; } = new();

    public MemoriaDeDisponibilidadDoble Memoria { get; } = new();

    public AlmacenDeArchivosDoble Almacen { get; } = new();

    public RetiroDeCarritosEspia Carritos { get; } = new();

    public UnidadDeTrabajoReversible Unidad { get; }

    public AutorizadorDelCatalogo Autorizador { get; }

    public ConsumirReferenciaDeArchivo ConsumirReferencia { get; }

    public ConstructorDeEdiciones Constructor { get; }

    public SolicitudDeIngresosIniciales IngresosIniciales { get; }

    public LecturasDelCatalogoEnMemoria Lecturas { get; }

    public ResolutorDeDisponibilidad Resolutor { get; }

    public ConsultaDeCatalogoDeLaAplicacion Consulta { get; }

    private readonly Guid _usuarioPorOmision = Guid.NewGuid();

    /// <summary>El usuario del actor; para un visitante o el sistema, un identificador cualquiera (para sembrar datos).</summary>
    public Guid UsuarioId => Actor.Actual?.UsuarioId ?? _usuarioPorOmision;

    public DateOnly Hoy => Reloj.HoyEnLima;

    /// <summary>Envuelve un caso de uso en su transacción, como la composición real.</summary>
    public ICasoDeUso<TEntrada, TSalida> Transaccional<TEntrada, TSalida>(ICasoDeUso<TEntrada, TSalida> casoDeUso) =>
        new TransaccionPorCasoDeUso<TEntrada, TSalida>(casoDeUso, Unidad);

    public ICasoDeUso<RegistrarObraEntrada, ResultadoDeAlta> RegistrarObra() =>
        Transaccional(new RegistrarObra(Autorizador, new ValidadorDeRegistrarObra(), ConsumirReferencia, Constructor, IngresosIniciales, Obras, Auditoria, Reloj));

    public ICasoDeUso<RegistrarEdicionEntrada, ResultadoDeAlta> RegistrarEdicion() =>
        Transaccional(new RegistrarEdicion(Autorizador, new ValidadorDeRegistrarEdicion(), Constructor, IngresosIniciales, Obras, Auditoria));

    public ICasoDeUso<EditarObraEntrada, ResultadoDeEscritura> EditarObra() =>
        Transaccional(new EditarObra(Autorizador, new ValidadorDeEditarObra(), ConsumirReferencia, Obras, Referencias, Auditoria, Reloj));

    public ICasoDeUso<EditarEdicionEntrada, ResultadoDeEscritura> EditarEdicion() =>
        Transaccional(new EditarEdicion(Autorizador, new ValidadorDeEditarEdicion(), ConsumirReferencia, Constructor, Obras, Referencias, Auditoria, Reloj));

    public ICasoDeUso<DarDeBajaObraEntrada, ResultadoDeEscritura> DarDeBajaObra() =>
        Transaccional(new DarDeBajaObra(Autorizador, new ValidadorDeDarDeBajaObra(), Obras, Carritos, Auditoria, Reloj));

    public ICasoDeUso<DarDeBajaEdicionEntrada, ResultadoDeEscritura> DarDeBajaEdicion() =>
        Transaccional(new DarDeBajaEdicion(Autorizador, new ValidadorDeDarDeBajaEdicion(), Obras, Carritos, Auditoria, Reloj));

    public ICasoDeUso<RetirarEdicionDigitalEntrada, ResultadoDeEscritura> RetirarEdicionDigital() =>
        Transaccional(new RetirarEdicionDigital(Autorizador, new ValidadorDeRetirarEdicionDigital(), Obras, Auditoria, Reloj));

    public ICasoDeUso<MarcarResultadoDeIngresoEntrada, EstadoDeIngresoSolicitado> Marcar() => Transaccional(new MarcarResultadoDeIngreso(Ingresos, Memoria, Reloj));

    public OrquestadorDeIngresos Orquestador() => new(Inventario, Marcar());

    public ICasoDeUso<RegistrarIngresoEntrada, IngresoPedido> PedirIngreso() =>
        Transaccional(new RegistrarIngresoDeMercaderia(Autorizador, new ValidadorDeRegistrarIngreso(), Obras, Ingresos, Emisor, Reloj));

    public ICasoDeUso<ConciliarIngresoEntrada, ResultadoDeIngreso> Conciliar() =>
        new ConciliarIngresoPendiente(Autorizador, new ValidadorDeConciliarIngreso(), Ingresos, Inventario, Unidad, Auditoria, Memoria, Reloj);

    public IAdministracionDelCatalogo Administracion() =>
        new AdministracionDelCatalogoDeLaAplicacion(
            RegistrarObra(),
            EditarObra(),
            DarDeBajaObra(),
            RegistrarEdicion(),
            EditarEdicion(),
            DarDeBajaEdicion(),
            RetirarEdicionDigital(),
            new LecturasDeAdministracion(Autorizador, new ValidadorDeListarObrasDeAdministracion(), Lecturas, Reloj),
            Orquestador(),
            Ingresos);

    public IIngresosDeMercaderia IngresosDeMercaderia() =>
        new IngresosDeMercaderiaDeLaAplicacion(
            Transaccional(new EmitirClaveDeIngreso(Autorizador, new ValidadorDeEmitirClaveDeIngreso(), Obras, Emisor, Reloj)),
            PedirIngreso(),
            Orquestador(),
            new ReintentarIngresoPendiente(Autorizador, new ValidadorDeReintentarIngreso(), Ingresos, Unidad, Orquestador(), Reloj),
            Conciliar(),
            Transaccional(new ListarIngresos(Autorizador, new ValidadorDeListarIngresos(), Obras, Ingresos, Inventario)));

    /// <summary>Una obra con una impresa ya guardada (sin pasar por el alta) para probar ingresos sueltos.</summary>
    public Guid ImpresaSembrada(string isbn = "978-0-00-000001-9")
    {
        var edicion = Edicion.Impresa(Guid.NewGuid(), Isbn.Crear(isbn), Precio.Crear(50m));
        var obra = Domain.Catalogo.Obra.Registrar(Guid.NewGuid(), new DatosDeObra("Con ingresos", "A", "E", "C"), new PortadaDeObra(ClaveEnAlmacen.Nueva(), TipoDeArchivo.Jpeg, HuellaDeArchivo.Desde(SHA256.HashData(Guid.NewGuid().ToByteArray()))), [edicion], Reloj.Ahora).Valor;
        Obras.Sembrar(obra);
        return edicion.Id;
    }

    /// <summary>El token del formulario de la edición para el usuario actual.</summary>
    public string Token(Guid edicionId, Guid? usuario = null) => Emisor.Emitir(usuario ?? UsuarioId, edicionId, Reloj.Ahora).Token;

    // ---------- Datos de prueba ----------

    public Guid PortadaLista(Guid? usuario = null) => Referencias.SembrarLista(usuario ?? UsuarioId, FinalidadDeArchivo.Portada, TipoDeArchivo.Jpeg, Reloj.Ahora);

    public Guid LibroListo(TipoDeArchivo tipo = TipoDeArchivo.Epub, Guid? usuario = null) => Referencias.SembrarLista(usuario ?? UsuarioId, FinalidadDeArchivo.Libro, tipo, Reloj.Ahora);

    public NuevaEdicionEntrada EdicionImpresa(string isbn = "978-0-00-000001-9", decimal precio = 129m, int? stock = null) =>
        new(FormatoDeEdicion.Impresa, isbn, precio, null, null, stock);

    public NuevaEdicionEntrada EdicionDigital(string isbn = "9790000000018", decimal precio = 49m, LicenciaDeEdicionEntrada? licencia = null, Guid? archivoRef = null) =>
        new(FormatoDeEdicion.Digital, isbn, precio, licencia ?? Licencia(), archivoRef ?? LibroListo(), null);

    public LicenciaDeEdicionEntrada Licencia(string? titular = "Editorial Andina", DateOnly? hasta = null, IReadOnlyList<string>? territorios = null, FormatoDeArchivoDigital formato = FormatoDeArchivoDigital.Epub) =>
        new(TipoDeLicenciaDeEdicion.Comercial, titular, territorios ?? ["PE"], hasta ?? Hoy.AddYears(1), formato);

    public RegistrarObraEntrada Obra(string titulo = "Estructuras de datos", params NuevaEdicionEntrada[] ediciones) =>
        new(titulo, "Ana Quispe", "Editorial Andina", "Técnico", PortadaLista(), ediciones);

    /// <summary>Registra una obra por el caso de uso (con éxito) y la devuelve.</summary>
    public async Task<Obra> ObraRegistrada(string titulo = "Estructuras de datos", params NuevaEdicionEntrada[] ediciones)
    {
        var resultado = await RegistrarObra().Ejecutar(Obra(titulo, ediciones));
        Assert.True(resultado.EsExito, resultado.Error?.Clave);
        return (await Obras.Obtener(resultado.Valor.ObraId, false))!;
    }
}

/// <summary>La lectura del catálogo sobre el repositorio en memoria: texto con subcadena sin distinguir mayúsculas ni tildes, filtros por formato y precio, orden por título e identificador, y azar controlado.</summary>
internal sealed class LecturasDelCatalogoEnMemoria(RepositorioDeObrasEnMemoria obras) : ILecturasDelCatalogo, IAzarDelCatalogo
{
    public int ContarCandidatasLlamadas { get; private set; }

    /// <summary>La posición que elegirá <see cref="Siguiente"/> (módulo la cantidad).</summary>
    public int Eleccion { get; set; }

    private static string Normalizar(string? texto) =>
        new string((texto ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD).Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();

    private IEnumerable<Obra> Activas() => obras.Todas.Where(o => !o.EstaDadaDeBaja).OrderBy(o => o.Titulo, StringComparer.Ordinal).ThenBy(o => o.Id);

    private static bool Candidata(Edicion e, FiltrosDeObras f) =>
        !e.EstaDadaDeBaja
        && (f.Formato is null || (f.Formato == FormatoDeEdicion.Digital) == e.EsDigital)
        && (f.PrecioMinimo is null || e.Precio.Valor >= f.PrecioMinimo)
        && (f.PrecioMaximo is null || e.Precio.Valor <= f.PrecioMaximo);

    private IEnumerable<Obra> Cumplen(FiltrosDeObras f) =>
        Activas().Where(o =>
            (string.IsNullOrEmpty(f.Texto) || Normalizar(o.Titulo).Contains(Normalizar(f.Texto), StringComparison.Ordinal))
            && (string.IsNullOrEmpty(f.Autor) || Normalizar(o.Autor).Contains(Normalizar(f.Autor), StringComparison.Ordinal))
            && (string.IsNullOrEmpty(f.Editorial) || Normalizar(o.Editorial).Contains(Normalizar(f.Editorial), StringComparison.Ordinal))
            && (string.IsNullOrEmpty(f.Categoria) || Normalizar(o.Categoria) == Normalizar(f.Categoria))
            && o.Ediciones.Any(e => Candidata(e, f)));

    public Task<PaginaDeObrasDeLaBase> Buscar(FiltrosDeObras filtros, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var todas = Cumplen(filtros).ToList();
        return Task.FromResult(new PaginaDeObrasDeLaBase(todas.Skip((pagina - 1) * tamano).Take(tamano).Select(RepositorioDeObrasEnMemoria.Clonar).ToList(), todas.Count));
    }

    public Task<(int Obras, int Ediciones)> ContarCandidatas(FiltrosDeObras filtros, CancellationToken cancelacion = default)
    {
        ContarCandidatasLlamadas++;
        var cumplen = Cumplen(filtros).ToList();
        return Task.FromResult((cumplen.Count, cumplen.Sum(o => o.Ediciones.Count(e => Candidata(e, filtros)))));
    }


    public Task<PaginaDeObrasDeLaBase> BuscarParaAdministracion(string? texto, bool incluirDadasDeBaja, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        var todas = obras.Todas
            .Where(o => (incluirDadasDeBaja || !o.EstaDadaDeBaja)
                && (string.IsNullOrEmpty(texto) || Normalizar(o.Titulo + " " + o.Autor + " " + o.Editorial).Contains(Normalizar(texto), StringComparison.Ordinal)))
            .OrderBy(o => o.Titulo, StringComparer.Ordinal)
            .ThenBy(o => o.Id)
            .ToList();
        return Task.FromResult(new PaginaDeObrasDeLaBase(todas.Skip((pagina - 1) * tamano).Take(tamano).Select(RepositorioDeObrasEnMemoria.Clonar).ToList(), todas.Count));
    }
    public Task<IReadOnlyList<Obra>> TodasLasQueCumplen(FiltrosDeObras filtros, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Obra>>(Cumplen(filtros).Select(RepositorioDeObrasEnMemoria.Clonar).ToList());

    public Task<Obra?> ObraPorId(Guid obraId, CancellationToken cancelacion = default) =>
        Task.FromResult(obras.Todas.FirstOrDefault(o => o.Id == obraId) is { } o ? RepositorioDeObrasEnMemoria.Clonar(o) : null);

    public Task<IReadOnlyList<EdicionConObra>> EdicionesPorId(IReadOnlyCollection<Guid> edicionIds, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<EdicionConObra>>(
            edicionIds.Distinct().Select(id => obras.Todas.FirstOrDefault(o => o.BuscarEdicion(id) is not null)).OfType<Obra>().DistinctBy(o => o.Id).Select(RepositorioDeObrasEnMemoria.Clonar)
                .SelectMany(o => o.Ediciones.Where(e => edicionIds.Contains(e.Id)).Select(e => new EdicionConObra(o, e))).ToList());

    public Task<IReadOnlyList<Obra>> ObrasAlAzar(int cantidad, IReadOnlyCollection<Guid> excluidas, CancellationToken cancelacion = default) =>
        Task.FromResult<IReadOnlyList<Obra>>(Activas().Where(o => !excluidas.Contains(o.Id)).Take(cantidad).Select(RepositorioDeObrasEnMemoria.Clonar).ToList());

    public int Siguiente(int maximoExclusivo) => Eleccion % maximoExclusivo;
}
