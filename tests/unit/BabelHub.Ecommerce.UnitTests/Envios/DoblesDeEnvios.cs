using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Catalogo;

namespace BabelHub.Ecommerce.UnitTests.Envios;

/// <summary>
/// El repositorio de envíos en memoria: guarda copias (como una base de datos), respeta el único <c>(linea_id)</c>, cuenta las lecturas con <c>FOR UPDATE</c> y se restaura al revertir la unidad de trabajo.
/// </summary>
internal sealed class RepositorioDeEnviosEnMemoria : IRepositorioDeEnvios, IInstantanea
{
    private Dictionary<Guid, Envio> _envios = [];

    public IReadOnlyCollection<Envio> Todos => _envios.Values;

    /// <summary>El bloqueo del envío no responde (un <c>lock_timeout</c> de <c>FOR UPDATE</c>).</summary>
    public bool BloqueoNoDisponible { get; set; }

    /// <summary>La base no responde a ninguna operación.</summary>
    public bool NoDisponible { get; set; }

    /// <summary>La inserción falla después de la validación (un error de la base).</summary>
    public bool FallaAlAgregar { get; set; }

    /// <summary>Se ejecuta justo después de que <c>BuscarPorLineas</c> calcula su respuesta: simula otra transacción que gana la carrera.</summary>
    public Action? DespuesDeBuscarPorLineas { get; set; }

    public int Operaciones { get; private set; }

    public int LecturasConBloqueo { get; private set; }

    /// <summary>Si es mayor que cero, el guardado con ese número (desde 1) falla como un error de la base.</summary>
    public int FallaEnElGuardadoNumero { get; set; }

    private int _guardados;

    /// <summary>El orden en que se bloquearon los envíos (el orden de identificador de <c>Cancelar</c>).</summary>
    public List<Guid> OrdenDeBloqueo { get; } = [];

    public static Envio Clonar(Envio e) =>
        Envio.Rehidratar(
            e.Id,
            e.PedidoId,
            e.CodigoDeFuente,
            e.Lineas.Select(l => l.LineaId),
            e.Estado,
            e.NumeroDeGuia,
            e.Operador,
            e.ClaveDeSolicitud,
            e.GuiaEnCursoHasta,
            e.DespachoIniciadoEn,
            e.DespachoEnCursoHasta,
            e.DespachoIncertoEn,
            e.CreadoEn,
            e.GuiaGeneradaEn,
            e.EnRepartoEn,
            e.EntregadoEn,
            e.CanceladoEn,
            e.ProgresoEnRepartoInformadoEn,
            e.ProgresoEntregadoInformadoEn,
            e.Version);

    public void Sembrar(Envio envio) => _envios[envio.Id] = Clonar(envio);

    private void Comprobar()
    {
        Operaciones++;
        if (NoDisponible)
        {
            throw new EnviosNoDisponiblesException("sin base de datos");
        }
    }

    public Task<ResultadoDeAgregadoDeEnvio> Agregar(Envio envio, CancellationToken cancelacion = default)
    {
        Comprobar();
        if (FallaAlAgregar)
        {
            throw new InvalidOperationException("La base falló.");
        }

        var usadas = _envios.Values.SelectMany(e => e.Lineas).Select(l => l.LineaId).ToHashSet();
        if (envio.Lineas.Any(l => usadas.Contains(l.LineaId)))
        {
            return Task.FromResult(ResultadoDeAgregadoDeEnvio.LineaRepetida);
        }

        _envios[envio.Id] = Clonar(envio);
        return Task.FromResult(ResultadoDeAgregadoDeEnvio.Agregado);
    }

    public Task<Envio?> Obtener(Guid id, bool paraActualizar, CancellationToken cancelacion = default)
    {
        Comprobar();
        if (paraActualizar)
        {
            Bloquear(id);
        }

        return Task.FromResult(_envios.TryGetValue(id, out var e) ? Clonar(e) : null);
    }

    public Task<IReadOnlyList<Envio>> ObtenerDelPedido(Guid pedidoId, bool paraActualizar, CancellationToken cancelacion = default)
    {
        Comprobar();
        var envios = _envios.Values.Where(e => e.PedidoId == pedidoId).OrderBy(e => e.Id).ToList();
        if (paraActualizar)
        {
            foreach (var e in envios)
            {
                Bloquear(e.Id);
            }
        }

        return Task.FromResult<IReadOnlyList<Envio>>(envios.Select(Clonar).ToList());
    }

