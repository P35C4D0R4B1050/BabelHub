using BabelHub.Ecommerce.Application.Envios.Interno;
using BabelHub.Ecommerce.Application.Pedidos;
using BabelHub.Ecommerce.Application.Plataforma;
using BabelHub.Ecommerce.Domain.Envios;
using BabelHub.Ecommerce.UnitTests.Auditoria;
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

    /// <summary>El próximo guardado falla como un error inesperado de la base (y se rearma solo).</summary>
    public bool FallaAlGuardarElSiguiente { get; set; }

    /// <summary>El próximo guardado de un envío con guía falla como el único (operador, numero_de_guia) de la base.</summary>
    public bool GuiaRepetidaAlGuardar { get; set; }

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
        if (GuiaRepetidaAlGuardar && envio.NumeroDeGuia is not null)
        {
            GuiaRepetidaAlGuardar = false;
            throw new GuiaRepetidaException("guia-repetida");
        }

        if (FallaAlGuardarElSiguiente)
        {
            FallaAlGuardarElSiguiente = false;
            throw new InvalidOperationException("TEXTO-INTERNO-DE-LA-BASE");
        }

        if (_guardados == FallaEnElGuardadoNumero)
        {
            throw new InvalidOperationException("La base falló al guardar.");
        }

        _envios[envio.Id] = Clonar(envio);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Envio>> ObtenerConProgresoPendiente(int maximo, CancellationToken cancelacion = default)
    {
        Comprobar();
        var pendientes = _envios.Values
            .Where(e => (e.EnRepartoEn is not null && e.ProgresoEnRepartoInformadoEn is null) || (e.EntregadoEn is not null && e.ProgresoEntregadoInformadoEn is null))
            .OrderBy(e => e.EnRepartoEn).ThenBy(e => e.Id).Take(maximo).Select(Clonar).ToList();
        return Task.FromResult<IReadOnlyList<Envio>>(pendientes);
    }

    public Task<IReadOnlyList<Envio>> ObtenerConDespachoIniciadoAntesDe(DateTimeOffset limite, int maximo, CancellationToken cancelacion = default)
    {
        Comprobar();
        var lista = _envios.Values.Where(e => e.Estado == EstadoDeEnvio.GuiaGenerada && e.DespachoIniciadoEn is { } d && d < limite).OrderBy(e => e.DespachoIniciadoEn).Take(maximo).Select(Clonar).ToList();
        return Task.FromResult<IReadOnlyList<Envio>>(lista);
    }

    public Task<IReadOnlyList<Envio>> ObtenerCanceladosConGuiaEntre(DateTimeOffset desde, DateTimeOffset hasta, int maximo, CancellationToken cancelacion = default)
    {
        Comprobar();
        var lista = _envios.Values.Where(e => e.Estado == EstadoDeEnvio.Cancelado && e.NumeroDeGuia is not null && e.CanceladoEn is { } c && c > desde && c <= hasta).OrderBy(e => e.CanceladoEn).Take(maximo).Select(Clonar).ToList();
        return Task.FromResult<IReadOnlyList<Envio>>(lista);
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

/// <summary>El contacto de entrega que lee Envíos (contrato de Pedidos): sembrado por pedido; sin sembrar, no disponible (falla cerrado). Cuenta las lecturas y si ocurrieron dentro de una transacción.</summary>
internal sealed class ContactoDeEntregaDoble(UnidadDeTrabajoReversible unidad) : IContactoDeEntregaDelPedido
{
    private readonly Dictionary<Guid, LecturaDelContactoDeEntrega> _lecturas = [];

    public int Lecturas { get; private set; }

    public int LecturasDentroDeLaTransaccion { get; private set; }

    public void Sembrar(Guid pedidoId, LecturaDelContactoDeEntrega lectura) => _lecturas[pedidoId] = lectura;

    public void SembrarDisponible(Guid pedidoId) =>
        Sembrar(pedidoId, LecturaDelContactoDeEntrega.Disponible(new ContactoDeEntrega("Ana Pérez Quispe", "987654321", "Av. Los Olivos 123", "Ayacucho")));

    public Task<LecturaDelContactoDeEntrega> Leer(Guid pedidoId, CancellationToken cancelacion = default)
    {
        Lecturas++;
        if (unidad.EnCurso)
        {
            LecturasDentroDeLaTransaccion++;
        }

        return Task.FromResult(_lecturas.TryGetValue(pedidoId, out var lectura) ? lectura : LecturaDelContactoDeEntrega.NoDisponible());
    }
}

/// <summary>
/// El operador logístico simulado de las pruebas: idempotente por clave de solicitud (la misma clave devuelve la misma guía), con modos de no respuesta, rechazo y respuesta inválida. Guarda lo que recibió
/// y si la llamada ocurrió dentro de una transacción.
/// </summary>
internal sealed class OperadorLogisticoDoble(UnidadDeTrabajoReversible unidad) : IOperadorLogistico
{
    private readonly Dictionary<Guid, NumeroDeGuia> _guias = [];
    private int _secuencia;

    public string Nombre => "operador-a";

    public ResultadoDelOperador? Forzado { get; set; }

    /// <summary>Se ejecuta durante la llamada al operador: simula lo que pasa mientras el operador trabaja (por ejemplo, una cancelación).</summary>
    public Action? AlLlamar { get; set; }

    /// <summary>La próxima llamada lanza una excepción inesperada (con un texto que no debe propagarse).</summary>
    public bool Lanza { get; set; }

    public List<(Guid Clave, string Nombre, string Direccion, string Telefono)> Recibidas { get; } = [];

    public int LlamadasDentroDeLaTransaccion { get; private set; }

    /// <summary>Cuántas guías distintas creó el operador (para comprobar que un reintento no duplica).</summary>
    public int GuiasCreadas => _guias.Count;

    public Task<ResultadoDelOperador> CrearOObtenerGuia(Guid claveDeSolicitud, DestinatarioParaGuia destinatario, CancellationToken cancelacion = default)
    {
        if (unidad.EnCurso)
        {
            LlamadasDentroDeLaTransaccion++;
        }

        Recibidas.Add((claveDeSolicitud, destinatario.Nombre, destinatario.Direccion, destinatario.Telefono));
        AlLlamar?.Invoke();
        if (Lanza)
        {
            Lanza = false;
            throw new InvalidOperationException("TEXTO-INTERNO-DEL-OPERADOR");
        }

        if (Forzado is { } forzado)
        {
            return Task.FromResult(forzado);
        }

        if (!_guias.TryGetValue(claveDeSolicitud, out var numero))
        {
            numero = NumeroDeGuia.Crear($"GUIA-{++_secuencia:D6}");
            _guias[claveDeSolicitud] = numero;
        }

        return Task.FromResult<ResultadoDelOperador>(new ResultadoDelOperador.GuiaCreada(numero));
    }
}

/// <summary>
/// El cierre de reservas con motivo reparto del Inventario (T07): idempotente por <c>envioId</c> (una segunda llamada del mismo envío no descuenta otra vez), con una cola de resultados forzados por llamada y registro de las
/// llamadas y de si ocurrieron dentro de una transacción.
/// </summary>
internal sealed class ReservasDelEnvioDoble(UnidadDeTrabajoReversible unidad) : IReservasDelEnvio
{
    /// <summary>Se ejecuta durante la llamada al Inventario: simula lo que pasa mientras el Inventario trabaja (por ejemplo, una cancelación).</summary>
    public Action? AlCerrar { get; set; }

    private readonly Queue<ResultadoDeCierreDeReservas> _forzados = new();
    private readonly HashSet<Guid> _descontados = [];

    public List<(Guid EnvioId, IReadOnlyCollection<Guid> Lineas, Guid PedidoId)> Llamadas { get; } = [];

    /// <summary>La próxima llamada lanza una excepción inesperada (con un texto que no debe propagarse).</summary>
    public bool Lanza { get; set; }

    public int LlamadasDentroDeLaTransaccion { get; private set; }

    /// <summary>Cuántos envíos tienen el físico descontado (una sola vez cada uno).</summary>
    public int EnviosDescontados => _descontados.Count;

    /// <summary>Un resultado para la próxima llamada; si el resultado es <c>Cerrado</c> el descuento se aplica.</summary>
    public void Encolar(ResultadoDeCierreDeReservas resultado) => _forzados.Enqueue(resultado);

    public Task<ResultadoDeCierreDeReservas> Cerrar(Guid pedidoId, Guid envioId, IReadOnlyCollection<Guid> lineaIds, CancellationToken cancelacion = default)
    {
        if (unidad.EnCurso)
        {
            LlamadasDentroDeLaTransaccion++;
        }

        Llamadas.Add((envioId, lineaIds, pedidoId));
        if (Lanza)
        {
            Lanza = false;
            throw new InvalidOperationException("TEXTO-INTERNO-DEL-INVENTARIO");
        }

        AlCerrar?.Invoke();
        var resultado = _forzados.Count > 0 ? _forzados.Dequeue() : new ResultadoDeCierreDeReservas.Cerrado();
        if (resultado is ResultadoDeCierreDeReservas.Cerrado)
        {
            _descontados.Add(envioId);
        }

        return Task.FromResult(resultado);
    }
}

/// <summary>
/// El progreso de los envíos hacia Pedidos (contrato de Pedidos): guarda las llamadas, el estado que tenía el envío guardado en ese momento (para comprobar que se llama <b>después de confirmar</b>) y si ocurrieron dentro de una
/// transacción; puede fallar.
/// </summary>
internal sealed class ProgresoDeEnviosDoble(UnidadDeTrabajoReversible unidad, RepositorioDeEnviosEnMemoria envios) : IProgresoDeEnvios
{
    public bool Falla { get; set; }

    public List<(EnvioEnRepartoEntrada? EnReparto, EnvioEntregadoEntrada? Entregado, EstadoDeEnvio EstadoGuardado)> Llamadas { get; } = [];

    public int LlamadasDentroDeLaTransaccion { get; private set; }

    public Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEnReparto(EnvioEnRepartoEntrada entrada, CancellationToken cancelacion = default) =>
        Registrar(entrada, null, entrada.EnvioId);

    public Task<Resultado<ProgresoDeEnvioAplicado>> EnvioEntregado(EnvioEntregadoEntrada entrada, CancellationToken cancelacion = default) =>
        Registrar(null, entrada, entrada.EnvioId);

    private Task<Resultado<ProgresoDeEnvioAplicado>> Registrar(EnvioEnRepartoEntrada? enReparto, EnvioEntregadoEntrada? entregado, Guid envioId)
    {
        if (unidad.EnCurso)
        {
            LlamadasDentroDeLaTransaccion++;
        }

        Llamadas.Add((enReparto, entregado, envios.Todos.Single(e => e.Id == envioId).Estado));
        return Task.FromResult(
            Falla
                ? Resultado<ProgresoDeEnvioAplicado>.Fallo(new ErrorDeAplicacion(CategoriaDeError.NoDisponible, "servicio-no-disponible"))
                : Resultado<ProgresoDeEnvioAplicado>.Exito(new ProgresoDeEnvioAplicado(false)));
    }
}

/// <summary>El Notificador de las pruebas: guarda las solicitudes y si se pidieron dentro de una transacción; puede devolver «no se guardó», rechazar o lanzar (el contrato dice que nunca lanza, pero Envíos no debe depender de ello).</summary>
internal sealed class NotificadorDeEnviosEspia(UnidadDeTrabajoReversible unidad) : BabelHub.Ecommerce.Application.Notificaciones.INotificador
{
    public List<BabelHub.Ecommerce.Application.Notificaciones.SolicitudDeAviso> Solicitudes { get; } = [];

    public int DentroDeLaTransaccion { get; private set; }

    public BabelHub.Ecommerce.Application.Notificaciones.ResultadoDeLaSolicitudDeAviso Resultado { get; set; } = BabelHub.Ecommerce.Application.Notificaciones.ResultadoDeLaSolicitudDeAviso.Creado;

    public bool Lanza { get; set; }

    public Task<BabelHub.Ecommerce.Application.Notificaciones.ResultadoDeLaSolicitudDeAviso> Solicitar(BabelHub.Ecommerce.Application.Notificaciones.SolicitudDeAviso solicitud, CancellationToken cancelacion = default)
    {
        if (Lanza)
        {
            throw new InvalidOperationException("El notificador falló.");
        }

        if (unidad.EnCurso)
        {
            DentroDeLaTransaccion++;
        }

        Solicitudes.Add(solicitud);
        return Task.FromResult(Resultado);
    }
}

/// <summary>Los límites de uso de Envíos en memoria: un tope configurable por política, con registro de lo reservado y un almacén que puede estar caído (falla cerrado).</summary>
internal sealed class LimitesDeEnviosDoble : BabelHub.Ecommerce.Application.Plataforma.IControlDeLimites
{
    private readonly Dictionary<string, int> _usadas = [];

    public Dictionary<string, int> Maximos { get; } = [];

    public bool Caido { get; set; }

    public List<(string Politica, string? Sujeto)> Reservas { get; } = [];

    public int Usadas(string politica) => _usadas.GetValueOrDefault(politica);

    public Task<BabelHub.Ecommerce.Application.Plataforma.ResultadoDeLimite> Reservar(
        BabelHub.Ecommerce.Application.Plataforma.ClaveDePolitica politica,
        string? sujeto = null,
        string? claveDelLlamador = null,
        BabelHub.Ecommerce.Application.Plataforma.HuellaDeSujeto? huellaDelLlamador = null,
        BabelHub.Ecommerce.Application.Plataforma.ClaveDeIdempotencia? idempotencia = null,
        CancellationToken cancelacion = default)
    {
        Reservas.Add((politica.Valor, sujeto));
        if (Caido)
        {
            return Task.FromResult(BabelHub.Ecommerce.Application.Plataforma.ResultadoDeLimite.NoDisponible());
        }

        var usadas = _usadas.GetValueOrDefault(politica.Valor);
        if (usadas >= Maximos.GetValueOrDefault(politica.Valor, int.MaxValue))
        {
            return Task.FromResult(BabelHub.Ecommerce.Application.Plataforma.ResultadoDeLimite.Excedido(politica.Valor, 30, DateTimeOffset.UtcNow.AddSeconds(30)));
        }

        _usadas[politica.Valor] = usadas + 1;
        return Task.FromResult(BabelHub.Ecommerce.Application.Plataforma.ResultadoDeLimite.Permitido(new BabelHub.Ecommerce.Application.Plataforma.ReservaDeLimite(politica.Valor, "usuario", Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow)));
    }

    public Task Liberar(BabelHub.Ecommerce.Application.Plataforma.ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Completar(BabelHub.Ecommerce.Application.Plataforma.ReservaDeLimite reserva, BabelHub.Ecommerce.Application.Plataforma.ResultadoMinimo resultado, CancellationToken cancelacion = default) => Task.CompletedTask;

    public Task Fallar(BabelHub.Ecommerce.Application.Plataforma.ReservaDeLimite reserva, CancellationToken cancelacion = default) => Task.CompletedTask;
}