    public Task<IReadOnlyList<Envio>> BuscarPorLineas(IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default)
    {
        Comprobar();
        var encontrados = _envios.Values.Where(e => e.Lineas.Any(l => lineaIds.Contains(l.LineaId))).OrderBy(e => e.Id).Select(Clonar).ToList();
        var hook = DespuesDeBuscarPorLineas;
        DespuesDeBuscarPorLineas = null;
        hook?.Invoke();
        return Task.FromResult<IReadOnlyList<Envio>>(encontrados);
    }

    public Task Guardar(Envio envio, CancellationToken cancelacion = default)
    {
        Comprobar();
        _guardados++;
        if (_guardados == FallaEnElGuardadoNumero)
        {
            throw new InvalidOperationException("La base falló al guardar.");
        }

        _envios[envio.Id] = Clonar(envio);
        return Task.CompletedTask;
    }

    public Task<ListadoDeEnvios> Listar(EstadoDeEnvio? estado, int pagina, int tamano, CancellationToken cancelacion = default)
    {
        Comprobar();
        var filtrados = _envios.Values.Where(e => estado is null || e.Estado == estado).OrderByDescending(e => e.CreadoEn).ThenBy(e => e.Id).ToList();
        return Task.FromResult(new ListadoDeEnvios(filtrados.Skip((pagina - 1) * tamano).Take(tamano).Select(Clonar).ToList(), filtrados.Count));
    }

    private void Bloquear(Guid id)
    {
        if (BloqueoNoDisponible)
        {
            throw new EnviosNoDisponiblesException("lock_timeout");
        }

        LecturasConBloqueo++;
        OrdenDeBloqueo.Add(id);
    }

    public object Capturar() => _envios.ToDictionary(p => p.Key, p => Clonar(p.Value));

    public void Restaurar(object instantanea) => _envios = (Dictionary<Guid, Envio>)instantanea;
}

/// <summary>
/// El contrato de lectura de Pedidos para Envíos (D01): sembrado a mano, sin bloqueo. Guarda si cada lectura ocurrió <b>dentro de una transacción abierta</b> (la de quien llama a <c>Crear</c>) y puede no estar disponible.
/// </summary>
internal sealed class PedidosParaEnviosDoble(UnidadDeTrabajoReversible unidad) : IPedidosParaEnvios
{
    private readonly Dictionary<Guid, PedidoParaEnvios> _pedidos = [];

    public bool NoDisponible { get; set; }

    public int Lecturas { get; private set; }

    /// <summary>Cuántas lecturas se hicieron con una transacción abierta (la unidad de trabajo del llamador).</summary>
    public int LecturasDentroDeLaTransaccion { get; private set; }

    public void Sembrar(PedidoParaEnvios pedido) => _pedidos[pedido.PedidoId] = pedido;

    public Task<Resultado<PedidoParaEnvios>> Obtener(Guid pedidoId, CancellationToken cancelacion = default)
    {
        Lecturas++;
        if (unidad.EnCurso)
        {
            LecturasDentroDeLaTransaccion++;
        }

        if (NoDisponible)
        {
            return Task.FromResult(Resultado<PedidoParaEnvios>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible")));
        }

        return Task.FromResult(
            _pedidos.TryGetValue(pedidoId, out var pedido)
                ? Resultado<PedidoParaEnvios>.Exito(pedido)
                : Resultado<PedidoParaEnvios>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoEncontrado, "pedido-no-encontrado")));
    }
}

/// <summary>
/// Una auditoría que guarda lo registrado, <b>se revierte con la unidad de trabajo</b> (como la auditoría real, que escribe en la misma transacción) y puede caer en la llamada número N, para probar que una falla a
/// mitad de una operación no deja entradas sueltas (AUD-04).
/// </summary>
internal sealed class AuditoriaDeEnviosEspia : BabelHub.Ecommerce.Application.Auditoria.IAuditoria, IInstantanea
{
    private List<BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro> _solicitudes = [];
    private int _llamadas;

    public IReadOnlyList<BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro> Solicitudes => _solicitudes;

    public bool Caida { get; set; }

    /// <summary>Si es mayor que cero, la llamada con ese número (desde 1) falla como una auditoría caída.</summary>
    public int FallaEnLaLlamada { get; set; }

    public Task Registrar(BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro solicitud, CancellationToken cancelacion = default)
    {
        _llamadas++;
        if (Caida || _llamadas == FallaEnLaLlamada)
        {
            return Task.FromException(new BabelHub.Ecommerce.Application.Auditoria.AuditoriaNoDisponibleException("sin auditoría"));
        }

        _solicitudes.Add(solicitud);
        return Task.CompletedTask;
    }

    public object Capturar() => _solicitudes.ToList();

    public void Restaurar(object instantanea) => _solicitudes = (List<BabelHub.Ecommerce.Application.Auditoria.SolicitudDeRegistro>)instantanea;
}
